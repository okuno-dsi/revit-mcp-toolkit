#nullable enable
// ================================================================
// File   : Commands/ViewOps/ViewportAutoCommands.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary:
//   Step 5: High-level viewport command:
//   - viewport.move_to_sheet_center: Move a viewport to the center of its sheet.
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    [RpcCommand(
        "viewport.move_to_sheet_center",
        Aliases = new[] { "viewport_move_to_sheet_center" },
        Category = "ViewSheet Ops",
        Tags = new[] { "viewport", "sheet", "move", "center" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Importance = "normal",
        Summary = "Move a viewport to the center of its sheet (supports dryRun).",
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"viewport.move_to_sheet_center\", \"params\":{ \"viewportId\": 123456, \"dryRun\": true } }"
    )]
    public class ViewportMoveToSheetCenterCommand : IRevitCommandHandler
    {
        // Legacy dispatch
        public string CommandName => "viewport_move_to_sheet_center";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document.");

            var p = (JObject)(cmd.Params ?? new JObject());
            bool dryRun = p.Value<bool?>("dryRun") ?? false;

            var vp = ResolveViewport(doc, p, out var resolveWarn, out var resolveErr);
            if (vp == null)
            {
                var err = RpcResultEnvelope.Fail("INVALID_PARAMS", resolveErr ?? "Viewport not found.");
                if (!string.IsNullOrWhiteSpace(resolveWarn))
                    err["warnings"] = new JArray(resolveWarn);
                return err;
            }

            var sheet = doc.GetElement(vp.SheetId) as ViewSheet;
            if (sheet == null)
                return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "Viewport.SheetId is not a valid ViewSheet.");

            var target = SheetUtil.GetSheetCenterFt(sheet);

            XYZ current;
            try { current = vp.GetBoxCenter(); }
            catch { current = XYZ.Zero; }

            var delta = new XYZ(target.X - current.X, target.Y - current.Y, 0);

            if (dryRun)
            {
                return new
                {
                    ok = true,
                    code = "OK",
                    msg = "DryRun: viewport would be moved to sheet center.",
                    warnings = string.IsNullOrWhiteSpace(resolveWarn) ? new string[0] : new[] { resolveWarn },
                    data = new
                    {
                        dryRun = true,
                        viewportId = vp.Id.IntValue(),
                        sheetId = sheet.Id.IntValue(),
                        sheetNumber = sheet.SheetNumber ?? "",
                        currentCenterMm = new { x = SheetUtil.FtToMm(current.X), y = SheetUtil.FtToMm(current.Y) },
                        targetCenterMm = new { x = SheetUtil.FtToMm(target.X), y = SheetUtil.FtToMm(target.Y) },
                        deltaMm = new { x = SheetUtil.FtToMm(delta.X), y = SheetUtil.FtToMm(delta.Y) }
                    }
                };
            }

            using (var tx = new Transaction(doc, "Viewport.Move To Sheet Center"))
            {
                try
                {
                    tx.Start();
                    ElementTransformUtils.MoveElement(doc, vp.Id, delta);
                    tx.Commit();

                    XYZ after;
                    try { after = vp.GetBoxCenter(); } catch { after = target; }

                    return new
                    {
                        ok = true,
                        code = "OK",
                        msg = "Moved viewport to sheet center.",
                        warnings = string.IsNullOrWhiteSpace(resolveWarn) ? new string[0] : new[] { resolveWarn },
                        data = new
                        {
                            viewportId = vp.Id.IntValue(),
                            sheetId = sheet.Id.IntValue(),
                            sheetNumber = sheet.SheetNumber ?? "",
                            centerMm = new { x = SheetUtil.FtToMm(after.X), y = SheetUtil.FtToMm(after.Y) }
                        }
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return RpcResultEnvelope.Fail("INTERNAL_ERROR", "Failed to move viewport: " + ex.Message);
                }
            }
        }

        private static Viewport ResolveViewport(Document doc, JObject p, out string? warning, out string? error)
        {
            warning = null;
            error = null;

            int viewportId = p.Value<int?>("viewportId") ?? 0;
            if (viewportId > 0)
            {
                var vp = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewportId)) as Viewport;
                if (vp != null) return vp;
                error = $"Viewport not found: viewportId={viewportId}";
                return null;
            }

            // Fallback: sheet + view locator
            var sheet = PlaceViewOnSheetAutoCommand.ResolveSheetFlexible(doc, p, out var sw);
            int viewId = p.Value<int?>("viewId") ?? 0;
            if (sheet != null && viewId > 0)
            {
                try
                {
                    var vp = (sheet.GetAllViewports() ?? Enumerable.Empty<ElementId>())
                        .Select(id => doc.GetElement(id) as Viewport)
                        .FirstOrDefault(x => x != null && x.ViewId.IntValue() == viewId);
                    if (vp != null)
                    {
                        if (!string.IsNullOrWhiteSpace(sw)) warning = sw;
                        return vp;
                    }
                }
                catch { /* ignore */ }

                error = $"Viewport not found on sheet for viewId={viewId}";
                if (!string.IsNullOrWhiteSpace(sw)) warning = sw;
                return null;
            }

            error = "Provide viewportId, or (sheet + viewId) to locate the viewport.";
            if (!string.IsNullOrWhiteSpace(sw)) warning = sw;
            return null;
        }
    }
}
