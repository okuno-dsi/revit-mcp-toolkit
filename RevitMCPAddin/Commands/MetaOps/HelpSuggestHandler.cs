#nullable enable
// ================================================================
// File: RevitMCPAddin/Commands/MetaOps/HelpSuggestHandler.cs
// Desc: JSON-RPC "help.suggest" (deterministic command/recipe suggestion)
// Target: .NET Framework 4.8 / C# 8.0
// Notes:
//  - Never executes other commands; it only returns ranked suggestions.
//  - Uses GlossaryJaService + current Revit context + CommandMetadataRegistry.
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    internal sealed class HelpSuggestParams
    {
        public string? queryJa { get; set; }
        public string? query { get; set; }
        public string? q { get; set; }
        public int? limit { get; set; }
        public bool? safeMode { get; set; }
        public bool? includeContext { get; set; }
    }

    [RpcCommand("help.suggest",
        Category = "MetaOps",
        Tags = new[] { "help", "discovery" },
        Risk = RiskLevel.Low,
        Summary = "Suggest commands/recipes for a Japanese query using glossary + current Revit context.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"help.suggest\", \"params\":{ \"queryJa\":\"部屋にW5を内張り。柱も拾って。既存はスキップ。\", \"limit\":5, \"safeMode\":true } }")]
    public sealed class HelpSuggestHandler : IRevitCommandHandler
    {
        public string CommandName => "help.suggest";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (cmd.Params as JObject) != null
                    ? ((JObject)cmd.Params!).ToObject<HelpSuggestParams>() ?? new HelpSuggestParams()
                    : new HelpSuggestParams();

                var query = (p.queryJa ?? p.query ?? p.q ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(query))
                    return RpcResultEnvelope.Fail(code: "INVALID_PARAMS", msg: "Missing 'queryJa' (or 'query' / 'q').");

                int limit = Math.Max(1, Math.Min(20, p.limit ?? 5));
                bool safeMode = p.safeMode != false; // default true
                bool includeContext = p.includeContext == true;

                var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
                var doc = uidoc != null ? uidoc.Document : null;
                var selIds = uidoc != null ? uidoc.Selection.GetElementIds() : new HashSet<ElementId>();

                var selectionCtx = BuildSelectionContext(doc, selIds);
                var activeViewCtx = BuildActiveViewContext(uidoc);

                // Glossary matching (best-effort)
                var g = GlossaryJaService.Analyze(query, limit: 256);
                var intent = BuildIntent(query, g, selectionCtx, doc);

                // Suggestions: recipes first, then commands
                var merged = BuildRecipeSuggestions(intent, safeMode)
                    .Concat(BuildCommandSuggestions(intent, safeMode))
                    .OrderByDescending(x => x.confidence)
                    .ThenBy(x => x.kind == "recipe" ? 0 : 1)
                    .ThenBy(x => x.idOrMethod, StringComparer.OrdinalIgnoreCase)
                    .Take(limit)
                    .Select(x => x.ToJson())
                    .ToArray();

                var data = new JObject
                {
                    ["queryJa"] = query,
                    ["normalized"] = intent.normalized,
                    ["suggestions"] = new JArray(merged),
                    ["didYouMean"] = intent.didYouMean ?? new JArray(),
                    ["glossary"] = JObject.FromObject(GlossaryJaService.GetStatus())
                };

                if (includeContext)
                {
                    data["context"] = new JObject
                    {
                        ["selection"] = selectionCtx,
                        ["activeView"] = activeViewCtx
                    };
                }

                var res = new JObject
                {
                    ["ok"] = true,
                    ["code"] = "OK",
                    ["msg"] = "Suggestions",
                    ["data"] = data
                };

                // Low-confidence hints (deterministic)
                try
                {
                    var top = merged.Length > 0 ? merged[0].Value<double?>("confidence") ?? 0.0 : 0.0;
                    if (merged.Length == 0)
                    {
                        res["warnings"] = new JArray("No suggestions. Try help.search_commands.");
                        res["nextActions"] = new JArray(
                            new JObject { ["method"] = "help.search_commands", ["reason"] = "Keyword search fallback." }
                        );
                    }
                    else if (top < 0.45)
                    {
                        res["warnings"] = new JArray("Low confidence. Add context (selection/view) or refine the query.");
                        res["nextActions"] = new JArray(
                            new JObject { ["method"] = "help.search_commands", ["reason"] = "Refine discovery with keywords/tags." },
                            new JObject { ["method"] = "help.describe_command", ["reason"] = "Confirm parameters for a candidate command." }
                        );
                    }
                }
                catch { /* ignore */ }

                return res;
            }
            catch (Exception ex)
            {
                RevitLogger.Error("help.suggest failed: " + ex);
                return RpcResultEnvelope.Fail(code: "INTERNAL_ERROR", msg: ex.Message);
            }
        }

        // ------------------------------------------------------------
        // Context helpers
        // ------------------------------------------------------------

        private static JObject BuildSelectionContext(Document? doc, ICollection<ElementId> selIds)
        {
            var ids = new List<int>();
            var cats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (selIds != null)
            {
                foreach (var id in selIds)
                {
                    if (id == null) continue;
                    var intId = id.IntValue();
                    if (intId > 0) ids.Add(intId);

                    try
                    {
                        if (doc == null) continue;
                        var e = doc.GetElement(id);
                        var cat = e != null ? e.Category : null;
                        if (cat == null) continue;
                        var bicName = TryGetBuiltInCategoryName(cat.Id);
                        if (string.IsNullOrWhiteSpace(bicName)) continue;
                        cats.Add(bicName!);
                        foreach (var k in GlossaryJaService.LookupEntityKeysByCategory(bicName!))
                            entityKeys.Add(k);
                    }
                    catch { /* ignore */ }
                }
            }

            return new JObject
            {
                ["count"] = ids.Count,
                ["elementIds"] = new JArray(ids.ToArray()),
                ["categories"] = new JArray(cats.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()),
                ["entityKeys"] = new JArray(entityKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray())
            };
        }

        private static JObject BuildActiveViewContext(UIDocument? uidoc)
        {
            try
            {
                var v = uidoc != null ? uidoc.ActiveView : null;
                if (v == null) return new JObject { ["ok"] = false };

                return new JObject
                {
                    ["ok"] = true,
                    ["id"] = v.Id.IntValue(),
                    ["name"] = v.Name ?? "",
                    ["viewType"] = v.ViewType.ToString(),
                    ["isTemplate"] = v.IsTemplate
                };
            }
            catch
            {
                return new JObject { ["ok"] = false };
            }
        }

        private static string? TryGetBuiltInCategoryName(ElementId? categoryId)
        {
            try
            {
                if (categoryId == null) return null;
                var v = categoryId.IntValue();
                return Enum.GetName(typeof(BuiltInCategory), (BuiltInCategory)v);
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------
        // Intent + suggestion model
        // ------------------------------------------------------------

        private sealed class SuggestIntent
        {
            public JObject normalized { get; set; } = new JObject();
            public HashSet<string> entityKeysFromQuery { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> entityKeysFromSelection { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> conceptKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> actionKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> actionOps { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public JObject paramHints { get; set; } = new JObject();
            public string[] unknownTerms { get; set; } = Array.Empty<string>();
            public string[] boostCommands { get; set; } = Array.Empty<string>();
            public JArray? didYouMean { get; set; }
        }

        private sealed class Suggestion
        {
            public string kind { get; set; } = "command"; // command|recipe
            public string idOrMethod { get; set; } = string.Empty;
            public string? titleJa { get; set; }
            public string? method { get; set; }
            public double confidence { get; set; }
            public List<string> why { get; set; } = new List<string>();
            public bool writesModel { get; set; }
            public bool recommendedDryRun { get; set; }
            public string risk { get; set; } = "low";
            public JObject? proposedParams { get; set; }

            public JObject ToJson()
            {
                var jo = new JObject
                {
                    ["kind"] = kind,
                    ["confidence"] = Math.Round(confidence, 3),
                    ["why"] = new JArray(why.ToArray()),
                    ["safety"] = new JObject
                    {
                        ["writesModel"] = writesModel,
                        ["recommendedDryRun"] = recommendedDryRun,
                        ["risk"] = risk
                    }
                };

                if (kind == "recipe")
                {
                    jo["id"] = idOrMethod;
                    if (!string.IsNullOrWhiteSpace(titleJa)) jo["titleJa"] = titleJa;
                    if (!string.IsNullOrWhiteSpace(method)) jo["method"] = method;
                }
                else
                {
                    jo["method"] = idOrMethod;
                }

                if (proposedParams != null && proposedParams.HasValues)
                    jo["proposedParams"] = proposedParams;

                return jo;
            }
        }

        private static SuggestIntent BuildIntent(string query, GlossaryQueryAnalysis g, JObject selectionCtx, Document? doc)
        {
            var intent = new SuggestIntent();

            // Selection entity keys
            try
            {
                if (selectionCtx["entityKeys"] is JArray arr)
                {
                    foreach (var x in arr.Values<string>())
                    {
                        if (!string.IsNullOrWhiteSpace(x)) intent.entityKeysFromSelection.Add(x!);
                    }
                }
            }
            catch { /* ignore */ }

            var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var concepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var boost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paramHints = new JObject();

            var matchedPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in (g.hits ?? Array.Empty<GlossaryHit>()))
            {
                if (h == null) continue;

                if (string.Equals(h.type, "action", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(h.key))
                {
                    actions.Add(h.key);
                    if (!string.IsNullOrWhiteSpace(h.op)) intent.actionOps.Add(h.op!);
                }
                if (string.Equals(h.type, "entity", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(h.key))
                    entities.Add(h.key);
                if (string.Equals(h.type, "concept", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(h.key))
                {
                    concepts.Add(h.key);
                    foreach (var bc in (h.boostCommands ?? Array.Empty<string>()))
                        if (!string.IsNullOrWhiteSpace(bc)) boost.Add(bc.Trim());
                }

                if (string.Equals(h.type, "param_value", StringComparison.OrdinalIgnoreCase))
                {
                    var prm = (h.param ?? string.Empty).Trim();
                    if (prm.Length > 0 && prm != "*" && h.value != null && paramHints[prm] == null)
                        paramHints[prm] = h.value.DeepClone();
                }

                if (string.Equals(h.type, "modifier", StringComparison.OrdinalIgnoreCase) && h.flags is JObject fl)
                {
                    foreach (var prop in fl.Properties())
                    {
                        var k = (prop.Name ?? string.Empty).Trim();
                        if (k.Length == 0) continue;
                        if (paramHints[k] == null) paramHints[k] = prop.Value.DeepClone();
                    }
                }

                foreach (var m in (h.matched ?? Array.Empty<string>()))
                {
                    var mm = (m ?? string.Empty).Trim();
                    if (mm.Length > 0) matchedPhrases.Add(mm);
                }
            }

            intent.actionKeys = actions;
            intent.entityKeysFromQuery = entities;
            intent.conceptKeys = concepts;
            intent.paramHints = paramHints;
            intent.boostCommands = boost.ToArray();

            intent.unknownTerms = ExtractUnknownAsciiTokens(query, matchedPhrases);
            intent.didYouMean = BuildDidYouMean(doc, intent.unknownTerms);

            intent.normalized = new JObject
            {
                ["actions"] = new JArray(actions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()),
                ["entities"] = new JArray(entities.Union(intent.entityKeysFromSelection).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()),
                ["concepts"] = new JArray(concepts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()),
                ["paramHints"] = paramHints,
                ["unknownTerms"] = new JArray(intent.unknownTerms)
            };

            return intent;
        }

        private static string[] ExtractUnknownAsciiTokens(string query, HashSet<string> matchedPhrases)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();

                var rx = new Regex(@"[A-Za-z0-9_()（）\-]{2,}", RegexOptions.Compiled);
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in rx.Matches(query))
                {
                    var s = (m.Value ?? string.Empty).Trim();
                    if (s.Length < 2) continue;
                    if (Regex.IsMatch(s, @"^\d+$")) continue; // pure numbers

                    bool seen = matchedPhrases.Any(mp => mp.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!seen) found.Add(s);
                }
                return found.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static JArray BuildDidYouMean(Document? doc, string[] unknownTokens)
        {
            var arr = new JArray();
            if (doc == null) return arr;
            if (unknownTokens == null || unknownTokens.Length == 0) return arr;

            foreach (var tok in unknownTokens)
            {
                var t = (tok ?? string.Empty).Trim();
                if (t.Length < 2 || t.Length > 64) continue;

                if (!Regex.IsMatch(t, @"(^[A-Za-z]\d+$)|(\)[A-Za-z]\d+$)", RegexOptions.IgnoreCase) &&
                    t.IndexOf("w", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var needle = NormalizeForContains(t);
                if (needle.Length == 0) continue;

                try
                {
                    var matches = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .Where(wt => NormalizeForContains(wt.Name).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Take(5)
                        .Select(wt => new JObject { ["name"] = wt.Name ?? "", ["id"] = wt.Id.IntValue() })
                        .ToArray();

                    if (matches.Length > 0)
                    {
                        arr.Add(new JObject
                        {
                            ["hintJa"] = "\u201c" + t + "\u201d が壁タイプ名の断片なら、候補の正確なタイプ名を指定してください。",
                            ["expected"] = "newWallTypeNameOrId",
                            ["candidates"] = new JArray(matches)
                        });
                    }
                }
                catch { /* ignore */ }
            }

            return arr;
        }

        private static string NormalizeForContains(string? s)
        {
            var t = (s ?? string.Empty).Trim();
            if (t.Length == 0) return string.Empty;
            t = t.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            t = Regex.Replace(t, @"\s+", "");
            return t;
        }

        private static bool IntentLooksWrite(SuggestIntent intent)
        {
            foreach (var op in intent.actionOps)
            {
                var o = (op ?? string.Empty).Trim().ToLowerInvariant();
                if (o == "create" || o == "delete" || o == "update" || o == "move" || o == "rotate" ||
                    o == "copy" || o == "duplicate" || o == "mirror" || o == "align" || o == "offset" ||
                    o == "override_graphics" || o == "apply_view_template" || o == "crop" || o == "scopebox" ||
                    o == "attach" || o == "detach" || o == "paint" || o == "set" || o == "add" || o == "remove")
                    return true;
            }
            return false;
        }

        private static IEnumerable<Suggestion> BuildRecipeSuggestions(SuggestIntent intent, bool safeMode)
        {
            var list = new List<Suggestion>();

            bool hasRoom = intent.entityKeysFromQuery.Contains("ent_room") || intent.entityKeysFromSelection.Contains("ent_room");
            bool hasFinishOverlay = intent.conceptKeys.Contains("concept_finish_wall_overlay");

            if (hasRoom && hasFinishOverlay)
            {
                var preset = new JObject
                {
                    ["fromSelection"] = true,
                    ["newWallTypeNameOrId"] = "(内壁)W5",
                    ["boundaryLocation"] = "Finish",
                    ["skipExisting"] = true,
                    ["includeBoundaryColumns"] = true
                };

                list.Add(new Suggestion
                {
                    kind = "recipe",
                    idOrMethod = "finish_wall_overlay_room_w5_v1",
                    titleJa = "部屋に仕上げ壁を内張り（柱含む・既存スキップ）",
                    method = "room.apply_finish_wall_type_on_room_boundary",
                    confidence = 0.86,
                    why = new List<string>
                    {
                        "Matched concept: concept_finish_wall_overlay",
                        hasRoom ? "Context: Room" : "Context: unknown"
                    },
                    writesModel = true,
                    recommendedDryRun = safeMode,
                    risk = "medium",
                    proposedParams = preset
                });
            }

            return list;
        }

        private static IEnumerable<Suggestion> BuildCommandSuggestions(SuggestIntent intent, bool safeMode)
        {
            var all = CommandMetadataRegistry.GetAll();
            var list = new List<Suggestion>(256);

            bool intentWrite = IntentLooksWrite(intent);

            var entQueryTokens = intent.entityKeysFromQuery.Select(EntityKeyToToken).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var entSelTokens = intent.entityKeysFromSelection.Select(EntityKeyToToken).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var conceptTokenGroups = intent.conceptKeys.Select(ConceptKeyToTokens).Where(a => a.Length > 0).ToArray();
            var asciiTokens = intent.unknownTerms.Select(NormalizeAsciiToken).Where(t => t.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (var meta in all)
            {
                if (meta == null) continue;

                var s = 0.0;
                var why = new List<string>();
                var methodLower = (meta.name ?? string.Empty).ToLowerInvariant();
                var tagLower = (meta.tags ?? Array.Empty<string>()).Select(x => (x ?? string.Empty).ToLowerInvariant()).ToArray();

                if (intent.boostCommands.Any(bc => string.Equals(bc, meta.name, StringComparison.OrdinalIgnoreCase)))
                {
                    s += 3.5;
                    why.Add("Boosted by glossary concept (boostCommands).");
                }

                foreach (var t in entSelTokens)
                {
                    if (TokenHit(methodLower, tagLower, t))
                    {
                        s += 2.5;
                        why.Add("Selection entity match: " + t);
                    }
                }

                foreach (var t in entQueryTokens)
                {
                    if (TokenHit(methodLower, tagLower, t))
                    {
                        s += 1.4;
                        why.Add("Query entity match: " + t);
                    }
                }

                foreach (var toks in conceptTokenGroups)
                {
                    int hit = toks.Count(t => TokenHit(methodLower, tagLower, t));
                    if (hit <= 0) continue;
                    s += 0.7 * hit;
                    if (hit >= 2) why.Add("Concept match: " + string.Join("_", toks));
                }

                foreach (var t in asciiTokens)
                {
                    if (t.Length == 0) continue;
                    if (methodLower.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        s += 1.0;
                        why.Add("Keyword match: " + t);
                    }
                }

                // Action alignment
                if (string.Equals(meta.kind, "write", StringComparison.OrdinalIgnoreCase))
                {
                    if (intentWrite) { s += 1.0; why.Add("Action implies write."); }
                    else if (safeMode) { s -= 1.0; why.Add("SafeMode down-ranks write commands without clear write intent."); }
                }
                else
                {
                    if (!intentWrite) s += 0.25;
                }

                // Composite boost (heuristic)
                if (IsComposite(methodLower, tagLower))
                {
                    s += 0.6;
                    why.Add("Composite/one-shot heuristic boost.");
                }

                if (s <= 0.0) continue;

                list.Add(new Suggestion
                {
                    kind = "command",
                    idOrMethod = meta.name,
                    confidence = ScoreToConfidence(s),
                    why = why,
                    writesModel = string.Equals(meta.kind, "write", StringComparison.OrdinalIgnoreCase),
                    recommendedDryRun = safeMode && string.Equals(meta.kind, "write", StringComparison.OrdinalIgnoreCase),
                    risk = meta.risk ?? "low",
                    proposedParams = BuildProposedParams(meta, intent)
                });
            }

            return list;
        }

        private static JObject? BuildProposedParams(RpcCommandMeta meta, SuggestIntent intent)
        {
            try
            {
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in (meta.requires ?? Array.Empty<string>()))
                {
                    var rr = (r ?? string.Empty).Trim();
                    if (rr.Length == 0) continue;
                    if (rr.IndexOf('|') >= 0)
                    {
                        foreach (var part in rr.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var pp = (part ?? string.Empty).Trim();
                            if (pp.Length > 0) allowed.Add(pp);
                        }
                    }
                    else allowed.Add(rr);
                }

                try
                {
                    var tok = JToken.Parse(meta.exampleJsonRpc ?? "");
                    var p = tok["params"] as JObject;
                    if (p != null)
                    {
                        foreach (var prop in p.Properties())
                        {
                            var n = (prop.Name ?? string.Empty).Trim();
                            if (n.Length > 0) allowed.Add(n);
                        }
                    }
                }
                catch { /* ignore */ }

                if (allowed.Count == 0) return null;

                var outParams = new JObject();
                foreach (var prop in intent.paramHints.Properties())
                {
                    var k = (prop.Name ?? string.Empty).Trim();
                    if (k.Length == 0) continue;
                    if (!allowed.Contains(k)) continue;
                    outParams[k] = prop.Value.DeepClone();
                }

                if (allowed.Contains("newWallTypeNameOrId") && outParams["newWallTypeNameOrId"] == null)
                {
                    var wallTypeToken = TryExtractLikelyWallTypeToken(intent.unknownTerms, rawQuery: null);
                    if (!string.IsNullOrWhiteSpace(wallTypeToken))
                        outParams["newWallTypeNameOrId"] = wallTypeToken;
                }

                return outParams.HasValues ? outParams : null;
            }
            catch
            {
                return null;
            }
        }

        private static string EntityKeyToToken(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            var k = key.Trim();
            if (k.StartsWith("ent_", StringComparison.OrdinalIgnoreCase))
                k = k.Substring(4);
            k = Regex.Replace(k, @"[^A-Za-z0-9_]+", "");
            return k.ToLowerInvariant();
        }

        private static string[] ConceptKeyToTokens(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return Array.Empty<string>();
            var k = key.Trim();
            if (k.StartsWith("concept_", StringComparison.OrdinalIgnoreCase))
                k = k.Substring("concept_".Length);
            return k.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Select(x => Regex.Replace(x, @"[^A-Za-z0-9]+", "").ToLowerInvariant())
                .Where(x => x.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string NormalizeAsciiToken(string token)
        {
            var t = (token ?? string.Empty).Trim();
            if (t.Length == 0) return string.Empty;
            t = t.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            t = Regex.Replace(t, @"[^a-z0-9_]+", "");
            return t;
        }

        private static bool TokenHit(string methodLower, string[] tagLower, string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            if (methodLower.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return tagLower.Any(t => t.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsComposite(string methodLower, string[] tagLower)
        {
            if (methodLower.IndexOf("batch", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (methodLower.IndexOf("bulk", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (methodLower.IndexOf("apply_", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (methodLower.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (methodLower.IndexOf("ensure", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (methodLower.IndexOf("generate", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return tagLower.Any(t => t == "batch" || t == "composite" || t == "one-shot");
        }

        private static double ScoreToConfidence(double score)
        {
            // score~4 => 0.5, score~6 => 0.88
            try
            {
                var x = score - 4.0;
                var c = 1.0 / (1.0 + Math.Exp(-x));
                if (c < 0.0) return 0.0;
                if (c > 1.0) return 1.0;
                return c;
            }
            catch
            {
                var c = score / 10.0;
                if (c < 0.0) return 0.0;
                if (c > 1.0) return 1.0;
                return c;
            }
        }

        private static string? TryExtractLikelyWallTypeToken(string[] unknownTokens, string? rawQuery)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(rawQuery))
                {
                    var m = Regex.Match(rawQuery!, @"\([^)]*\)[A-Za-z]\d+", RegexOptions.IgnoreCase);
                    if (m.Success) return m.Value.Trim();
                }

                foreach (var t in unknownTokens ?? Array.Empty<string>())
                {
                    var s = (t ?? string.Empty).Trim();
                    if (Regex.IsMatch(s, @"^[A-Za-z]\d+$")) return s;
                    if (Regex.IsMatch(s, @"^\([^)]*\)[A-Za-z]\d+$")) return s;
                }
            }
            catch { /* ignore */ }
            return null;
        }
    }
}
