// ================================================================
// File: Commands/ViewOps/SyncViewStateCommand.cs
// Purpose: Synchronize essential view properties from a source view to a target view
// Params: { srcViewId:int, dstViewId:int }
//   Copies: ViewTemplate detach, Temporary Hide/Isolate reset,
//           Phase (VIEW_PHASE), Phase Filter (VIEW_PHASE_FILTER),
//           Plan View Range (if both are ViewPlan),
//           Ensures Structural categories (Framing/Columns) visible
// Target: .NET Framework 4.8 / Revit 2023+
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.RevitUI;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class SyncViewStateCommand : IRevitCommandHandler
    {
        public string CommandName => "sync_view_state";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            try
            {
                //  C: ` ParamsAsJObject() g킸Af JObject 
                var p = (JObject)(cmd.Params ?? new JObject());

                var srcId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("srcViewId"));
                var dstId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("dstViewId"));
                var src = doc.GetElement(srcId) as View;
                var dst = doc.GetElement(dstId) as View;
                if (src == null || dst == null)
                    return new { ok = false, msg = "source or target view not found" };

                int changed = 0;
                using (var tx = new Transaction(doc, "[MCP] Sync View State"))
                {
                    tx.Start();

                    // Detach template on target (if any)
                    if (dst.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        try { dst.ViewTemplateId = ElementId.InvalidElementId; changed++; } catch { }
                    }

                    // Reset Temporary Hide/Isolate on target
                    try
                    {
                        if (dst.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                            dst.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    }
                    catch { }

                    //  C: Phase / Phase Filter  BuiltInParameter 𐳂
                    try
                    {
                        var ph = src.get_Parameter(BuiltInParameter.VIEW_PHASE);
                        var pf = src.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);

                        if (ph != null && ph.StorageType == StorageType.ElementId)
                        {
                            var id = ph.AsElementId();
                            var t = dst.get_Parameter(BuiltInParameter.VIEW_PHASE);
                            if (t != null && !t.IsReadOnly) { t.Set(id); changed++; }
                        }

                        if (pf != null && pf.StorageType == StorageType.ElementId)
                        {
                            var id = pf.AsElementId();
                            var t = dst.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);
                            if (t != null && !t.IsReadOnly) { t.Set(id); changed++; }
                        }
                    }
                    catch { }

                    // Plan View Range (if both are ViewPlan)
                    try
                    {
                        if (src is ViewPlan sPlan && dst is ViewPlan dPlan)
                        {
                            var srcVr = sPlan.GetViewRange();
                            var dstVr = dPlan.GetViewRange();

                            foreach (PlanViewPlane plane in Enum.GetValues(typeof(PlanViewPlane)))
                            {
                                try
                                {
                                    var lvl = srcVr.GetLevelId(plane);
                                    var off = srcVr.GetOffset(plane);
                                    dstVr.SetLevelId(plane, lvl);
                                    dstVr.SetOffset(plane, off);
                                }
                                catch { }
                            }
                            dPlan.SetViewRange(dstVr); changed++;
                        }
                    }
                    catch { }

                    // Ensure Structural categories visible on target
                    try
                    {
                        var cats = new[] { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns };
                        foreach (var bic in cats)
                        {
                            var cat = Category.GetCategory(doc, bic);
                            if (cat != null)
                            {
                                try { dst.SetCategoryHidden(cat.Id, false); } catch { }
                            }
                        }
                    }
                    catch { }

                    tx.Commit();
                }

                return new { ok = true, changed };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}

