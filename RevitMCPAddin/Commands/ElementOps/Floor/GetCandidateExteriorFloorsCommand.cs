using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    /// <summary>
    /// get_candidate_exterior_floors
    /// Room 情報と床まわりの垂直サンプリングのみを用いて、
    /// 「床上に部屋があり、床下に部屋が無い（またはその逆）」床を「外部床候補」として抽出します。
    /// - XY グリッド上のサンプル点を床バウンディングボックス内に配置
    /// - 各サンプル点について、床上側と床下側に Z をずらした点が Room XY 内に入るかを判定
    /// - 床上のみ室内 / 床下のみ室内とみなせる場合に exterior 候補とします。
    /// 旧ロジックで使用していた外壁距離や ShellLayerType には依存しません。
    /// </summary>
    public class GetCandidateExteriorFloorsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_candidate_exterior_floors";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            Document doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return new
                {
                    ok = false,
                    msg = "アクティブドキュメントがありません。",
                    floors = new List<object>()
                };
            }

            JObject p = (JObject)(cmd.Params ?? new JObject());

            // オプション: viewId / levelId / levelIds / sampleGrid / useRooms
            View view = null;
            try
            {
                int? viewId = p.Value<int?>("viewId");
                if (viewId.HasValue && viewId.Value > 0)
                {
                    view = doc.GetElement(new ElementId(viewId.Value)) as View;
                }
            }
            catch
            {
                view = null;
            }

            HashSet<int> levelFilterIds = null;
            try
            {
                int? levelId = p.Value<int?>("levelId");
                if (levelId.HasValue && levelId.Value > 0)
                {
                    levelFilterIds = new HashSet<int> { levelId.Value };
                }

                if (p["levelIds"] is JArray levelArr && levelArr.Count > 0)
                {
                    levelFilterIds = new HashSet<int>();
                    foreach (JToken t in levelArr)
                    {
                        if (t.Type == JTokenType.Integer)
                        {
                            int idVal = t.Value<int>();
                            if (idVal > 0) levelFilterIds.Add(idVal);
                        }
                    }
                    if (levelFilterIds.Count == 0) levelFilterIds = null;
                }
            }
            catch
            {
                levelFilterIds = null;
            }

            int sampleGrid = p.Value<int?>("sampleGrid") ?? 3;
            if (sampleGrid < 1) sampleGrid = 1;
            if (sampleGrid > 7) sampleGrid = 7;

            bool useRooms = p.Value<bool?>("useRooms") ?? true;

            // 任意: elementIds で床候補を限定（ピンポイント判定用）
            HashSet<int> elementFilterIds = null;
            try
            {
                if (p["elementIds"] is JArray elemArr && elemArr.Count > 0)
                {
                    elementFilterIds = new HashSet<int>();
                    foreach (JToken t in elemArr)
                    {
                        if (t.Type == JTokenType.Integer)
                        {
                            int idVal = t.Value<int>();
                            if (idVal > 0) elementFilterIds.Add(idVal);
                        }
                    }
                    if (elementFilterIds.Count == 0) elementFilterIds = null;
                }
            }
            catch
            {
                elementFilterIds = null;
            }

            // レベル情報を準備
            IList<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            Dictionary<int, Level> levelById = new Dictionary<int, Level>();
            foreach (Level l in levels)
            {
                levelById[l.Id.IntegerValue] = l;
            }

            // Room コレクション
            List<Autodesk.Revit.DB.Architecture.Room> rooms = new List<Autodesk.Revit.DB.Architecture.Room>();
            if (useRooms)
            {
                try
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Autodesk.Revit.DB.Architecture.Room>()
                        .ToList();
                }
                catch
                {
                    rooms = new List<Autodesk.Revit.DB.Architecture.Room>();
                }
            }

            // 対象 Floor を収集
            FilteredElementCollector floorCollector = (view != null)
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);

            List<Floor> floors = floorCollector
                .OfClass(typeof(Floor))
                .Cast<Floor>()
                .ToList();

            if (elementFilterIds != null)
            {
                floors = floors
                    .Where(f =>
                    {
                        try
                        {
                            return elementFilterIds.Contains(f.Id.IntegerValue);
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ToList();
            }

            if (levelFilterIds != null)
            {
                floors = floors
                    .Where(f =>
                    {
                        try
                        {
                            ElementId lid = f.LevelId;
                            return lid != null && lid != ElementId.InvalidElementId &&
                                   levelFilterIds.Contains(lid.IntegerValue);
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .ToList();
            }

            List<object> resultFloors = new List<object>();

            foreach (Floor floor in floors)
            {
                if (floor == null) continue;

                int levelId = 0;
                Level level = null;
                try
                {
                    ElementId lid = floor.LevelId;
                    if (lid != null && lid != ElementId.InvalidElementId)
                    {
                        levelId = lid.IntegerValue;
                        if (!levelById.TryGetValue(levelId, out level))
                        {
                            level = doc.GetElement(lid) as Level;
                        }
                    }
                }
                catch
                {
                    levelId = 0;
                    level = null;
                }

                BoundingBoxXYZ bb = null;
                try
                {
                    bb = floor.get_BoundingBox(view);
                    if (bb == null) bb = floor.get_BoundingBox(null);
                }
                catch
                {
                    bb = null;
                }

                if (bb == null) continue;

                double minX = bb.Min.X;
                double maxX = bb.Max.X;
                double minY = bb.Min.Y;
                double maxY = bb.Max.Y;

                if (Math.Abs(maxX - minX) < 1e-6 || Math.Abs(maxY - minY) < 1e-6)
                {
                    continue;
                }

                bool hasLevelBelow = false;
                bool hasLevelAbove = false;
                double levelBelowElev = 0.0;
                double levelAboveElev = 0.0;

                if (level != null && levels.Count > 0)
                {
                    int idx = levels.IndexOf(level);
                    if (idx > 0)
                    {
                        Level lvlBelow = levels[idx - 1];
                        hasLevelBelow = true;
                        levelBelowElev = lvlBelow.Elevation;
                    }
                    if (idx >= 0 && idx < levels.Count - 1)
                    {
                        Level lvlAbove = levels[idx + 1];
                        hasLevelAbove = true;
                        levelAboveElev = lvlAbove.Elevation;
                    }
                }

                double floorTopZ = bb.Max.Z;
                double floorBottomZ = bb.Min.Z;

                // 床上側サンプル Z
                double zTopSample;
                if (hasLevelAbove)
                {
                    zTopSample = 0.5 * (floorTopZ + levelAboveElev);
                }
                else
                {
                    zTopSample = floorTopZ + UnitHelper.MmToFt(300.0);
                }

                // 床下側サンプル Z
                double zBottomSample;
                if (hasLevelBelow)
                {
                    zBottomSample = 0.5 * (floorBottomZ + levelBelowElev);
                }
                else
                {
                    zBottomSample = floorBottomZ - UnitHelper.MmToFt(300.0);
                }

                int n = sampleGrid;
                List<XYZ> samplePointsTop = new List<XYZ>();
                List<XYZ> samplePointsBottom = new List<XYZ>();

                for (int iy = 0; iy < n; iy++)
                {
                    double ty = (n == 1) ? 0.5 : (iy + 0.5) / n;
                    double y = minY + (maxY - minY) * ty;

                    for (int ix = 0; ix < n; ix++)
                    {
                        double tx = (n == 1) ? 0.5 : (ix + 0.5) / n;
                        double x = minX + (maxX - minX) * tx;

                        samplePointsTop.Add(new XYZ(x, y, zTopSample));
                        samplePointsBottom.Add(new XYZ(x, y, zBottomSample));
                    }
                }

                int sampleCount = samplePointsTop.Count;
                if (sampleCount == 0)
                {
                    continue;
                }

                int insideAboveCount = 0;
                int insideBelowCount = 0;

                if (useRooms && rooms != null && rooms.Count > 0)
                {
                    // レベルに応じて上側・下側に見る Room を切り替える
                    var roomsAtLevel = (levelId != 0)
                        ? rooms.Where(r => r.LevelId.IntegerValue == levelId).ToList()
                        : new List<Autodesk.Revit.DB.Architecture.Room>();

                    Level levelBelow = null;
                    if (hasLevelBelow)
                    {
                        int idx = levels.IndexOf(level);
                        if (idx > 0) levelBelow = levels[idx - 1];
                    }

                    Level levelAbove = null;
                    if (hasLevelAbove)
                    {
                        int idx = levels.IndexOf(level);
                        if (idx >= 0 && idx < levels.Count - 1) levelAbove = levels[idx + 1];
                    }

                    var roomsBelowLevel = (levelBelow != null)
                        ? rooms.Where(r => r.LevelId.IntegerValue == levelBelow.Id.IntegerValue).ToList()
                        : new List<Autodesk.Revit.DB.Architecture.Room>();
                    var roomsAboveLevel = (levelAbove != null)
                        ? rooms.Where(r => r.LevelId.IntegerValue == levelAbove.Id.IntegerValue).ToList()
                        : new List<Autodesk.Revit.DB.Architecture.Room>();

                    IList<Autodesk.Revit.DB.Architecture.Room> roomsForTop;
                    IList<Autodesk.Revit.DB.Architecture.Room> roomsForBottom;

                    bool isLowest = !hasLevelBelow;
                    bool isHighest = !hasLevelAbove;

                    if (isLowest)
                    {
                        // 最下階: 床上に現在レベルの部屋、床下は基本的に void
                        roomsForTop = roomsAtLevel;
                        roomsForBottom = roomsBelowLevel;
                    }
                    else if (isHighest)
                    {
                        // 最上階（屋根など）: 床下に現在レベルの部屋、床上は基本的に void
                        roomsForTop = roomsAboveLevel;
                        roomsForBottom = roomsAtLevel;
                    }
                    else
                    {
                        // 中間階: 床上=現在レベル、床下=一つ下のレベルを基本とする
                        roomsForTop = roomsAtLevel;
                        roomsForBottom = roomsBelowLevel;
                    }

                    foreach (XYZ ptTop in samplePointsTop)
                    {
                        try
                        {
                            if (IsPointInInteriorRoomVolume(doc, roomsForTop, ptTop))
                            {
                                insideAboveCount++;
                            }
                        }
                        catch
                        {
                        }
                    }

                    foreach (XYZ ptBottom in samplePointsBottom)
                    {
                        try
                        {
                            if (IsPointInInteriorRoomVolume(doc, roomsForBottom, ptBottom))
                            {
                                insideBelowCount++;
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                double aboveRatio = (double)insideAboveCount / sampleCount;
                double belowRatio = (double)insideBelowCount / sampleCount;

                bool hasRoomAbove = aboveRatio >= 0.5;
                bool hasRoomBelow = belowRatio >= 0.5;

                bool isLowestLevel = !hasLevelBelow;
                bool isHighestLevel = !hasLevelAbove;

                // ケース1: 床上に Room があり、床下には Room が無い → 地盤スラブ等
                bool caseAboveInsideBelowVoid = hasRoomAbove && (belowRatio == 0.0);

                // ケース2: 床下に Room があり、床上には Room が無い → 屋根スラブ等
                bool caseBelowInsideAboveVoid = hasRoomBelow && (aboveRatio == 0.0);

                bool isCandidate = caseAboveInsideBelowVoid || caseBelowInsideAboveVoid;
                // 追加ルール: 床上・床下とも Room がない場合も外部候補とする
                if (!isCandidate && aboveRatio == 0.0 && belowRatio == 0.0)
                {
                    isCandidate = true;
                }
                if (!isCandidate)
                {
                    continue;
                }

                string classification;
                if (caseAboveInsideBelowVoid)
                {
                    classification = "room_above_void_below";
                }
                else if (caseBelowInsideAboveVoid)
                {
                    classification = "room_below_void_above";
                }
                else
                {
                    if (aboveRatio == 0.0 && belowRatio == 0.0)
                    {
                        classification = "void_above_void_below";
                    }
                    else
                    {
                        classification = "other";
                    }
                }

                int eid = floor.Id.IntegerValue;
                int typeId = 0;
                string typeName = "";
                try
                {
                    ElementId tid = floor.GetTypeId();
                    if (tid != null && tid != ElementId.InvalidElementId)
                    {
                        typeId = tid.IntegerValue;
                        ElementType t = doc.GetElement(tid) as ElementType;
                        if (t != null) typeName = t.Name ?? "";
                    }
                }
                catch
                {
                    typeId = 0;
                    typeName = "";
                }

                resultFloors.Add(new
                {
                    elementId = eid,
                    id = eid,
                    uniqueId = floor.UniqueId,
                    levelId,
                    typeId,
                    typeName,
                    roomAboveRatio = Math.Round(aboveRatio, 3),
                    roomBelowRatio = Math.Round(belowRatio, 3),
                    isLowestLevel,
                    isHighestLevel,
                    classification
                });
            }

            return new
            {
                ok = true,
                msg = (string)null,
                totalCount = resultFloors.Count,
                floors = resultFloors,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }

        /// <summary>
        /// 点 p が「室内」とみなせるかどうかを判定します。
        /// XY がいずれかの Room の内部に入っていれば室内とみなします。
        /// 高さ方向は、Room の基準レベル付近で評価し、明示的な上限は設けません。
        /// </summary>
        private static bool IsPointInInteriorRoomVolume(
            Document doc,
            IList<Autodesk.Revit.DB.Architecture.Room> rooms,
            XYZ p,
            double zToleranceFt = 0.1)
        {
            if (rooms == null || rooms.Count == 0) return false;

            foreach (var r in rooms)
            {
                if (r == null) continue;

                try
                {
                    double zTest = p.Z;
                    try
                    {
                        var baseLevel = doc.GetElement(r.LevelId) as Level;
                        if (baseLevel != null)
                        {
                            zTest = baseLevel.Elevation + 0.1;
                        }
                    }
                    catch
                    {
                        zTest = p.Z;
                    }

                    var testPt = new XYZ(p.X, p.Y, zTest);
                    if (r.IsPointInRoom(testPt))
                    {
                        return true;
                    }
                }
                catch
                {
                    // この Room で判定に失敗した場合は次へ
                }
            }

            return false;
        }
    }
}
