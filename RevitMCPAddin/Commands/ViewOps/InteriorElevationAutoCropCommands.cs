// ============================================================================
// File: Commands/ViewOps/InteriorElevationAutoCropCommands.cs
// Purpose : Interior Elevation（展開図）を部屋境界に合わせて自動クロップ
// Target  : .NET Framework 4.8 / C# 8 / Revit 2023 API
// Depends : Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq
//           RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand)
// Usage   :
//   1) auto_crop_elevation_to_room
//      { "viewId": 601, "roomId": 60455799, "boundaryLocation": "Finish",
//        "paddingMm": 200, "minWidthMm": 1200, "minHeightMm": 1200, "clipDepthMm": 1200 }
//
//   2) auto_crop_elevations_for_room
//      { "roomId": 60455799, "boundaryLocation": "Finish", "paddingMm": 200,
//        "minWidthMm": 1500, "minHeightMm": 1500, "clipDepthMm": 1200 }
//
// Notes   :
//  - ViewSection.CropBox は Transform 付きの BoundingBoxXYZ（ローカル=ビュー基底 Right/Up/Dir）
//  - 2D bbox は Room 境界をビュー座標（Right,Up）へ射影したうえで padding を加算して算出
//  - clipDepthMm を指定すれば、ビュー奥行（BasisZ）方向の厚みを設定（Min.Z=0, Max.Z=clipDepth）
//  - View Template でクロップがロックされる場合は “skipped” として理由を返却
// ============================================================================

#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    internal static class ElevCropUtil
    {
        public static object UnitsIn() => new { Length = "mm" };
        public static object UnitsInt() => new { Length = "ft" };

        public static double MmToFt(double mm) => ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) => ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        /// <summary>ビューの基底（Right/Up/Dir, Origin）に基づく world↔view 変換を構築</summary>
        public static (Transform toWorld, Transform toView) BuildViewBasis(ViewSection v)
        {
            var T = Transform.Identity;
            T.Origin = v.Origin;                 // ビュー原点（カット面上）
            T.BasisX = v.RightDirection;         // +X: 右方向
            T.BasisY = v.UpDirection;            // +Y: 上方向
            T.BasisZ = v.ViewDirection;          // +Z: 観測方向（奥行）
            return (T, T.Inverse);
        }

        /// <summary>Room の境界頂点（モデル座標 ft）を列挙（Finish/Core 外周含む全ループ）</summary>
        public static List<XYZ> CollectRoomBoundaryPoints(Document doc, Autodesk.Revit.DB.Architecture.Room room, string boundaryLocation)
        {
            var opt = new SpatialElementBoundaryOptions();
            // 文字列で場所を切替（Revitは enum 相当だが 2023 でのAPIは Location 指定の拡張が弱いので簡易）
            if (string.Equals(boundaryLocation, "Core", StringComparison.OrdinalIgnoreCase))
                opt.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.CoreBoundary;
            else
                opt.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;

            var loops = room.GetBoundarySegments(opt);
            var pts = new List<XYZ>(256);
            foreach (var loop in loops)
            {
                foreach (var seg in loop)
                {
                    var c = seg.GetCurve();
                    if (c == null) continue;
                    // 端点を集約（密度を上げたい場合は Tessellate へ切替）
                    pts.Add(c.GetEndPoint(0));
                    pts.Add(c.GetEndPoint(1));
                }
            }
            return pts;
        }

        /// <summary>2D（Right/Up）射影の AABB を計算（mm padding, 最小幅/高さの下限適用）</summary>
        public static (double minX, double minY, double maxX, double maxY) ComputeViewRect(
            IEnumerable<XYZ> worldPts, Transform toView, double paddingMm, double minWidthMm, double minHeightMm)
        {
            double pad = MmToFt(paddingMm);
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

            foreach (var wp in worldPts)
            {
                var p = toView.OfPoint(wp);
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            if (!IsFinite(minX) || !IsFinite(minY) || !IsFinite(maxX) || !IsFinite(maxY))
            {
                // 空集合 → 0サイズ回避
                minX = minY = -pad;
                maxX = maxY = +pad;
            }

            // padding 適用
            minX -= pad; minY -= pad;
            maxX += pad; maxY += pad;

            // 最小幅・高さを確保（中心据え置きで拡大）
            double w = maxX - minX;
            double h = maxY - minY;
            double minW = MmToFt(minWidthMm);
            double minH = MmToFt(minHeightMm);

            if (w < minW)
            {
                double d = (minW - w) / 2.0;
                minX -= d; maxX += d;
            }
            if (h < minH)
            {
                double d = (minH - h) / 2.0;
                minY -= d; maxY += d;
            }

            return (minX, minY, maxX, maxY);
        }

        /// <summary>ビューの CropBox を矩形 + depth に設定（Min.Z=0, Max.Z=depthFt）</summary>
        public static void ApplyCropBox(ViewSection v, (double minX, double minY, double maxX, double maxY) rect, double depthFt)
        {
            var (toWorld, _) = BuildViewBasis(v);

            // 変換付き BBox（ローカルはビュー座標系）
            var bb = new BoundingBoxXYZ
            {
                Transform = toWorld,
                Min = new XYZ(rect.minX, rect.minY, 0.0),
                Max = new XYZ(rect.maxX, rect.maxY, Math.Max(depthFt, 1e-3)) // depth 最小確保
            };

            v.CropBoxActive = true;
            v.CropBoxVisible = true;
            v.CropBox = bb;

            // 可能なら FarClip を有効化（テンプレや種別で無効な場合は黙ってスキップ）
            try
            {
                var farClipParam = v.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING);   // 0:無効 / 1:クリップ
                var farOffset = v.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                if (farClipParam != null && !farClipParam.IsReadOnly) farClipParam.Set(1);
                if (farOffset != null && !farOffset.IsReadOnly && depthFt > 1e-6) farOffset.Set(depthFt);
            }
            catch { /* ignore */ }
        }

        /// <summary>ビューがテンプレ適用でクロップがロックされているか簡易判定（安全側にスキップ）</summary>
        public static bool IsCropLockedByTemplate(View v)
        {
            try
            {
                // ViewTemplateId != Invalid かつ Cropping params が ReadOnly ならロック扱い
                if (v != null && v.ViewTemplateId != ElementId.InvalidElementId)
                {
                    var hasReadOnly =
                        (v.get_Parameter(BuiltInParameter.VIEWER_CROP_REGION)?.IsReadOnly ?? false) ||
                        (v.get_Parameter(BuiltInParameter.VIEWER_CROP_REGION_VISIBLE)?.IsReadOnly ?? false) ||
                        (v.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)?.IsReadOnly ?? false);
                    return hasReadOnly;
                }
            }
            catch { }
            return false;
        }
        private static bool IsFinite(double v) => !(double.IsNaN(v) || double.IsInfinity(v));
    }

    // ------------------------------------------------------------------------
    // 1) auto_crop_elevation_to_room
    //    ViewSection を room 境界に合わせてクロップ（padding/minW/minH/clipDepth）
    // ------------------------------------------------------------------------
    public class AutoCropElevationToRoomCommand : IRevitCommandHandler
    {
        public string CommandName => "auto_crop_elevation_to_room";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)cmd.Params;

                int viewId = p.Value<int>("viewId");
                int roomId = p.Value<int>("roomId");
                string boundaryLocation = p.Value<string>("boundaryLocation") ?? "Finish";
                double paddingMm = p.Value<double?>("paddingMm") ?? 200.0;
                double minWidthMm = p.Value<double?>("minWidthMm") ?? 1200.0;
                double minHeightMm = p.Value<double?>("minHeightMm") ?? 1200.0;
                double clipDepthMm = p.Value<double?>("clipDepthMm") ?? 1000.0;

                var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as ViewSection;
                if (view == null || view.ViewType != ViewType.Elevation)
                    return new { ok = false, msg = "Elevation view が見つかりません。" };

                if (ElevCropUtil.IsCropLockedByTemplate(view))
                    return new { ok = true, viewId, cropApplied = false, skipped = true, reason = "View has a template; crop parameters are locked." };

                var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(roomId)) as Autodesk.Revit.DB.Architecture.Room;
                if (room == null) return new { ok = false, msg = $"Room not found: {roomId}" };

                var pts = ElevCropUtil.CollectRoomBoundaryPoints(doc, room, boundaryLocation);
                var (toWorld, toView) = ElevCropUtil.BuildViewBasis(view);
                var rect = ElevCropUtil.ComputeViewRect(pts, toView, paddingMm, minWidthMm, minHeightMm);
                var depthFt = ElevCropUtil.MmToFt(Math.Max(clipDepthMm, 1.0));

                using (var tx = new Transaction(doc, "Auto Crop Elevation to Room"))
                {
                    tx.Start();
                    ElevCropUtil.ApplyCropBox(view, rect, depthFt);
                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    viewId,
                    roomId,
                    boundaryLocation,
                    paddingMm,
                    cropBox = new
                    {
                        min = new { x = ElevCropUtil.FtToMm(rect.minX), y = ElevCropUtil.FtToMm(rect.minY), z = 0.0 },
                        max = new { x = ElevCropUtil.FtToMm(rect.maxX), y = ElevCropUtil.FtToMm(rect.maxY), z = clipDepthMm }
                    },
                    inputUnits = ElevCropUtil.UnitsIn(),
                    internalUnits = ElevCropUtil.UnitsInt()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------------------
    // 2) auto_crop_elevations_for_room
    //    指定 Room 内にマーカー原点（View.Origin）がある全 Elevation を一括クロップ
    // ------------------------------------------------------------------------
    public class AutoCropElevationsForRoomCommand : IRevitCommandHandler
    {
        public string CommandName => "auto_crop_elevations_for_room";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                int roomId = p.Value<int>("roomId");
                string boundaryLocation = p.Value<string>("boundaryLocation") ?? "Finish";
                double paddingMm = p.Value<double?>("paddingMm") ?? 200.0;
                double minWidthMm = p.Value<double?>("minWidthMm") ?? 1500.0;
                double minHeightMm = p.Value<double?>("minHeightMm") ?? 1500.0;
                double clipDepthMm = p.Value<double?>("clipDepthMm") ?? 1200.0;

                var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(roomId)) as Autodesk.Revit.DB.Architecture.Room;
                if (room == null) return new { ok = false, msg = $"Room not found: {roomId}" };

                // 対象 Elevation を列挙（Origin が Room 内にあるもの）
                var candidates = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .Cast<ViewSection>()
                    .Where(v => v.ViewType == ViewType.Elevation)
                    .Where(v =>
                    {
                        try { return room.IsPointInRoom(v.Origin); } catch { return false; }
                    })
                    .ToList();

                if (candidates.Count == 0)
                    return new { ok = true, roomId, updated = new object[0], skipped = new object[0] };

                var pts = ElevCropUtil.CollectRoomBoundaryPoints(doc, room, boundaryLocation);
                var updated = new List<object>();
                var skipped = new List<object>();

                using (var tx = new Transaction(doc, "Auto Crop Elevations For Room"))
                {
                    tx.Start();

                    foreach (var view in candidates)
                    {
                        if (ElevCropUtil.IsCropLockedByTemplate(view))
                        {
                            skipped.Add(new { viewId = view.Id.IntValue(), reason = "View has a template; crop parameters are locked." });
                            continue;
                        }

                        var (_, toView) = ElevCropUtil.BuildViewBasis(view);
                        var rect = ElevCropUtil.ComputeViewRect(pts, toView, paddingMm, minWidthMm, minHeightMm);
                        var depthFt = ElevCropUtil.MmToFt(Math.Max(clipDepthMm, 1.0));

                        try
                        {
                            ElevCropUtil.ApplyCropBox(view, rect, depthFt);
                            updated.Add(new { viewId = view.Id.IntValue(), cropApplied = true });
                        }
                        catch (Exception ex)
                        {
                            skipped.Add(new { viewId = view.Id.IntValue(), reason = ex.Message });
                        }
                    }

                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    roomId,
                    updated,
                    skipped,
                    inputUnits = ElevCropUtil.UnitsIn(),
                    internalUnits = ElevCropUtil.UnitsInt()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}


