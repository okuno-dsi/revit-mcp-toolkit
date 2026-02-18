// Commands/ViewOps/ClearVisualOverrideCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class ClearVisualOverrideCommand : IRevitCommandHandler
    {
        public string CommandName => "clear_visual_override";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "ANeBuhLg܂B" };

            var p = (JObject)cmd.Params;

            // r[iviewId w/0  ANeBuOtBbNr[j
            int reqViewId = p.Value<int?>("viewId") ?? 0;
            View view = null;
            ElementId viewId = ElementId.InvalidElementId;
            if (reqViewId > 0)
            {
                viewId = Autodesk.Revit.DB.ElementIdCompat.From(reqViewId);
                view = doc.GetElement(viewId) as View;
            }
            if (view == null)
            {
                view = uiapp.ActiveUIDocument?.ActiveGraphicalView
                    ?? (uiapp.ActiveUIDocument?.ActiveView is View av && av.ViewType != ViewType.ProjectBrowser ? av : null);
                if (view != null) viewId = view.Id;
            }

            bool autoWorkingView = p.Value<bool?>("autoWorkingView") ?? true;
            if (view == null && autoWorkingView)
            {
                using (var tx = new Transaction(doc, "Create Working 3D (ClearVisualOverride)"))
                {
                    try
                    {
                        tx.Start();
                        var vtf = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.ThreeDimensional);
                        if (vtf == null) throw new InvalidOperationException("3D view family type not found");
                        var v3d = View3D.CreateIsometric(doc, vtf.Id);
                        v3d.Name = UniqueViewName(doc, "MCP_Working_3D");
                        tx.Commit();
                        view = v3d;
                        viewId = v3d.Id;
                        try { uiapp.ActiveUIDocument?.RequestViewChange(v3d); } catch { }
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return new
                        {
                            ok = false,
                            msg = "Kpr[ł܂łBviewId w肵ĂB",
                            detail = ex.Message
                        };
                    }
                }
            }
            if (view == null)
            {
                return new
                {
                    ok = false,
                    msg = "Kpr[ł܂łBviewId w肵ĂB"
                };
            }

            // View Template Ή
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            bool templateApplied = view.ViewTemplateId != ElementId.InvalidElementId;
            int? templateViewId = templateApplied ? (int?)view.ViewTemplateId.IntValue() : null;
            if (templateApplied && detachTemplate)
            {
                using (var tx0 = new Transaction(doc, "Detach View Template (ClearOverride)"))
                {
                    try
                    {
                        tx0.Start();
                        view.ViewTemplateId = ElementId.InvalidElementId;
                        tx0.Commit();
                        templateApplied = false;
                        templateViewId = null;
                    }
                    catch
                    {
                        try { tx0.RollBack(); } catch { }
                    }
                }
            }
            if (templateApplied)
            {
                // View Template Kpr[ł͕`ύXsȂ
                return new
                {
                    ok = true,
                    viewId = view.Id.IntValue(),
                    count = 0,
                    skipped = 0,
                    completed = true,
                    nextIndex = 0,
                    batchSize = 0,
                    elapsedMs = 0,
                    templateApplied = true,
                    templateViewId,
                    skippedDueToTemplate = true,
                    errorCode = "VIEW_TEMPLATE_LOCK",
                    message = "View has a template; detach view template before calling clear_visual_override."
                };
            }

            var ids = new List<ElementId>();
            if (p["elementIds"] != null)
            {
                foreach (var v in (JArray)p["elementIds"])
                    ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(v.Value<int>()));
            }
            else if (p["elementId"] != null)
            {
                ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("elementId")));
            }
            if (ids.Count == 0) return new { ok = false, msg = "elementId(s) ܂B" };

            var ogsEmpty = new OverrideGraphicSettings();
            int count = 0, skipped = 0;
            int batchSize = Math.Max(50, Math.Min(5000, p.Value<int?>("batchSize") ?? 800));
            int maxMillisPerTx = Math.Max(500, Math.Min(10000, p.Value<int?>("maxMillisPerTx") ?? 3000));
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            bool refreshView = p.Value<bool?>("refreshView") ?? true;

            var swAll = System.Diagnostics.Stopwatch.StartNew();
            int nextIndex = startIndex;
            while (nextIndex < ids.Count)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var tx = new Transaction(doc, "[MCP] Clear Visual Override (batched)"))
                {
                    try
                    {
                        tx.Start();
                        int end = Math.Min(ids.Count, nextIndex + batchSize);
                        for (int i = nextIndex; i < end; i++)
                        {
                            var eid = ids[i];
                            try
                            {
                                view.SetElementOverrides(eid, ogsEmpty);
                                count++;
                            }
                            catch (Exception ex)
                            {
                                skipped++;
                                RevitLogger.Error($"Clear SetElementOverrides failed: eid={eid.IntValue()}", ex);
                            }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { tx.RollBack(); } catch { }
                        RevitLogger.Error("Clear override transaction failed", ex);
                        break;
                    }
                }
                if (refreshView)
                {
                    try { doc.Regenerate(); } catch { }
                    try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { }
                }
                nextIndex += batchSize;
                if (sw.ElapsedMilliseconds > maxMillisPerTx) break;
            }

            return new
            {
                ok = true,
                viewId = viewId.IntValue(),
                count,
                skipped,
                completed = nextIndex >= ids.Count,
                nextIndex,
                batchSize,
                elapsedMs = swAll.ElapsedMilliseconds,
                templateApplied = false,
                templateViewId = (int?)null,
                skippedDueToTemplate = false
            };
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int i = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} {i++}";
            }
            return name;
        }
    }
}



