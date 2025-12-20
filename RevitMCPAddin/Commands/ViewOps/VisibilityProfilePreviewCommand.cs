// ================================================================
// File   : Commands/ViewOps/VisibilityProfilePreviewCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary: Preview counts for a visibility filter without modifying the view
// I/O    : Input  -> { viewId?:int, filter: object, keepAnnotations?: false }
//          Output -> { ok, viewId, total, kept, hidden,
//                      byCategory: [{ categoryId, categoryName, kept, hidden }] }
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class VisibilityProfilePreviewCommand : RevitMCPAddin.Core.IRevitCommandHandler
    {
        public string CommandName => "visibility_profile_preview";

        private static int GetOrZero(Dictionary<int, int> map, int key)
            => map.TryGetValue(key, out var v) ? v : 0;

        private sealed class Rule
        {
            public string Target = "both"; // instance|type|both
            public string? Name;
            public BuiltInParameter? BuiltIn;
            public int? BuiltInId;
            public string? Guid;
            public string Op = "eq";       // eq, neq, contains, ncontains, regex, in, nin, gt, gte, lt, lte
            public JToken? Value;
            public bool CaseInsensitive = true;
            public string? Unit;           // mm, cm, m, ft, in, deg, rad
        }

        private sealed class CatStat
        {
            public int CategoryId { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public int Kept { get; set; }
            public int Hidden { get; set; }
        }

        public object Execute(UIApplication uiapp, RevitMCPAddin.Core.RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントが見つかりません。" };

            var p = (JObject?)cmd.Params ?? new JObject();
            int viewId = p.Value<int?>("viewId") ?? 0;
            View? view = viewId > 0 ? doc.GetElement(new ElementId(viewId)) as View : uidoc?.ActiveView;
            if (view == null) return new { ok = false, msg = "対象ビューが見つかりません。" };

            var f = p["filter"] as JObject ?? new JObject();
            bool keepAnnotations = p.Value<bool?>("keepAnnotations") ?? false;

            // include/exclude sets
            var includeCatIds = ToIntSet((f["includeCategoryIds"] as JArray)?.Values<int>());
            var excludeCatIds = ToIntSet((f["excludeCategoryIds"] as JArray)?.Values<int>());
            var includeCatNames = ToStrSet((f["includeCategoryNames"] as JArray)?.Values<string>());
            var excludeCatNames = ToStrSet((f["excludeCategoryNames"] as JArray)?.Values<string>());
            var includeClasses = ToStrSet((f["includeClasses"] as JArray)?.Values<string>());
            var excludeClasses = ToStrSet((f["excludeClasses"] as JArray)?.Values<string>());

            bool modelOnly = f.Value<bool?>("modelOnly") ?? false;
            bool invertMatch = f.Value<bool?>("invertMatch") ?? false;
            string logic = (f.Value<string>("logic") ?? "all").Trim().ToLowerInvariant(); // all|any

            // Parameter rules
            var rules = new List<Rule>();
            var ruleGroups = new List<List<Rule>>();

            var prGroups = f["parameterRuleGroups"] as JArray;
            if (prGroups != null)
            {
                foreach (var grpTok in prGroups.OfType<JArray>())
                {
                    var grp = new List<Rule>();
                    foreach (var jr in grpTok.OfType<JObject>())
                    {
                        var rr = ParseRule(jr);
                        if (rr != null) grp.Add(rr);
                    }
                    if (grp.Count > 0) ruleGroups.Add(grp);
                }
            }
            var prules = f["parameterRules"] as JArray;
            if (prules != null)
            {
                foreach (var jr in prules.OfType<JObject>())
                {
                    var r = ParseRule(jr);
                    if (r != null) rules.Add(r);
                }
            }

            var keptByCat = new Dictionary<int, int>();
            var hiddenByCat = new Dictionary<int, int>();
            int kept = 0, hidden = 0, total = 0;
            bool namesResolved = false;

            var coll = new FilteredElementCollector(doc, view.Id)
                       .WhereElementIsNotElementType()
                       .ToElements();

            foreach (var e in coll)
            {
                total++;

                var cat = e.Category;
                int catId = cat?.Id.IntegerValue ?? -1;
                var catType = cat != null ? cat.CategoryType : CategoryType.Model;
                bool isModel = (catType == CategoryType.Model);

                if (keepAnnotations)
                {
                    if (!isModel) continue;
                }

                if (!namesResolved && (includeCatNames != null || excludeCatNames != null))
                {
                    ResolveCategoryNameSets(doc, includeCatNames, excludeCatNames, ref includeCatIds, ref excludeCatIds);
                    namesResolved = true;
                }

                // Category filters
                if (includeCatIds != null && cat != null && !includeCatIds.Contains(catId))
                { hiddenByCat[catId] = GetOrZero(hiddenByCat, catId) + 1; hidden++; continue; }
                if (excludeCatIds != null && cat != null && excludeCatIds.Contains(catId))
                { hiddenByCat[catId] = GetOrZero(hiddenByCat, catId) + 1; hidden++; continue; }

                // Class filters
                var cls = e.GetType().Name;
                if (includeClasses != null && !includeClasses.Contains(cls))
                { hiddenByCat[catId] = GetOrZero(hiddenByCat, catId) + 1; hidden++; continue; }
                if (excludeClasses != null && excludeClasses.Contains(cls))
                { hiddenByCat[catId] = GetOrZero(hiddenByCat, catId) + 1; hidden++; continue; }

                if (modelOnly && !isModel)
                { hiddenByCat[catId] = GetOrZero(hiddenByCat, catId) + 1; hidden++; continue; }

                // Parameter rules
                bool paramMatch = true;
                if (ruleGroups.Count > 0) paramMatch = EvaluateRuleGroups(doc, e, ruleGroups);
                else if (rules.Count > 0) paramMatch = EvaluateRules(doc, e, rules, logic);

                if (invertMatch)
                {
                    if (paramMatch) { hiddenByCat[catId] = GetOrZero(hiddenByCat, catId) + 1; hidden++; }
                    else { keptByCat[catId] = GetOrZero(keptByCat, catId) + 1; kept++; }
                }
                else
                {
                    if (!paramMatch) { hiddenByCat[catId] = GetOrZero(hiddenByCat, catId) + 1; hidden++; }
                    else { keptByCat[catId] = GetOrZero(keptByCat, catId) + 1; kept++; }
                }
            }

            // Aggregate by category
            var allCatIds = new HashSet<int>();
            foreach (var k in keptByCat.Keys) allCatIds.Add(k);
            foreach (var k in hiddenByCat.Keys) allCatIds.Add(k);

            var byCat = new List<CatStat>();
            foreach (var cid in allCatIds)
            {
                if (cid < 0) continue;
                string name = "(Unknown)";
                try
                {
                    foreach (Category c in doc.Settings.Categories)
                    {
                        if (c != null && c.Id.IntegerValue == cid) { name = c.Name ?? "(Unknown)"; break; }
                    }
                }
                catch { }

                byCat.Add(new CatStat
                {
                    CategoryId = cid,
                    CategoryName = name,
                    Kept = GetOrZero(keptByCat, cid),
                    Hidden = GetOrZero(hiddenByCat, cid)
                });
            }
            var orderedByCat = byCat
                .OrderBy(x => x.CategoryName, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new { categoryId = x.CategoryId, categoryName = x.CategoryName, kept = x.Kept, hidden = x.Hidden })
                .ToList();

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                total,
                kept,
                hidden,
                byCategory = orderedByCat
            };
        }

        // ---------- Helpers ----------
        private static HashSet<int>? ToIntSet(IEnumerable<int>? arr)
            => (arr != null) ? new HashSet<int>(arr) : null;

        private static HashSet<string>? ToStrSet(IEnumerable<string>? arr)
            => (arr != null)
               ? new HashSet<string>(arr.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase)
               : null;

        private static void ResolveCategoryNameSets(
            Document doc,
            HashSet<string>? includeCatNames,
            HashSet<string>? excludeCatNames,
            ref HashSet<int>? includeCatIds,
            ref HashSet<int>? excludeCatIds)
        {
            try
            {
                if (includeCatNames != null)
                {
                    foreach (Category c in doc.Settings.Categories)
                    {
                        if (includeCatNames.Contains(c?.Name ?? string.Empty))
                        {
                            includeCatIds ??= new HashSet<int>();
                            includeCatIds.Add(c.Id.IntegerValue);
                        }
                    }
                }
                if (excludeCatNames != null)
                {
                    foreach (Category c in doc.Settings.Categories)
                    {
                        if (excludeCatNames.Contains(c?.Name ?? string.Empty))
                        {
                            excludeCatIds ??= new HashSet<int>();
                            excludeCatIds.Add(c.Id.IntegerValue);
                        }
                    }
                }
            }
            catch { }
        }

        private static Rule? ParseRule(JObject jr)
        {
            if (jr == null) return null;
            var r = new Rule();

            r.Target = (jr.Value<string>("target") ?? "both").Trim().ToLowerInvariant();
            r.Name = jr.Value<string>("name");
            r.Op = (jr.Value<string>("op") ?? "eq").Trim().ToLowerInvariant();
            r.Value = jr["value"];
            r.CaseInsensitive = jr.Value<bool?>("caseInsensitive") ?? true;
            r.Unit = jr.Value<string>("unit");

            if (jr.TryGetValue("builtInId", out var tokId) && tokId.Type != JTokenType.Null)
            {
                if (int.TryParse(tokId.ToString(), out var id)) r.BuiltInId = id;
            }

            if (jr.TryGetValue("builtIn", out var tokBi) && tokBi.Type != JTokenType.Null)
            {
                if (tokBi.Type == JTokenType.Integer)
                {
                    r.BuiltInId = tokBi.Value<int>();
                }
                else
                {
                    var s = tokBi.Value<string>() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        try { r.BuiltIn = (BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), s, ignoreCase: true); }
                        catch { if (int.TryParse(s, out var v)) r.BuiltInId = v; }
                    }
                }
            }

            var builtInName = jr.Value<string>("builtInName");
            if (!string.IsNullOrWhiteSpace(builtInName) && !r.BuiltIn.HasValue && !r.BuiltInId.HasValue)
            {
                try { r.BuiltIn = (BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), builtInName, true); } catch { }
            }

            if (jr.TryGetValue("guid", out var tokGuid) && tokGuid.Type != JTokenType.Null)
            {
                var g = tokGuid.Value<string>();
                if (!string.IsNullOrWhiteSpace(g)) r.Guid = g;
            }
            return r;
        }

        private static bool EvaluateRules(Document doc, Element e, List<Rule> rules, string logic)
        {
            bool any = false; bool all = true;
            foreach (var r in rules)
            {
                bool m = MatchRule(doc, e, r);
                any |= m; all &= m;
                if (logic == "any" && any) return true;
                if (logic != "any" && !all) return false;
            }
            return logic == "any" ? any : all;
        }

        private static bool EvaluateRuleGroups(Document doc, Element e, List<List<Rule>> groups)
        {
            foreach (var grp in groups)
            {
                bool okAll = true;
                foreach (var r in grp)
                {
                    if (!MatchRule(doc, e, r)) { okAll = false; break; }
                }
                if (okAll) return true;
            }
            return false;
        }

        private static bool MatchRule(Document doc, Element e, Rule r)
        {
            var vals = new List<string?>();
            var numVals = new List<double?>();

            if (r.Target == "instance" || r.Target == "both")
            {
                var (s, d) = ResolveParamValues(doc, e, r);
                if (s != null) vals.Add(s);
                if (d.HasValue) numVals.Add(d);
            }
            if (r.Target == "type" || r.Target == "both")
            {
                try
                {
                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                    if (et != null)
                    {
                        var (s, d) = ResolveParamValues(doc, et, r);
                        if (s != null) vals.Add(s);
                        if (d.HasValue) numVals.Add(d);
                    }
                }
                catch { }
            }
            if (vals.Count == 0 && numVals.Count == 0) return false;

            var cmp = r.Value?.ToString() ?? string.Empty;

            if (r.Op == "gt" || r.Op == "gte" || r.Op == "lt" || r.Op == "lte")
            {
                double? threshold = ParseNumberWithUnits(cmp, r.Unit, TryGetSpec(doc, e, r));
                if (!threshold.HasValue) return false;

                foreach (var v in numVals)
                {
                    if (!v.HasValue) continue;
                    var a = v.Value; var b = threshold.Value;
                    if (r.Op == "gt" && a > b) return true;
                    if (r.Op == "gte" && a >= b) return true;
                    if (r.Op == "lt" && a < b) return true;
                    if (r.Op == "lte" && a <= b) return true;
                }
                return false;
            }

            foreach (var s in vals)
            {
                if (s == null) continue;
                if (Compare(s, cmp, r.Op, r.CaseInsensitive)) return true;
            }
            return false;
        }

        private static (string? asString, double? asDouble) ResolveParamValues(Document doc, Element e, Rule r)
        {
            Parameter? p = null;
            try
            {
                if (r.BuiltInId.HasValue) p = e.get_Parameter((BuiltInParameter)r.BuiltInId.Value);
                if (p == null && r.BuiltIn.HasValue) p = e.get_Parameter(r.BuiltIn.Value);
                if (p == null && !string.IsNullOrWhiteSpace(r.Guid)) p = e.get_Parameter(new Guid(r.Guid));
                if (p == null && !string.IsNullOrWhiteSpace(r.Name)) p = e.LookupParameter(r.Name);
            }
            catch { p = null; }

            if (p == null) return (null, null);

            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return (p.AsString() ?? string.Empty, null);

                    case StorageType.Integer:
                        return (p.AsInteger().ToString(CultureInfo.InvariantCulture), (double)p.AsInteger());

                    case StorageType.Double:
                        return (p.AsValueString() ?? string.Empty, p.AsDouble());

                    case StorageType.ElementId:
                        {
                            var id = p.AsElementId();
                            if (id != null && id != ElementId.InvalidElementId)
                                return (id.IntegerValue.ToString(CultureInfo.InvariantCulture), (double)id.IntegerValue);
                            return (string.Empty, null);
                        }

                    default:
                        return (p.AsValueString() ?? string.Empty, null);
                }
            }
            catch { return (null, null); }
        }

        private static bool Compare(string src, string cmp, string op, bool ci)
        {
            var comp = ci ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            switch (op)
            {
                case "eq": return string.Equals(src, cmp, comp);
                case "neq": return !string.Equals(src, cmp, comp);
                case "contains": return (src ?? string.Empty).IndexOf(cmp ?? string.Empty, comp) >= 0;
                case "ncontains": return (src ?? string.Empty).IndexOf(cmp ?? string.Empty, comp) < 0;
                case "regex":
                    try
                    {
                        var options = ci ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None;
                        return System.Text.RegularExpressions.Regex.IsMatch(src ?? string.Empty, cmp ?? string.Empty, options);
                    }
                    catch { return false; }
                case "in":
                    return (cmp ?? string.Empty).Split('|').Any(x => string.Equals(src, x, comp));
                case "nin":
                    return !((cmp ?? string.Empty).Split('|').Any(x => string.Equals(src, x, comp)));
                default:
                    return string.Equals(src, cmp, comp);
            }
        }

        private static ForgeTypeId? TryGetSpec(Document doc, Element e, Rule r)
        {
            try
            {
                Parameter? p = null;
                if (r.BuiltInId.HasValue) p = e.get_Parameter((BuiltInParameter)r.BuiltInId.Value);
                if (p == null && r.BuiltIn.HasValue) p = e.get_Parameter(r.BuiltIn.Value);
                if (p == null && !string.IsNullOrWhiteSpace(r.Guid)) p = e.get_Parameter(new Guid(r.Guid));
                if (p == null && !string.IsNullOrWhiteSpace(r.Name)) p = e.LookupParameter(r.Name);
                if (p?.Definition == null) return null;
                return p.Definition.GetDataType();
            }
            catch { return null; }
        }

        private static double? ParseNumberWithUnits(string raw, string? unitHint, ForgeTypeId? spec)
        {
            try
            {
                raw = (raw ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(raw)) return null;

                string u = unitHint?.Trim().ToLowerInvariant() ?? string.Empty;
                string digits = raw;
                if (raw.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 2); if (string.IsNullOrEmpty(u)) u = "mm"; }
                else if (raw.EndsWith("cm", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 2); if (string.IsNullOrEmpty(u)) u = "cm"; }
                else if (raw.EndsWith("m", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 1); if (string.IsNullOrEmpty(u)) u = "m"; }
                else if (raw.EndsWith("ft", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 2); if (string.IsNullOrEmpty(u)) u = "ft"; }
                else if (raw.EndsWith("in", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 2); if (string.IsNullOrEmpty(u)) u = "in"; }
                else if (raw.EndsWith("deg", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 3); if (string.IsNullOrEmpty(u)) u = "deg"; }
                else if (raw.EndsWith("rad", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 3); if (string.IsNullOrEmpty(u)) u = "rad"; }

                if (!double.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    return null;

                try
                {
                    if (spec != null)
                    {
                        if (spec.Equals(SpecTypeId.Length))
                        {
                            if (u == "cm") return UnitUtils.ConvertToInternalUnits(val * 10.0, UnitTypeId.Millimeters);
                            else if (u == "m") return UnitUtils.ConvertToInternalUnits(val * 1000.0, UnitTypeId.Millimeters);
                            else if (u == "ft") return val;
                            else if (u == "in") return val / 12.0;
                            else return UnitUtils.ConvertToInternalUnits(val, UnitTypeId.Millimeters);
                        }
                        if (spec.Equals(SpecTypeId.Angle))
                        {
                            if (u == "rad") return val;
                            else return val * (Math.PI / 180.0);
                        }
                    }
                }
                catch { }

                return val;
            }
            catch { return null; }
        }
    }
}
