// ================================================================
// File: Commands/ViewOps/IsolateByFilterInViewCommand.cs
// Purpose: Generic element isolation in a view by categories/classes/parameters
// Notes  :
//  - Detaches view template when requested
//  - Optionally resets: unhide elements + clear graphic overrides
//  - Parameter rules support instance/type/both with eq/neq/contains/regex/gt/gte/lt/lte/in/nin
//  - Supports category names, built-in ids, shared param guid, grouped rules (OR-of-AND), and basic unit hints
// Target : .NET Framework 4.8 / Revit 2023+
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

namespace RevitMCPAddin.Commands.ViewOps
{
    public class IsolateByFilterInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "isolate_by_filter_in_view";

        private sealed class Rule
        {
            public string Target = "both"; // instance|type|both
            public string? Name;
            public BuiltInParameter? BuiltIn;   // by enum name
            public int? BuiltInId;              // by int id
            public string? Guid;                // shared parameter guid
            public string Op = "eq";
            public JToken? Value;
            public bool CaseInsensitive = true;
            public string? Unit;                // for numeric ops (mm/m/cm/ft/in/deg/rad)
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントが見つかりません。" };

            var p = (JObject?)cmd.Params ?? new JObject();
            // View resolve
            int viewId = p.Value<int?>("viewId") ?? 0;
            string? uniqueId = p.Value<string>("uniqueId");
            View? view = null;
            if (viewId > 0) view = doc.GetElement(new ElementId(viewId)) as View;
            else if (!string.IsNullOrWhiteSpace(uniqueId)) view = doc.GetElement(uniqueId) as View;
            else view = uidoc?.ActiveView;
            if (view == null) return new { ok = false, msg = "ビューが見つかりません。viewId または uniqueId を指定してください。" };

            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? true;
            bool reset = p.Value<bool?>("reset") ?? true;
            bool keepAnnotations = p.Value<bool?>("keepAnnotations") ?? true;
            int batchSize = Math.Max(50, Math.Min(5000, p.Value<int?>("batchSize") ?? 1000));
            // Timeslice controls (opt-in)
            bool startProvided = p["startIndex"] != null;
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            int maxMillisPerTx = Math.Max(500, Math.Min(15000, p.Value<int?>("maxMillisPerTx") ?? (startProvided ? 3000 : int.MaxValue)));
            bool refreshView = p.Value<bool?>("refreshView") ?? true;

            var f = p["filter"] as JObject ?? new JObject();
            var includeCatIds = ToIntSet((f["includeCategoryIds"] as JArray)?.Values<int>());
            var excludeCatIds = ToIntSet((f["excludeCategoryIds"] as JArray)?.Values<int>());
            var includeCatNames = ToStrSet((f["includeCategoryNames"] as JArray)?.Values<string>());
            var excludeCatNames = ToStrSet((f["excludeCategoryNames"] as JArray)?.Values<string>());
            var includeClasses = ToStrSet((f["includeClasses"] as JArray)?.Values<string>());
            var excludeClasses = ToStrSet((f["excludeClasses"] as JArray)?.Values<string>());
            bool modelOnly = f.Value<bool?>("modelOnly") ?? false;
            bool invertMatch = f.Value<bool?>("invertMatch") ?? false; // if true, hide matches; else keep matches and hide others
            string logic = (f.Value<string>("logic") ?? "all").Trim().ToLowerInvariant(); // all|any

            var rules = new List<Rule>();
            var ruleGroups = new List<List<Rule>>(); // OR-of-AND groups

            // grouped rules (optional): [[{rule},{rule}], [{rule}], ...]
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

            try
            {
                var swAll = System.Diagnostics.Stopwatch.StartNew();

                // Detach/reset only on the first slice
                if (startIndex == 0)
                {
                    using (var tx0 = new Transaction(doc, "IsolateByFilter: Prepare"))
                    {
                        tx0.Start();
                        if (detachTemplate && view.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            try { view.ViewTemplateId = ElementId.InvalidElementId; } catch { }
                        }
                        if (reset)
                        {
                            try
                            {
                                try { if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)) view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate); } catch { }
                            }
                            catch { }
                        }
                        tx0.Commit();
                    }
                }

                // Candidate ids cache (per document+view), sliced processing
                var allIds = CandidatesCache.GetOrBuild(doc, view);
                if (startIndex >= allIds.Count) startIndex = 0;
                int nextIndex = startIndex;

                int kept = 0;
                int hidden = 0;
                bool namesResolved = false;

                while (nextIndex < allIds.Count)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int end = Math.Min(allIds.Count, nextIndex + batchSize);

                    var toHideBatch = new List<ElementId>(Math.Min(batchSize, 2048));

                    for (int i = nextIndex; i < end; i++)
                    {
                        var id = allIds[i];
                        Element e = null; try { e = doc.GetElement(id); } catch { e = null; }
                        if (e == null) continue;

                        var cat = e.Category; var catType = cat != null ? cat.CategoryType : CategoryType.Model;
                        bool isModel = (catType == CategoryType.Model);

                        if (!keepAnnotations && catType != CategoryType.Model)
                        {
                            // allow filtering as model by rules when keepAnnotations=false
                        }
                        else if (keepAnnotations && !isModel)
                        {
                            // skip annotations entirely
                            continue;
                        }

                        // resolve category names once
                        if (!namesResolved && (includeCatNames != null || excludeCatNames != null))
                        {
                            ResolveCategoryNameSets(doc, includeCatNames, excludeCatNames, ref includeCatIds, ref excludeCatIds);
                            namesResolved = true;
                        }

                        // Category filters
                        if (includeCatIds != null && cat != null && !includeCatIds.Contains(cat.Id.IntegerValue))
                        {
                            if (!invertMatch) { toHideBatch.Add(e.Id); continue; } else { continue; }
                        }
                        if (excludeCatIds != null && cat != null && excludeCatIds.Contains(cat.Id.IntegerValue))
                        {
                            if (!invertMatch) { toHideBatch.Add(e.Id); continue; } else { continue; }
                        }

                        // Class filters
                        var cls = e.GetType().Name;
                        if (includeClasses != null && !includeClasses.Contains(cls))
                        {
                            if (!invertMatch) { toHideBatch.Add(e.Id); continue; } else { continue; }
                        }
                        if (excludeClasses != null && excludeClasses.Contains(cls))
                        {
                            if (!invertMatch) { toHideBatch.Add(e.Id); continue; } else { continue; }
                        }

                        if (modelOnly && !isModel)
                        {
                            if (!invertMatch) { toHideBatch.Add(e.Id); continue; } else { continue; }
                        }

                        bool paramMatch = true;
                        if (ruleGroups.Count > 0)
                        {
                            paramMatch = EvaluateRuleGroups(doc, e, ruleGroups);
                        }
                        else if (rules.Count > 0)
                        {
                            paramMatch = EvaluateRules(doc, e, rules, logic);
                        }

                        if (invertMatch)
                        {
                            if (paramMatch) { toHideBatch.Add(e.Id); }
                            else { kept++; }
                        }
                        else
                        {
                            if (!paramMatch) { toHideBatch.Add(e.Id); }
                            else { kept++; }
                        }
                    }

                    using (var tx = new Transaction(doc, "Isolate By Filter In View (slice)"))
                    {
                        try
                        {
                            tx.Start();
                            if (toHideBatch.Count > 0)
                            {
                                try { view.HideElements(toHideBatch); hidden += toHideBatch.Count; }
                                catch { foreach (var eid in toHideBatch) { try { view.HideElements(new List<ElementId> { eid }); hidden++; } catch { } } }
                            }
                            tx.Commit();
                        }
                        catch { try { tx.RollBack(); } catch { } }
                    }

                    try { doc.Regenerate(); } catch { }
                    if (refreshView) { try { uidoc?.RefreshActiveView(); } catch { } }

                    nextIndex = end;
                    if (sw.ElapsedMilliseconds > maxMillisPerTx) break; // time-slice boundary
                }

                bool completed = nextIndex >= allIds.Count;
                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    kept,
                    hidden,
                    total = allIds.Count,
                    completed,
                    nextIndex = completed ? (int?)null : nextIndex,
                    batchSize,
                    elapsedMs = swAll.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        private static class CandidatesCache
        {
            private static readonly object _gate = new object();
            private static readonly System.Collections.Generic.Dictionary<string, System.WeakReference<System.Collections.Generic.List<ElementId>>> _store
                = new System.Collections.Generic.Dictionary<string, System.WeakReference<System.Collections.Generic.List<ElementId>>>();

            private static string Key(Document doc, View view)
                => doc.GetHashCode().ToString() + ":" + (view?.Id.IntegerValue ?? 0).ToString();

            public static System.Collections.Generic.List<ElementId> GetOrBuild(Document doc, View view)
            {
                var key = Key(doc, view);
                lock (_gate)
                {
                    if (_store.TryGetValue(key, out var wr))
                    {
                        if (wr != null && wr.TryGetTarget(out var cached) && cached != null)
                            return cached;
                    }

                    var fresh = new FilteredElementCollector(doc, view.Id)
                                    .WhereElementIsNotElementType()
                                    .ToElementIds()
                                    .ToList();

                    _store[key] = new System.WeakReference<System.Collections.Generic.List<ElementId>>(fresh);
                    if (_store.Count > 64)
                    {
                        var dead = _store.FirstOrDefault(kv => kv.Value == null || !kv.Value.TryGetTarget(out _));
                        if (!string.IsNullOrEmpty(dead.Key)) _store.Remove(dead.Key);
                    }
                    return fresh;
                }
            }
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
                if (okAll) return true; // any group matches
            }
            return false;
        }

        private static bool MatchRule(Document doc, Element e, Rule r)
        {
            // Get parameter value from instance and/or type
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
            else
            {
                foreach (var s in vals)
                {
                    if (s == null) continue;
                    if (Compare(s, cmp, r.Op, r.CaseInsensitive)) return true;
                }
                return false;
            }
        }

        private static (string? asString, double? asDouble) ResolveParamValues(Document doc, Element e, Rule r)
        {
            Parameter? p = null;
            if (r.BuiltInId.HasValue) { try { p = e.get_Parameter((BuiltInParameter)r.BuiltInId.Value); } catch { p = null; } }
            if (p == null && r.BuiltIn.HasValue) { try { p = e.get_Parameter(r.BuiltIn.Value); } catch { p = null; } }
            if (p == null && !string.IsNullOrWhiteSpace(r.Guid)) { try { p = e.get_Parameter(new Guid(r.Guid)); } catch { p = null; } }
            if (p == null && !string.IsNullOrWhiteSpace(r.Name)) { try { p = e.LookupParameter(r.Name); } catch { p = null; } }
            if (p == null)
            {
                if (!string.IsNullOrWhiteSpace(r.Name) && r.Name.Equals("Type Name", StringComparison.OrdinalIgnoreCase))
                {
                    try { p = e.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM); } catch { }
                }
            }
            if (p == null) return (null, null);

            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String: return (p.AsString() ?? string.Empty, null);
                    case StorageType.Integer: return (p.AsInteger().ToString(), (double)p.AsInteger());
                    case StorageType.Double: return (p.AsValueString() ?? string.Empty, p.AsDouble());
                    case StorageType.ElementId:
                        var id = p.AsElementId(); return ((id != null && id != ElementId.InvalidElementId) ? id.IntegerValue.ToString() : string.Empty, (double?)((id != null && id != ElementId.InvalidElementId) ? id.IntegerValue : (int?)null));
                    default: return (p.AsValueString() ?? string.Empty, null);
                }
            }
            catch { return (null, null); }
        }

        private static bool Compare(string src, string cmp, string op, bool ci)
        {
            var c = ci ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            switch (op)
            {
                case "eq": return string.Equals(src, cmp, c);
                case "neq": return !string.Equals(src, cmp, c);
                case "contains": return src?.IndexOf(cmp ?? string.Empty, c) >= 0;
                case "ncontains": return src?.IndexOf(cmp ?? string.Empty, c) < 0;
                case "regex":
                    try { return Regex.IsMatch(src ?? string.Empty, cmp ?? string.Empty, ci ? RegexOptions.IgnoreCase : RegexOptions.None); } catch { return false; }
                case "gt":
                case "gte":
                case "lt":
                case "lte":
                    return false; // handled in numeric branch
                case "in":
                    return (cmp ?? string.Empty).Split('|').Any(x => string.Equals(src, x, c));
                case "nin":
                    return !( (cmp ?? string.Empty).Split('|').Any(x => string.Equals(src, x, c)) );
                default:
                    return string.Equals(src, cmp, c);
            }
        }

        private static Autodesk.Revit.DB.ForgeTypeId? TryGetSpec(Document doc, Element e, Rule r)
        {
            try
            {
                Parameter p = null;
                if (r.BuiltInId.HasValue) p = e.get_Parameter((BuiltInParameter)r.BuiltInId.Value);
                if (p == null && r.BuiltIn.HasValue) p = e.get_Parameter(r.BuiltIn.Value);
                if (p == null && !string.IsNullOrWhiteSpace(r.Guid)) p = e.get_Parameter(new Guid(r.Guid));
                if (p == null && !string.IsNullOrWhiteSpace(r.Name)) p = e.LookupParameter(r.Name);
                if (p?.Definition == null) return null;
                return p.Definition.GetDataType();
            }
            catch { return null; }
        }

        private static double? ParseNumberWithUnits(string raw, string? unitHint, Autodesk.Revit.DB.ForgeTypeId? spec)
        {
            try
            {
                raw = (raw ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(raw)) return null;
                string u = unitHint?.Trim().ToLowerInvariant() ?? string.Empty;
                double val;
                string digits = raw;
                if (raw.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 2); if (string.IsNullOrEmpty(u)) u = "mm"; }
                else if (raw.EndsWith("cm", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 2); if (string.IsNullOrEmpty(u)) u = "cm"; }
                else if (raw.EndsWith("m", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 1); if (string.IsNullOrEmpty(u)) u = "m"; }
                else if (raw.EndsWith("ft", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 2); if (string.IsNullOrEmpty(u)) u = "ft"; }
                else if (raw.EndsWith("in", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 2); if (string.IsNullOrEmpty(u)) u = "in"; }
                else if (raw.EndsWith("deg", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 3); if (string.IsNullOrEmpty(u)) u = "deg"; }
                else if (raw.EndsWith("rad", StringComparison.OrdinalIgnoreCase)) { digits = raw.Substring(0, raw.Length - 3); if (string.IsNullOrEmpty(u)) u = "rad"; }

                if (!double.TryParse(digits, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
                    return null;

                try
                {
                    if (spec != null)
                    {
                        if (spec.Equals(SpecTypeId.Length))
                        {
                            if (u == "cm") val = Autodesk.Revit.DB.UnitUtils.ConvertToInternalUnits(val * 10.0, UnitTypeId.Millimeters);
                            else if (u == "m") val = Autodesk.Revit.DB.UnitUtils.ConvertToInternalUnits(val * 1000.0, UnitTypeId.Millimeters);
                            else if (u == "ft") { /* internal ft; keep */ }
                            else if (u == "in") val = val / 12.0;
                            else /* default mm */ val = Autodesk.Revit.DB.UnitUtils.ConvertToInternalUnits(val, UnitTypeId.Millimeters);
                            return val;
                        }
                        if (spec.Equals(SpecTypeId.Angle))
                        {
                            if (u == "rad") return val;
                            else /* default deg */ return val * (Math.PI / 180.0);
                        }
                    }
                }
                catch { }

                return val;
            }
            catch { return null; }
        }

        private static void ResolveCategoryNameSets(Document doc, HashSet<string>? includeCatNames, HashSet<string>? excludeCatNames, ref HashSet<int>? includeCatIds, ref HashSet<int>? excludeCatIds)
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

        private static HashSet<int>? ToIntSet(IEnumerable<int>? arr)
            => (arr != null) ? new HashSet<int>(arr) : null;

        private static HashSet<string>? ToStrSet(IEnumerable<string>? arr)
            => (arr != null) ? new HashSet<string>(arr.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase) : null;

        private static Rule? ParseRule(JObject jr)
        {
            try
            {
                var r = new Rule();
                r.Target = (jr.Value<string>("target") ?? "both").Trim().ToLowerInvariant();
                r.Name = jr.Value<string>("name");
                string? builtInName = jr.Value<string>("builtInName");
                if (!string.IsNullOrWhiteSpace(builtInName))
                {
                    try { r.BuiltIn = (BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), builtInName, true); } catch { r.BuiltIn = null; }
                }
                int? bid = jr.Value<int?>("builtInId"); if (bid.HasValue) r.BuiltInId = bid.Value;
                r.Guid = jr.Value<string>("guid");
                r.Op = (jr.Value<string>("op") ?? "eq").Trim().ToLowerInvariant();
                r.Value = jr["value"];
                r.CaseInsensitive = jr.Value<bool?>("caseInsensitive") ?? true;
                r.Unit = jr.Value<string>("unit");
                return r;
            }
            catch { return null; }
        }
    }
}
