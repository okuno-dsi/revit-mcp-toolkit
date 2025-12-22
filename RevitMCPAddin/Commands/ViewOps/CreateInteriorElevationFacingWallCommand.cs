// ============================================================================
// File: Commands/ViewOps/CreateInteriorElevationFacingWallCommand.cs
// Purpose : 部屋と壁を指定して、壁に正対した Interior Elevation を作成
// Target  : .NET Framework 4.8 / Revit 2023 API
// Depends : Autodesk.Revit.DB, Autodesk.Revit.DB.Architecture, Autodesk.Revit.UI
//           Newtonsoft.Json.Linq, RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand)
// Notes   :
//   - ElevationMarker を任意方向に向けるため、マーカーをZ軸回りに回転して slot=0 を壁方向に合わせる
//   - origin は壁の中点から「部屋内向き」に offsetMm だけ入った位置
//   - optional: autoCrop で部屋境界に合わせてクロップ（padding, minW/minH, clipDepth）
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
    internal static class FacingWallUtil
    {
        public static double MmToFt(double mm) => ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) => ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        public static ViewPlan ResolveHostPlanForLevel(Document doc, ElementId levelId)
        {
            if (levelId == ElementId.InvalidElementId) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(vp => vp.ViewType == ViewType.FloorPlan && vp.GenLevel?.Id == levelId);
        }

        public static (XYZ mid, XYZ tangent) GetWallMidAndTangent(Curve c)
        {
            if (c == null) return (XYZ.Zero, XYZ.BasisX);
            try
            {
                // 0.5 パラメータで中点
                XYZ mid = c.Evaluate(0.5, true);
                XYZ t;
                if (c is Line ln)
                {
                    t = (ln.GetEndPoint(1) - ln.GetEndPoint(0)).Normalize();
                }
                else if (c is Arc arc)
                {
                    // 近似：前後微小差分で接線を推定
                    var p0 = c.Evaluate(0.49, true);
                    var p1 = c.Evaluate(0.51, true);
                    t = (p1 - p0).Normalize();
                }
                else
                {
                    var p0 = c.Evaluate(0.49, true);
                    var p1 = c.Evaluate(0.51, true);
                    t = (p1 - p0).Normalize();
                }
                // XY 平面に落とす
                t = new XYZ(t.X, t.Y, 0).Normalize();
                return (mid, (t.IsZeroLength() ? XYZ.BasisX : t));
            }
            catch { return (XYZ.Zero, XYZ.BasisX); }
        }

        public static bool IsZeroLength(this XYZ v, double tol = 1e-9)
        {
            return v == null || v.GetLength() <= tol;
        }

        public static XYZ PerpLeft(XYZ t) => new XYZ(-t.Y, t.X, 0).Normalize();
        public static XYZ PerpRight(XYZ t) => new XYZ(t.Y, -t.X, 0).Normalize();

        public static bool TryRoomSideNormal(Autodesk.Revit.DB.Architecture.Room room, XYZ basePoint, XYZ nCandidate, double testDistFt, out XYZ inward)
        {
            inward = XYZ.Zero;
            try
            {
                var p = basePoint + nCandidate * testDistFt;
                if (room.IsPointInRoom(p))
                {
                    inward = nCandidate; // nCandidate が室内向き
                    return true;
                }
                // 逆側を試す
                var q = basePoint - nCandidate * testDistFt;
                if (room.IsPointInRoom(q))
                {
                    inward = -nCandidate;
                    return true;
                }
            }
            catch { }
            return false;
        }

        // ---- オプションの自動クロップ補助 ----
        public static (Transform toWorld, Transform toView) BuildViewBasis(ViewSection v)
        {
            var T = Transform.Identity;
            T.Origin = v.Origin;
            T.BasisX = v.RightDirection;
            T.BasisY = v.UpDirection;
            T.BasisZ = v.ViewDirection;
            return (T, T.Inverse);
        }

        public static IList<XYZ> CollectRoomBoundary(Document doc, Autodesk.Revit.DB.Architecture.Room room, string boundaryLocation)
        {
            var opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = boundaryLocation.Equals("Core", StringComparison.OrdinalIgnoreCase)
                    ? SpatialElementBoundaryLocation.CoreBoundary
                    : SpatialElementBoundaryLocation.Finish
            };
            var pts = new List<XYZ>(256);
            foreach (var loop in room.GetBoundarySegments(opt))
            {
                foreach (var seg in loop)
                {
                    var c = seg.GetCurve();
                    if (c == null) continue;
                    pts.Add(c.GetEndPoint(0));
                    pts.Add(c.GetEndPoint(1));
                }
            }
            return pts;
        }

        public static (double minX, double minY, double maxX, double maxY) ComputeRectInView(
            IEnumerable<XYZ> worldPts, Transform toView, double paddingMm, double minWidthMm, double minHeightMm)
        {
            double pad = MmToFt(paddingMm);

            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

            foreach (var w in worldPts)
            {
                var p = toView.OfPoint(w);
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            // .NET 4.8 互換：IsFinite の代替
            bool Fin(double v) => !(double.IsNaN(v) || double.IsInfinity(v));
            if (!Fin(minX) || !Fin(minY) || !Fin(maxX) || !Fin(maxY))
            {
                minX = minY = -pad;
                maxX = maxY = +pad;
            }

            // padding
            minX -= pad; minY -= pad;
            maxX += pad; maxY += pad;

            // 最小幅・高さの確保
            double wth = maxX - minX;
            double hgt = maxY - minY;
            double minW = MmToFt(minWidthMm);
            double minH = MmToFt(minHeightMm);

            if (wth < minW)
            {
                double d = (minW - wth) * 0.5;
                minX -= d; maxX += d;
            }
            if (hgt < minH)
            {
                double d = (minH - hgt) * 0.5;
                minY -= d; maxY += d;
            }
            return (minX, minY, maxX, maxY);
        }

        public static void ApplyCrop(ViewSection v, (double minX, double minY, double maxX, double maxY) rect, double depthFt)
        {
            var (toWorld, _) = BuildViewBasis(v);
            var bb = new BoundingBoxXYZ
            {
                Transform = toWorld,
                Min = new XYZ(rect.minX, rect.minY, 0.0),
                Max = new XYZ(rect.maxX, rect.maxY, Math.Max(depthFt, 1e-3))
            };
            v.CropBoxActive = true;
            v.CropBoxVisible = true;
            v.CropBox = bb;

            try
            {
                var farClip = v.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING);
                var farOff = v.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                if (farClip != null && !farClip.IsReadOnly) farClip.Set(1);
                if (farOff != null && !farOff.IsReadOnly) farOff.Set(depthFt);
            }
            catch { /* ignore */ }
        }
    }

    // ----------------------------------------------------------------------------
    // create_interior_elevation_facing_wall
    //   Params:
    //     roomId|roomUniqueId, wallId|wallUniqueId (必須)
    //     offsetMm?: double = 800
    //     scale?: int = 100
    //     viewTypeId?: int  (Elevation用)
    //     hostViewId? / levelId? / levelName?  (Plan解決)
    //     name?: string
    //     autoCrop?: {
    //        boundaryLocation?: "Finish"|"Core" = "Finish",
    //        paddingMm?: 200, minWidthMm?:1200, minHeightMm?:1200, clipDepthMm?:1000
    //     }
    // ----------------------------------------------------------------------------
    public class CreateInteriorElevationFacingWallCommand : IRevitCommandHandler
    {
        public string CommandName => "create_interior_elevation_facing_wall";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params ?? new JObject();

            // ---- Resolve room ----
            Autodesk.Revit.DB.Architecture.Room room = null;
            if (p.TryGetValue("roomId", out var rIdTok))
                room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(rIdTok.Value<int>())) as Autodesk.Revit.DB.Architecture.Room;
            else if (p.TryGetValue("roomUniqueId", out var rUidTok))
                room = doc.GetElement(rUidTok.Value<string>()) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return new { ok = false, msg = "Room not found (roomId/roomUniqueId)." };

            // ---- Resolve wall ----
            Wall wall = null;
            if (p.TryGetValue("wallId", out var wIdTok))
                wall = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wIdTok.Value<int>())) as Wall;
            else if (p.TryGetValue("wallUniqueId", out var wUidTok))
                wall = doc.GetElement(wUidTok.Value<string>()) as Wall;
            if (wall == null) return new { ok = false, msg = "Wall not found (wallId/wallUniqueId)." };

            // ---- Host plan ----
            ViewPlan hostPlan = null;
            int hostViewId = p.Value<int?>("hostViewId") ?? 0;
            if (hostViewId > 0)
                hostPlan = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(hostViewId)) as ViewPlan;

            if (hostPlan == null)
            {
                int levelId = p.Value<int?>("levelId") ?? room.LevelId.IntValue();
                string levelName = p.Value<string>("levelName");
                ElementId lid = ElementId.InvalidElementId;

                if (levelId > 0) lid = Autodesk.Revit.DB.ElementIdCompat.From(levelId);
                else if (!string.IsNullOrWhiteSpace(levelName))
                {
                    var lv = new FilteredElementCollector(doc).OfClass(typeof(Level))
                        .Cast<Level>().FirstOrDefault(x => string.Equals(x.Name ?? "", levelName, StringComparison.OrdinalIgnoreCase));
                    if (lv != null) lid = lv.Id;
                }
                if (lid == ElementId.InvalidElementId) lid = room.LevelId;

                hostPlan = FacingWallUtil.ResolveHostPlanForLevel(doc, lid);
                if (hostPlan == null)
                    hostPlan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>().FirstOrDefault(vp => vp.ViewType == ViewType.FloorPlan);
            }
            if (hostPlan == null) return new { ok = false, msg = "ホストとなる平面ビュー(ViewPlan)が解決できませんでした。" };

            // ---- Elevation ViewFamilyType ----
            ViewFamilyType vft = null;
            int vftId = p.Value<int?>("viewTypeId") ?? 0;
            if (vftId > 0)
                vft = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vftId)) as ViewFamilyType;
            if (vft == null)
            {
                vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.Elevation);
            }
            if (vft == null) return new { ok = false, msg = "Elevation 用の ViewFamilyType が見つかりません。" };

            // ---- Compute wall mid / tangent / inward normal ----
            var lc = wall.Location as LocationCurve;
            if (lc?.Curve == null) return new { ok = false, msg = "Wall has no LocationCurve." };

            var (mid, tangent) = FacingWallUtil.GetWallMidAndTangent(lc.Curve);
            var nCand = FacingWallUtil.PerpLeft(tangent); // 左手法線を候補
            if (nCand.IsZeroLength()) nCand = XYZ.BasisY;

            double testFt = FacingWallUtil.MmToFt(300.0);
            if (!FacingWallUtil.TryRoomSideNormal(room, mid, nCand, testFt, out var inward))
            {
                // セーフティ：部屋重心方向を使用
                XYZ centroid = XYZ.Zero;
                try
                {
                    var calc = new SpatialElementGeometryCalculator(doc);
                    var res = calc.CalculateSpatialElementGeometry(room);
                    centroid = res.GetGeometry()?.ComputeCentroid() ?? XYZ.Zero;
                }
                catch { }
                var dirToRoom = (centroid - mid); dirToRoom = new XYZ(dirToRoom.X, dirToRoom.Y, 0).Normalize();
                if (dirToRoom.IsZeroLength()) dirToRoom = XYZ.BasisX;
                inward = dirToRoom;
            }

            // ---- Marker origin & desired view dir ----
            double offsetMm = p.Value<double?>("offsetMm") ?? 800.0;      // 部屋側に入った位置
            double offsetFt = FacingWallUtil.MmToFt(offsetMm);

            var origin = mid + inward * offsetFt;                          // 室内側
            var desiredDir = -inward;                                       // 壁へ向く

            int scale = p.Value<int?>("scale") ?? 100;
            string name = p.Value<string>("name") ?? $"IntElev-{wall.Id.IntValue()}";

            ViewSection elevView = null;
            using (var tx = new Transaction(doc, "Create Interior Elevation (Facing Wall)"))
            {
                tx.Start();

                // 1) マーカー作成
                var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, scale);

                // 2) マーカーをZ回転させ、slot=0（East）が desiredDir を向くように整列
                //    BasisX(=East) から desiredDir への回転角を算出
                double ang = Math.Atan2(desiredDir.Y, desiredDir.X) - Math.Atan2(0.0, 1.0); // = atan2(y,x)
                var axis = Line.CreateUnbound(origin, XYZ.BasisZ);
                try { ElementTransformUtils.RotateElement(doc, marker.Id, axis, ang); } catch { /* 続行 */ }

                // 3) slot=0 で Elevation 作成（正対のスロット）
                elevView = marker.CreateElevation(doc, hostPlan.Id, 0);
                elevView.Scale = scale;
                try { elevView.Name = name; } catch { /* 競合時はRevitが(2)等 */ }

                // 4) optional: autoCrop
                if (p.TryGetValue("autoCrop", out var acTok) && acTok is JObject ac)
                {
                    string boundaryLoc = ac.Value<string>("boundaryLocation") ?? "Finish";
                    double paddingMm = ac.Value<double?>("paddingMm") ?? 200.0;
                    double minWidthMm = ac.Value<double?>("minWidthMm") ?? 1200.0;
                    double minHeightMm = ac.Value<double?>("minHeightMm") ?? 1200.0;
                    double clipDepthMm = ac.Value<double?>("clipDepthMm") ?? 1000.0;

                    var pts = FacingWallUtil.CollectRoomBoundary(doc, room, boundaryLoc);
                    var (_, toView) = FacingWallUtil.BuildViewBasis(elevView);
                    var rect = FacingWallUtil.ComputeRectInView(pts, toView, paddingMm, minWidthMm, minHeightMm);
                    var depthFt = FacingWallUtil.MmToFt(Math.Max(clipDepthMm, 1.0));

                    FacingWallUtil.ApplyCrop(elevView, rect, depthFt);
                }

                tx.Commit();
            }

            return new
            {
                ok = true,
                viewId = elevView?.Id.IntValue() ?? 0,
                roomId = room.Id.IntValue(),
                wallId = wall.Id.IntValue(),
                origin = new
                {
                    x = Math.Round(FacingWallUtil.FtToMm(origin.X), 3),
                    y = Math.Round(FacingWallUtil.FtToMm(origin.Y), 3),
                    z = Math.Round(FacingWallUtil.FtToMm(origin.Z), 3)
                },
                viewDirection = new { x = desiredDir.X, y = desiredDir.Y, z = desiredDir.Z },
                scale,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }
}


