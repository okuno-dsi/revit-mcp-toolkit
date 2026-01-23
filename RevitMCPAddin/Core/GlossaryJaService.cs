#nullable enable
// ================================================================
// File   : Core/GlossaryJaService.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Japanese glossary loader + lightweight matcher (for help.suggest)
// Notes  :
//  - Best-effort, deterministic, no external NLP dependency.
//  - Loads glossary_ja.json (fallback: glossary_ja.seed.json) and caches an index.
//  - Handles duplicate keys by merging JA phrases (keeps the first entry as base).
// ================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal sealed class GlossaryLoadStatus
    {
        public bool ok { get; set; }
        public string? code { get; set; }
        public string? msg { get; set; }
        public string? path { get; set; }
        public string? locale { get; set; }
        public string? updated { get; set; }
        public int? version { get; set; }
        public string? sha8 { get; set; }
        public string[] warnings { get; set; } = Array.Empty<string>();
    }

    internal sealed class GlossaryHit
    {
        public string key { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public double score { get; set; }
        public string[] matched { get; set; } = Array.Empty<string>();

        // Optional helpers for downstream inference (help.suggest)
        public string? op { get; set; }
        public string? param { get; set; }
        public JToken? value { get; set; }
        public JObject? flags { get; set; }
        public string[] categoryHints { get; set; } = Array.Empty<string>();
        public string[] boostCommands { get; set; } = Array.Empty<string>();
    }

    internal sealed class GlossaryQueryAnalysis
    {
        public bool ok { get; set; }
        public string? code { get; set; }
        public string? msg { get; set; }
        public string normalized { get; set; } = string.Empty;
        public GlossaryHit[] hits { get; set; } = Array.Empty<GlossaryHit>();
        public GlossaryLoadStatus? glossary { get; set; }
    }

    internal static class GlossaryJaService
    {
        private const string EnvGlossaryPath = "REVITMCP_GLOSSARY_JA_PATH";
        private const string DefaultFileName = "glossary_ja.json";
        private const string FallbackFileName = "glossary_ja.seed.json";

        private static readonly object _gate = new object();
        private static bool _loaded;
        private static GlossaryIndex? _index;
        private static GlossaryLoadStatus _status = new GlossaryLoadStatus { ok = false, code = "NOT_LOADED", msg = "Glossary not loaded." };

        // Invalid/legacy BuiltInCategory spellings observed in seed files (best-effort fixups).
        private static readonly Dictionary<string, string[]> CategoryFixups
            = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["OST_AreaReinforcement"] = new[] { "OST_AreaRein" },
                ["OST_SlabEdge"] = new[] { "OST_EdgeSlab" },
                ["OST_Soffit"] = new[] { "OST_RoofSoffit" },
                ["OST_PropertyLines"] = new[] { "OST_SitePropertyLineSegment" },
                ["OST_RepeatingDetail"] = new[] { "OST_RepeatingDetailLines" },
                // "Openings" is not a single BuiltInCategory in Revit; expand to common opening categories.
                ["OST_Openings"] = new[] { "OST_ShaftOpening", "OST_FloorOpening", "OST_RoofOpening", "OST_CeilingOpening" },
            };

        public static GlossaryLoadStatus GetStatus()
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                return new GlossaryLoadStatus
                {
                    ok = _status.ok,
                    code = _status.code,
                    msg = _status.msg,
                    path = _status.path,
                    locale = _status.locale,
                    updated = _status.updated,
                    version = _status.version,
                    sha8 = _status.sha8,
                    warnings = _status.warnings ?? Array.Empty<string>()
                };
            }
        }

        public static GlossaryQueryAnalysis Analyze(string query, int limit = 128)
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                if (!_loaded || _index == null)
                {
                    var st = GetStatus();
                    return new GlossaryQueryAnalysis
                    {
                        ok = false,
                        code = st.code ?? "NOT_READY",
                        msg = st.msg ?? "Glossary not available.",
                        normalized = Normalize(query),
                        hits = Array.Empty<GlossaryHit>(),
                        glossary = st
                    };
                }

                var nq = Normalize(query);
                var hitMap = new Dictionary<string, HitAcc>(StringComparer.OrdinalIgnoreCase);

                foreach (var pat in _index.Patterns)
                {
                    if (string.IsNullOrEmpty(pat.PhraseNorm)) continue;
                    if (nq.IndexOf(pat.PhraseNorm, StringComparison.Ordinal) < 0) continue;

                    if (!hitMap.TryGetValue(pat.Entry.key, out var acc))
                    {
                        acc = new HitAcc(pat.Entry);
                        hitMap[pat.Entry.key] = acc;
                    }

                    acc.score += pat.Weight;
                    acc.AddMatched(pat.PhraseRaw);
                }

                var hits = hitMap.Values
                    .OrderByDescending(x => x.score)
                    .ThenBy(x => x.entry.key, StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Max(1, Math.Min(512, limit)))
                    .Select(x => x.ToHit())
                    .ToArray();

                return new GlossaryQueryAnalysis
                {
                    ok = true,
                    code = "OK",
                    msg = "Glossary matches",
                    normalized = nq,
                    hits = hits,
                    glossary = GetStatus()
                };
            }
        }

        /// <summary>
        /// Reverse lookup: BuiltInCategory name -> glossary entity keys.
        /// Used for context-derived entity inference (selection/view).
        /// </summary>
        public static string[] LookupEntityKeysByCategory(string builtInCategoryName)
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                if (!_loaded || _index == null) return Array.Empty<string>();
                if (string.IsNullOrWhiteSpace(builtInCategoryName)) return Array.Empty<string>();
                return _index.CategoryToEntityKeys.TryGetValue(builtInCategoryName.Trim(), out var keys)
                    ? keys.ToArray()
                    : Array.Empty<string>();
            }
        }

        // ---------------------------- internals ----------------------------

        private static void EnsureLoadedBestEffort()
        {
            lock (_gate)
            {
                if (_loaded) return;
                try
                {
                    var path = ResolveGlossaryPath(out var usedFallbackName);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        _status = new GlossaryLoadStatus
                        {
                            ok = false,
                            code = "GLOSSARY_NOT_FOUND",
                            msg = "glossary_ja.json not found. Place it in %LOCALAPPDATA%\\RevitMCP, %USERPROFILE%\\Documents\\Codex\\Design, or the add-in folder.",
                            path = path,
                            warnings = usedFallbackName ? new[] { "Tried fallback file name: " + FallbackFileName } : Array.Empty<string>()
                        };
                        _loaded = false;
                        _index = null;
                        return;
                    }

                    var bytes = File.ReadAllBytes(path);
                    var json = Encoding.UTF8.GetString(bytes);
                    if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);

                    var root = JObject.Parse(json);
                    var sha8 = Sha256Hex(bytes).Substring(0, 8);

                    var warnings = new List<string>();
                    _index = GlossaryIndex.Build(root, path, sha8, warnings);
                    _loaded = true;
                    _status = new GlossaryLoadStatus
                    {
                        ok = true,
                        code = "OK",
                        msg = "Glossary loaded.",
                        path = path,
                        locale = _index.Locale,
                        updated = _index.GeneratedAt,
                        version = _index.Version,
                        sha8 = sha8,
                        warnings = warnings.ToArray()
                    };
                }
                catch (Exception ex)
                {
                    _loaded = false;
                    _index = null;
                    _status = new GlossaryLoadStatus
                    {
                        ok = false,
                        code = "GLOSSARY_LOAD_FAILED",
                        msg = ex.Message
                    };
                }
            }
        }

        private static string? ResolveGlossaryPath(out bool usedFallbackName)
        {
            usedFallbackName = false;

            // 0) env override
            try
            {
                var env = Environment.GetEnvironmentVariable(EnvGlossaryPath);
                if (!string.IsNullOrWhiteSpace(env) && File.Exists(env!)) return env;
            }
            catch { /* ignore */ }

            // 1) LocalRoot
            var p1 = TryPath(Paths.LocalRoot, DefaultFileName);
            if (p1 != null) return p1;

            // 2) MyDocuments\\Codex\\Design
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    var p2 = TryPath(Path.Combine(docs, "Codex", "Design"), DefaultFileName);
                    if (p2 != null) return p2;
                }
            }
            catch { /* ignore */ }

            // 3) Add-in folder (Resources/ and root)
            var p3 = TryPath(Path.Combine(Paths.AddinFolder, "Resources"), DefaultFileName);
            if (p3 != null) return p3;
            var p4 = TryPath(Paths.AddinFolder, DefaultFileName);
            if (p4 != null) return p4;

            // 4) Dev convenience: walk up and look for "<ancestor>\\Codex\\Design\\glossary_ja.json"
            try
            {
                var cur = Paths.AddinFolder;
                for (int i = 0; i < 8; i++)
                {
                    var parent = Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(parent)) break;
                    var cand = TryPath(Path.Combine(parent, "Codex", "Design"), DefaultFileName);
                    if (cand != null) return cand;
                    cur = parent;
                }
            }
            catch { /* ignore */ }

            // ---- fallback name (seed) ----
            usedFallbackName = true;

            // LocalRoot fallback
            var f1 = TryPath(Paths.LocalRoot, FallbackFileName);
            if (f1 != null) return f1;

            // Documents fallback
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    var f2 = TryPath(Path.Combine(docs, "Codex", "Design"), FallbackFileName);
                    if (f2 != null) return f2;
                }
            }
            catch { /* ignore */ }

            // Add-in fallback
            var f3 = TryPath(Path.Combine(Paths.AddinFolder, "Resources"), FallbackFileName);
            if (f3 != null) return f3;
            var f4 = TryPath(Paths.AddinFolder, FallbackFileName);
            if (f4 != null) return f4;

            // Dev convenience fallback
            try
            {
                var cur = Paths.AddinFolder;
                for (int i = 0; i < 8; i++)
                {
                    var parent = Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(parent)) break;
                    var cand = TryPath(Path.Combine(parent, "Codex", "Design"), FallbackFileName);
                    if (cand != null) return cand;
                    cur = parent;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static string? TryPath(string folder, string file)
        {
            try
            {
                var p = Path.Combine(folder, file);
                if (File.Exists(p)) return p;
            }
            catch { /* ignore */ }
            return null;
        }

        private static string Normalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var t = (s ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC);
            if (t.Length == 0) return string.Empty;

            // Lowercase ASCII only (keeps Japanese as-is).
            var sb = new StringBuilder(t.Length);
            for (int i = 0; i < t.Length; i++)
            {
                var ch = t[i];
                if (ch <= 0x7F) sb.Append(char.ToLowerInvariant(ch));
                else sb.Append(ch);
            }

            // Normalize whitespace.
            var u = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            return u;
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private sealed class GlossaryEntry
        {
            public string key { get; set; } = string.Empty;
            public string type { get; set; } = string.Empty;
            public string[] ja { get; set; } = Array.Empty<string>();

            public string? op { get; set; }
            public string[] categoryHints { get; set; } = Array.Empty<string>();
            public string[] revitClassHints { get; set; } = Array.Empty<string>();
            public string? param { get; set; }
            public JToken? value { get; set; }
            public JObject? flags { get; set; }
            public string? unit { get; set; }
            public double? multiplierToBase { get; set; }
            public string[] boostCommands { get; set; } = Array.Empty<string>();
        }

        private sealed class Pattern
        {
            public string PhraseRaw { get; private set; } = string.Empty;
            public string PhraseNorm { get; private set; } = string.Empty;
            public double Weight { get; private set; }
            public GlossaryEntry Entry { get; private set; }

            public Pattern(string raw, string norm, double weight, GlossaryEntry entry)
            {
                PhraseRaw = raw ?? string.Empty;
                PhraseNorm = norm ?? string.Empty;
                Weight = weight;
                Entry = entry;
            }
        }

        private sealed class HitAcc
        {
            public GlossaryEntry entry;
            public double score;
            private readonly HashSet<string> _matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public HitAcc(GlossaryEntry e) { entry = e; score = 0.0; }
            public void AddMatched(string raw)
            {
                var s = (raw ?? string.Empty).Trim();
                if (s.Length == 0) return;
                _matched.Add(s);
            }

            public GlossaryHit ToHit()
            {
                return new GlossaryHit
                {
                    key = entry.key,
                    type = entry.type,
                    score = score,
                    matched = _matched.ToArray(),
                    op = entry.op,
                    param = entry.param,
                    value = entry.value != null ? entry.value.DeepClone() : null,
                    flags = entry.flags != null ? (JObject)entry.flags.DeepClone() : null,
                    categoryHints = entry.categoryHints ?? Array.Empty<string>(),
                    boostCommands = entry.boostCommands ?? Array.Empty<string>()
                };
            }
        }

        private sealed class GlossaryIndex
        {
            public int Version { get; private set; }
            public string Locale { get; private set; } = "ja-JP";
            public string GeneratedAt { get; private set; } = string.Empty;
            public string Path { get; private set; } = string.Empty;
            public string Sha8 { get; private set; } = string.Empty;

            public List<Pattern> Patterns { get; private set; } = new List<Pattern>();
            public Dictionary<string, string[]> CategoryToEntityKeys { get; private set; }
                = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            public static GlossaryIndex Build(JObject root, string path, string sha8, List<string> warnings)
            {
                var idx = new GlossaryIndex();
                idx.Path = path ?? string.Empty;
                idx.Sha8 = sha8 ?? string.Empty;

                try { idx.Version = root.Value<int?>("version") ?? 0; } catch { idx.Version = 0; }
                try { idx.Locale = root.Value<string>("language") ?? "ja-JP"; } catch { idx.Locale = "ja-JP"; }
                try { idx.GeneratedAt = root.Value<string>("generatedAt") ?? string.Empty; } catch { idx.GeneratedAt = string.Empty; }

                var entriesTok = root["entries"] as JArray;
                if (entriesTok == null) throw new Exception("Invalid glossary JSON: missing 'entries' array.");

                var byKey = new Dictionary<string, GlossaryEntry>(StringComparer.OrdinalIgnoreCase);
                int dupKeys = 0;
                int invalidCats = 0;

                foreach (var item in entriesTok)
                {
                    if (!(item is JObject jo)) continue;
                    var key = (jo.Value<string>("key") ?? string.Empty).Trim();
                    var type = (jo.Value<string>("type") ?? string.Empty).Trim();
                    if (key.Length == 0 || type.Length == 0) continue;

                    var jaArr = ReadStringArray(jo["ja"]);
                    if (jaArr.Length == 0) continue;

                    var e = new GlossaryEntry
                    {
                        key = key,
                        type = type,
                        ja = jaArr,
                        op = jo.Value<string>("op"),
                        revitClassHints = ReadStringArray(jo["revitClassHints"]),
                        param = jo.Value<string>("param"),
                        value = jo["value"],
                        flags = jo["flags"] as JObject,
                        unit = jo.Value<string>("unit"),
                        multiplierToBase = jo.Value<double?>("multiplierToBase"),
                        boostCommands = ReadStringArray(jo["boostCommands"])
                    };

                    var cats = ReadStringArray(jo["categoryHints"]);
                    if (cats.Length > 0)
                    {
                        var fixedCats = new List<string>();
                        foreach (var c in cats)
                        {
                            var cc = (c ?? string.Empty).Trim();
                            if (cc.Length == 0) continue;

                            if (CategoryFixups.TryGetValue(cc, out var repl) && repl != null && repl.Length > 0)
                            {
                                foreach (var r in repl)
                                {
                                    var rr = (r ?? string.Empty).Trim();
                                    if (rr.Length == 0) continue;
                                    if (TryValidateBuiltInCategory(rr)) fixedCats.Add(rr);
                                    else { invalidCats++; warnings.Add("Invalid BuiltInCategory dropped: " + rr + " (from fixup for " + cc + ")"); }
                                }
                                continue;
                            }

                            if (TryValidateBuiltInCategory(cc)) fixedCats.Add(cc);
                            else
                            {
                                invalidCats++;
                                warnings.Add("Invalid BuiltInCategory dropped: " + cc + " (key=" + key + ")");
                            }
                        }
                        e.categoryHints = fixedCats.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    }

                    if (byKey.TryGetValue(key, out var existing))
                    {
                        dupKeys++;
                        // Merge JA phrases (keep existing metadata as base)
                        existing.ja = existing.ja.Concat(e.ja).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                        // Merge category hints / class hints
                        existing.categoryHints = (existing.categoryHints ?? Array.Empty<string>())
                            .Concat(e.categoryHints ?? Array.Empty<string>())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                        existing.revitClassHints = (existing.revitClassHints ?? Array.Empty<string>())
                            .Concat(e.revitClassHints ?? Array.Empty<string>())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    }
                    else
                    {
                        byKey[key] = e;
                    }
                }

                if (dupKeys > 0) warnings.Add("Duplicate glossary keys merged: " + dupKeys.ToString(CultureInfo.InvariantCulture));
                if (invalidCats > 0) warnings.Add("Invalid categoryHints dropped: " + invalidCats.ToString(CultureInfo.InvariantCulture));

                // Build patterns (length-weighted; ignore very noisy ASCII 1-char phrases except units).
                var patterns = new List<Pattern>(4096);
                foreach (var e in byKey.Values)
                {
                    foreach (var raw in (e.ja ?? Array.Empty<string>()))
                    {
                        var pr = (raw ?? string.Empty).Trim();
                        if (pr.Length == 0) continue;

                        var pn = Normalize(pr);
                        if (pn.Length == 0) continue;

                        if (IsNoisyAsciiSingleChar(pn) && !string.Equals(e.type, "unit", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var w = ComputeWeight(e.type, pn);
                        patterns.Add(new Pattern(pr, pn, w, e));
                    }
                }

                // Prefer longer phrases first (reduces noise when building explanations).
                idx.Patterns = patterns
                    .OrderByDescending(p => p.PhraseNorm.Length)
                    .ThenBy(p => p.PhraseNorm, StringComparer.Ordinal)
                    .ToList();

                // Build category -> entityKey lookup.
                var catMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in byKey.Values)
                {
                    if (!string.Equals(e.type, "entity", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var c in e.categoryHints ?? Array.Empty<string>())
                    {
                        if (string.IsNullOrWhiteSpace(c)) continue;
                        if (!catMap.TryGetValue(c, out var set)) catMap[c] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        set.Add(e.key);
                    }
                }
                idx.CategoryToEntityKeys = catMap.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

                return idx;
            }

            private static string[] ReadStringArray(JToken? tok)
            {
                try
                {
                    if (tok == null) return Array.Empty<string>();
                    if (tok.Type == JTokenType.Array)
                    {
                        return tok.Values<string>()
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => (x ?? string.Empty).Trim())
                            .Where(x => x.Length > 0)
                            .ToArray();
                    }
                }
                catch { /* ignore */ }
                return Array.Empty<string>();
            }

            private static bool TryValidateBuiltInCategory(string builtInCategoryName)
            {
                try
                {
                    // BuiltInCategory names are enum identifiers; accept only exact enum parses.
                    BuiltInCategory _ = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), builtInCategoryName, ignoreCase: false);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static bool IsNoisyAsciiSingleChar(string s)
            {
                if (string.IsNullOrEmpty(s) || s.Length != 1) return false;
                var ch = s[0];
                return ch <= 0x7F && char.IsLetterOrDigit(ch);
            }

            private static double ComputeWeight(string type, string phraseNorm)
            {
                var t = (type ?? string.Empty).Trim().ToLowerInvariant();
                int len = phraseNorm != null ? phraseNorm.Length : 0;

                double baseW;
                switch (t)
                {
                    case "action": baseW = 1.2; break;
                    case "entity": baseW = 1.0; break;
                    case "concept": baseW = 1.1; break;
                    case "param_value": baseW = 0.9; break;
                    case "modifier": baseW = 0.8; break;
                    case "unit": baseW = 0.6; break;
                    default: baseW = 0.7; break;
                }

                double lenW = 1.0;
                if (len <= 0) lenW = 0.0;
                else if (len == 1) lenW = 0.15;
                else if (len == 2) lenW = 0.35;
                else if (len == 3) lenW = 0.6;
                else if (len >= 8) lenW = 1.25;

                return baseW * lenW;
            }
        }
    }
}
