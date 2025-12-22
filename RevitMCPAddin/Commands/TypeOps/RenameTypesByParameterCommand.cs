// File: Commands/TypeOps/RenameTypesByParameterCommand.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.TypeOps
{
    /// <summary>
    /// Generic, batched type rename by parameter value with prefix/suffix mapping.
    /// Supports all ElementType categories, optional in-view scope, categories/typeIds filters,
    /// dry-run, prefix stripping, and conflict checks. Time-sliced via startIndex/batchSize.
    ///
    /// Params (JSON):
    /// - scope?: "all" | "in_view" (default: all)
    /// - viewId?: int (required when scope=in_view)
    /// - categories?: int[] BuiltInCategory ids (optional)
    /// - typeIds?: int[] explicit type ids (optional)
    /// - parameter: { builtInId?:int | builtInName?:string | guid?:string | name?:string, useDisplay?:bool, op?:"eq"|"contains", caseInsensitive?:bool }
    /// - rules: [ { when:string, prefix?:string, suffix?:string } ]
    /// - stripPrefixes?: string[] (prefix variants to remove before applying new prefix)
    /// - startIndex?: int, batchSize?: int
    /// - dryRun?: bool
    ///
    /// Result: { ok, processed, renamed, skipped, items:[{ typeId, oldName, newName?, reason? }], nextIndex?, completed }
    /// </summary>
    public class RenameTypesByParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_types_by_parameter";

        private class ParamKey
        {
            public int? BuiltInId;
            public string BuiltInName;
            public string Guid;
            public string Name;
            public bool UseDisplay = true;
            public string Op = "eq"; // eq | contains
            public bool CaseInsensitive = true;
        }

        private class Rule
        {
            public string When;
            public string Prefix = string.Empty;
            public string Suffix = string.Empty;
            // Templates: allow tokens {value}, {display}, {display_no_space}
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());

            string scope = p.Value<string>("scope") ?? "all";
            int viewId = p.Value<int?>("viewId") ?? 0;
            bool dryRun = p.Value<bool?>("dryRun") ?? false;
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 300);

            var stripList = ParseStringArray(p["stripPrefixes"]);
            if (stripList.Count == 0)
            {
                // Defaults for JP + ASCII variants
                stripList.AddRange(new[] {
                    "(外壁) ", "（外壁） ", "(外壁)", "（外壁）",
                    "(内壁) ", "（内壁） ", "(内壁)", "（内壁）",
                    "外壁 ", "内壁 "
                });
            }

            var key = ParseParamKey(p["parameter"]);
            if (key == null)
            {
                // Provide sensible default for JP Function/機能 parameter
                key = new ParamKey { Name = "機能", UseDisplay = true, Op = "eq", CaseInsensitive = true };
            }

            var rules = ParseRules(p["rules"]);
            if (rules.Count == 0)
            {
                // Default mapping for 外部/内部 → (外壁)/(内壁)
                rules.Add(new Rule { When = "外部", Prefix = "(外壁) " });
                rules.Add(new Rule { When = "内部", Prefix = "(内壁) " });
            }

            // Resolve target type ids
            var targetIds = ResolveTargetTypeIds(doc, p, scope, viewId);
            if (targetIds.Count == 0) return new { ok = true, processed = 0, renamed = 0, skipped = 0, completed = true };

            var total = targetIds.Count;
            var slice = targetIds.Skip(startIndex).Take(batchSize).ToList();

            var items = new List<object>(slice.Count);
            int renamed = 0, skipped = 0, processed = 0;

            // Build per-category name set for conflict checks
            var nameSetByCat = new Dictionary<int, HashSet<string>>();
            foreach (var id in targetIds)
            {
                var et = doc.GetElement(id) as ElementType;
                if (et?.Category == null) continue;
                int catId = et.Category.Id.IntValue();
                if (!nameSetByCat.TryGetValue(catId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var t in new FilteredElementCollector(doc).WhereElementIsElementType().Where(e => e.Category != null && e.Category.Id.IntValue() == catId))
                    {
                        try { set.Add(((ElementType)t).Name); } catch { }
                    }
                    nameSetByCat[catId] = set;
                }
            }

            using (var tx = new Transaction(doc, "Rename Types By Parameter"))
            {
                if (!dryRun) tx.Start();

                foreach (var id in slice)
                {
                    processed++;
                    try
                    {
                        var et = doc.GetElement(id) as ElementType;
                        if (et == null)
                        {
                            skipped++; items.Add(new { typeId = id.IntValue(), reason = "not_found" }); continue;
                        }

                        var (val, shown) = TryGetParamValue(et, key);
                        string s = key.UseDisplay ? (shown ?? string.Empty) : (val ?? string.Empty);
                        if (key.CaseInsensitive) s = s?.ToLowerInvariant();

                        string? prefix = null, suffix = null;
                        bool matched = false;
                        foreach (var r in rules)
                        {
                            var w = r.When ?? string.Empty;
                            var ww = key.CaseInsensitive ? w.ToLowerInvariant() : w;
                            if (Matches(s, ww, key.Op))
                            {
                                prefix = r.Prefix; suffix = r.Suffix; matched = true; break;
                            }
                        }
                        if (!matched)
                        {
                            skipped++; items.Add(new { typeId = et.Id.IntValue(), oldName = et.Name, reason = "no_rule_match" }); continue;
                        }

                        var baseName = StripPrefix(et.Name ?? string.Empty, stripList);
                        var pre = RenderTemplate(prefix ?? string.Empty, val, shown);
                        var suf = RenderTemplate(suffix ?? string.Empty, val, shown);
                        var newName = pre + baseName + suf;
                        if (string.Equals(newName, et.Name, StringComparison.Ordinal))
                        {
                            skipped++; items.Add(new { typeId = et.Id.IntValue(), oldName = et.Name, reason = "already_up_to_date" }); continue;
                        }

                        int catId = et.Category?.Id?.IntValue() ?? 0;
                        if (catId != 0 && nameSetByCat.TryGetValue(catId, out var set2) && set2.Contains(newName))
                        {
                            skipped++; items.Add(new { typeId = et.Id.IntValue(), oldName = et.Name, newName, reason = "name_conflict" }); continue;
                        }

                        if (!dryRun)
                        {
                            et.Name = newName;
                            if (catId != 0 && nameSetByCat.TryGetValue(catId, out var sset)) sset.Add(newName);
                        }

                        renamed++;
                        items.Add(new { typeId = et.Id.IntValue(), oldName = et.Name, newName });
                    }
                    catch (Exception ex)
                    {
                        skipped++; items.Add(new { typeId = id.IntValue(), reason = ex.Message });
                    }
                }

                if (!dryRun) tx.Commit();
            }

            int next = startIndex + slice.Count;
            bool completed = next >= total;
            return new { ok = true, processed, renamed, skipped, items, nextIndex = completed ? (int?)null : next, completed, totalCount = total };
        }

        private static string RenderTemplate(string template, string? val, string? shown)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            string disp = shown ?? string.Empty;
            string dispNoSpace = disp.Replace(" ", string.Empty).Replace("\u00A0", string.Empty);
            string sval = val ?? string.Empty;
            return template
                .Replace("{display}", disp)
                .Replace("{display_no_space}", dispNoSpace)
                .Replace("{value}", sval);
        }

        private static List<ElementId> ResolveTargetTypeIds(Document doc, JObject p, string scope, int viewId)
        {
            var typeIds = new List<ElementId>();
            // typeIds explicit
            var typeIdsTok = p["typeIds"] as JArray;
            if (typeIdsTok != null && typeIdsTok.Count > 0)
            {
                foreach (var t in typeIdsTok) { try { typeIds.Add(Autodesk.Revit.DB.ElementIdCompat.From(Convert.ToInt32(t))); } catch { } }
                return typeIds.Distinct(new ElementIdComparer()).ToList();
            }

            // categories
            var catIds = ParseIntArray(p["categories"]);
            if (catIds.Count > 0)
            {
                foreach (var ci in catIds)
                {
                    try
                    {
                        var bic = (BuiltInCategory)ci;
                        var ids = new FilteredElementCollector(doc)
                            .WhereElementIsElementType()
                            .OfCategory(bic)
                            .Select(e => e.Id);
                        typeIds.AddRange(ids);
                    }
                    catch { /* ignore bad category */ }
                }
                return typeIds.Distinct(new ElementIdComparer()).ToList();
            }

            if (string.Equals(scope, "in_view", StringComparison.OrdinalIgnoreCase) && viewId > 0)
            {
                try
                {
                    var ids = new FilteredElementCollector(doc, Autodesk.Revit.DB.ElementIdCompat.From(viewId))
                        .WhereElementIsNotElementType()
                        .ToElements()
                        .Select(e => e.GetTypeId())
                        .Where(id => id != null && id.IntValue() > 0)
                        .Distinct(new ElementIdComparer());
                    typeIds.AddRange(ids);
                    return typeIds.Distinct(new ElementIdComparer()).ToList();
                }
                catch { /* fallback to all */ }
            }

            // all element types (may be large)
            typeIds.AddRange(new FilteredElementCollector(doc).WhereElementIsElementType().Select(e => e.Id));
            return typeIds.Distinct(new ElementIdComparer()).ToList();
        }

        private static (string? val, string? shown) TryGetParamValue(ElementType et, ParamKey key)
        {
            Parameter? param = null;
            if (key.BuiltInId.HasValue)
            {
                try { param = et.get_Parameter((BuiltInParameter)key.BuiltInId.Value); } catch { }
            }
            if (param == null && !string.IsNullOrWhiteSpace(key.BuiltInName))
            {
                try
                {
                    if (Enum.TryParse<BuiltInParameter>(key.BuiltInName, ignoreCase: true, out var bip))
                        param = et.get_Parameter(bip);
                }
                catch { }
            }
            if (param == null && !string.IsNullOrWhiteSpace(key.Guid))
            {
                try { param = et.get_Parameter(new Guid(key.Guid)); } catch { }
            }
            if (param == null && !string.IsNullOrWhiteSpace(key.Name))
            {
                try
                {
                    // Fallback by display name
                    foreach (Parameter p in et.Parameters)
                    {
                        try { if (string.Equals(p.Definition?.Name, key.Name, key.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) { param = p; break; } }
                        catch { }
                    }
                }
                catch { }
            }

            if (param == null) return (null, null);

            string? shown = null;
            try { shown = param.AsValueString(); } catch { }
            string? val = null;
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String: val = param.AsString(); break;
                    case StorageType.Integer: val = param.AsInteger().ToString(CultureInfo.InvariantCulture); break;
                    case StorageType.Double: val = param.AsDouble().ToString(CultureInfo.InvariantCulture); break;
                    case StorageType.ElementId: val = param.AsElementId()?.IntValue().ToString(CultureInfo.InvariantCulture); break;
                    default: val = shown; break;
                }
            }
            catch { val = shown; }

            return (val, shown);
        }

        private static bool Matches(string? value, string? when, string op)
        {
            value ??= string.Empty; when ??= string.Empty;
            if (when == "*") return true;
            if (op.Equals("contains", StringComparison.OrdinalIgnoreCase))
                return value.Contains(when);
            // default eq
            return string.Equals(value, when, StringComparison.Ordinal);
        }

        private static string StripPrefix(string name, List<string> prefixes)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var n = name.TrimStart();
            foreach (var p in prefixes)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (n.StartsWith(p, StringComparison.Ordinal))
                    return n.Substring(p.Length).TrimStart();
            }
            // Heuristic: strip leading parentheses like "(200mm) "
            try
            {
                if (n.Length > 2 && n[0] == '(')
                {
                    int end = n.IndexOf(')');
                    if (end > 0)
                    {
                        var inner = n.Substring(1, end - 1).Replace(" ", string.Empty);
                        if (inner.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                            return n.Substring(end + 1).TrimStart();
                    }
                }
            }
            catch { }
            return name;
        }

        private static ParamKey? ParseParamKey(JToken tok)
        {
            if (tok == null) return null;
            var jo = tok as JObject; if (jo == null) return null;
            var k = new ParamKey
            {
                BuiltInId = jo.Value<int?>("builtInId"),
                BuiltInName = jo.Value<string>("builtInName"),
                Guid = jo.Value<string>("guid"),
                Name = jo.Value<string>("name"),
                UseDisplay = jo.Value<bool?>("useDisplay") ?? true,
                Op = jo.Value<string>("op") ?? "eq",
                CaseInsensitive = jo.Value<bool?>("caseInsensitive") ?? true,
            };
            return k;
        }

        private static List<Rule> ParseRules(JToken tok)
        {
            var list = new List<Rule>();
            if (tok is JArray arr)
            {
                foreach (var t in arr.OfType<JObject>())
                {
                    var r = new Rule
                    {
                        When = t.Value<string>("when") ?? string.Empty,
                        Prefix = t.Value<string>("prefix") ?? string.Empty,
                        Suffix = t.Value<string>("suffix") ?? string.Empty
                    };
                    if (!string.IsNullOrWhiteSpace(r.When) && (!string.IsNullOrWhiteSpace(r.Prefix) || !string.IsNullOrWhiteSpace(r.Suffix)))
                        list.Add(r);
                }
            }
            return list;
        }

        private static List<int> ParseIntArray(JToken tok)
        {
            var list = new List<int>();
            if (tok is JArray arr)
            {
                foreach (var t in arr) { try { list.Add(Convert.ToInt32(t)); } catch { } }
            }
            return list;
        }

        private static List<string> ParseStringArray(JToken tok)
        {
            var list = new List<string>();
            if (tok is JArray arr)
            {
                foreach (var t in arr) { try { list.Add(Convert.ToString(t)); } catch { } }
            }
            return list;
        }

        private class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y) => (x?.IntValue() ?? 0) == (y?.IntValue() ?? 0);
            public int GetHashCode(ElementId obj) => obj?.IntValue() ?? 0;
        }
    }
}


