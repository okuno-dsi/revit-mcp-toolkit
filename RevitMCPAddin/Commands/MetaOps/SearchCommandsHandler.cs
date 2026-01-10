// ================================================================
// File: RevitMCPAddin/Commands/MetaOps/SearchCommandsHandler.cs
// Desc: JSON-RPC "search_commands" / "help.search_commands"
//       CommandMetadataRegistry を使ってコマンド検索（外部 lex ファイル依存なし）
// Target: .NET Framework 4.8 / C# 8.0
// Notes: 依存を最小化（Microsoft.Extensions.* 不要 / ILogger<> 不要）
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    internal sealed class SearchParams
    {
        // Step 3 spec: query/tags/riskMax/limit
        public string? query { get; set; }
        // Backward compat
        public string? q { get; set; }

        public string[]? tags { get; set; }

        public int? limit { get; set; }
        public int? top { get; set; }
        public string? category { get; set; }
        public string? kind { get; set; }           // read|write
        public string? importance { get; set; }     // high|normal|low
        public string? riskMax { get; set; }        // low|medium|high
        public bool? prefixOnly { get; set; }
        public bool? includeDeprecated { get; set; } // if true, include deprecated alias commands as separate items
    }

    [RpcCommand("search_commands",
        Aliases = new[] { "help.search_commands" },
        Category = "MetaOps",
        Tags = new[] { "help", "discovery" },
        Risk = RiskLevel.Low,
        Summary = "Search available commands by keyword (name/category/tags/summary).")]
    public sealed class SearchCommandsHandler : IRevitCommandHandler
    {
        public string CommandName => "search_commands";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (cmd.Params as JObject) != null
                    ? ((JObject)cmd.Params!).ToObject<SearchParams>() ?? new SearchParams()
                    : new SearchParams();

                var query = (p.query ?? p.q ?? string.Empty).Trim();
                var reqTags = (p.tags ?? Array.Empty<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => Normalize(t!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (string.IsNullOrWhiteSpace(query) && (reqTags == null || reqTags.Length == 0))
                    return RpcResultEnvelope.Fail(code: "INVALID_PARAMS", msg: "Missing 'query' (or 'q') and 'tags'. Provide at least one.");

                var all = CommandMetadataRegistry.GetAll();
                if (all == null || all.Count == 0)
                    return RpcResultEnvelope.Fail(code: "NOT_READY", msg: "Command metadata is not available yet.");

                string[] qtokens = Array.Empty<string>();
                if (!string.IsNullOrWhiteSpace(query))
                {
                    string nq = Normalize(query);
                    qtokens = nq.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                }

                // Term map synonyms (data-driven; best-effort). This allows Japanese architectural terms
                // like "断面/平断面/立面/RCP" to rank the correct view commands.
                var termHits = !string.IsNullOrWhiteSpace(query)
                    ? TermMapService.SearchSynonyms(query)
                    : (IReadOnlyDictionary<string, TermMapMatch>)new Dictionary<string, TermMapMatch>(StringComparer.OrdinalIgnoreCase);

                IEnumerable<RpcCommandMeta> pool = all;
                if (!string.IsNullOrWhiteSpace(p.category))
                    pool = pool.Where(r => ContainsIgnoreCase(r.category, p.category!));
                if (!string.IsNullOrWhiteSpace(p.kind))
                    pool = pool.Where(r => string.Equals(r.kind, p.kind, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(p.importance))
                    pool = pool.Where(r => string.Equals(r.importance, p.importance, StringComparison.OrdinalIgnoreCase));
                var riskMax = !string.IsNullOrWhiteSpace(p.riskMax) ? NormalizeRisk(p.riskMax) : null;

                bool includeDeprecated = p.includeDeprecated == true;
                if (includeDeprecated)
                {
                    // Expand legacy/alias names as separate "deprecated" entries (non-breaking opt-in).
                    // This keeps default results canonical-only, while allowing explicit visibility of legacy names.
                    var expanded = new List<RpcCommandMeta>(all.Count * 2);
                    foreach (var meta in pool)
                    {
                        if (meta == null) continue;
                        expanded.Add(meta);
                        foreach (var a in (meta.aliases ?? Array.Empty<string>()))
                        {
                            var aa = (a ?? string.Empty).Trim();
                            if (aa.Length == 0) continue;
                            expanded.Add(new RpcCommandMeta
                            {
                                name = aa,
                                aliases = new[] { meta.name },
                                category = meta.category,
                                tags = (meta.tags ?? Array.Empty<string>()).Concat(new[] { "deprecated" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                                kind = meta.kind,
                                importance = meta.importance,
                                risk = meta.risk,
                                summary = meta.summary,
                                exampleJsonRpc = meta.exampleJsonRpc,
                                requires = meta.requires,
                                constraints = meta.constraints,
                                handlerType = meta.handlerType,
                                handlerNamespace = meta.handlerNamespace
                            });
                        }
                    }
                    pool = expanded;
                }

                bool prefixOnly = p.prefixOnly == true;
                var scored = new List<ScoredItem>(256);
                foreach (var r in pool)
                {
                    double s = Score(r, qtokens, reqTags, prefixOnly, riskMax);
                    termHits.TryGetValue(r.name, out var tm);
                    if (tm != null) s += tm.score;

                    if (s > 0.0)
                        scored.Add(new ScoredItem { score = s, meta = r, termMatch = tm });
                }

                int top = Math.Max(1, Math.Min(200, p.limit ?? p.top ?? 10));
                var items = scored
                    .OrderByDescending(x => x.score)
                    .ThenBy(x => x.meta.name, StringComparer.OrdinalIgnoreCase)
                    .Take(top)
                    .Select(x =>
                    {
                        var jo = new JObject
                        {
                            ["name"] = x.meta.name,
                            ["score"] = Math.Round(Score01(x.score), 3),
                            ["scoreRaw"] = Math.Round(x.score, 3),
                            ["summary"] = x.meta.summary ?? "",
                            ["risk"] = x.meta.risk,
                            ["tags"] = new JArray((x.meta.tags ?? Array.Empty<string>()).ToArray()),

                            // extra fields (non-breaking; useful for agents)
                            ["category"] = x.meta.category,
                            ["kind"] = x.meta.kind,
                            ["importance"] = x.meta.importance,
                            ["aliases"] = new JArray((x.meta.aliases ?? Array.Empty<string>()).ToArray()),
                            ["deprecated"] = (x.meta.tags ?? Array.Empty<string>()).Any(t => string.Equals(t, "deprecated", StringComparison.OrdinalIgnoreCase))
                        };

                        if (x.termMatch != null)
                        {
                            jo["termScore"] = Math.Round(x.termMatch.score, 3);
                            jo["matched"] = new JArray((x.termMatch.matched ?? Array.Empty<string>()).ToArray());
                            if (!string.IsNullOrWhiteSpace(x.termMatch.hint)) jo["hint"] = x.termMatch.hint;
                            if (x.termMatch.suggestedParams != null) jo["suggestedParams"] = x.termMatch.suggestedParams;
                        }
                        return jo;
                    })
                    .ToList();

                var data = new JObject
                {
                    ["items"] = new JArray(items.ToArray()),
                    ["termMap"] = TermMapService.GetStatus().ok
                        ? TermMapService.BuildTerminologyContextBlock(maxDefaults: 4, maxRules: 4)
                        : JObject.FromObject(TermMapService.GetStatus())
                };

                // Step 3 spec shape (+ backward compatible keys)
                return new JObject
                {
                    ["ok"] = true,
                    ["code"] = "OK",
                    ["msg"] = "Top matches",
                    ["data"] = data,

                    // backward compat keys
                    ["total"] = scored.Count,
                    ["items"] = data["items"],
                    ["source"] = "registry",
                    ["query"] = query,
                    ["tags"] = new JArray(reqTags)
                };
            }
            catch (Exception ex)
            {
                RevitLogger.Error("search_commands failed: " + ex);
                return RpcResultEnvelope.Fail(code: "INTERNAL_ERROR", msg: ex.Message);
            }
        }

        private static double Score(RpcCommandMeta meta, string[] qtokens, string[] reqTags, bool prefixOnly, string? riskMax)
        {
            if (meta == null) return 0.0;

            var nameLower = (meta.name ?? "").ToLowerInvariant();
            var catLower = (meta.category ?? "").ToLowerInvariant();
            var sumLower = (meta.summary ?? "").ToLowerInvariant();
            var tagsLower = (meta.tags ?? Array.Empty<string>()).Select(t => (t ?? "").ToLowerInvariant()).ToArray();
            var aliasLower = (meta.aliases ?? Array.Empty<string>()).Select(a => (a ?? "").ToLowerInvariant()).ToArray();
            var nameTokens = nameLower.Split(new[] { '_', '.', '-', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var aliasTokens = aliasLower
                .SelectMany(a => a.Split(new[] { '_', '.', '-', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            double s = 0.0;

            // Query token overlap
            if (qtokens != null && qtokens.Length > 0)
            {
                for (int i = 0; i < qtokens.Length; i++)
                {
                    var q = (qtokens[i] ?? "").Trim().ToLowerInvariant();
                    if (q.Length == 0) continue;

                    bool exactTag = tagsLower.Contains(q);
                    bool partialTag = tagsLower.Any(t => TokenMatch(t, q, prefixOnly));
                    if (exactTag) s += 3.0;
                    else if (partialTag) s += 1.0;

                    bool exactNameTok = nameTokens.Contains(q);
                    bool partialNameTok = nameTokens.Any(t => TokenMatch(t, q, prefixOnly));
                    if (exactNameTok) s += 3.0;
                    else if (partialNameTok) s += 1.0;

                    bool exactAliasTok = aliasTokens.Contains(q);
                    bool partialAliasTok = aliasTokens.Any(t => TokenMatch(t, q, prefixOnly));
                    if (exactAliasTok) s += 2.0;
                    else if (partialAliasTok) s += 0.75;

                    if (TokenMatch(nameLower, q, prefixOnly)) s += 2.0;
                    if (TokenMatch(catLower, q, prefixOnly)) s += 0.5;
                    if (TokenMatch(sumLower, q, prefixOnly)) s += 0.5;
                    if (aliasLower.Any(a => TokenMatch(a, q, prefixOnly))) s += 0.5;
                }
            }

            // Requested tags boost (Step 3 heuristic)
            if (reqTags != null && reqTags.Length > 0)
            {
                int matchCount = 0;
                for (int i = 0; i < reqTags.Length; i++)
                {
                    var rt = (reqTags[i] ?? "").Trim().ToLowerInvariant();
                    if (rt.Length == 0) continue;
                    if (tagsLower.Contains(rt) || tagsLower.Any(t => TokenMatch(t, rt, prefixOnly)))
                        matchCount++;
                }

                if (matchCount == reqTags.Length) s += 6.0;
                else if (matchCount > 0) s += matchCount * 2.0;
            }

            // Risk penalty when exceeding riskMax (Step 3 heuristic: penalize, don't hard-filter)
            if (!string.IsNullOrWhiteSpace(riskMax))
            {
                int mr = RiskRank(meta.risk);
                int mx = RiskRank(riskMax);
                if (mr > mx)
                {
                    int diff = mr - mx;
                    if (diff == 1) s *= 0.6;
                    else s *= 0.3;
                }
            }

            if (string.Equals(meta.risk, "low", StringComparison.OrdinalIgnoreCase)) s *= 1.02;
            if (string.Equals(meta.kind, "read", StringComparison.OrdinalIgnoreCase)) s *= 1.01;
            return s;
        }

        private static string Normalize(string s)
        {
            string lower = (s ?? "").Trim().ToLowerInvariant();
            string one = System.Text.RegularExpressions.Regex.Replace(lower, @"\\s+", " ");
            return one.Trim();
        }

        // Convert raw score to [0..1] for agent-friendly ranking.
        private static double Score01(double raw)
        {
            if (raw <= 0.0) return 0.0;
            // tuned: raw 10 -> ~0.71, raw 20 -> ~0.92
            var v = 1.0 - Math.Exp(-raw / 8.0);
            if (v < 0.0) return 0.0;
            if (v > 1.0) return 1.0;
            return v;
        }

        private static bool ContainsIgnoreCase(string hay, string needle)
        {
            if (hay == null) return false;
            if (needle == null) return true;
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TokenMatch(string token, string q, bool prefixOnly)
        {
            if (prefixOnly)
                return token.StartsWith(q, StringComparison.OrdinalIgnoreCase);
            return token.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeRisk(string s)
        {
            var v = (s ?? "").Trim().ToLowerInvariant();
            if (v == "high") return "high";
            if (v == "medium") return "medium";
            return "low";
        }

        private static int CompareRisk(string a, string b)
        {
            int ra = RiskRank(NormalizeRisk(a));
            int rb = RiskRank(NormalizeRisk(b));
            return ra.CompareTo(rb);
        }

        private static int RiskRank(string r)
        {
            switch ((r ?? "").Trim().ToLowerInvariant())
            {
                case "high": return 2;
                case "medium": return 1;
                default: return 0;
            }
        }

        private sealed class ScoredItem
        {
            public double score;
            public RpcCommandMeta meta = null!;
            public TermMapMatch? termMatch;
        }
    }
}
