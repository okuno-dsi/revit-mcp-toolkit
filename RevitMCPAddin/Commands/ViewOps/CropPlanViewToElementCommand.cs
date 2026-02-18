// File: Commands/ViewOps/CropPlanViewToElementCommand.cs
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// JSON-RPC: crop_plan_view_to_element
    /// Plan/Ceiling/Engineering view の CropBox を、指定要素の外接BBox(+margin)に更新する。
    /// </summary>
    public class CropPlanViewToElementCommand : IRevitCommandHandler
    {
        public string CommandName => "crop_plan_view_to_element";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null || uidoc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd.Params as JObject ?? new JObject();
            var guard = ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            int viewIdInt = p.Value<int?>("viewId") ?? uidoc.ActiveView?.Id.IntValue() ?? -1;
            int elemIdInt = p.Value<int?>("elementId") ?? -1;
            double marginMm = p.Value<double?>("marginMm")
                              ?? p.Value<double?>("margin_mm")
                              ?? p.Value<double?>("cropMarginMm")
                              ?? 100.0;
            bool cropVisible = p.Value<bool?>("cropVisible") ?? true;
            bool cropActive = p.Value<bool?>("cropActive") ?? true;

            if (viewIdInt <= 0) return new { ok = false, msg = "viewId を解決できませんでした。" };
            if (elemIdInt <= 0) return new { ok = false, msg = "elementId を指定してください。" };
            if (marginMm < 0) marginMm = 0;

            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdInt)) as View;
            if (view == null) return new { ok = false, msg = $"viewId={viewIdInt} のビューが見つかりません。" };

            if (!(view is ViewPlan))
            {
                return new
                {
                    ok = false,
                    msg = "このコマンドは Plan/Ceiling/Engineering のビューを対象にしています。",
                    viewType = view.ViewType.ToString()
                };
            }

            var elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elemIdInt));
            if (elem == null) return new { ok = false, msg = $"elementId={elemIdInt} の要素が見つかりません。" };

            var bbWorld = elem.get_BoundingBox(null) ?? elem.get_BoundingBox(view);
            if (bbWorld == null) return new { ok = false, msg = "要素の BoundingBox を取得できませんでした。" };

            BoundingBoxXYZ? crop;
            try { crop = view.CropBox; }
            catch (Exception ex) { return new { ok = false, msg = "CropBox 取得に失敗: " + ex.Message }; }
            if (crop == null) return new { ok = false, msg = "このビューは CropBox を持ちません。" };

            Transform toView;
            try { toView = (crop.Transform ?? Transform.Identity).Inverse; }
            catch { toView = Transform.Identity; }

            var corners = new[]
            {
                new XYZ(bbWorld.Min.X, bbWorld.Min.Y, bbWorld.Min.Z),
                new XYZ(bbWorld.Min.X, bbWorld.Min.Y, bbWorld.Max.Z),
                new XYZ(bbWorld.Min.X, bbWorld.Max.Y, bbWorld.Min.Z),
                new XYZ(bbWorld.Min.X, bbWorld.Max.Y, bbWorld.Max.Z),
                new XYZ(bbWorld.Max.X, bbWorld.Min.Y, bbWorld.Min.Z),
                new XYZ(bbWorld.Max.X, bbWorld.Min.Y, bbWorld.Max.Z),
                new XYZ(bbWorld.Max.X, bbWorld.Max.Y, bbWorld.Min.Z),
                new XYZ(bbWorld.Max.X, bbWorld.Max.Y, bbWorld.Max.Z),
            };

            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var c in corners)
            {
                var lc = toView.OfPoint(c);
                if (lc.X < minX) minX = lc.X;
                if (lc.Y < minY) minY = lc.Y;
                if (lc.X > maxX) maxX = lc.X;
                if (lc.Y > maxY) maxY = lc.Y;
            }

            double marginFt = UnitUtils.ConvertToInternalUnits(marginMm, UnitTypeId.Millimeters);
            var oldMin = crop.Min; var oldMax = crop.Max;

            var newMin = new XYZ(minX - marginFt, minY - marginFt, oldMin.Z);
            var newMax = new XYZ(maxX + marginFt, maxY + marginFt, oldMax.Z);

            // ゼロ幅回避
            const double eps = 1e-6;
            if (newMax.X <= newMin.X) newMax = new XYZ(newMin.X + eps, newMax.Y, newMax.Z);
            if (newMax.Y <= newMin.Y) newMax = new XYZ(newMax.X, newMin.Y + eps, newMax.Z);

            string? txErr = null;
            using (var tx = new Transaction(doc, "MCP Crop Plan View To Element"))
            {
                try
                {
                    tx.Start();
                    crop.Min = newMin;
                    crop.Max = newMax;
                    view.CropBox = crop;
                    view.CropBoxActive = cropActive;
                    view.CropBoxVisible = cropVisible;
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    txErr = ex.Message;
                    try { tx.RollBack(); } catch { }
                }
            }

            if (!string.IsNullOrEmpty(txErr))
                return new { ok = false, msg = "Crop 適用に失敗: " + txErr };

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                elementId = elem.Id.IntegerValue,
                marginMm = Math.Round(marginMm, 3),
                crop = new
                {
                    min = new
                    {
                        x = Math.Round(UnitUtils.ConvertFromInternalUnits(newMin.X, UnitTypeId.Millimeters), 3),
                        y = Math.Round(UnitUtils.ConvertFromInternalUnits(newMin.Y, UnitTypeId.Millimeters), 3),
                        z = Math.Round(UnitUtils.ConvertFromInternalUnits(newMin.Z, UnitTypeId.Millimeters), 3),
                    },
                    max = new
                    {
                        x = Math.Round(UnitUtils.ConvertFromInternalUnits(newMax.X, UnitTypeId.Millimeters), 3),
                        y = Math.Round(UnitUtils.ConvertFromInternalUnits(newMax.Y, UnitTypeId.Millimeters), 3),
                        z = Math.Round(UnitUtils.ConvertFromInternalUnits(newMax.Z, UnitTypeId.Millimeters), 3),
                    }
                }
            };
        }
    }
}

