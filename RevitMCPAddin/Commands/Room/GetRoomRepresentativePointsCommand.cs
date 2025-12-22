// ================================================================
// File: Commands/Room/GetRoomRepresentativePointsCommand.cs
// Purpose : 体積重心(既存)に加えて、2D平面重心・ラベル位置を取得できるようにする
// Commands: get_room_planar_centroid, get_room_label_point
// Notes   : 2D平面重心は外周ループを優先（穴は将来対応）。Zは LocationPoint または 0。
// Depends : UnitHelper, ResultUtil, GetRoomBoundaryCommand:contentReference[oaicite:5]{index=5}
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public static class _PolyUtil
    {
        // 2Dポリゴンの重心（Shoelace）
        public static (double cx, double cy, double area2) Centroid2D(IList<XYZ> pts)
        {
            // 末尾に先頭を繋ぐ想定なし→内部で処理
            int n = pts.Count;
            if (n < 3) return (0, 0, 0);
            double A = 0, Cx = 0, Cy = 0;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % n];
                double cross = p.X * q.Y - q.X * p.Y;
                A += cross;
                Cx += (p.X + q.X) * cross;
                Cy += (p.Y + q.Y) * cross;
            }
            if (Math.Abs(A) < 1e-12) return (0, 0, 0);
            A *= 0.5;
            Cx /= (6 * A);
            Cy /= (6 * A);
            return (Cx, Cy, A * 2); // area2 は符号付き2A
        }
    }

    public class GetRoomPlanarCentroidCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_planar_centroid";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)(cmd.Params ?? new JObject());
            if (!p.TryGetValue("elementId", out var idTok))
                return ResultUtil.Err("elementId を指定してください。");

            var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idTok.Value<int>())) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return ResultUtil.Err("Room が見つかりません。");

            var opts = new SpatialElementBoundaryOptions();
            var loops = room.GetBoundarySegments(opts); //:contentReference[oaicite:6]{index=6}
            if (loops == null || loops.Count == 0) return ResultUtil.Err("境界が見つかりません（NotEnclosed かもしれません）。");

            // ひとまず最外周らしいループ（最長周長）を採用
            List<XYZ> outer = null;
            double bestLen = -1;

            foreach (var segs in loops)
            {
                var pts = new List<XYZ>();
                double sum = 0;
                foreach (var bs in segs)
                {
                    var c = bs.GetCurve();
                    sum += c.Length;
                    // 連続重複を避けつつ端点を蓄積
                    var p0 = c.GetEndPoint(0);
                    if (pts.Count == 0 || !pts.Last().IsAlmostEqualTo(p0)) pts.Add(p0);
                    var p1 = c.GetEndPoint(1);
                    if (!pts.Last().IsAlmostEqualTo(p1)) pts.Add(p1);
                }
                if (sum > bestLen) { bestLen = sum; outer = pts; }
            }

            if (outer == null || outer.Count < 3) return ResultUtil.Err("有効な外周が見つかりません。");

            // XYとして重心（ft単位）
            var (cx, cy, _) = _PolyUtil.Centroid2D(outer);
            double z = 0;
            if (room.Location is LocationPoint lp && lp.Point != null) z = lp.Point.Z;

            return ResultUtil.Ok(new
            {
                centroid = new
                {
                    x = Math.Round(UnitHelper.FtToMm(cx), 3),
                    y = Math.Round(UnitHelper.FtToMm(cy), 3),
                    z = Math.Round(UnitHelper.FtToMm(z), 3)
                },
                basis = "outer_loop_xy",
                units = UnitHelper.DefaultUnitsMeta()
            });
        }
    }

    public class GetRoomLabelPointCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_label_point";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)(cmd.Params ?? new JObject());
            if (!p.TryGetValue("elementId", out var idTok))
                return ResultUtil.Err("elementId を指定してください。");

            var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idTok.Value<int>())) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return ResultUtil.Err("Room が見つかりません。");

            XYZ pt = null;

            // Label用の既定位置（Room.Locationが持つ点）— UIのタグ位置と近い
            if (room.Location is LocationPoint lp && lp.Point != null)
            {
                pt = lp.Point;
            }
            else
            {
                // フォールバック: バウンディングボックス中心
                var bb = room.get_BoundingBox(null);
                if (bb != null) pt = (bb.Min + bb.Max) * 0.5;
            }

            if (pt == null) return ResultUtil.Err("位置点を特定できませんでした。");

            return ResultUtil.Ok(new
            {
                labelPoint = new
                {
                    x = Math.Round(UnitHelper.FtToMm(pt.X), 3),
                    y = Math.Round(UnitHelper.FtToMm(pt.Y), 3),
                    z = Math.Round(UnitHelper.FtToMm(pt.Z), 3)
                },
                units = UnitHelper.DefaultUnitsMeta()
            });
        }
    }
}

