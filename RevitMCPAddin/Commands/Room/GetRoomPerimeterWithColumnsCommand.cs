// ================================================================
// File   : Commands/Room/GetRoomPerimeterWithColumnsCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose:
//   Room の周長に、指定または自動検出した Column を一時的に Room Bounding として含めて
//   再計算した値を返します。
//   TransactionGroup.RollBack() で必ず元に戻すため、モデル自体は変更しません。
// Notes  :
//   - 出力の周長は mm 単位です。
//   - autoDetectColumnsInRoom:true を指定すると、Room の BoundingBox 周辺から
//     （OST_Columns / OST_StructuralColumns）を自動検出して columnIds に追加します。
//   - includeSegments:true を指定すると、周長を構成するループとセグメント座標を mm 単位で返します。
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class GetRoomPerimeterWithColumnsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_perimeter_with_columns";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("roomId", out var roomToken))
                return ResultUtil.Err("roomId は必須です。");

            int roomId = roomToken.Value<int>();
            var room = doc.GetElement(new ElementId(roomId)) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null)
                return ResultUtil.Err($"Room が見つかりません: roomId={roomId}");

            // オプション
            bool includeSegments = p.Value<bool?>("includeSegments") ?? false;
            bool includeIslands = p.Value<bool?>("includeIslands") ?? true;
            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            bool useBuiltInPerimeterIfAvailable = p.Value<bool?>("useBuiltInPerimeterIfAvailable") ?? true;

            // 対象 Column IDs（明示指定）
            var columnIds = new List<ElementId>();
            if (p.TryGetValue("columnIds", out var colArrayToken) && colArrayToken is JArray colArray)
            {
                foreach (var t in colArray)
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        int id = t.Value<int>();
                        if (id > 0) columnIds.Add(new ElementId(id));
                    }
                }
            }

            // 自動検出フラグとサーチ範囲（mm）
            bool autoDetectColumns = p.Value<bool?>("autoDetectColumnsInRoom") ?? false;
            double searchMarginMm = p.Value<double?>("searchMarginMm") ?? 1000.0; // default 1m

            IList<ElementId> autoDetectedColumns = new List<ElementId>();
            if (autoDetectColumns)
            {
                try
                {
                    autoDetectedColumns = AutoDetectColumnsInRoom(doc, room, searchMarginMm);
                    if (autoDetectedColumns != null)
                    {
                        foreach (var eid in autoDetectedColumns)
                        {
                            if (!columnIds.Contains(eid))
                                columnIds.Add(eid);
                        }
                    }
                }
                catch
                {
                    // best-effort
                    autoDetectedColumns = new List<ElementId>();
                }
            }

            // boundaryLocation 文字列を Revit API オプションへマッピング
            var opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };

            double perimeterFeet = 0.0;
            var toggledColumnIds = new List<int>();
            List<object> loopsOut = includeSegments ? new List<object>() : null;

            using (var tg = new TransactionGroup(doc, "Temp RoomBounding for Perimeter"))
            {
                tg.Start();

                // (1) 一時的に Column の Room Bounding を ON
                if (columnIds.Count > 0)
                {
                    using (var t = new Transaction(doc, "Enable Room Bounding for columns"))
                    {
                        t.Start();
                        foreach (var id in columnIds)
                        {
                            var e = doc.GetElement(id);
                            if (e == null) continue;

                            var pRoomBound = e.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                            if (pRoomBound != null && !pRoomBound.IsReadOnly)
                            {
                                try
                                {
                                    pRoomBound.Set(1);
                                    toggledColumnIds.Add(id.IntegerValue);
                                }
                                catch
                                {
                                    // 変更不可の場合はスキップ
                                }
                            }
                        }
                        t.Commit();
                    }

                    // 変更を反映してから周長を取得
                    try { doc.Regenerate(); } catch { /* ignore */ }
                }

                // (2) まず ROOM_PERIMETER（組込パラメータ）を試す
                if (useBuiltInPerimeterIfAvailable)
                {
                    var paramPerim = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
                    if (paramPerim != null && paramPerim.StorageType == StorageType.Double)
                    {
                        try { perimeterFeet = paramPerim.AsDouble(); }
                        catch { perimeterFeet = 0.0; }
                    }
                }

                // (3) 必要に応じて Room.GetBoundarySegments を使って周長とセグメント情報を取得
                //   - ROOM_PERIMETER を使わない場合
                //   - ROOM_PERIMETER が 0 以下の場合
                //   - includeSegments:true の場合（セグメント情報のため）
                if (!useBuiltInPerimeterIfAvailable || perimeterFeet <= 0.0 || includeSegments)
                {
                    var boundaryLoops = room.GetBoundarySegments(opt);
                    if (boundaryLoops != null)
                    {
                        int loopIndex = 0;
                        foreach (var loop in boundaryLoops)
                        {
                            // includeIslands == false のときはループ 0 のみを対象にする（島は無視）
                            if (!includeIslands && loopIndex > 0)
                                break;

                            List<object> segObjs = includeSegments ? new List<object>() : null;

                            foreach (var bs in loop)
                            {
                                var c = bs.GetCurve();
                                if (c == null) continue;

                                // ROOM_PERIMETER を使わない場合、または値が 0 以下の場合のみ、ここで周長を加算
                                if (!useBuiltInPerimeterIfAvailable || perimeterFeet <= 0.0)
                                    perimeterFeet += c.Length;

                                if (includeSegments && segObjs != null)
                                {
                                    var p0 = c.GetEndPoint(0);
                                    var p1 = c.GetEndPoint(1);

                                    segObjs.Add(new
                                    {
                                        start = new
                                        {
                                            x = Math.Round(UnitHelper.FtToMm(p0.X), 3),
                                            y = Math.Round(UnitHelper.FtToMm(p0.Y), 3),
                                            z = Math.Round(UnitHelper.FtToMm(p0.Z), 3)
                                        },
                                        end = new
                                        {
                                            x = Math.Round(UnitHelper.FtToMm(p1.X), 3),
                                            y = Math.Round(UnitHelper.FtToMm(p1.Y), 3),
                                            z = Math.Round(UnitHelper.FtToMm(p1.Z), 3)
                                        }
                                    });
                                }
                            }

                            if (includeSegments && segObjs != null)
                            {
                                loopsOut.Add(new
                                {
                                    loopIndex,
                                    segments = segObjs
                                });
                            }

                            loopIndex++;
                        }
                    }
                }

                // (4) 変更はすべて破棄して元の状態に戻す
                tg.RollBack();
            }

            double perimeterMm = UnitUtils.ConvertFromInternalUnits(perimeterFeet, UnitTypeId.Millimeters);

            return ResultUtil.Ok(new
            {
                roomId = roomId,
                perimeterMm,
                loops = loopsOut,
                units = new { Length = "mm" },
                basis = new
                {
                    includeIslands,
                    boundaryLocation = opt.SpatialElementBoundaryLocation.ToString(),
                    useBuiltInPerimeterIfAvailable,
                    autoDetectColumnsInRoom = autoDetectColumns,
                    searchMarginMm,
                    autoDetectedColumnIds = autoDetectedColumns.Select(x => x.IntegerValue).ToArray(),
                    toggledColumnIds = toggledColumnIds
                }
            });
        }

        /// <summary>
        /// Room の BoundingBox の周辺にある Column を自動検出し、
        /// Room.IsPointInRoom(...) を使って「部分的にでも Room に掛かっている」ものを抽出します。
        /// searchMarginMm は Room の BoundingBox をどれだけ広げて探索するか（mm 単位）。
        /// </summary>
        private static IList<ElementId> AutoDetectColumnsInRoom(Document doc, Autodesk.Revit.DB.Architecture.Room room, double searchMarginMm)
        {
            var result = new List<ElementId>();
            if (doc == null || room == null) return result;

            var roomBb = room.get_BoundingBox(null);
            if (roomBb == null) return result;

            double marginFt = UnitUtils.ConvertToInternalUnits(searchMarginMm, UnitTypeId.Millimeters);

            var min = new XYZ(roomBb.Min.X - marginFt, roomBb.Min.Y - marginFt, roomBb.Min.Z - marginFt);
            var max = new XYZ(roomBb.Max.X + marginFt, roomBb.Max.Y + marginFt, roomBb.Max.Z + marginFt);
            var outline = new Outline(min, max);
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            var filters = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_Columns),
                new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns)
            };
            var catFilter = new LogicalOrFilter(filters);

            var collector = new FilteredElementCollector(doc)
                .WherePasses(catFilter)
                .WherePasses(bbFilter)
                .WhereElementIsNotElementType();

            foreach (var e in collector)
            {
                var fi = e as FamilyInstance;
                if (fi == null) continue;
                var bb = fi.get_BoundingBox(null);
                if (bb == null) continue;

                if (IntersectsRoomApprox(room, bb, roomBb))
                    result.Add(fi.Id);
            }

            return result;
        }

        /// <summary>
        /// Column の BoundingBox が Room と高さ方向で重なっている範囲の中間高さで
        /// 代表点（中心＋４隅）をサンプリングし、いずれかが Room 内にあれば
        /// 「Room と交差している」とみなします。
        /// </summary>
        private static bool IntersectsRoomApprox(Autodesk.Revit.DB.Architecture.Room room, BoundingBoxXYZ colBb, BoundingBoxXYZ roomBb)
        {
            if (room == null || colBb == null || roomBb == null) return false;

            // Z 方向の重なりを確認
            double zMin = Math.Max(colBb.Min.Z, roomBb.Min.Z);
            double zMax = Math.Min(colBb.Max.Z, roomBb.Max.Z);
            if (zMax <= zMin) return false;

            double zMid = 0.5 * (zMin + zMax);

            double xMin = colBb.Min.X, xMax = colBb.Max.X;
            double yMin = colBb.Min.Y, yMax = colBb.Max.Y;

            double xMid = 0.5 * (xMin + xMax);
            double yMid = 0.5 * (yMin + yMax);

            var pts = new[]
            {
                new XYZ(xMid, yMid, zMid),
                new XYZ(xMin, yMin, zMid),
                new XYZ(xMax, yMin, zMid),
                new XYZ(xMax, yMax, zMid),
                new XYZ(xMin, yMax, zMid)
            };

            foreach (var pt in pts)
            {
                try
                {
                    if (room.IsPointInRoom(pt)) return true;
                }
                catch
                {
                    // ポイントごとの例外は無視
                }
            }

            return false;
        }
    }
}
