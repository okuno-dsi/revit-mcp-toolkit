// ================================================================
// File   : Commands/Room/CreateRoomMassesCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Command: create_room_masses
// Purpose:
//   複数 Room から、その外形線と高さに合わせた DirectShape Mass
//   をまとめて生成する安全な書き込みコマンド。
// Policy :
//   - roomIds[] 指定 or viewId 指定 or ドキュメント全体から Room を解決。
//   - heightMode は bbox / fixed をサポート（将来拡張も見据えた文字列指定）。
//   - 個々の Room での失敗は itemErrors[] に記録し、他は継続。
//   - maxRooms を超える場合は即座にエラーを返して過負荷を防止。
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
    public class CreateRoomMassesCommand : IRevitCommandHandler
    {
        public string CommandName => "create_room_masses";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
                return ResultUtil.Err("アクティブドキュメントが見つかりません。");

            var p = (JObject)(cmd.Params ?? new JObject());

            var roomIds = new List<int>();
            if (p.TryGetValue("roomIds", out var roomIdsToken) && roomIdsToken is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        var id = t.Value<int>();
                        if (id > 0) roomIds.Add(id);
                    }
                }
            }

            int viewIdParam = p.Value<int?>("viewId") ?? 0;
            int maxRooms = p.Value<int?>("maxRooms") ?? 200;
            if (maxRooms <= 0) maxRooms = 200;

            string heightMode = p.Value<string>("heightMode") ?? "bbox";
            double fixedHeightMm = p.Value<double?>("fixedHeightMm") ?? 2800.0;
            bool useMassCategory = p.Value<bool?>("useMassCategory") ?? true;

            // 対象 Room 解決
            var rooms = ResolveRooms(doc, roomIds, viewIdParam, out var resolveErrors);
            if (rooms.Count == 0)
            {
                return ResultUtil.Err(new
                {
                    msg = "対象となる Room が見つかりませんでした。",
                    itemErrors = resolveErrors
                });
            }

            if (rooms.Count > maxRooms)
            {
                return ResultUtil.Err(new
                {
                    msg = $"対象 Room 数が上限を超えています: count={rooms.Count}, maxRooms={maxRooms}",
                    count = rooms.Count,
                    maxRooms
                });
            }

            var created = new List<object>();
            var itemErrors = new List<object>();

            var catId = useMassCategory
                ? new ElementId(BuiltInCategory.OST_Mass)
                : new ElementId(BuiltInCategory.OST_GenericModel);

            using (var tx = new Transaction(doc, "Create Room Masses"))
            {
                tx.Start();

                foreach (var room in rooms)
                {
                    int roomId = room.Id.IntegerValue;
                    try
                    {
                        // Room の外周ループ（内側の最大ループを採用）
                        var loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                        if (loops == null || loops.Count == 0)
                        {
                            itemErrors.Add(new { roomId, message = "Room 境界が取得できませんでした。（Not enclosed / Unplaced の可能性）" });
                            continue;
                        }

                        IList<BoundarySegment> outerLoop = null;
                        double maxPerim = -1.0;
                        foreach (var loop in loops)
                        {
                            if (loop == null || loop.Count == 0) continue;
                            double per = 0.0;
                            foreach (var bs in loop)
                            {
                                var c = bs.GetCurve();
                                if (c != null) per += c.Length;
                            }
                            if (per > maxPerim)
                            {
                                maxPerim = per;
                                outerLoop = loop;
                            }
                        }

                        if (outerLoop == null || outerLoop.Count == 0)
                        {
                            itemErrors.Add(new { roomId, message = "Room の外周ループを特定できませんでした。" });
                            continue;
                        }

                        // Room 高さ（BoundingBox の Z 差分）を基準にする
                        var bb = room.get_BoundingBox(null);
                        double heightFt = 0.0;
                        if (bb != null)
                        {
                            heightFt = Math.Max(0.0, bb.Max.Z - bb.Min.Z);
                        }

                        if (heightMode.Equals("fixed", StringComparison.OrdinalIgnoreCase))
                        {
                            // 固定高さ mm → ft
                            heightFt = UnitUtils.ConvertToInternalUnits(fixedHeightMm, UnitTypeId.Millimeters);
                        }
                        else if (heightMode.Equals("bbox", StringComparison.OrdinalIgnoreCase))
                        {
                            // bbox ベースで高さが 0 の場合はフォールバック高さを使用
                            if (heightFt <= 0.0)
                            {
                                heightFt = UnitUtils.ConvertToInternalUnits(fixedHeightMm, UnitTypeId.Millimeters);
                            }
                        }
                        else
                        {
                            // 未知の heightMode は bbox と同等扱い
                            if (heightFt <= 0.0)
                            {
                                heightFt = UnitUtils.ConvertToInternalUnits(fixedHeightMm, UnitTypeId.Millimeters);
                            }
                        }

                        if (heightFt <= 0.0)
                        {
                            itemErrors.Add(new { roomId, message = "Room 高さが 0 でマスを作成できませんでした。" });
                            continue;
                        }

                        // ループを CurveLoop として構築（既存カーブをコピーして使用）
                        var curves = new List<Curve>();
                        foreach (var bs in outerLoop)
                        {
                            var c = bs.GetCurve();
                            if (c == null) continue;
                            curves.Add(c.Clone());
                        }

                        if (curves.Count < 3)
                        {
                            itemErrors.Add(new { roomId, message = "Room 外周の有効な曲線が不足しています。" });
                            continue;
                        }

                        CurveLoop loopCl;
                        try
                        {
                            loopCl = CurveLoop.Create(curves);
                        }
                        catch (Exception exLoop)
                        {
                            itemErrors.Add(new { roomId, message = $"CurveLoop の構築に失敗しました: {exLoop.Message}" });
                            continue;
                        }

                        if (loopCl.IsOpen())
                        {
                            itemErrors.Add(new { roomId, message = "Room 外周ループが閉じていません。" });
                            continue;
                        }

                        if (!loopCl.HasPlane())
                        {
                            itemErrors.Add(new { roomId, message = "Room 外周ループが平面上にありません。" });
                            continue;
                        }

                        GeometryObject solid;
                        try
                        {
                            solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                                new List<CurveLoop> { loopCl },
                                XYZ.BasisZ,
                                heightFt);
                        }
                        catch (Exception exSolid)
                        {
                            itemErrors.Add(new { roomId, message = $"マスの押し出し形状の生成に失敗しました: {exSolid.Message}" });
                            continue;
                        }

                        try
                        {
                            var ds = DirectShape.CreateElement(doc, catId);
                            ds.ApplicationId = "MCPRoomMass";
                            ds.ApplicationDataId = Guid.NewGuid().ToString();
                            ds.SetShape(new List<GeometryObject> { solid });
                            created.Add(new
                            {
                                roomId,
                                massId = ds.Id.IntegerValue
                            });
                        }
                        catch (Exception exDs)
                        {
                            itemErrors.Add(new { roomId, message = $"DirectShape Mass の作成に失敗しました: {exDs.Message}" });
                        }
                    }
                    catch (Exception ex)
                    {
                        itemErrors.Add(new
                        {
                            roomId,
                            message = ex.Message
                        });
                    }
                }

                tx.Commit();
            }

            return ResultUtil.Ok(new
            {
                createdCount = created.Count,
                created,
                units = UnitHelper.DefaultUnitsMeta(),
                issues = new
                {
                    itemErrors
                }
            });
        }

        private static List<Autodesk.Revit.DB.Architecture.Room> ResolveRooms(
            Document doc,
            List<int> roomIds,
            int viewId,
            out List<object> errors)
        {
            errors = new List<object>();
            var result = new List<Autodesk.Revit.DB.Architecture.Room>();

            if (roomIds != null && roomIds.Count > 0)
            {
                foreach (var id in roomIds.Distinct())
                {
                    try
                    {
                        var e = doc.GetElement(new ElementId(id)) as Autodesk.Revit.DB.Architecture.Room;
                        if (e == null)
                        {
                            errors.Add(new { roomId = id, message = "Room が見つかりません。" });
                            continue;
                        }
                        result.Add(e);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new { roomId = id, message = ex.Message });
                    }
                }
                return result;
            }

            if (viewId > 0)
            {
                var viewElem = doc.GetElement(new ElementId(viewId)) as View;
                if (viewElem == null)
                {
                    errors.Add(new { viewId, message = "指定された viewId のビューが見つかりません。" });
                    return result;
                }

                var collector = new FilteredElementCollector(doc, viewElem.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();

                result.AddRange(collector.OfType<Autodesk.Revit.DB.Architecture.Room>());
            }
            else
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();
                result.AddRange(collector.OfType<Autodesk.Revit.DB.Architecture.Room>());
            }

            return result;
        }
    }
}

