// ================================================================
// File   : Commands/Room/GetRoomBoundariesCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Command: get_room_boundaries
// Purpose:
//   複数 Room の境界線（外形線＋必要に応じて島ループ）を
//   一括で mm 座標として返す読み取り専用コマンド。
// Policy :
//   - 入力は elementId 配列（roomIds）または viewId（ビュー内に可視な Room を対象）。
//   - 失敗した Room は issues.itemErrors[] に集約し、他は継続。
//   - mm/単位メタは UnitHelper に揃える。
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
    public class GetRoomBoundariesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_boundaries";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
                return ResultUtil.Err("アクティブドキュメントが見つかりません。");

            var p = (JObject)(cmd.Params ?? new JObject());

            // roomIds: 明示指定があればそれを優先
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
            bool includeIslands = p.Value<bool?>("includeIslands") ?? true;
            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? "Finish";

            // 対象 Room 一覧を解決
            var rooms = ResolveRooms(doc, roomIds, viewIdParam, out var resolveErrors);

            var roomsOut = new List<object>();
            var itemErrors = new List<object>();
            if (resolveErrors.Count > 0)
                itemErrors.AddRange(resolveErrors);

            // boundaryLocation → SpatialElementBoundaryOptions
            var opt = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };

            foreach (var room in rooms)
            {
                try
                {
                    var segmentsPerLoop = room.GetBoundarySegments(opt);
                    if (segmentsPerLoop == null || segmentsPerLoop.Count == 0)
                    {
                        itemErrors.Add(new
                        {
                            roomId = room.Id.IntValue(),
                            message = "Room 境界が取得できませんでした。（Not enclosed / Unplaced の可能性）"
                        });
                        continue;
                    }

                    var loops = new List<object>();
                    int loopIndex = 0;
                    foreach (var segs in segmentsPerLoop)
                    {
                        if (segs == null || segs.Count == 0)
                        {
                            loopIndex++;
                            continue;
                        }

                        // includeIslands=false の場合は loopIndex==0 のみを外周として返す
                        if (!includeIslands && loopIndex > 0)
                        {
                            loopIndex++;
                            continue;
                        }

                        var segObjs = new List<object>(segs.Count);
                        foreach (var bs in segs)
                        {
                            var c = bs.GetCurve();
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

                        loops.Add(new
                        {
                            loopIndex,
                            segments = segObjs
                        });

                        loopIndex++;
                        if (!includeIslands)
                        {
                            // islands を含めない場合は最初の有効ループだけで十分
                            break;
                        }
                    }

                    var levelName = GetLevelName(doc, room.LevelId);

                    roomsOut.Add(new
                    {
                        roomId = room.Id.IntValue(),
                        uniqueId = room.UniqueId ?? string.Empty,
                        level = levelName,
                        loops
                    });
                }
                catch (Exception ex)
                {
                    itemErrors.Add(new
                    {
                        roomId = room.Id.IntValue(),
                        message = ex.Message
                    });
                }
            }

            return ResultUtil.Ok(new
            {
                totalCount = roomsOut.Count,
                rooms = roomsOut,
                boundaryLocation = opt.SpatialElementBoundaryLocation.ToString(),
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
                        var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as Autodesk.Revit.DB.Architecture.Room;
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

            // roomIds が指定されていない場合:
            // viewId があればそのビューに可視な Room を対象、それ以外はドキュメント内の全 Room。
            if (viewId > 0)
            {
                var viewElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
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

        private static string GetLevelName(Document doc, ElementId levelId)
        {
            if (levelId == ElementId.InvalidElementId) return string.Empty;
            var level = doc.GetElement(levelId) as Level;
            return level?.Name ?? string.Empty;
        }
    }
}


