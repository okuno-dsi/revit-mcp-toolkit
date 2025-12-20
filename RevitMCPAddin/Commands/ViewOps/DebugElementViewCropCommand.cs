// File: Commands/ViewOps/DebugElementViewCropCommand.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// JSON-RPC: debug_element_view_crop
    /// 指定したビューと要素について、CropBox と要素 BB の座標を比較するためのデバッグ用コマンド。
    /// - viewId が省略された場合はアクティブビューを使用。
    /// - 単位は ft / mm を両方返す。
    /// </summary>
    public class DebugElementViewCropCommand : IRevitCommandHandler
    {
        public string CommandName => "debug_element_view_crop";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            if (uidoc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var doc = uidoc.Document;
            var p = cmd.Params as JObject ?? new JObject();

            int elementIdInt = p.Value<int?>("elementId") ?? -1;
            if (elementIdInt <= 0)
                return new { ok = false, msg = "elementId を指定してください。" };

            int viewIdInt = p.Value<int?>("viewId") ?? uidoc.ActiveView?.Id.IntegerValue ?? -1;
            if (viewIdInt <= 0)
                return new { ok = false, msg = "viewId を解決できませんでした。" };

            var viewElem = doc.GetElement(new ElementId(viewIdInt)) as View;
            if (viewElem == null)
                return new { ok = false, msg = $"viewId={viewIdInt} の View が見つかりません。" };

            var elem = doc.GetElement(new ElementId(elementIdInt));
            if (elem == null)
                return new { ok = false, msg = $"elementId={elementIdInt} の要素が見つかりません。" };

            BoundingBoxXYZ crop;
            try
            {
                crop = viewElem.CropBox;
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "CropBox を取得できません: " + ex.Message };
            }

            if (crop == null)
                return new { ok = false, msg = "CropBox が null です。" };

            // 要素の BB（ワールド座標）
            BoundingBoxXYZ bbWorld = elem.get_BoundingBox(null) ?? elem.get_BoundingBox(viewElem);
            if (bbWorld == null)
            {
                return new { ok = false, msg = "要素の BoundingBox を取得できませんでした。" };
            }

            // ビュー座標系（CropBox.Transform または ViewSection の基底）を構築
            Transform toWorld = crop.Transform;
            Transform toView = toWorld.Inverse;

            // 要素 BB をビュー座標系に射影
            var corners = new[]
            {
                new XYZ(bbWorld.Min.X, bbWorld.Min.Y, bbWorld.Min.Z),
                new XYZ(bbWorld.Min.X, bbWorld.Min.Y, bbWorld.Max.Z),
                new XYZ(bbWorld.Min.X, bbWorld.Max.Y, bbWorld.Min.Z),
                new XYZ(bbWorld.Min.X, bbWorld.Max.Y, bbWorld.Max.Z),
                new XYZ(bbWorld.Max.X, bbWorld.Min.Y, bbWorld.Min.Z),
                new XYZ(bbWorld.Max.X, bbWorld.Min.Y, bbWorld.Max.Z),
                new XYZ(bbWorld.Max.X, bbWorld.Max.Y, bbWorld.Min.Z),
                new XYZ(bbWorld.Max.X, bbWorld.Max.Y, bbWorld.Max.Z)
            };

            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
            double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;

            foreach (var c in corners)
            {
                var lc = toView.OfPoint(c);
                if (lc.X < minX) minX = lc.X;
                if (lc.X > maxX) maxX = lc.X;
                if (lc.Y < minY) minY = lc.Y;
                if (lc.Y > maxY) maxY = lc.Y;
                if (lc.Z < minZ) minZ = lc.Z;
                if (lc.Z > maxZ) maxZ = lc.Z;
            }

            var elemLocal = new
            {
                min = new
                {
                    x_ft = minX,
                    y_ft = minY,
                    z_ft = minZ,
                    x_mm = UnitUtils.ConvertFromInternalUnits(minX, UnitTypeId.Millimeters),
                    y_mm = UnitUtils.ConvertFromInternalUnits(minY, UnitTypeId.Millimeters),
                    z_mm = UnitUtils.ConvertFromInternalUnits(minZ, UnitTypeId.Millimeters)
                },
                max = new
                {
                    x_ft = maxX,
                    y_ft = maxY,
                    z_ft = maxZ,
                    x_mm = UnitUtils.ConvertFromInternalUnits(maxX, UnitTypeId.Millimeters),
                    y_mm = UnitUtils.ConvertFromInternalUnits(maxY, UnitTypeId.Millimeters),
                    z_mm = UnitUtils.ConvertFromInternalUnits(maxZ, UnitTypeId.Millimeters)
                }
            };

            var cropLocal = new
            {
                min = new
                {
                    x_ft = crop.Min.X,
                    y_ft = crop.Min.Y,
                    z_ft = crop.Min.Z,
                    x_mm = UnitUtils.ConvertFromInternalUnits(crop.Min.X, UnitTypeId.Millimeters),
                    y_mm = UnitUtils.ConvertFromInternalUnits(crop.Min.Y, UnitTypeId.Millimeters),
                    z_mm = UnitUtils.ConvertFromInternalUnits(crop.Min.Z, UnitTypeId.Millimeters)
                },
                max = new
                {
                    x_ft = crop.Max.X,
                    y_ft = crop.Max.Y,
                    z_ft = crop.Max.Z,
                    x_mm = UnitUtils.ConvertFromInternalUnits(crop.Max.X, UnitTypeId.Millimeters),
                    y_mm = UnitUtils.ConvertFromInternalUnits(crop.Max.Y, UnitTypeId.Millimeters),
                    z_mm = UnitUtils.ConvertFromInternalUnits(crop.Max.Z, UnitTypeId.Millimeters)
                }
            };

            bool insideXY =
                minX >= crop.Min.X && maxX <= crop.Max.X &&
                minY >= crop.Min.Y && maxY <= crop.Max.Y;
            bool insideZ =
                minZ >= crop.Min.Z && maxZ <= crop.Max.Z;

            return new
            {
                ok = true,
                inputs = new { viewId = viewIdInt, elementId = elementIdInt },
                view = new
                {
                    name = viewElem.Name,
                    viewType = viewElem.ViewType.ToString(),
                    cropBoxActive = SafeGetBool(() => viewElem.CropBoxActive),
                    cropBoxVisible = SafeGetBool(() => viewElem.CropBoxVisible)
                },
                cropBoxLocal = cropLocal,
                elementBoundsWorld = new
                {
                    min = new
                    {
                        x_ft = bbWorld.Min.X,
                        y_ft = bbWorld.Min.Y,
                        z_ft = bbWorld.Min.Z,
                        x_mm = UnitUtils.ConvertFromInternalUnits(bbWorld.Min.X, UnitTypeId.Millimeters),
                        y_mm = UnitUtils.ConvertFromInternalUnits(bbWorld.Min.Y, UnitTypeId.Millimeters),
                        z_mm = UnitUtils.ConvertFromInternalUnits(bbWorld.Min.Z, UnitTypeId.Millimeters)
                    },
                    max = new
                    {
                        x_ft = bbWorld.Max.X,
                        y_ft = bbWorld.Max.Y,
                        z_ft = bbWorld.Max.Z,
                        x_mm = UnitUtils.ConvertFromInternalUnits(bbWorld.Max.X, UnitTypeId.Millimeters),
                        y_mm = UnitUtils.ConvertFromInternalUnits(bbWorld.Max.Y, UnitTypeId.Millimeters),
                        z_mm = UnitUtils.ConvertFromInternalUnits(bbWorld.Max.Z, UnitTypeId.Millimeters)
                    }
                },
                elementBoundsLocal = elemLocal,
                containment = new
                {
                    insideXY,
                    insideZ
                },
                units = UnitHelper.DefaultUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }

        private static bool SafeGetBool(Func<bool> getter)
        {
            try { return getter(); }
            catch { return false; }
        }
    }
}

