// ================================================================
// File: Commands/Room/GetRoomNeighborsCommand.cs
// Purpose : Roomの外周から隣接する他Roomを列挙（共有長さmmも集計）
// Strategy: BoundarySegment（start/end）→ 中点・法線方向に微小オフセットして
//           IsPointInRoom で反対側の部屋を推定。重複は集計でまとめる。
// Notes   : 境界は Line/Arc 等に対応。Arc の法線は微分ベクトルから算出。
// Depends : UnitHelper, ResultUtil
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
    public class GetRoomNeighborsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_neighbors";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)(cmd.Params ?? new JObject());
            if (!p.TryGetValue("roomId", out var idTok)) return ResultUtil.Err("roomId を指定してください。");
            var room = doc.GetElement(new ElementId(idTok.Value<int>())) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return ResultUtil.Err("Room が見つかりません。");

            // すべてのRoom（判定用）— 多数でもOK
            var allRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.Room>()
                .ToList();

            // 自分自身は除外
            allRooms.RemoveAll(r => r.Id.IntegerValue == room.Id.IntegerValue);

            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var opts = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };
            var loops = room.GetBoundarySegments(opts); // ループ列（既存と同等）:contentReference[oaicite:4]{index=4}

            // 隣接集計: roomId -> { name, sharedLengthMm }
            var agg = new Dictionary<int, (string name, double lengthMm)>();

            // サンプリング距離（mm→ft）
            double sampleMm = 45.0; // 25mm だけ両側にオフセット
            double sampleFt = UnitUtils.ConvertToInternalUnits(sampleMm, UnitTypeId.Millimeters);

            foreach (var segs in loops)
            {
                foreach (var bs in segs)
                {
                    var c = bs.GetCurve();
                    if (c == null) continue;

                    // 中点 & 接線（2D 前提、Zは据え置き）
                    var mid = c.Evaluate(0.5, true); // 中点
                    var der = c.ComputeDerivatives(0.5, true);
                    var tan = der?.BasisX ?? XYZ.BasisX;    // 接線
                    var up = XYZ.BasisZ;
                    var nrm = up.CrossProduct(tan).Normalize(); // XY法線（右手系）

                    if (nrm.IsZeroLength()) nrm = new XYZ(-tan.Y, tan.X, 0).Normalize();

                    // 両側サンプル
                    var aPt = mid + nrm * sampleFt;
                    var bPt = mid - nrm * sampleFt;

                    Autodesk.Revit.DB.Architecture.Room? find(XYZ pt)
                    {
                        foreach (var r in allRooms)
                        {
                            try { if (r.IsPointInRoom(pt)) return r; }
                            catch { /* ignore */ }
                        }
                        return null;
                    }

                    var rA = find(aPt);
                    var rB = find(bPt);

                    Autodesk.Revit.DB.Architecture.Room? neighbor = null;
                    if (rA != null && rB == null) neighbor = rA;
                    else if (rB != null && rA == null) neighbor = rB;
                    else if (rA != null && rB != null) // 両側に別室ヒット→“より遠い側”は壁外のこともあるので優先度なし。片方を採用
                        neighbor = rA.Id.IntegerValue != room.Id.IntegerValue ? rA : rB;

                    if (neighbor != null)
                    {
                        var nid = neighbor.Id.IntegerValue;
                        var lengthMm = UnitUtils.ConvertFromInternalUnits(c.Length, UnitTypeId.Millimeters);

                        if (agg.TryGetValue(nid, out var cur))
                            agg[nid] = (cur.name, cur.lengthMm + lengthMm);
                        else
                            agg[nid] = (neighbor.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? neighbor.Name ?? "", lengthMm);
                    }
                }
            }

            var items = agg
                .OrderByDescending(kv => kv.Value.lengthMm)
                .Select(kv => new
                {
                    roomId = kv.Key,
                    name = kv.Value.name,
                    sharedLengthMm = Math.Round(kv.Value.lengthMm, 3)
                })
                .ToList();

            return ResultUtil.Ok(new
            {
                target = new { roomId = room.Id.IntegerValue, name = room.Name ?? "" },
                neighbors = items,
                boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(),
                units = new { Length = "mm" }
            });
        }
    }
}
