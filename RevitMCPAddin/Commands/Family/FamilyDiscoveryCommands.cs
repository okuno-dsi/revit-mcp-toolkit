// ================================================================
// File: Commands/Family/FamilyDiscoveryCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: Fast discovery of loaded families/types (loadable, in-place, system).
// Notes  : - Avoids scanning instances; uses type-level collectors.
//          - Category filtering supports Category name or BuiltInCategory string.
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Family
{
    internal enum LoadedFamilyKind
    {
        LoadableSymbol,
        InPlaceFamily,
        SystemType
    }

    internal static class FamilyDiscoveryUtil
    {
        private static readonly Regex TokenSplit = new Regex("[^a-zA-Z0-9]+", RegexOptions.Compiled);

        public static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            var lower = text.Trim().ToLowerInvariant();
            return TokenSplit.Split(lower).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        }

        public static bool TryParseBuiltInCategory(string name, out BuiltInCategory bic)
        {
            bic = BuiltInCategory.INVALID;
            if (string.IsNullOrWhiteSpace(name)) return false;
            return Enum.TryParse(name.Trim(), true, out bic);
        }

        public static bool CategoryMatches(Category cat, string categoryName, string builtInCategory)
        {
            if (!string.IsNullOrWhiteSpace(builtInCategory))
            {
                if (TryParseBuiltInCategory(builtInCategory, out var bic))
                {
                    return cat != null && cat.Id.IntegerValue == (int)bic;
                }
                // Fallback to name match if parsing failed.
                return cat != null && string.Equals(cat.Name, builtInCategory, StringComparison.OrdinalIgnoreCase);
            }
            if (!string.IsNullOrWhiteSpace(categoryName))
                return cat != null && string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        public static bool CategoryInWhitelist(Category cat, HashSet<int> bicIds, HashSet<string> catNames)
        {
            if ((bicIds == null || bicIds.Count == 0) && (catNames == null || catNames.Count == 0)) return true;
            if (cat == null) return false;
            if (bicIds != null && bicIds.Count > 0 && bicIds.Contains(cat.Id.IntegerValue)) return true;
            if (catNames != null && catNames.Count > 0 && catNames.Contains(cat.Name ?? "")) return true;
            return false;
        }

        public static double ScoreByTokens(string haystack, List<string> tokens)
        {
            if (tokens == null || tokens.Count == 0) return 1.0;
            if (string.IsNullOrWhiteSpace(haystack)) return 0.0;
            var lower = haystack.ToLowerInvariant();
            int hits = 0;
            foreach (var t in tokens)
            {
                if (lower.Contains(t)) hits++;
            }
            return (double)hits / (double)tokens.Count;
        }
    }

    [RpcCommand("family.query_loaded",
        Category = "Family",
        Tags = new[] { "Family", "Types", "Discovery" },
        Kind = "read",
        Risk = RiskLevel.Low,
        Summary = "Query loaded families/types (loadable symbols, in-place families, optional system types) with fast filtering.",
        Requires = new string[] { },
        Constraints = new[]
        {
            "Searches loaded definitions (types), not instances.",
            "Category filtering supports Category name or BuiltInCategory string (e.g. OST_Doors).",
            "System types are included only when includeSystemTypes=true."
        })]
    public class QueryLoadedFamiliesCommand : IRevitCommandHandler
    {
        public string CommandName => "family.query_loaded";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = cmd.Params as JObject ?? new JObject();
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("NO_DOC", "アクティブドキュメントがありません。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            string q = p.Value<string>("q") ?? "";
            string category = p.Value<string>("category") ?? "";
            string builtInCategory = p.Value<string>("builtInCategory") ?? "";
            bool includeLoadableSymbols = p.Value<bool?>("includeLoadableSymbols") ?? true;
            bool includeInPlaceFamilies = p.Value<bool?>("includeInPlaceFamilies") ?? true;
            bool includeSystemTypes = p.Value<bool?>("includeSystemTypes") ?? false;
            int limit = p.Value<int?>("limit") ?? 50;
            int offset = p.Value<int?>("offset") ?? 0;
            if (limit <= 0) limit = 50;
            if (offset < 0) offset = 0;

            // System type whitelist (optional)
            HashSet<int> sysBicIds = null;
            HashSet<string> sysCatNames = null;
            if (p["systemTypeCategoryWhitelist"] is JArray wl && wl.Count > 0)
            {
                sysBicIds = new HashSet<int>();
                sysCatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in wl)
                {
                    var s = t?.ToString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (FamilyDiscoveryUtil.TryParseBuiltInCategory(s, out var bic))
                        sysBicIds.Add((int)bic);
                    else
                        sysCatNames.Add(s);
                }
            }

            var tokens = FamilyDiscoveryUtil.Tokenize(q);
            var items = new List<JObject>();

            // Loadable symbols (FamilySymbol)
            if (includeLoadableSymbols)
            {
                var syms = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>();

                foreach (var sym in syms)
                {
                    var fam = sym.Family;
                    if (fam != null && fam.IsInPlace) continue; // handled separately
                    var cat = sym.Category;
                    if (!FamilyDiscoveryUtil.CategoryMatches(cat, category, builtInCategory)) continue;

                    var text = (sym.Family?.Name ?? "") + " " + (sym.Name ?? "") + " " + (cat?.Name ?? "") + " " + (cat != null ? cat.Id.IntegerValue.ToString() : "");
                    var score = FamilyDiscoveryUtil.ScoreByTokens(text, tokens);
                    if (tokens.Count > 0 && score <= 0) continue;

                    var obj = new JObject
                    {
                        ["kind"] = LoadedFamilyKind.LoadableSymbol.ToString(),
                        ["category"] = cat?.Name ?? "",
                        ["builtInCategory"] = cat != null && cat.Id != ElementId.InvalidElementId ? ((BuiltInCategory)cat.Id.IntegerValue).ToString() : "",
                        ["familyId"] = fam != null ? fam.Id.IntegerValue : (int?)null,
                        ["familyName"] = fam?.Name ?? "",
                        ["typeId"] = sym.Id.IntegerValue,
                        ["typeName"] = sym.Name ?? "",
                        ["isInPlace"] = false,
                        ["score"] = Math.Round(score, 4)
                    };
                    items.Add(obj);
                }
            }

            // In-place families
            if (includeInPlaceFamilies)
            {
                var fams = new FilteredElementCollector(doc)
                    .OfClass(typeof(global::Autodesk.Revit.DB.Family))
                    .Cast<global::Autodesk.Revit.DB.Family>()
                    .Where(f => f.IsInPlace);

                foreach (var fam in fams)
                {
                    var cat = fam.FamilyCategory;
                    if (!FamilyDiscoveryUtil.CategoryMatches(cat, category, builtInCategory)) continue;

                    var text = (fam.Name ?? "") + " " + (cat?.Name ?? "") + " " + (cat != null ? cat.Id.IntegerValue.ToString() : "");
                    var score = FamilyDiscoveryUtil.ScoreByTokens(text, tokens);
                    if (tokens.Count > 0 && score <= 0) continue;

                    var obj = new JObject
                    {
                        ["kind"] = LoadedFamilyKind.InPlaceFamily.ToString(),
                        ["category"] = cat?.Name ?? "",
                        ["builtInCategory"] = cat != null && cat.Id != ElementId.InvalidElementId ? ((BuiltInCategory)cat.Id.IntegerValue).ToString() : "",
                        ["familyId"] = fam.Id.IntegerValue,
                        ["familyName"] = fam.Name ?? "",
                        ["typeId"] = null,
                        ["typeName"] = "",
                        ["isInPlace"] = true,
                        ["score"] = Math.Round(score, 4)
                    };
                    items.Add(obj);
                }
            }

            // System types (ElementType excluding FamilySymbol)
            if (includeSystemTypes)
            {
                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .Where(t => !(t is FamilySymbol));

                foreach (var t in types)
                {
                    var cat = t.Category;
                    if (cat == null) continue;
                    if (!FamilyDiscoveryUtil.CategoryMatches(cat, category, builtInCategory)) continue;
                    if (!FamilyDiscoveryUtil.CategoryInWhitelist(cat, sysBicIds, sysCatNames)) continue;

                    var text = (t.Name ?? "") + " " + (cat?.Name ?? "") + " " + (cat != null ? cat.Id.IntegerValue.ToString() : "");
                    var score = FamilyDiscoveryUtil.ScoreByTokens(text, tokens);
                    if (tokens.Count > 0 && score <= 0) continue;

                    var obj = new JObject
                    {
                        ["kind"] = LoadedFamilyKind.SystemType.ToString(),
                        ["category"] = cat?.Name ?? "",
                        ["builtInCategory"] = cat.Id != ElementId.InvalidElementId ? ((BuiltInCategory)cat.Id.IntegerValue).ToString() : "",
                        ["familyId"] = null,
                        ["familyName"] = "",
                        ["typeId"] = t.Id.IntegerValue,
                        ["typeName"] = t.Name ?? "",
                        ["isInPlace"] = false,
                        ["score"] = Math.Round(score, 4)
                    };
                    items.Add(obj);
                }
            }

            // Order and page
            var ordered = items
                .OrderByDescending(x => (double)x["score"])
                .ThenBy(x => x.Value<string>("category") ?? "")
                .ThenBy(x => x.Value<string>("familyName") ?? "")
                .ThenBy(x => x.Value<string>("typeName") ?? "")
                .ToList();

            int totalApprox = ordered.Count;
            var page = ordered.Skip(offset).Take(limit).ToList();

            var payload = new JObject
            {
                ["ok"] = true,
                ["msg"] = "",
                ["items"] = new JArray(page),
                ["totalApprox"] = totalApprox
            };
            return RpcResultEnvelope.StandardizePayload(payload, uiapp, cmd.Command, sw.ElapsedMilliseconds);
        }
    }
}
