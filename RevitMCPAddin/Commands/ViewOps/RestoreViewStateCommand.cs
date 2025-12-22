// RevitMCPAddin/Commands/ViewOps/RestoreViewStateCommand.cs
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
    /// restore_view_state
    /// Restore view state previously captured by save_view_state.
    /// Params: { viewId?, state:{...}, apply:{ template?, categories?, filters?, worksets?, hiddenElements? } }
    /// Notes:
    ///  - If a templateViewId is present and apply.template != false, the template is assigned first.
    ///    Category/filter changes may be overridden by templates; for full fidelity prefer a dedicated API in template-less views.
    /// </summary>
    public class RestoreViewStateCommand : IRevitCommandHandler
    {
        public string CommandName => "restore_view_state";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var state = p["state"] as JObject;
            if (state == null) return new { ok = false, msg = "state object is required (from save_view_state)." };

            // Resolve target view
            View view = null;
            try { view = ViewUtil.ResolveView(doc, p); } catch { view = null; }
            if (view == null)
            {
                int sid = state.Value<int?>("viewId") ?? 0; // optional carry-over
                view = sid > 0 ? (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(sid)) as View) : uidoc.ActiveView;
            }
            if (view == null) return new { ok = false, msg = "Target view not found." };

            // Apply flags
            var apply = p["apply"] as JObject ?? new JObject();
            bool applyTemplate = apply.Value<bool?>("template") ?? true;
            bool applyCategories = apply.Value<bool?>("categories") ?? true;
            bool applyFilters = apply.Value<bool?>("filters") ?? true;
            bool applyWorksets = apply.Value<bool?>("worksets") ?? true;
            bool applyHiddenElements = apply.Value<bool?>("hiddenElements") ?? false;

            int templateViewId = state.Value<int?>("templateViewId") ?? -1;
            bool desiredTempMode = state.Value<bool?>("tempHideIsolate") ?? false;

            int changedCats = 0, changedFilters = 0, changedWorksets = 0, hiddenCount = 0;
            bool templateApplied = false;

            using (var tx = new Transaction(doc, "[MCP] Restore View State"))
            {
                tx.Start();

                // Template assignment first (if requested)
                if (applyTemplate && templateViewId > 0)
                {
                    try
                    {
                        var tmpl = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(templateViewId)) as View;
                        if (tmpl != null && tmpl.IsTemplate)
                        {
                            view.ViewTemplateId = tmpl.Id;
                            templateApplied = true;
                        }
                    }
                    catch (Exception ex) { RevitLogger.Warn("Restore template failed", ex); }
                }

                // Temporary view mode: We only disable if snapshot had it off
                try
                {
                    if (!desiredTempMode && view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                        view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                }
                catch { /* ignore */ }

                // Categories (skip when template is applied since it may override)
                if (applyCategories && !templateApplied)
                {
                    var cats = state["categories"] as JArray;
                    if (cats != null)
                    {
                        // Build lookup of categories
                        var byId = new Dictionary<int, Category>();
                        try
                        {
                            foreach (Category c in doc.Settings.Categories)
                                if (c != null) byId[c.Id.IntValue()] = c;
                        }
                        catch { }

                        foreach (var t in cats.OfType<JObject>())
                        {
                            int cid = t.Value<int?>("categoryId") ?? 0;
                            bool hidden = t.Value<bool?>("hidden") ?? false;
                            if (cid <= 0) continue;
                            if (!byId.TryGetValue(cid, out var cat)) continue;
                            try { if (view.CanCategoryBeHidden(cat.Id)) { view.SetCategoryHidden(cat.Id, hidden); changedCats++; } }
                            catch (Exception ex) { RevitLogger.Warn($"SetCategoryHidden failed: {cid}", ex); }
                        }
                    }
                }

                // Filters (skip when template is applied)
                if (applyFilters && !templateApplied)
                {
                    var filters = state["filters"] as JArray;
                    if (filters != null)
                    {
                        var current = new HashSet<int>();
                        try { foreach (var fid in view.GetFilters()) current.Add(fid.IntValue()); } catch { }
                        foreach (var t in filters.OfType<JObject>())
                        {
                            int fid = t.Value<int?>("filterId") ?? 0;
                            bool visible = t.Value<bool?>("visible") ?? true;
                            if (fid <= 0 || !current.Contains(fid)) continue;
                            try { view.SetFilterVisibility(Autodesk.Revit.DB.ElementIdCompat.From(fid), visible); changedFilters++; }
                            catch (Exception ex) { RevitLogger.Warn($"SetFilterVisibility failed: {fid}", ex); }
                        }
                    }
                }

                // Worksets
                if (applyWorksets && doc.IsWorkshared)
                {
                    var wsArr = state["worksets"] as JArray;
                    if (wsArr != null)
                    {
                        var wsById = new Dictionary<int, Workset>();
                        try
                        {
                            var wsCol = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                            foreach (Workset ws in wsCol) wsById[ws.Id.IntValue()] = ws;
                        }
                        catch { }
                        foreach (var t in wsArr.OfType<JObject>())
                        {
                            int wid = t.Value<int?>("worksetId") ?? 0;
                            string visStr = t.Value<string>("visibility") ?? "UseGlobalSetting";
                            if (wid <= 0 || !wsById.TryGetValue(wid, out var ws)) continue;
                            WorksetVisibility vis = WorksetVisibility.UseGlobalSetting;
                            try { vis = (WorksetVisibility)Enum.Parse(typeof(WorksetVisibility), visStr, true); } catch { vis = WorksetVisibility.UseGlobalSetting; }
                            try { view.SetWorksetVisibility(ws.Id, vis); changedWorksets++; }
                            catch (Exception ex) { RevitLogger.Warn($"SetWorksetVisibility failed: {wid}", ex); }
                        }
                    }
                }

                // Hidden elements (re-hide)
                if (applyHiddenElements)
                {
                    var hid = state["hiddenElements"] as JArray;
                    if (hid != null && hid.Count > 0)
                    {
                        var ids = new List<ElementId>();
                        foreach (var v in hid) { try { ids.Add(Autodesk.Revit.DB.ElementIdCompat.From((int)v)); } catch { } }
                        // Batch to avoid overly long single calls
                        int batch = 1000;
                        for (int i = 0; i < ids.Count; i += batch)
                        {
                            var chunk = ids.GetRange(i, Math.Min(batch, ids.Count - i));
                            try { view.HideElements(chunk); hiddenCount += chunk.Count; }
                            catch (Exception ex) { RevitLogger.Warn("HideElements batch failed", ex); }
                        }
                    }
                }

                tx.Commit();
            }

            // Regenerate & refresh
            try { doc.Regenerate(); } catch { }
            try { uidoc?.RefreshActiveView(); } catch { }

            return new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                applied = new { templateApplied, changedCats, changedFilters, changedWorksets, hiddenCount }
            };
        }
    }
}


