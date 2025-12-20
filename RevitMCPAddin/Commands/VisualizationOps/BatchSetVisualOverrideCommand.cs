#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.VisualizationOps
{
    /// <summary>
    /// batch_set_visual_override: Apply override graphics to multiple elements in a view.
    /// Params: { viewId:int, elementIds:int[], r:int, g:int, b:int, transparency:int, detachViewTemplate?:bool }
    /// Returns: { ok:true, applied:int, skipped?:[], errors?:[] }
    /// </summary>
    public class BatchSetVisualOverrideCommand : IRevitCommandHandler
    {
        public string CommandName => "batch_set_visual_override";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            // Optional execution guard
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;
            int viewId = p.Value<int?>("viewId") ?? 0;
            var v = (viewId > 0)
                ? doc.GetElement(new ElementId(viewId)) as View
                : uidoc.ActiveGraphicalView ?? uidoc.ActiveView as View;
            if (v == null) return new { ok = false, code = "NO_VIEW", msg = "Target view could not be resolved." };

            // View Template �K�p�r���[�̏ꍇ�͕`��ύX���s���Ȃ���
            bool templateApplied = v.ViewTemplateId != ElementId.InvalidElementId;
            int? templateViewId = templateApplied ? (int?)v.ViewTemplateId.IntegerValue : null;
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? true;
            if (templateApplied && detachTemplate)
            {
                try
                {
                    using var t = new Transaction(doc, "Detach View Template (BatchSetVisualOverride)");
                    t.Start();
                    v.ViewTemplateId = ElementId.InvalidElementId;
                    t.Commit();
                    templateApplied = false;
                    templateViewId = null;
                }
                catch
                {
                    // best-effort; if detach fails we fall through and treat as templateApplied
                    templateApplied = v.ViewTemplateId != ElementId.InvalidElementId;
                    templateViewId = templateApplied ? (int?)v.ViewTemplateId.IntegerValue : null;
                }
            }
            if (templateApplied)
            {
                return new
                {
                    ok = true,
                    viewId = v.Id.IntegerValue,
                    applied = 0,
                    skipped = new[] { new { reason = "View has a template; detach view template before calling batch_set_visual_override." } },
                    errors = new object[0],
                    templateApplied = true,
                    templateViewId,
                    skippedDueToTemplate = true,
                    errorCode = "VIEW_TEMPLATE_LOCK",
                    message = "View has a template; detach view template before calling batch_set_visual_override."
                };
            }

            var idsArr = (p["elementIds"] as JArray)?.Values<int>()?.ToList() ?? new List<int>();
            if (idsArr.Count == 0) return new { ok = false, code = "NO_IDS", msg = "elementIds is required and non-empty." };

            int r = p.Value<int?>("r") ?? 200;
            int g = p.Value<int?>("g") ?? 230;
            int b = p.Value<int?>("b") ?? 80;
            int transparency = p.Value<int?>("transparency") ?? 40; // 0..100

            var color = new Color((byte)Math.Max(0, Math.Min(255, r)), (byte)Math.Max(0, Math.Min(255, g)), (byte)Math.Max(0, Math.Min(255, b)));
            var ogs = new OverrideGraphicSettings();
            GraphicsOverrideHelper.TrySetLineColors(ogs, color);
            GraphicsOverrideHelper.TrySetAllSurfaceAndCutPatterns(doc, ogs, color, visible: true);
            GraphicsOverrideHelper.TrySetSurfaceTransparency(ogs, Math.Max(0, Math.Min(100, transparency)));

            int applied = 0; var errors = new List<object>();
            try
            {
                using (var tx = new Transaction(doc, "Batch Set Visual Override"))
                {
                    tx.Start();
                    foreach (var id in idsArr)
                    {
                        try
                        {
                            var eid = new ElementId(id);
                            v.SetElementOverrides(eid, ogs);
                            applied++;
                        }
                        catch (Exception exEach)
                        {
                            errors.Add(new { elementId = id, error = exEach.Message });
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                errors.Add(new { error = ex.Message });
            }

            return new
            {
                ok = errors.Count == 0,
                viewId = v.Id.IntegerValue,
                applied,
                errors,
                templateApplied = false,
                templateViewId = (int?)null,
                skippedDueToTemplate = false
            };
        }
    }
}
