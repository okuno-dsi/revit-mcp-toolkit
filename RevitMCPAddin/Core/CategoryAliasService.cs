#nullable enable
// ================================================================
// File   : Core/CategoryAliasService.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Resolve ambiguous category text via alias dictionary (JP)
// Notes  : Best-effort; dictionary is optional and loaded from add-in folder.
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    internal sealed class CategoryAliasDictionary
    {
        public string? version { get; set; }
        public string? lang { get; set; }
        public int? schemaVersion { get; set; }
        public string? notes { get; set; }
        public List<CategoryAliasItem> items { get; set; } = new List<CategoryAliasItem>();
    }

    internal sealed class CategoryAliasItem
    {
        public string? id { get; set; }              // e.g. "OST_Grids"
        public string? labelJa { get; set; }         // for UI/messages/logs
        public List<string> aliases { get; set; } = new List<string>();
        public List<string> aliasesWeak { get; set; } = new List<string>();
        public List<string> negative { get; set; } = new List<string>();
        public int priority { get; set; } = 0;
    }

    internal sealed class CategoryResolveContext
    {
        public List<string> selectedCategoryIds { get; set; } = new List<string>();
        public string? disciplineHint { get; set; }
        public string? activeViewType { get; set; }
    }

    internal sealed class CategoryCandidate
    {
        public string? id { get; set; }
        public int? builtInId { get; set; }
        public string? labelJa { get; set; }
        public double score { get; set; }
        public string? reason { get; set; }
    }

    internal sealed class CategoryResolveResult
    {
        public bool ok { get; set; }
        public string? msg { get; set; }
        public string normalizedText { get; set; } = string.Empty;
        public bool recoveredFromMojibake { get; set; }
        public CategoryCandidate? resolved { get; set; }
        public List<CategoryCandidate> candidates { get; set; } = new List<CategoryCandidate>();
    }

    internal sealed class CategoryAliasLoadStatus
    {
        public bool ok { get; set; }
        public string? code { get; set; }
        public string? msg { get; set; }
        public string? path { get; set; }
        public string? version { get; set; }
        public string? lang { get; set; }
        public int? schemaVersion { get; set; }
        public string? sha8 { get; set; }
        public string? updatedUtc { get; set; }
    }

    internal static class CategoryAliasService
    {
        private const string DefaultFileName = "category_alias_ja.json";
        private const double ResolveThreshold = 0.75;
        private const double ResolveGap = 0.15;

        private static readonly object _gate = new object();
        private static bool _loaded;
        private static CategoryAliasDictionary? _dict;
        private static CategoryAliasLoadStatus _status = new CategoryAliasLoadStatus
        {
            ok = false,
            code = "NOT_LOADED",
            msg = "Category alias dictionary not loaded."
        };
        private static DateTime? _lastWriteUtc;
        private static string? _path;

        public static CategoryAliasLoadStatus GetStatus()
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                return new CategoryAliasLoadStatus
                {
                    ok = _status.ok,
                    code = _status.code,
                    msg = _status.msg,
                    path = _status.path,
                    version = _status.version,
                    lang = _status.lang,
                    schemaVersion = _status.schemaVersion,
                    sha8 = _status.sha8,
                    updatedUtc = _status.updatedUtc
                };
            }
        }

        public static bool TryResolveBuiltInCategory(string text, out BuiltInCategory bic)
        {
            bic = BuiltInCategory.INVALID;
            var res = Resolve(text, null, 5);
            if (!res.ok || res.resolved == null || string.IsNullOrWhiteSpace(res.resolved.id)) return false;
            if (Enum.TryParse(res.resolved.id, true, out BuiltInCategory parsed) && parsed != BuiltInCategory.INVALID)
            {
                bic = parsed;
                return true;
            }
            return false;
        }

        public static CategoryResolveResult Resolve(string rawText, CategoryResolveContext? ctx, int maxCandidates)
        {
            EnsureLoadedBestEffort();
            if (!_loaded || _dict == null)
            {
                return new CategoryResolveResult
                {
                    ok = false,
                    msg = _status.msg ?? "Category alias dictionary not available.",
                    normalizedText = NormalizeText(rawText ?? string.Empty),
                    recoveredFromMojibake = false,
                    candidates = new List<CategoryCandidate>()
                };
            }

            ctx = ctx ?? new CategoryResolveContext();
            string normalizedRaw = NormalizeText(rawText ?? string.Empty);
            var recover = TryRecoverMojibake(normalizedRaw);
            string normalized = recover.text;
            bool recovered = recover.recovered;

            var candidates = new List<CategoryCandidate>();
            foreach (var item in _dict.items)
            {
                var cand = ScoreItem(item, normalized, ctx);
                if (cand.score > 0)
                    candidates.Add(cand);
            }

            candidates = candidates
                .OrderByDescending(c => c.score)
                .ThenBy(c => c.id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxCandidates))
                .ToList();

            var result = new CategoryResolveResult
            {
                normalizedText = normalized,
                recoveredFromMojibake = recovered,
                candidates = candidates
            };

            if (candidates.Count == 0)
            {
                result.ok = false;
                result.msg = "No matching category was found.";
                return result;
            }

            var top = candidates[0];
            CategoryCandidate? second = candidates.Count > 1 ? candidates[1] : null;
            bool isResolved = top.score >= ResolveThreshold && (second == null || (top.score - second.score) >= ResolveGap);

            if (isResolved)
            {
                result.ok = true;
                result.resolved = top;
                return result;
            }

            result.ok = false;
            result.msg = "Category is ambiguous. Please choose one of the candidates.";
            return result;
        }

        private static CategoryCandidate ScoreItem(CategoryAliasItem item, string text, CategoryResolveContext ctx)
        {
            var candidate = new CategoryCandidate
            {
                id = item.id,
                builtInId = TryBuiltInId(item.id),
                labelJa = item.labelJa,
                score = 0,
                reason = null
            };

            if (string.IsNullOrWhiteSpace(text)) return candidate;

            var reasonParts = new List<string>();
            double score = 0;

            if (item.negative != null && item.negative.Count > 0)
            {
                foreach (var n in item.negative)
                {
                    var nrm = NormalizeText(n);
                    if (!string.IsNullOrEmpty(nrm) && text.Contains(nrm))
                    {
                        score -= 0.60;
                        reasonParts.Add("negative match");
                        break;
                    }
                }
            }

            double strongScore = ScoreAliasList(item.aliases, text, false, reasonParts);
            double weakScore = ScoreAliasList(item.aliasesWeak, text, true, reasonParts);
            score += Math.Max(strongScore, weakScore);

            if (item.priority > 0)
            {
                score += Math.Min(0.15, Math.Max(0, item.priority) / 1000.0);
                reasonParts.Add("priority");
            }

            if (ctx.selectedCategoryIds != null && ctx.selectedCategoryIds.Contains(item.id ?? string.Empty))
            {
                score += 0.25;
                reasonParts.Add("context selectedCategoryIds");
            }

            if (!string.IsNullOrEmpty(ctx.disciplineHint))
            {
                if (ctx.disciplineHint.Equals("Structure", StringComparison.OrdinalIgnoreCase))
                {
                    if (item.id == "OST_StructuralColumns" || item.id == "OST_StructuralFraming" || item.id == "OST_StructuralFoundation")
                    {
                        score += 0.10;
                        reasonParts.Add("context discipline=Structure");
                    }
                }
            }

            score = Math.Max(0, Math.Min(1, score));
            candidate.score = score;
            candidate.reason = string.Join("; ", reasonParts);
            return candidate;
        }

        private static double ScoreAliasList(List<string> aliases, string text, bool weak, List<string> reasons)
        {
            if (aliases == null || aliases.Count == 0) return 0;
            if (string.IsNullOrWhiteSpace(text)) return 0;

            double exactScore = weak ? 0.55 : 0.80;
            double substringScore = weak ? 0.35 : 0.55;
            double tokenBase = weak ? 0.12 : 0.20;
            double tokenCap = weak ? 0.10 : 0.20;
            string label = weak ? "aliasWeak" : "alias";

            var normAliases = aliases.Select(NormalizeText).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();

            if (normAliases.Any(a => a == text))
            {
                reasons.Add(label + " exact match");
                return exactScore;
            }

            var hit = normAliases.FirstOrDefault(a => a.Length >= 2 && (text.Contains(a) || a.Contains(text)));
            if (!string.IsNullOrEmpty(hit))
            {
                reasons.Add(label + " substring match");
                return substringScore;
            }

            var tokens = Tokenize(text);
            int tokenHitCount = tokens.Count(t => normAliases.Any(a => a.Contains(t)));
            if (tokenHitCount > 0)
            {
                reasons.Add(label + " token match");
                return tokenBase + Math.Min(tokenCap, tokenHitCount * 0.05);
            }

            return 0;
        }

        private static int? TryBuiltInId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (Enum.TryParse(id, true, out BuiltInCategory bic) && bic != BuiltInCategory.INVALID)
                return (int)bic;
            return null;
        }

        private static void EnsureLoadedBestEffort()
        {
            lock (_gate)
            {
                var path = ResolvePath();
                if (string.IsNullOrWhiteSpace(path))
                {
                    _loaded = false;
                    _dict = null;
                    _status = new CategoryAliasLoadStatus
                    {
                        ok = false,
                        code = "PATH_NOT_FOUND",
                        msg = "Unable to resolve add-in folder for category_alias_ja.json.",
                        path = path
                    };
                    return;
                }

                if (!File.Exists(path))
                {
                    _loaded = false;
                    _dict = null;
                    _status = new CategoryAliasLoadStatus
                    {
                        ok = false,
                        code = "DICT_NOT_FOUND",
                        msg = "category_alias_ja.json not found in add-in folder.",
                        path = path
                    };
                    return;
                }

                var lastWriteUtc = File.GetLastWriteTimeUtc(path);
                if (_loaded && _dict != null && _path == path && _lastWriteUtc == lastWriteUtc)
                    return;

                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var json = Encoding.UTF8.GetString(bytes);
                    if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);

                    var dict = JsonConvert.DeserializeObject<CategoryAliasDictionary>(json);
                    if (dict == null || dict.items == null)
                        throw new InvalidOperationException("Invalid category_alias_ja.json");

                    _dict = dict;
                    _loaded = true;
                    _path = path;
                    _lastWriteUtc = lastWriteUtc;

                    var sha8 = Sha256Hex(bytes).Substring(0, 8);
                    _status = new CategoryAliasLoadStatus
                    {
                        ok = true,
                        code = "OK",
                        msg = "Category alias dictionary loaded.",
                        path = path,
                        version = dict.version,
                        lang = dict.lang,
                        schemaVersion = dict.schemaVersion,
                        sha8 = sha8,
                        updatedUtc = lastWriteUtc.ToString("o")
                    };
                }
                catch (Exception ex)
                {
                    _loaded = false;
                    _dict = null;
                    _status = new CategoryAliasLoadStatus
                    {
                        ok = false,
                        code = "DICT_LOAD_FAILED",
                        msg = ex.Message,
                        path = path
                    };
                }
            }
        }

        private static string? ResolvePath()
        {
            try
            {
                var asmPath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(asmPath)) return null;
                var dir = Path.GetDirectoryName(asmPath);
                if (string.IsNullOrWhiteSpace(dir)) return null;
                return Path.Combine(dir, DefaultFileName);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeText(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            s = s.Replace('\u3000', ' ');
            s = s.Trim();
            s = Regex.Replace(s, "\\s+", " ");
            s = s.Normalize(NormalizationForm.FormKC);
            s = s.ToLowerInvariant();
            return s;
        }

        private static List<string> Tokenize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new List<string>();
            return Regex.Split(s, "[\\s,;:/\\\\()\\[\\]{}\\-]+")
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
        }

        private static (string text, bool recovered) TryRecoverMojibake(string normalizedText)
        {
            if (string.IsNullOrEmpty(normalizedText)) return (normalizedText, false);
            if (!LooksGarbled(normalizedText)) return (normalizedText, false);

            var candidates = new List<string>();
            candidates.Add(ReDecode(normalizedText, Encoding.GetEncoding("ISO-8859-1"), Encoding.UTF8));
            candidates.Add(ReDecode(normalizedText, Encoding.GetEncoding(1252), Encoding.UTF8));
            try { candidates.Add(ReDecode(normalizedText, Encoding.GetEncoding(932), Encoding.UTF8)); } catch { /* ignore */ }

            var best = normalizedText;
            var bestScore = JapaneseLikelihoodScore(normalizedText);

            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                var n = NormalizeText(c);
                var sc = JapaneseLikelihoodScore(n);
                if (sc > bestScore)
                {
                    best = n;
                    bestScore = sc;
                }
            }

            if (best != normalizedText)
                return (best, true);

            return (normalizedText, false);
        }

        private static bool LooksGarbled(string s)
        {
            if (s.IndexOf('\uFFFD') >= 0) return true;
            if (s.Contains("ã") || s.Contains("â") || s.Contains("Ã")) return true;
            if (s.Contains("縺") || s.Contains("繧") || s.Contains("繝")) return true;
            return false;
        }

        private static string ReDecode(string s, Encoding assumedCurrent, Encoding target)
        {
            var bytes = assumedCurrent.GetBytes(s);
            return target.GetString(bytes);
        }

        private static double JapaneseLikelihoodScore(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;

            int hira = 0, kata = 0, kanji = 0, ascii = 0, other = 0;
            foreach (var ch in s)
            {
                if (ch <= 0x7F) { ascii++; continue; }
                int uc = (int)ch;
                if (uc >= 0x3040 && uc <= 0x309F) hira++;
                else if (uc >= 0x30A0 && uc <= 0x30FF) kata++;
                else if (uc >= 0x4E00 && uc <= 0x9FFF) kanji++;
                else other++;
            }

            var total = hira + kata + kanji + ascii + other;
            if (total == 0) return 0;

            var jp = (hira + kata + kanji) / (double)total;
            var penalty = Math.Min(0.3, other / (double)total);
            return Math.Max(0, Math.Min(1, jp - penalty));
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
