// ================================================================
// File: Commands/ViewOps/CompareViewStatesCommand.cs
// Purpose : Compare multiple views against a baseline for properties
//           and visibility (categories/filters/worksets; optional hidden elements)
// Target  : .NET Framework 4.8 / Revit 2023+
// Notes   : Heavy operations (element hidden scan) are opt-in via includeHiddenElements
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class CompareViewStatesCommand : IRevitCommandHandler
    {
        public string CommandName => "compare_view_states";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());

            var jViewIds = p["viewIds"] as JArray;
            var viewIdList = new List<int>();
            if (jViewIds != null)
            {
                foreach (var t in jViewIds)
                {
                    try { viewIdList.Add((int)t); } catch { }
                }
            }

            int baselineId = p.Value<int?>("baselineViewId") ?? 0;
            if (baselineId <= 0)
            {
                if (viewIdList.Count > 0) baselineId = viewIdList[0];
                else baselineId = uidoc?.ActiveView?.Id?.IntegerValue ?? 0;
            }

            if (baselineId <= 0) return new { ok = false, msg = "No baselineViewId and no viewIds/active view available." };

            var targetIds = new List<int>();
            if (viewIdList.Count > 0)
                targetIds.AddRange(viewIdList.Where(id => id > 0 && id != baselineId));

            if (targetIds.Count == 0)
            {
                try
                {
                    foreach (var uiv in uidoc.GetOpenUIViews())
                    {
                        int id = uiv.ViewId.IntegerValue;
                        if (id > 0 && id != baselineId) targetIds.Add(id);
                    }
                }
                catch { }
            }

            if (targetIds.Count == 0)
                return new { ok = false, msg = "No target views to compare." };

            bool includeHiddenElements = p.Value<bool?>("includeHiddenElements") ?? false;
            bool includeCategories = p.Value<bool?>("includeCategories") ?? true;
            bool includeFilters = p.Value<bool?>("includeFilters") ?? true;
            bool includeWorksets = p.Value<bool?>("includeWorksets") ?? true;

            var baseView = doc.GetElement(new ElementId(baselineId)) as View;
            if (baseView == null) return new { ok = false, msg = $"Baseline view {baselineId} not found." };

            var baseSnap = CaptureSnapshot(doc, baseView, includeHiddenElements, includeCategories, includeFilters, includeWorksets);

            var results = new List<object>();
            foreach (var tid in targetIds.Distinct())
            {
                var tv = doc.GetElement(new ElementId(tid)) as View;
                if (tv == null)
                {
                    results.Add(new { viewId = tid, error = "View not found" });
                    continue;
                }
                var tsnap = CaptureSnapshot(doc, tv, includeHiddenElements, includeCategories, includeFilters, includeWorksets);
                var diffs = BuildDiff(baseSnap, tsnap);
                results.Add(new { viewId = tid, viewName = tv.Name ?? string.Empty, diffs });
            }

            return new
            {
                ok = true,
                baseline = new { viewId = baselineId, viewName = baseView.Name ?? string.Empty },
                includeHiddenElements,
                includeCategories,
                includeFilters,
                includeWorksets,
                comparisons = results
            };
        }

        private static Dictionary<string, object> CaptureSnapshot(Document doc, View v,
            bool includeHiddenElements, bool includeCategories, bool includeFilters, bool includeWorksets)
        {
            var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            try { props["viewType"] = v.ViewType.ToString(); } catch { }
            try { props["scale"] = v.Scale; } catch { }
            try { props["discipline"] = v.Discipline.ToString(); } catch { }
            try { props["detailLevel"] = v.DetailLevel.ToString(); } catch { }
            try { props["templateViewId"] = v.ViewTemplateId != null ? v.ViewTemplateId.IntegerValue : -1; } catch { }

            var categories = new Dictionary<int, bool>();
            if (includeCategories)
            {
                try
                {
                    foreach (Category c in doc.Settings.Categories)
                    {
                        if (c == null) continue;
                        bool canHide = false; try { canHide = v.CanCategoryBeHidden(c.Id); } catch { canHide = false; }
                        if (!canHide) continue;
                        bool hidden = false; try { hidden = v.GetCategoryHidden(c.Id); } catch { hidden = false; }
                        categories[c.Id.IntegerValue] = hidden;
                    }
                }
                catch { }
            }

            var filters = new Dictionary<int, bool>();
            if (includeFilters)
            {
                try
                {
                    var fids = v.GetFilters();
                    if (fids != null)
                    {
                        foreach (var fid in fids)
                        {
                            bool vis = true; try { vis = v.GetFilterVisibility(fid); } catch { vis = true; }
                            filters[fid.IntegerValue] = vis;
                        }
                    }
                }
                catch { }
            }

            var worksets = new Dictionary<int, string>();
            if (includeWorksets && doc.IsWorkshared)
            {
                try
                {
                    var wsCol = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                    foreach (Workset ws in wsCol)
                    {
                        WorksetVisibility vis = WorksetVisibility.UseGlobalSetting;
                        try { vis = v.GetWorksetVisibility(ws.Id); } catch { vis = WorksetVisibility.UseGlobalSetting; }
                        worksets[ws.Id.IntegerValue] = vis.ToString();
                    }
                }
                catch { }
            }

            HashSet<int> hiddenElements = null;
            if (includeHiddenElements)
            {
                hiddenElements = new HashSet<int>();
                try
                {
                    var allIds = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds();
                    foreach (var id in allIds)
                    {
                        Element e = null; try { e = doc.GetElement(id); } catch { e = null; }
                        if (e == null) continue;
                        bool canHide = false; try { canHide = e.CanBeHidden(v); } catch { canHide = false; }
                        if (!canHide) continue;
                        bool isHidden = false; try { isHidden = e.IsHidden(v); } catch { isHidden = false; }
                        if (isHidden) hiddenElements.Add(id.IntegerValue);
                    }
                }
                catch { }
            }

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["viewId"] = v.Id.IntegerValue,
                ["name"] = v.Name ?? string.Empty,
                ["properties"] = props,
                ["categories"] = categories,
                ["filters"] = filters,
                ["worksets"] = worksets,
                ["hiddenElements"] = hiddenElements
            };
        }

        private static object BuildDiff(Dictionary<string, object> baseSnap, Dictionary<string, object> targetSnap)
        {
            var propsBase = baseSnap["properties"] as Dictionary<string, object> ?? new Dictionary<string, object>();
            var propsT = targetSnap["properties"] as Dictionary<string, object> ?? new Dictionary<string, object>();

            var propDiffs = new List<object>();
            foreach (var kv in propsBase)
            {
                var key = kv.Key;
                var v0 = kv.Value?.ToString() ?? string.Empty;
                var v1 = propsT.ContainsKey(key) ? (propsT[key]?.ToString() ?? string.Empty) : string.Empty;
                if (!string.Equals(v0, v1, StringComparison.Ordinal))
                {
                    propDiffs.Add(new { property = key, baseline = v0, target = v1 });
                }
            }

            var catBase = baseSnap["categories"] as Dictionary<int, bool> ?? new Dictionary<int, bool>();
            var catT = targetSnap["categories"] as Dictionary<int, bool> ?? new Dictionary<int, bool>();
            var catToHidden = new List<int>();
            var catToVisible = new List<int>();
            foreach (var kv in catBase)
            {
                int id = kv.Key; bool bHidden = kv.Value;
                if (!catT.TryGetValue(id, out var tHidden)) continue;
                if (bHidden != tHidden)
                {
                    if (tHidden) catToHidden.Add(id); else catToVisible.Add(id);
                }
            }

            var filtBase = baseSnap["filters"] as Dictionary<int, bool> ?? new Dictionary<int, bool>();
            var filtT = targetSnap["filters"] as Dictionary<int, bool> ?? new Dictionary<int, bool>();
            var filtToHidden = new List<int>();
            var filtToVisible = new List<int>();
            foreach (var kv in filtBase)
            {
                int id = kv.Key; bool bVis = kv.Value;
                if (!filtT.TryGetValue(id, out var tVis)) continue;
                if (bVis != tVis)
                {
                    if (tVis) filtToVisible.Add(id); else filtToHidden.Add(id);
                }
            }

            var wsBase = baseSnap["worksets"] as Dictionary<int, string> ?? new Dictionary<int, string>();
            var wsT = targetSnap["worksets"] as Dictionary<int, string> ?? new Dictionary<int, string>();
            var wsChanged = new List<object>();
            foreach (var kv in wsBase)
            {
                int id = kv.Key; string b = kv.Value ?? "";
                if (!wsT.TryGetValue(id, out var t)) continue;
                t = t ?? "";
                if (!string.Equals(b, t, StringComparison.Ordinal))
                {
                    wsChanged.Add(new { worksetId = id, baseline = b, target = t });
                }
            }

            List<int> heBase = null;
            try { heBase = (baseSnap["hiddenElements"] as HashSet<int>)?.ToList(); } catch { }
            List<int> heT = null;
            try { heT = (targetSnap["hiddenElements"] as HashSet<int>)?.ToList(); } catch { }
            var heOnlyBaseline = new List<int>();
            var heOnlyTarget = new List<int>();
            if (heBase != null && heT != null)
            {
                var sb = new HashSet<int>(heBase);
                var st = new HashSet<int>(heT);
                foreach (var id in sb) if (!st.Contains(id)) heOnlyBaseline.Add(id);
                foreach (var id in st) if (!sb.Contains(id)) heOnlyTarget.Add(id);
            }

            return new
            {
                properties = propDiffs,
                categories = new { toHidden = catToHidden, toVisible = catToVisible },
                filters = new { toHidden = filtToHidden, toVisible = filtToVisible },
                worksets = new { changed = wsChanged },
                hiddenElements = (heBase != null && heT != null) ? new { onlyBaseline = heOnlyBaseline, onlyTarget = heOnlyTarget } : null
            };
        }
    }
}

