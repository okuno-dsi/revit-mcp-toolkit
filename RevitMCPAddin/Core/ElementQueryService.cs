// ================================================================
// File: Core/ElementQueryService.cs
// Purpose: element.search_elements / element.query_elements core engine
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Notes  :
//  - Keep outputs "AI-friendly" (small summaries + optional parameter snapshots)
//  - Prefer quick filters (collector/category/bbox) then slow filters (level/name/params)
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RevitMCPAddin.Core.Progress;
using RevitMCPAddin.Models;

namespace RevitMCPAddin.Core
{
    internal static class ElementQueryService
    {
        private const int SearchMaxResultsHardLimit = 500;
        private const int QueryMaxResultsHardLimit = 2000;

        public static object SearchElements(Document doc, SearchElementsRequest req, string progressJobId = null)
        {
            if (doc == null) return new { ok = false, msg = "No active document." };
            if (req == null) req = new SearchElementsRequest();

            var keyword = (req.Keyword ?? string.Empty).Trim();
            if (keyword.Length == 0)
                return new { ok = false, code = "INVALID_PARAMS", msg = "keyword is required." };

            int max = req.MaxResults > 0 ? req.MaxResults : 50;
            if (max > SearchMaxResultsHardLimit) max = SearchMaxResultsHardLimit;

            View view = null;
            if (req.ViewId.HasValue && req.ViewId.Value > 0)
            {
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(req.ViewId.Value)) as View;
                if (view == null)
                    return new { ok = false, code = "VIEW_NOT_FOUND", msg = $"ViewId not found: {req.ViewId.Value}" };
            }

            var warnings = new List<string>();
            object catErr;
            var categories = ResolveCategoriesOrNull(req.Categories, warnings, failWhenNoneResolved: true, out catErr);
            if (catErr != null) return catErr;

            var col = view != null ? new FilteredElementCollector(doc, view.Id) : new FilteredElementCollector(doc);
            if (!req.IncludeTypes) col = col.WhereElementIsNotElementType();

            if (categories != null && categories.Count > 0)
                col = col.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(categories)));

            var elems = col.ToElements().ToList();

            // Level post-filter (best-effort)
            if (req.LevelId.HasValue && req.LevelId.Value > 0)
            {
                int target = req.LevelId.Value;
                elems = elems.Where(e => GetBestEffortLevelId(e) == target).ToList();
            }

            var cmp = req.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var candidatesCount = elems.Count;

            ProgressReporter rep = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(progressJobId) && candidatesCount > 0)
                {
                    rep = ProgressHub.Start(progressJobId, "element.search_elements", candidatesCount, TimeSpan.FromSeconds(1));
                    rep.SetMessage("scanning");
                }
            }
            catch { rep = null; }

            // Keyword match (name/type/category/uniqueId)
            var matched = new List<Element>(Math.Min(max, candidatesCount));
            foreach (var e in elems)
            {
                try { rep?.Step(null, 1); } catch { /* ignore */ }

                if (matched.Count >= max) continue;
                if (MatchesKeyword(doc, e, keyword, cmp))
                    matched.Add(e);
            }

            try { rep?.ReportNow("done"); } catch { /* ignore */ }

            matched = matched.OrderBy(e => e.Id.IntValue()).ToList();

            var outElems = new List<Dictionary<string, object>>(matched.Count);
            foreach (var e in matched)
            {
                outElems.Add(BuildElementSummary(doc, e, includeTypeId: true, includeBoundingBox: false, includeParameters: null));
            }

            return new
            {
                ok = true,
                elements = outElems,
                counts = new { candidates = candidatesCount, returned = outElems.Count },
                warnings = warnings.Count > 0 ? warnings : null
            };
        }

        public static object QueryElements(Document doc, QueryElementsRequest req, string progressJobId = null)
        {
            if (doc == null) return new { ok = false, msg = "No active document." };
            if (req == null) req = new QueryElementsRequest();

            var applied = new List<string>();
            var warnings = new List<string>();

            var scope = req.Scope ?? new QueryScope();
            var filters = req.Filters ?? new QueryFilters();
            var options = req.Options ?? new QueryOptions();

            int limit = options.MaxResults > 0 ? options.MaxResults : 200;
            if (limit > QueryMaxResultsHardLimit) limit = QueryMaxResultsHardLimit;

            View view = null;
            if (scope.ViewId.HasValue && scope.ViewId.Value > 0)
            {
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(scope.ViewId.Value)) as View;
                if (view == null)
                    return new { ok = false, code = "VIEW_NOT_FOUND", msg = $"ViewId not found: {scope.ViewId.Value}" };
                applied.Add("scope.viewId=" + scope.ViewId.Value.ToString(CultureInfo.InvariantCulture));
            }

            var baseCollector = view != null ? new FilteredElementCollector(doc, view.Id) : new FilteredElementCollector(doc);
            baseCollector = baseCollector.WhereElementIsNotElementType();
            int initial = SafeGetElementCount(baseCollector);

            // Quick filters: categories, bbox, (optional) class filters via ElementClassFilter
            var col = baseCollector;

            object catErr;
            var categories = ResolveCategoriesOrNull(filters.Categories, warnings, failWhenNoneResolved: false, out catErr);
            if (catErr != null) return catErr;
            if (categories != null && categories.Count > 0)
            {
                col = col.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(categories)));
                applied.Add("categories=" + string.Join(",", categories.Select(x => x.ToString())));
            }

            // Class quick filter (best-effort)
            var classNames = (filters.ClassNames ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ElementFilter classQuick = TryBuildClassQuickFilter(classNames, warnings);
            if (classQuick != null)
            {
                col = col.WherePasses(classQuick);
                applied.Add("classNames=" + string.Join(",", classNames));
            }

            // BBox quick filter (if provided)
            if (filters.BBox != null && filters.BBox.Min != null && filters.BBox.Max != null)
            {
                Outline outline;
                string bboxError;
                if (!TryBuildOutline(filters.BBox, out outline, out bboxError))
                    return new { ok = false, code = "INVALID_BBOX", msg = bboxError ?? "BBox requires min/max." };

                var mode = (filters.BBox.Mode ?? "intersects").Trim().ToLowerInvariant();
                if (mode == "inside")
                    col = col.WherePasses(new BoundingBoxIsInsideFilter(outline));
                else if (mode == "intersects")
                    col = col.WherePasses(new BoundingBoxIntersectsFilter(outline));
                else
                    return new { ok = false, code = "INVALID_BBOX_MODE", msg = "Unsupported bbox.mode: " + mode };

                applied.Add("bbox." + mode);
            }

            int afterQuick = SafeGetElementCount(col);
            var elems = col.ToElements().ToList();

            // includeHiddenInView (post-filter)
            if (view != null && scope.IncludeHiddenInView == false)
            {
                elems = elems.Where(e =>
                {
                    try { return !e.IsHidden(view); }
                    catch { return true; }
                }).ToList();
                applied.Add("scope.includeHiddenInView=false");
            }

            // Class post-filter (fallback when quick filter was not possible)
            if (classQuick == null && classNames.Count > 0)
            {
                var inc = new HashSet<string>(classNames, StringComparer.OrdinalIgnoreCase);
                elems = elems.Where(e => inc.Contains(e.GetType().Name)).ToList();
                applied.Add("classNames(post)=" + string.Join(",", classNames));
            }

            // Level post-filter (best-effort)
            if (filters.LevelId.HasValue && filters.LevelId.Value > 0)
            {
                int target = filters.LevelId.Value;
                elems = elems.Where(e => GetBestEffortLevelId(e) == target).ToList();
                applied.Add("levelId=" + target.ToString(CultureInfo.InvariantCulture));
            }

            // Name filter (post-filter)
            if (filters.Name != null && !string.IsNullOrWhiteSpace(filters.Name.Value))
            {
                var nf = filters.Name;
                var mode = (nf.Mode ?? "contains").Trim().ToLowerInvariant();
                var value = nf.Value ?? string.Empty;
                var cmp = nf.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                if (mode == "equals")
                    elems = elems.Where(e => string.Equals(SafeName(e), value, cmp)).ToList();
                else if (mode == "startswith")
                    elems = elems.Where(e => SafeName(e).StartsWith(value, cmp)).ToList();
                else if (mode == "endswith")
                    elems = elems.Where(e => SafeName(e).EndsWith(value, cmp)).ToList();
                else if (mode == "regex")
                {
                    try
                    {
                        var rx = new Regex(value, nf.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                        elems = elems.Where(e => rx.IsMatch(SafeName(e))).ToList();
                    }
                    catch (Exception ex)
                    {
                        return new { ok = false, code = "INVALID_REGEX", msg = ex.Message };
                    }
                }
                else if (mode == "contains")
                    elems = elems.Where(e => SafeName(e).IndexOf(value, cmp) >= 0).ToList();
                else
                    return new { ok = false, code = "INVALID_NAME_MODE", msg = "Unsupported name.mode: " + mode };

                applied.Add("name." + mode);
            }

            // Parameter conditions (post-filter first version)
            var conds = filters.Parameters ?? new List<ParameterCondition>();
            conds = conds.Where(c => c != null && c.Param != null && !string.IsNullOrWhiteSpace(c.Param.Value)).ToList();
            if (conds.Count > 0)
            {
                foreach (var c in conds)
                {
                    var op = (c.Op ?? "equals").Trim().ToLowerInvariant();
                    if (op == "range" && (!c.Min.HasValue || !c.Max.HasValue))
                        return new { ok = false, code = "INVALID_PARAMS", msg = "range requires min and max." };
                }

                // Parameter matching can be expensive; expose determinate progress when possible.
                ProgressReporter rep = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(progressJobId) && elems.Count > 0)
                    {
                        rep = ProgressHub.Start(progressJobId, "element.query_elements:parameters", elems.Count, TimeSpan.FromSeconds(1));
                        rep.SetMessage("parameters");
                    }
                }
                catch { rep = null; }

                var next = new List<Element>(elems.Count);
                foreach (var e in elems)
                {
                    try { rep?.Step(null, 1); } catch { /* ignore */ }
                    if (MatchesAllConditions(doc, e, conds)) next.Add(e);
                }
                try { rep?.ReportNow("done"); } catch { /* ignore */ }

                elems = next;
                applied.Add("parameters=" + conds.Count.ToString(CultureInfo.InvariantCulture));
            }

            int afterSlow = elems.Count;

            // Order + limit
            var orderBy = (options.OrderBy ?? "id").Trim().ToLowerInvariant();
            if (orderBy == "name")
                elems = elems.OrderBy(e => SafeName(e), StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id.IntValue()).ToList();
            else
                elems = elems.OrderBy(e => e.Id.IntValue()).ToList();

            if (elems.Count > limit) elems = elems.Take(limit).ToList();

            var includeParams = options.IncludeParameters != null && options.IncludeParameters.Count > 0 ? options.IncludeParameters : null;

            var outElems = new List<Dictionary<string, object>>(elems.Count);
            foreach (var e in elems)
            {
                outElems.Add(BuildElementSummary(
                    doc,
                    e,
                    includeTypeId: options.IncludeElementType,
                    includeBoundingBox: options.IncludeBoundingBox,
                    includeParameters: includeParams));
            }

            var diagnostics = new
            {
                appliedFilters = applied,
                counts = new
                {
                    initial = initial,
                    afterQuickFilters = afterQuick,
                    afterSlowFilters = afterSlow,
                    returned = outElems.Count
                },
                warnings = warnings.Count > 0 ? warnings : null
            };

            return new { ok = true, elements = outElems, diagnostics };
        }

        // -------------------------- helpers (added below) --------------------------
        private static int SafeGetElementCount(FilteredElementCollector c)
        {
            try { return c.GetElementCount(); } catch { return -1; }
        }

        private static IReadOnlyList<BuiltInCategory> ResolveCategoriesOrNull(
            List<string> names,
            List<string> warnings,
            bool failWhenNoneResolved,
            out object errorResponse)
        {
            errorResponse = null;
            if (names == null || names.Count == 0) return null;
            var resolved = new List<BuiltInCategory>();
            var unknown = new List<string>();
            foreach (var raw in names)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                BuiltInCategory bic;
                if (CategoryResolver.TryResolveCategory(raw, out bic))
                    resolved.Add(bic);
                else
                    unknown.Add(raw.Trim());
            }
            resolved = resolved.Distinct().ToList();
            if (unknown.Count > 0)
                warnings.Add("Unsupported category name(s): " + string.Join(", ", unknown) + " (use OST_* enum names if unsure)");
            if (resolved.Count == 0 && failWhenNoneResolved)
            {
                errorResponse = new { ok = false, code = "INVALID_CATEGORY", msg = "No supported categories resolved: " + string.Join(", ", unknown) };
                return null;
            }
            return resolved;
        }

        private static bool MatchesKeyword(Document doc, Element e, string keyword, StringComparison cmp)
        {
            if (e == null) return false;
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            try { if (SafeName(e).IndexOf(keyword, cmp) >= 0) return true; } catch { }
            try { var cat = e.Category?.Name ?? string.Empty; if (cat.Length > 0 && cat.IndexOf(keyword, cmp) >= 0) return true; } catch { }
            try { var uid = e.UniqueId ?? string.Empty; if (uid.Length > 0 && uid.IndexOf(keyword, cmp) >= 0) return true; } catch { }
            try
            {
                var tid = e.GetTypeId();
                if (tid != ElementId.InvalidElementId)
                {
                    var t = doc.GetElement(tid);
                    if (t != null && SafeName(t).IndexOf(keyword, cmp) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        private static string SafeName(Element e)
        {
            if (e == null) return string.Empty;
            try
            {
                var n = e.Name;
                return string.IsNullOrWhiteSpace(n) ? e.Id.IntValue().ToString(CultureInfo.InvariantCulture) : n;
            }
            catch
            {
                return e.Id.IntValue().ToString(CultureInfo.InvariantCulture);
            }
        }

        private static int? GetBestEffortLevelId(Element e)
        {
            if (e == null) return null;

            // Common typed accessors (fast path)
            try
            {
                switch (e)
                {
                    case Level lv:
                        return lv.Id.IntValue();
                    case Wall w:
                        if (w.LevelId != ElementId.InvalidElementId) return w.LevelId.IntValue();
                        break;
                    case Floor f:
                        if (f.LevelId != ElementId.InvalidElementId) return f.LevelId.IntValue();
                        break;
                    case Ceiling c:
                        if (c.LevelId != ElementId.InvalidElementId) return c.LevelId.IntValue();
                        break;
                    case RoofBase rb:
                        if (rb.LevelId != ElementId.InvalidElementId) return rb.LevelId.IntValue();
                        break;
                    case FamilyInstance fi:
                        // Some instances expose LevelId but it can be Invalid (e.g. structural framing).
                        if (fi.LevelId != ElementId.InvalidElementId) return fi.LevelId.IntValue();
                        break;
                    case Autodesk.Revit.DB.Architecture.Room room:
                        if (room.LevelId != ElementId.InvalidElementId) return room.LevelId.IntValue();
                        break;
                    case Autodesk.Revit.DB.Mechanical.Space space:
                        if (space.LevelId != ElementId.InvalidElementId) return space.LevelId.IntValue();
                        break;
                    case Area area:
                        if (area.LevelId != ElementId.InvalidElementId) return area.LevelId.IntValue();
                        break;
                }
            }
            catch { /* ignore */ }

            // Generic property reflection (some elements expose LevelId)
            try
            {
                var prop = e.GetType().GetProperty("LevelId");
                if (prop != null && prop.PropertyType == typeof(ElementId))
                {
                    var id = prop.GetValue(e, null) as ElementId;
                    if (id != null && id != ElementId.InvalidElementId) return id.IntValue();
                }
            }
            catch { /* ignore */ }

            // Generic parameter-based resolution (best-effort)
            try
            {
                var p = e.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                        ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                        ?? e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                        ?? e.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)
                        ?? e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId) return id.IntValue();
                }
            }
            catch { /* ignore */ }

            return null;
        }
        private static bool TryBuildOutline(BBoxFilter bbox, out Outline outline, out string error)
        {
            outline = null;
            error = null;

            try
            {
                if (bbox == null || bbox.Min == null || bbox.Max == null)
                {
                    error = "BBox requires min and max.";
                    return false;
                }

                var minUnit = string.IsNullOrWhiteSpace(bbox.Min.Unit) ? "mm" : bbox.Min.Unit;
                var maxUnit = string.IsNullOrWhiteSpace(bbox.Max.Unit) ? "mm" : bbox.Max.Unit;

                double minX, minY, minZ;
                double maxX, maxY, maxZ;

                if (!TryToInternalLength(bbox.Min.X, minUnit, out minX)) { error = "Unit not supported: " + minUnit; return false; }
                if (!TryToInternalLength(bbox.Min.Y, minUnit, out minY)) { error = "Unit not supported: " + minUnit; return false; }
                if (!TryToInternalLength(bbox.Min.Z, minUnit, out minZ)) { error = "Unit not supported: " + minUnit; return false; }

                if (!TryToInternalLength(bbox.Max.X, maxUnit, out maxX)) { error = "Unit not supported: " + maxUnit; return false; }
                if (!TryToInternalLength(bbox.Max.Y, maxUnit, out maxY)) { error = "Unit not supported: " + maxUnit; return false; }
                if (!TryToInternalLength(bbox.Max.Z, maxUnit, out maxZ)) { error = "Unit not supported: " + maxUnit; return false; }

                var min = new XYZ(Math.Min(minX, maxX), Math.Min(minY, maxY), Math.Min(minZ, maxZ));
                var max = new XYZ(Math.Max(minX, maxX), Math.Max(minY, maxY), Math.Max(minZ, maxZ));

                outline = new Outline(min, max);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                outline = null;
                return false;
            }
        }

        // Unit string (mm|cm|m|ft|in) → internal feet (length only)
        private static bool TryToInternalLength(double value, string unit, out double feet)
        {
            feet = value;
            var u = (unit ?? "ft").Trim().ToLowerInvariant();
            try
            {
                switch (u)
                {
                    case "mm":
                        feet = UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);
                        return true;
                    case "cm":
                        feet = UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Centimeters);
                        return true;
                    case "m":
                    case "meter":
                    case "meters":
                        feet = UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters);
                        return true;
                    case "ft":
                    case "feet":
                        feet = value;
                        return true;
                    case "in":
                    case "inch":
                    case "inches":
                        feet = UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Inches);
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                feet = value;
                return false;
            }
        }
        private static ElementFilter TryBuildClassQuickFilter(List<string> classNames, List<string> warnings)
        {
            if (classNames == null || classNames.Count == 0) return null;

            try
            {
                var asm = typeof(Element).Assembly;
                var filters = new List<ElementFilter>();

                foreach (var n0 in classNames)
                {
                    if (string.IsNullOrWhiteSpace(n0)) continue;
                    var n = n0.Trim();
                    var full = n.IndexOf('.') >= 0 ? n : ("Autodesk.Revit.DB." + n);

                    Type t = null;
                    try { t = asm.GetType(full, throwOnError: false, ignoreCase: true); } catch { t = null; }
                    if (t == null || !typeof(Element).IsAssignableFrom(t))
                    {
                        if (warnings != null)
                            warnings.Add("Unsupported className (post-filter will be used if possible): " + n);
                        continue;
                    }

                    filters.Add(new ElementClassFilter(t));
                }

                if (filters.Count == 0) return null;
                if (filters.Count == 1) return filters[0];
                return new LogicalOrFilter(filters);
            }
            catch
            {
                return null;
            }
        }
        private static bool MatchesAllConditions(Document doc, Element e, List<ParameterCondition> conds)
        {
            if (conds == null || conds.Count == 0) return true;
            foreach (var c in conds)
            {
                if (!MatchesCondition(doc, e, c))
                    return false;
            }
            return true;
        }

        private static bool MatchesCondition(Document doc, Element e, ParameterCondition c)
        {
            if (e == null) return false;
            if (c == null || c.Param == null || string.IsNullOrWhiteSpace(c.Param.Value)) return true;

            Parameter p = null;
            try
            {
                var kind = (c.Param.Kind ?? "name").Trim();
                if (kind.Equals("builtin", StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse<BuiltInParameter>(c.Param.Value, true, out var bip))
                        p = e.get_Parameter(bip);
                }
                else if (kind.Equals("guid", StringComparison.OrdinalIgnoreCase))
                {
                    Guid g;
                    if (Guid.TryParse(c.Param.Value, out g))
                        p = e.get_Parameter(g);
                }
                else if (kind.Equals("paramid", StringComparison.OrdinalIgnoreCase))
                {
                    int pid;
                    if (int.TryParse(c.Param.Value, out pid))
                    {
                        foreach (Parameter pr in e.Parameters)
                        {
                            try
                            {
                                if (pr != null && pr.Id != null && pr.Id.IntValue() == pid) { p = pr; break; }
                            }
                            catch { }
                        }
                    }
                }
                else
                {
                    p = e.LookupParameter(c.Param.Value);
                }
            }
            catch
            {
                p = null;
            }

            if (p == null) return false;

            var op = (c.Op ?? "equals").Trim().ToLowerInvariant();

            // String comparisons
            if (p.StorageType == StorageType.String)
            {
                var s = (p.AsString() ?? p.AsValueString() ?? string.Empty);
                var target = c.Value ?? string.Empty;
                var cmp = c.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                if (op == "contains") return s.IndexOf(target, cmp) >= 0;
                if (op == "equals") return string.Equals(s, target, cmp);
                if (op == "startswith") return s.StartsWith(target, cmp);
                if (op == "endswith") return s.EndsWith(target, cmp);
                return false;
            }

            // Integer comparisons
            if (p.StorageType == StorageType.Integer)
            {
                int v = p.AsInteger();
                if (op == "equals") return v == SafeToInt(c);
                if (op == "gt") return v > SafeToInt(c);
                if (op == "gte") return v >= SafeToInt(c);
                if (op == "lt") return v < SafeToInt(c);
                if (op == "lte") return v <= SafeToInt(c);
                if (op == "range")
                {
                    if (!c.Min.HasValue || !c.Max.HasValue) return false;
                    var min = (int)Math.Round(c.Min.Value);
                    var max = (int)Math.Round(c.Max.Value);
                    return v >= Math.Min(min, max) && v <= Math.Max(min, max);
                }
                return false;
            }

            // ElementId comparisons
            if (p.StorageType == StorageType.ElementId)
            {
                int v = 0;
                try { v = p.AsElementId()?.IntValue() ?? 0; } catch { v = 0; }
                if (op == "equals") return v == SafeToInt(c);
                if (op == "gt") return v > SafeToInt(c);
                if (op == "gte") return v >= SafeToInt(c);
                if (op == "lt") return v < SafeToInt(c);
                if (op == "lte") return v <= SafeToInt(c);
                return false;
            }

            // Double comparisons (length-only conversion in first version)
            if (p.StorageType == StorageType.Double)
            {
                double v = p.AsDouble(); // internal units

                if (op == "range")
                {
                    if (!c.Min.HasValue || !c.Max.HasValue) return false;
                    double min, max;
                    if (!TryToInternalLength(c.Min.Value, c.Unit ?? "ft", out min)) return false;
                    if (!TryToInternalLength(c.Max.Value, c.Unit ?? "ft", out max)) return false;
                    return v >= Math.Min(min, max) && v <= Math.Max(min, max);
                }

                double rhsUser;
                if (c.Number.HasValue) rhsUser = c.Number.Value;
                else if (c.Min.HasValue) rhsUser = c.Min.Value;
                else if (c.Max.HasValue) rhsUser = c.Max.Value;
                else
                {
                    if (!double.TryParse(c.Value ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out rhsUser))
                        return false;
                }

                double rhs;
                if (!TryToInternalLength(rhsUser, c.Unit ?? "ft", out rhs))
                    return false;

                const double eps = 1e-9;
                if (op == "equals") return Math.Abs(v - rhs) <= eps;
                if (op == "gt") return v > rhs + eps;
                if (op == "gte") return v >= rhs - eps;
                if (op == "lt") return v < rhs - eps;
                if (op == "lte") return v <= rhs + eps;
                return false;
            }

            return false;
        }

        private static int SafeToInt(ParameterCondition c)
        {
            try
            {
                if (c == null) return 0;
                if (c.Number.HasValue) return (int)Math.Round(c.Number.Value);
                if (c.Min.HasValue) return (int)Math.Round(c.Min.Value);
                if (c.Max.HasValue) return (int)Math.Round(c.Max.Value);

                int i;
                if (!string.IsNullOrWhiteSpace(c.Value) && int.TryParse(c.Value, out i)) return i;

                double d;
                if (!string.IsNullOrWhiteSpace(c.Value) && double.TryParse(c.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return (int)Math.Round(d);
            }
            catch { }
            return 0;
        }

        private static Dictionary<string, object> BuildElementSummary(Document doc, Element e, bool includeTypeId, bool includeBoundingBox, List<string> includeParameters)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (e == null) return d;

            d["id"] = e.Id.IntValue();
            try { d["uniqueId"] = e.UniqueId; } catch { d["uniqueId"] = null; }
            try { d["name"] = SafeName(e); } catch { d["name"] = e.Id.IntValue().ToString(CultureInfo.InvariantCulture); }
            try { d["category"] = e.Category?.Name; } catch { d["category"] = null; }
            try { d["className"] = e.GetType().Name; } catch { d["className"] = "Element"; }
            try { d["levelId"] = GetBestEffortLevelId(e); } catch { d["levelId"] = null; }

            if (includeTypeId)
            {
                try
                {
                    var tid = e.GetTypeId();
                    d["typeId"] = tid != ElementId.InvalidElementId ? tid.IntValue() : (int?)null;
                }
                catch { d["typeId"] = null; }
            }

            if (includeBoundingBox)
            {
                try
                {
                    var bb = e.get_BoundingBox(null);
                    if (bb != null)
                    {
                        var min = bb.Min;
                        var max = bb.Max;
                        d["bbox"] = new
                        {
                            min = new { x = UnitHelper.FtToMm(min.X), y = UnitHelper.FtToMm(min.Y), z = UnitHelper.FtToMm(min.Z), unit = "mm" },
                            max = new { x = UnitHelper.FtToMm(max.X), y = UnitHelper.FtToMm(max.Y), z = UnitHelper.FtToMm(max.Z), unit = "mm" }
                        };
                    }
                    else
                    {
                        d["bbox"] = null;
                    }
                }
                catch
                {
                    d["bbox"] = null;
                }
            }

            if (includeParameters != null && includeParameters.Count > 0)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pn in includeParameters)
                {
                    var key = (pn ?? string.Empty).Trim();
                    if (key.Length == 0) continue;
                    string val;
                    if (TryGetParamAsString(doc, e, key, out val))
                        map[key] = val ?? "";
                }
                d["parameters"] = map;
            }

            return d;
        }

        private static bool TryGetParamAsString(Document doc, Element e, string paramName, out string value)
        {
            value = null;
            if (e == null) return false;
            if (string.IsNullOrWhiteSpace(paramName)) return false;

            Parameter p = null;
            try
            {
                if (Enum.TryParse<BuiltInParameter>(paramName.Trim(), true, out var bip))
                    p = e.get_Parameter(bip);
            }
            catch { p = null; }

            if (p == null)
            {
                try { p = e.LookupParameter(paramName); } catch { p = null; }
            }

            // Fallback: check type element when instance doesn't have it
            if (p == null)
            {
                try
                {
                    var tid = e.GetTypeId();
                    if (tid != ElementId.InvalidElementId)
                    {
                        var t = doc.GetElement(tid);
                        if (t != null)
                        {
                            try
                            {
                                if (Enum.TryParse<BuiltInParameter>(paramName.Trim(), true, out var bip2))
                                    p = t.get_Parameter(bip2);
                            }
                            catch { p = null; }

                            if (p == null)
                            {
                                try { p = t.LookupParameter(paramName); } catch { p = null; }
                            }
                        }
                    }
                }
                catch { p = null; }
            }

            if (p == null) return false;

            try
            {
                value = p.AsString() ?? p.AsValueString() ?? "";
                return true;
            }
            catch
            {
                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Integer:
                            value = p.AsInteger().ToString(CultureInfo.InvariantCulture);
                            return true;
                        case StorageType.Double:
                            value = p.AsDouble().ToString("0.########", CultureInfo.InvariantCulture);
                            return true;
                        case StorageType.ElementId:
                            value = (p.AsElementId()?.IntValue() ?? 0).ToString(CultureInfo.InvariantCulture);
                            return true;
                        case StorageType.String:
                            value = p.AsString() ?? "";
                            return true;
                        default:
                            value = "";
                            return true;
                    }
                }
                catch
                {
                    value = null;
                    return false;
                }
            }
        }
    }
}
