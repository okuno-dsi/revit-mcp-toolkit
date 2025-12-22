// RevitMCPAddin/Commands/ViewOps/SaveViewStateCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// save_view_state
    /// Capture current view state: template binding, temporary hide/isolate, category visibility,
    /// filter visibility and workset visibility. Optionally capture hidden element ids.
    /// Params: { viewId?, includeHiddenElements?:bool }
    /// Result: { ok, viewId, viewName, viewType, state:{...} }
    /// </summary>
    public class SaveViewStateCommand : IRevitCommandHandler
    {
        public string CommandName => "save_view_state";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            // resolve view
            View view = ViewUtil.ResolveView(doc, p) ?? uidoc.ActiveView;
            if (view == null) return new { ok = false, msg = "No active view and no viewId specified." };

            bool includeHiddenElements = p.Value<bool?>("includeHiddenElements") ?? false;

            int viewId = view.Id.IntValue();
            string viewName = view.Name ?? string.Empty;
            string viewType = view.ViewType.ToString();

            // template / temp mode
            int templateViewId = view.ViewTemplateId != null ? view.ViewTemplateId.IntValue() : -1;
            bool tempHideIsolate = false;
            try { tempHideIsolate = view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate); } catch { tempHideIsolate = false; }

            // categories
            var catList = new List<object>();
            try
            {
                foreach (Category c in doc.Settings.Categories)
                {
                    if (c == null) continue;
                    bool canHide = false;
                    try { canHide = view.CanCategoryBeHidden(c.Id); } catch { canHide = false; }
                    if (!canHide) continue;
                    bool hidden = false;
                    try { hidden = view.GetCategoryHidden(c.Id); } catch { hidden = false; }
                    catList.Add(new { categoryId = c.Id.IntValue(), hidden });
                }
            }
            catch { /* best effort */ }

            // filters
            var filters = new List<object>();
            try
            {
                var fids = view.GetFilters();
                if (fids != null)
                {
                    foreach (var fid in fids)
                    {
                        bool vis = true; try { vis = view.GetFilterVisibility(fid); } catch { vis = true; }
                        filters.Add(new { filterId = fid.IntValue(), visible = vis });
                    }
                }
            }
            catch { /* best effort */ }

            // worksets
            var worksets = new List<object>();
            try
            {
                if (doc.IsWorkshared)
                {
                    var wsCol = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                    foreach (Workset ws in wsCol)
                    {
                        WorksetVisibility vis = WorksetVisibility.UseGlobalSetting;
                        try { vis = view.GetWorksetVisibility(ws.Id); } catch { vis = WorksetVisibility.UseGlobalSetting; }
                        worksets.Add(new { worksetId = ws.Id.IntValue(), visibility = vis.ToString() });
                    }
                }
            }
            catch { /* best effort */ }

            // hidden elements (optional, potentially heavy)
            List<int> hiddenElements = null;
            if (includeHiddenElements)
            {
                hiddenElements = new List<int>();
                try
                {
                    var allIds = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElementIds();
                    foreach (var id in allIds)
                    {
                        Element e = null; try { e = doc.GetElement(id); } catch { e = null; }
                        if (e == null) continue;
                        bool canHide = false; try { canHide = e.CanBeHidden(view); } catch { canHide = false; }
                        if (!canHide) continue;
                        bool isHidden = false; try { isHidden = e.IsHidden(view); } catch { isHidden = false; }
                        if (isHidden) hiddenElements.Add(id.IntValue());
                    }
                }
                catch { /* best effort */ }
            }

            var state = new
            {
                templateViewId,
                tempHideIsolate,
                categories = catList,
                filters,
                worksets,
                hiddenElements
            };

            return new
            {
                ok = true,
                viewId,
                viewName,
                viewType,
                state
            };
        }
    }
}

