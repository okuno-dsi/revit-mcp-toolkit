#nullable enable
// ================================================================
// File   : Core/TermMapService.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Data-driven terminology/synonym → command routing hints.
// Notes  :
//  - Loads term_map_ja.json once (best-effort) and caches an inverted index.
//  - Used by help.search_commands / help.describe_command / help.get_context / agent_bootstrap.
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal sealed class TermMapLoadStatus
    {
        public bool ok { get; set; }
        public string? code { get; set; }
        public string? msg { get; set; }
        public string? path { get; set; }
        public string? locale { get; set; }
        public string? updated { get; set; }
        public string? term_map_version { get; set; }
    }

    internal sealed class TermMapMatch
    {
        public double score { get; set; }
        public string[] matched { get; set; } = Array.Empty<string>();
        public string? hint { get; set; }
        public JObject? suggestedParams { get; set; }
    }

    internal sealed class TermMapCommandLexicon
    {
        public string[] synonyms { get; set; } = Array.Empty<string>();
        public string[] negative_terms { get; set; } = Array.Empty<string>();
        public string[] sources { get; set; } = Array.Empty<string>();
    }

    internal static class TermMapService
    {
        private const string EnvTermMapPath = "REVITMCP_TERM_MAP_JA_PATH";
        private const string DefaultFileName = "term_map_ja.json";

        private static readonly object _gate = new object();
        private static bool _loaded;
        private static TermMapIndex? _index;
        private static TermMapLoadStatus _status = new TermMapLoadStatus { ok = false, code = "NOT_LOADED", msg = "Term map not loaded." };

        public static TermMapLoadStatus GetStatus()
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                return new TermMapLoadStatus
                {
                    ok = _status.ok,
                    code = _status.code,
                    msg = _status.msg,
                    path = _status.path,
                    locale = _status.locale,
                    updated = _status.updated,
                    term_map_version = _status.term_map_version
                };
            }
        }

        public static JObject BuildTerminologyContextBlock(int maxDefaults = 5, int maxRules = 5)
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                if (!_loaded || _index == null)
                {
                    var st = GetStatus();
                    return new JObject
                    {
                        ["ok"] = false,
                        ["code"] = st.code ?? "NOT_READY",
                        ["msg"] = st.msg ?? "Term map not available.",
                        ["term_map_version"] = st.term_map_version,
                        ["path"] = st.path
                    };
                }

                return new JObject
                {
                    ["ok"] = true,
                    ["term_map_version"] = _index.VersionShort,
                    ["path"] = _index.Path,
                    ["locale"] = _index.Locale,
                    ["updated"] = _index.Updated,
                    ["defaults"] = new JArray((_index.DefaultsSummary ?? Array.Empty<string>()).Take(Math.Max(0, maxDefaults)).ToArray()),
                    ["disambiguation"] = new JArray((_index.DisambiguationSummary ?? Array.Empty<string>()).Take(Math.Max(0, maxRules)).ToArray())
                };
            }
        }

        public static IReadOnlyDictionary<string, TermMapMatch> SearchSynonyms(string query)
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                if (!_loaded || _index == null) return new Dictionary<string, TermMapMatch>(StringComparer.OrdinalIgnoreCase);
                return _index.Search(query);
            }
        }

        public static bool TryGetCommandLexicon(string commandName, out TermMapCommandLexicon lexicon)
        {
            lexicon = null!;
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                if (!_loaded || _index == null) return false;
                return _index.TryGetLexicon(commandName, out lexicon!);
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
                    var path = ResolveTermMapPath();
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        _status = new TermMapLoadStatus
                        {
                            ok = false,
                            code = "TERM_MAP_NOT_FOUND",
                            msg = "term_map_ja.json not found. Place it in %LOCALAPPDATA%\\RevitMCP, %USERPROFILE%\\Documents\\Codex\\Design, or the add-in folder.",
                            path = path
                        };
                        _loaded = false;
                        _index = null;
                        return;
                    }

                    var bytes = File.ReadAllBytes(path);
                    var json = Encoding.UTF8.GetString(bytes);
                    if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);

                    var root = JObject.Parse(json);
                    var sha8 = Sha256Hex(bytes);
                    _index = TermMapIndex.Build(root, path, sha8);
                    _loaded = true;
                    _status = new TermMapLoadStatus
                    {
                        ok = true,
                        code = "OK",
                        msg = "Term map loaded.",
                        path = path,
                        locale = _index.Locale,
                        updated = _index.Updated,
                        term_map_version = _index.VersionShort
                    };
                }
                catch (Exception ex)
                {
                    _loaded = false;
                    _index = null;
                    _status = new TermMapLoadStatus
                    {
                        ok = false,
                        code = "TERM_MAP_LOAD_FAILED",
                        msg = ex.Message
                    };
                }
            }
        }

        private static string? ResolveTermMapPath()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable(EnvTermMapPath);
                if (!string.IsNullOrWhiteSpace(env) && File.Exists(env!)) return env;
            }
            catch { /* ignore */ }

            try
            {
                var p1 = System.IO.Path.Combine(Paths.LocalRoot, DefaultFileName);
                if (File.Exists(p1)) return p1;
            }
            catch { /* ignore */ }

            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    var p2 = System.IO.Path.Combine(docs, "Codex", "Design", DefaultFileName);
                    if (File.Exists(p2)) return p2;
                }
            }
            catch { /* ignore */ }

            try
            {
                var p3 = System.IO.Path.Combine(Paths.AddinFolder, "Resources", DefaultFileName);
                if (File.Exists(p3)) return p3;
            }
            catch { /* ignore */ }

            try
            {
                var p4 = System.IO.Path.Combine(Paths.AddinFolder, DefaultFileName);
                if (File.Exists(p4)) return p4;
            }
            catch { /* ignore */ }

            // Dev convenience: walk up and look for "<ancestor>\\Codex\\Design\\term_map_ja.json"
            // (useful when the add-in project sits next to the Codex workspace).
            try
            {
                var cur = Paths.AddinFolder;
                for (int i = 0; i < 8; i++)
                {
                    var parent = System.IO.Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(parent)) break;
                    var cand = System.IO.Path.Combine(parent, "Codex", "Design", DefaultFileName);
                    if (File.Exists(cand)) return cand;
                    cur = parent;
                }
            }
            catch { /* ignore */ }

            return null;
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

        private sealed class TermMapIndex
        {
            public string Path { get; private set; } = string.Empty;
            public string VersionShort { get; private set; } = string.Empty;
            public string Locale { get; private set; } = "ja-JP";
            public string Updated { get; private set; } = string.Empty;

            public string[] DefaultsSummary { get; private set; } = Array.Empty<string>();
            public string[] DisambiguationSummary { get; private set; } = Array.Empty<string>();

            private readonly List<Entry> _entries = new List<Entry>(512);
            private readonly List<Rule> _rules = new List<Rule>(16);
            private readonly Dictionary<string, string> _intentToCommand = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, JObject> _intentHints = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, LexBuilder> _lex = new Dictionary<string, LexBuilder>(StringComparer.OrdinalIgnoreCase);

            public static TermMapIndex Build(JObject root, string path, string sha256Hex)
            {
                var idx = new TermMapIndex();
                idx.Path = path;
                idx.VersionShort = (sha256Hex ?? "").Length >= 8 ? sha256Hex.Substring(0, 8) : (sha256Hex ?? "");
                idx.Locale = (root.Value<string>("locale") ?? "ja-JP").Trim();
                idx.Updated = (root.Value<string>("updated") ?? "").Trim();

                idx.LoadViews(root["views"] as JObject);
                idx.LoadEntities(root["entities"] as JObject);
                idx.LoadSpecialIntents(root["special_intents"] as JObject);
                idx.LoadRules(root["disambiguation_rules"] as JArray);
                idx.BuildSummaries(root);
                idx.FinalizeLexicons();
                return idx;
            }

            public bool TryGetLexicon(string commandName, out TermMapCommandLexicon lexicon)
            {
                lexicon = null!;
                if (string.IsNullOrWhiteSpace(commandName)) return false;
                if (!_lex.TryGetValue(commandName.Trim(), out var b)) return false;
                lexicon = b.ToLexicon();
                return true;
            }

            public IReadOnlyDictionary<string, TermMapMatch> Search(string query)
            {
                var q = Normalize(query);
                if (string.IsNullOrWhiteSpace(q)) return new Dictionary<string, TermMapMatch>(StringComparer.OrdinalIgnoreCase);

                var acc = new Dictionary<string, Acc>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    if (q.IndexOf(e.term, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!acc.TryGetValue(e.command, out var a))
                    {
                        a = new Acc();
                        acc[e.command] = a;
                    }
                    a.score += e.weight;
                    a.matched.Add(e.reason + ":" + e.original);
                    if (!string.IsNullOrWhiteSpace(e.hint)) a.hints.Add(e.hint!);
                    if (e.suggestedParams != null && a.suggestedParams == null) a.suggestedParams = e.suggestedParams;
                }

                for (int i = 0; i < _rules.Count; i++)
                {
                    var r = _rules[i];
                    if (!r.IsTriggered(q)) continue;
                    var intent = r.GetChosenIntent(q);
                    if (string.IsNullOrWhiteSpace(intent)) continue;
                    var cmd = ResolveIntentCommand(intent!);
                    if (string.IsNullOrWhiteSpace(cmd)) continue;

                    if (!acc.TryGetValue(cmd!, out var a))
                    {
                        a = new Acc();
                        acc[cmd!] = a;
                    }
                    a.score += 8.0;
                    a.matched.Add("rule:" + r.id + ":" + intent);
                    a.hints.Add(r.SummaryShort);
                }

                var res = new Dictionary<string, TermMapMatch>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in acc)
                {
                    res[kv.Key] = new TermMapMatch
                    {
                        score = kv.Value.score,
                        matched = kv.Value.matched.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
                        hint = kv.Value.hints.Count > 0 ? string.Join(" | ", kv.Value.hints.Distinct(StringComparer.OrdinalIgnoreCase).Take(2)) : null,
                        suggestedParams = kv.Value.suggestedParams
                    };
                }
                return res;
            }

            // ------------------------- loaders -------------------------

            private void LoadViews(JObject? views)
            {
                if (views == null) return;
                foreach (var p in views.Properties())
                {
                    var intent = (p.Name ?? "").Trim();
                    var v = p.Value as JObject;
                    if (v == null || string.IsNullOrWhiteSpace(intent)) continue;

                    var cmd = (v.Value<string>("command") ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        _intentToCommand[intent] = cmd;
                        if (v["hints"] is JObject hints) _intentHints[intent] = hints;
                    }

                    var terms = GetStrings(v["terms"] as JArray);
                    var neg = GetStrings(v["negative_terms"] as JArray);

                    var hint = ViewHint(intent);
                    var suggestedParams = TryBuildSuggestedParams(intent);

                    for (int i = 0; i < terms.Count; i++)
                    {
                        AddEntry(terms[i], cmd, +50.0, "view." + intent, hint, suggestedParams);
                        AddLexSyn(cmd, terms[i], "view:" + intent);
                    }
                    for (int i = 0; i < neg.Count; i++)
                    {
                        AddEntry(neg[i], cmd, -40.0, "view.neg." + intent, null, null);
                        AddLexNeg(cmd, neg[i], "view:" + intent);
                    }
                }

                // Design spec: pseudo intent "PLAN"
                if (!_intentToCommand.ContainsKey("PLAN")) _intentToCommand["PLAN"] = "create_view_plan";
            }

            private void LoadEntities(JObject? entities)
            {
                if (entities == null) return;
                foreach (var p in entities.Properties())
                {
                    var entityKey = (p.Name ?? "").Trim();
                    var e = p.Value as JObject;
                    if (e == null || string.IsNullOrWhiteSpace(entityKey)) continue;

                    var terms = GetStrings(e["terms"] as JArray);
                    var commands = e["commands"] as JObject;
                    if (terms.Count == 0 || commands == null) continue;

                    foreach (var cp in commands.Properties())
                    {
                        var op = (cp.Name ?? "").Trim();
                        var cmd = (cp.Value.Type == JTokenType.String) ? ((string)cp.Value).Trim() : "";
                        if (string.IsNullOrWhiteSpace(cmd)) continue;

                        var w = WeightForEntityOp(op);
                        for (int i = 0; i < terms.Count; i++)
                        {
                            AddEntry(terms[i], cmd, w, "entity." + entityKey + "." + op, null, null);
                            AddLexSyn(cmd, terms[i], "entity:" + entityKey);
                        }
                    }
                }
            }

            private void LoadSpecialIntents(JObject? intents)
            {
                if (intents == null) return;
                foreach (var p in intents.Properties())
                {
                    var intentKey = (p.Name ?? "").Trim();
                    var si = p.Value as JObject;
                    if (si == null || string.IsNullOrWhiteSpace(intentKey)) continue;

                    var terms = GetStrings(si["terms"] as JArray);
                    var cmds = GetStrings(si["commands"] as JArray);
                    if (terms.Count == 0 || cmds.Count == 0) continue;

                    for (int c = 0; c < cmds.Count; c++)
                    {
                        var cmd = cmds[c];
                        for (int i = 0; i < terms.Count; i++)
                        {
                            AddEntry(terms[i], cmd, 35.0, "intent." + intentKey, null, null);
                            AddLexSyn(cmd, terms[i], "intent:" + intentKey);
                        }
                    }
                }
            }

            private void LoadRules(JArray? rules)
            {
                if (rules == null) return;
                foreach (var t in rules)
                {
                    var r = t as JObject;
                    if (r == null) continue;

                    var id = (r.Value<string>("id") ?? "").Trim();
                    var when = GetStrings(r["when_contains_any"] as JArray);
                    if (string.IsNullOrWhiteSpace(id) || when.Count == 0) continue;

                    _rules.Add(new Rule(
                        id: id,
                        when: when,
                        prefer: (r.Value<string>("prefer") ?? "").Trim(),
                        but: GetStrings(r["but_if_contains_any"] as JArray),
                        preferOverride: (r.Value<string>("prefer_override") ?? "").Trim(),
                        note: (r.Value<string>("note") ?? "").Trim()
                    ));
                }
            }

            private void BuildSummaries(JObject root)
            {
                var defaults = new List<string>();
                var dw = root["defaults"]?["word"] as JObject;
                if (dw != null)
                {
                    foreach (var p in dw.Properties())
                    {
                        var term = (p.Name ?? "").Trim();
                        var intent = (p.Value.Type == JTokenType.String) ? ((string)p.Value).Trim() : "";
                        if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(intent)) continue;
                        var cmd = ResolveIntentCommand(intent);
                        defaults.Add(!string.IsNullOrWhiteSpace(cmd) ? (term + " => " + intent + " (" + cmd + ")") : (term + " => " + intent));
                    }
                }
                DefaultsSummary = defaults.Take(10).ToArray();
                DisambiguationSummary = _rules.Select(r => r.SummaryShort).Where(s => !string.IsNullOrWhiteSpace(s)).Take(10).ToArray();
            }

            private void FinalizeLexicons()
            {
                foreach (var kv in _lex) kv.Value.Freeze();
            }

            // ------------------------- helpers -------------------------

            private sealed class Entry
            {
                public string term = string.Empty;         // normalized
                public string original = string.Empty;     // as-is
                public string command = string.Empty;
                public double weight = 0.0;
                public string reason = string.Empty;
                public string? hint;
                public JObject? suggestedParams;
            }

            private sealed class Acc
            {
                public double score = 0.0;
                public HashSet<string> matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                public HashSet<string> hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                public JObject? suggestedParams;
            }

            private sealed class LexBuilder
            {
                private readonly HashSet<string> _syn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                private readonly HashSet<string> _neg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                private readonly HashSet<string> _src = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                private TermMapCommandLexicon? _frozen;

                public void AddSyn(string s) { if (!string.IsNullOrWhiteSpace(s)) _syn.Add(s.Trim()); }
                public void AddNeg(string s) { if (!string.IsNullOrWhiteSpace(s)) _neg.Add(s.Trim()); }
                public void AddSource(string s) { if (!string.IsNullOrWhiteSpace(s)) _src.Add(s.Trim()); }

                public void Freeze()
                {
                    _frozen = new TermMapCommandLexicon
                    {
                        synonyms = _syn.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                        negative_terms = _neg.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                        sources = _src.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                    };
                }

                public TermMapCommandLexicon ToLexicon()
                {
                    return _frozen ?? new TermMapCommandLexicon();
                }
            }

            private sealed class Rule
            {
                public readonly string id;
                private readonly List<string> _when;
                private readonly List<string> _but;
                private readonly string _prefer;
                private readonly string _preferOverride;
                private readonly string _note;

                public Rule(string id, List<string> when, string prefer, List<string> but, string preferOverride, string note)
                {
                    this.id = id;
                    _when = when ?? new List<string>();
                    _but = but ?? new List<string>();
                    _prefer = prefer ?? "";
                    _preferOverride = preferOverride ?? "";
                    _note = note ?? "";
                }

                public bool IsTriggered(string q)
                {
                    for (int i = 0; i < _when.Count; i++)
                    {
                        if (q.IndexOf(_when[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                    return false;
                }

                public string? GetChosenIntent(string q)
                {
                    if (string.IsNullOrWhiteSpace(_prefer)) return null;
                    if (_but.Count > 0 && !string.IsNullOrWhiteSpace(_preferOverride))
                    {
                        for (int i = 0; i < _but.Count; i++)
                        {
                            if (q.IndexOf(_but[i], StringComparison.OrdinalIgnoreCase) >= 0) return _preferOverride;
                        }
                    }
                    return _prefer;
                }

                public string SummaryShort
                {
                    get
                    {
                        var sb = new StringBuilder();
                        sb.Append(id).Append(": prefer ").Append(_prefer);
                        if (_but.Count > 0 && !string.IsNullOrWhiteSpace(_preferOverride))
                        {
                            sb.Append("; override ").Append(_preferOverride).Append(" if ");
                            sb.Append(string.Join("/", _but.Take(4)));
                            if (_but.Count > 4) sb.Append("/…");
                        }
                        if (!string.IsNullOrWhiteSpace(_note)) sb.Append(" (").Append(_note).Append(")");
                        return sb.ToString();
                    }
                }
            }

            private void AddEntry(string term, string command, double weight, string reason, string? hint, JObject? suggestedParams)
            {
                if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(command)) return;
                _entries.Add(new Entry
                {
                    term = Normalize(term),
                    original = term.Trim(),
                    command = command.Trim(),
                    weight = weight,
                    reason = reason,
                    hint = hint,
                    suggestedParams = suggestedParams
                });
            }

            private string? ResolveIntentCommand(string intent)
            {
                if (string.IsNullOrWhiteSpace(intent)) return null;
                return _intentToCommand.TryGetValue(intent.Trim(), out var cmd) ? cmd : null;
            }

            private JObject? TryBuildSuggestedParams(string intent)
            {
                if (string.IsNullOrWhiteSpace(intent)) return null;
                if (_intentHints.TryGetValue(intent.Trim(), out var hints)) return hints.DeepClone() as JObject;
                return null;
            }

            private void AddLexSyn(string cmd, string term, string source)
            {
                if (string.IsNullOrWhiteSpace(cmd) || string.IsNullOrWhiteSpace(term)) return;
                if (!_lex.TryGetValue(cmd, out var b)) { b = new LexBuilder(); _lex[cmd] = b; }
                b.AddSyn(term);
                b.AddSource(source);
            }

            private void AddLexNeg(string cmd, string term, string source)
            {
                if (string.IsNullOrWhiteSpace(cmd) || string.IsNullOrWhiteSpace(term)) return;
                if (!_lex.TryGetValue(cmd, out var b)) { b = new LexBuilder(); _lex[cmd] = b; }
                b.AddNeg(term);
                b.AddSource(source);
            }

            private static double WeightForEntityOp(string op)
            {
                var k = (op ?? "").Trim().ToLowerInvariant();
                switch (k)
                {
                    case "create": return 32.0;
                    case "list": return 28.0;
                    case "types": return 26.0;
                    case "get":
                    case "get_params": return 24.0;
                    case "set_param":
                    case "update":
                    case "change_type": return 22.0;
                    case "move":
                    case "delete": return 20.0;
                    default: return 20.0;
                }
            }

            private static string ViewHint(string intent)
            {
                if (string.Equals(intent, "SECTION_VERTICAL", StringComparison.OrdinalIgnoreCase))
                    return "JP: \"断面\" defaults to vertical section (立断面).";
                if (string.Equals(intent, "PLAN_FLOOR", StringComparison.OrdinalIgnoreCase))
                    return "JP: \"平断面/水平断面\" means plan (平面図).";
                if (string.Equals(intent, "CEILING_PLAN", StringComparison.OrdinalIgnoreCase))
                    return "JP: \"RCP/天井伏図\" means reflected ceiling plan.";
                if (string.Equals(intent, "ELEVATION", StringComparison.OrdinalIgnoreCase))
                    return "JP: \"立面\" is elevation, not section.";
                return string.Empty;
            }

            private static List<string> GetStrings(JArray? arr)
            {
                var list = new List<string>();
                if (arr == null) return list;
                foreach (var t in arr)
                {
                    if (t == null || t.Type != JTokenType.String) continue;
                    var s = ((string)t).Trim();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                return list;
            }
        }

        private static string Normalize(string s)
        {
            var lower = (s ?? "").Trim().ToLowerInvariant();
            if (lower.Length == 0) return string.Empty;
            var sb = new StringBuilder(lower.Length);
            bool prevWs = false;
            for (int i = 0; i < lower.Length; i++)
            {
                var ch = lower[i];
                bool ws = ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
                if (ws)
                {
                    if (!prevWs) sb.Append(' ');
                    prevWs = true;
                }
                else
                {
                    sb.Append(ch);
                    prevWs = false;
                }
            }
            return sb.ToString().Trim();
        }
    }
}
