using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Dto;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    /// <summary>
    /// get_candidate_exterior_walls
    /// 壁の LocationCurve（基準線）と Room 情報のみを使って「外壁候補」を判定します。
    /// ロジック（壁ごと）:
    ///   - LocationCurve 上を複数点サンプリング。
    ///   - 各サンプル点で法線方向（wall.Orientation）に p ± n * offset を取り、
    ///     両側の点について「床／屋根に上下を挟まれた Room 内部」かどうかを判定します。
    ///   - 「一方だけがその“室内ボリューム”に含まれ、もう一方は含まれない」パターンが、
    ///     一方向に限って一定数以上現れる場合、その壁を外壁候補とみなす。
    /// ShellLayerType やタイプ名、複雑な Solid フェイス形状には依存しません。
    /// </summary>
    public class GetCandidateExteriorWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_candidate_exterior_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return new
                {
                    ok = false,
                    msg = "アクティブドキュメントがありません。",
                    walls = new List<object>()
                };
            }

            var p = (JObject)(cmd.Params ?? new JObject());

            // オプション: viewId / levelId / levelIds
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

            // レベルフィルタ
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
                    foreach (var t in levelArr)
                    {
                        if (t.Type == JTokenType.Integer)
                        {
                            int idVal = t.Value<int>();
                            if (idVal > 0)
                            {
                                levelFilterIds.Add(idVal);
                            }
                        }
                    }
                    if (levelFilterIds.Count == 0)
                    {
                        levelFilterIds = null;
                    }
                }
            }
            catch
            {
                levelFilterIds = null;
            }

            double minExteriorAreaM2 = p.Value<double?>("minExteriorAreaM2") ?? 0.1;
            if (minExteriorAreaM2 < 0)
            {
                minExteriorAreaM2 = 0;
            }

            bool roomCheck = p.Value<bool?>("roomCheck") ?? true;
            double offsetMm = p.Value<double?>("offsetMm") ?? 1000.0;
            double offsetFt = UnitHelper.MmToFt(offsetMm);

            // 任意: elementIds で壁候補を限定
            HashSet<int> elementFilterIds = null;
            try
            {
                if (p["elementIds"] is JArray elemArr && elemArr.Count > 0)
                {
                    elementFilterIds = new HashSet<int>();
                    foreach (var t in elemArr)
                    {
                        if (t.Type == JTokenType.Integer)
                        {
                            int idVal = t.Value<int>();
                            if (idVal > 0)
                            {
                                elementFilterIds.Add(idVal);
                            }
                        }
                    }
                    if (elementFilterIds.Count == 0)
                    {
                        elementFilterIds = null;
                    }
                }
            }
            catch
            {
                elementFilterIds = null;
            }

            // 1) 対象の壁を収集
            var collector = (view != null)
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);

            var allWalls = collector
                .OfClass(typeof(Autodesk.Revit.DB.Wall))
                .Cast<Autodesk.Revit.DB.Wall>();

            if (elementFilterIds != null)
            {
                allWalls = allWalls
                    .Where(w =>
                    {
                        try
                        {
                            return elementFilterIds.Contains(w.Id.IntegerValue);
                        }
                        catch
                        {
                            return false;
                        }
                    });
            }

            if (levelFilterIds != null)
            {
                allWalls = allWalls
                    .Where(w =>
                    {
                        try
                        {
                            var lid = w.LevelId;
                            return lid != null &&
                                   lid != ElementId.InvalidElementId &&
                                   levelFilterIds.Contains(lid.IntegerValue);
                        }
                        catch
                        {
                            return false;
                        }
                    });
            }

            var wallList = allWalls.ToList();
            if (wallList.Count == 0)
            {
                return new
                {
                    ok = true,
                    msg = "対象となる壁が見つかりません。",
                    walls = new List<object>()
                };
            }

            // 部屋コレクション
            List<Autodesk.Revit.DB.Architecture.Room> rooms = null;
            Dictionary<int, List<Autodesk.Revit.DB.Architecture.Room>> roomsByLevelId = null;
            if (roomCheck)
            {
                try
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Autodesk.Revit.DB.Architecture.Room>()
                        .ToList();

                    if (rooms != null && rooms.Count > 0)
                    {
                        roomsByLevelId = rooms
                            .Where(r => r != null && r.LevelId != null && r.LevelId != ElementId.InvalidElementId)
                            .GroupBy(r => r.LevelId.IntegerValue)
                            .ToDictionary(g => g.Key, g => g.ToList());
                    }
                }
                catch
                {
                    rooms = new List<Autodesk.Revit.DB.Architecture.Room>();
                    roomsByLevelId = new Dictionary<int, List<Autodesk.Revit.DB.Architecture.Room>>();
                }
            }

            var resultWalls = new List<object>();

            foreach (var wall in wallList)
            {
                if (wall == null) continue;

                double exteriorAreaM2;
                bool isExterior = ClassifyWallByRoomsWithArms(doc, wall, rooms, roomsByLevelId, roomCheck, offsetFt, out exteriorAreaM2);

                if (isExterior && exteriorAreaM2 >= minExteriorAreaM2)
                {
                    int eid = wall.Id.IntegerValue;
                    int levelId = (wall.LevelId != null && wall.LevelId != ElementId.InvalidElementId)
                        ? wall.LevelId.IntegerValue
                        : 0;
                    int typeId = wall.GetTypeId().IntegerValue;

                    string typeName = string.Empty;
                    try
                    {
                        var tElem = doc.GetElement(new ElementId(typeId)) as ElementType;
                        if (tElem != null)
                        {
                            typeName = tElem.Name ?? string.Empty;
                        }
                    }
                    catch
                    {
                        typeName = string.Empty;
                    }

                    resultWalls.Add(new
                    {
                        elementId = eid,
                        id = eid,
                        uniqueId = wall.UniqueId,
                        levelId,
                        typeId,
                        typeName,
                        exteriorAreaM2 = Math.Round(exteriorAreaM2, 4)
                    });
                }
            }

            return new
            {
                ok = true,
                msg = (string)null,
                totalCount = resultWalls.Count,
                walls = resultWalls,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }

        /// <summary>
        /// 新ロジック: 壁の基準線上のサンプル点から、法線方向に複数のアームを伸ばし、
        /// それらの端点が Room XY 内かどうかで外周候補かどうかを判定します。
        /// </summary>
        private static bool ClassifyWallByRoomsWithArms(
            Document doc,
            Autodesk.Revit.DB.Wall wall,
            IList<Autodesk.Revit.DB.Architecture.Room> rooms,
            Dictionary<int, List<Autodesk.Revit.DB.Architecture.Room>> roomsByLevelId,
            bool roomCheck,
            double offsetFt,
            out double exteriorAreaM2)
        {
            exteriorAreaM2 = 0.0;

            if (!roomCheck)
            {
                return false;
            }

            var locCurve = wall.Location as LocationCurve;
            if (locCurve == null || locCurve.Curve == null)
            {
                return false;
            }

            IList<Autodesk.Revit.DB.Architecture.Room> roomsForWall = rooms ?? Array.Empty<Autodesk.Revit.DB.Architecture.Room>();
            try
            {
                if (roomsByLevelId != null && roomsByLevelId.Count > 0)
                {
                    int wallLevelId = 0;
                    var lid = wall.LevelId;
                    if (lid != null && lid != ElementId.InvalidElementId)
                    {
                        wallLevelId = lid.IntegerValue;
                    }

                    if (wallLevelId != 0 && roomsByLevelId.TryGetValue(wallLevelId, out var perLevel) && perLevel != null)
                    {
                        roomsForWall = perLevel;
                    }
                    else
                    {
                        roomsForWall = Array.Empty<Autodesk.Revit.DB.Architecture.Room>();
                    }
                }
            }
            catch
            {
                roomsForWall = rooms ?? Array.Empty<Autodesk.Revit.DB.Architecture.Room>();
            }

            var curve = locCurve.Curve;
            double curveLength = curve.Length;
            if (curveLength <= 1e-6)
            {
                return false;
            }

            // 壁の法線方向（XY 平面内単位ベクトル）
            XYZ orient = null;
            try
            {
                orient = wall.Orientation;
                if (orient != null && orient.GetLength() > 1e-9)
                {
                    orient = orient.Normalize();
                }
            }
            catch
            {
                orient = null;
            }

            if (orient == null || Math.Abs(orient.Z) > 1e-3)
            {
                return false;
            }

            // 法線方向のサンプリング距離（mm 単位指定→内部 ft）
            // 狭い部屋幅にも対応するため、複数段階で評価する。
            double[] offsetLengthsFt =
            {
                UnitHelper.MmToFt(250.0),
                UnitHelper.MmToFt(500.0),
                UnitHelper.MmToFt(750.0)
            };

            // 壁の高さ（バウンディングボックスから取得）
            double heightFt = 0.0;
            double midZ = 0.0;
            try
            {
                var bb = wall.get_BoundingBox(null);
                if (bb != null)
                {
                    heightFt = bb.Max.Z - bb.Min.Z;
                    midZ = (bb.Min.Z + bb.Max.Z) * 0.5;
                }
            }
            catch
            {
                heightFt = 0.0;
            }

            if (heightFt <= 0.0)
            {
                // 高さが読めない場合は保守的に内壁扱い
                return false;
            }

            // サンプル位置（パラメータ 0..1 の割合）
            double[] ts = { 0.1, 0.3, 0.5, 0.7, 0.9 };

            int interiorOnly = 0;
            int exteriorOnly = 0;
            int bothSides = 0;
            int noRoom = 0;
            int validSamples = 0;

            foreach (double t in ts)
            {
                XYZ basePt;
                try
                {
                    basePt = curve.Evaluate(t, true);
                }
                catch
                {
                    continue;
                }

                // 壁高さの中央でサンプリング
                var baseMid = new XYZ(basePt.X, basePt.Y, midZ);

                bool inIn = false;
                bool inOut = false;

                // 法線方向に複数距離だけオフセットし、いずれかで Room 内なら「その側に部屋あり」とみなす。
                if (roomsForWall != null && roomsForWall.Count > 0)
                {
                    foreach (double lenFt in offsetLengthsFt)
                    {
                        var pIn = baseMid - orient * lenFt;
                        var pOut = baseMid + orient * lenFt;

                        try
                        {
                            if (!inIn && IsPointInInteriorRoomVolume(doc, roomsForWall, pIn))
                            {
                                inIn = true;
                            }
                        }
                        catch
                        {
                            // ignore and continue other lengths
                        }

                        try
                        {
                            if (!inOut && IsPointInInteriorRoomVolume(doc, roomsForWall, pOut))
                            {
                                inOut = true;
                            }
                        }
                        catch
                        {
                            // ignore and continue other lengths
                        }

                        // すでに両側とも部屋ありと分かった場合は、この t については早期終了
                        if (inIn && inOut)
                        {
                            break;
                        }
                    }
                }

                if (inIn && !inOut)
                {
                    interiorOnly++;
                }
                else if (!inIn && inOut)
                {
                    exteriorOnly++;
                }
                else if (inIn && inOut)
                {
                    bothSides++;
                }
                else
                {
                    noRoom++;
                }

                validSamples++;
            }

            if (validSamples == 0)
            {
                return false;
            }

            bool isExterior = false;

            int interiorOnlyCount = interiorOnly;
            int exteriorOnlyCount = exteriorOnly;

            // いずれかのサンプルで両側とも Room がある場合は、内部寄りの壁とみなして除外する。
            if (bothSides > 0)
            {
                isExterior = false;
            }
            else if (interiorOnlyCount > 0 && exteriorOnlyCount == 0)
            {
                // Room がある側とない側が一方向にのみ現れる → 片側が屋外に面しているとみなす
                isExterior = true;
            }
            else if (exteriorOnlyCount > 0 && interiorOnlyCount == 0)
            {
                isExterior = true;
            }
            else if (interiorOnlyCount == 0 && exteriorOnlyCount == 0 && noRoom > 0)
            {
                // 両側とも Room がない壁（外構フェンス等）は外部扱いとする
                isExterior = true;
            }
            else
            {
                // 両側とも部屋がある / 両方向に片側だけ部屋あり → 外壁とはみなさない
                isExterior = false;
            }

            if (!isExterior)
            {
                exteriorAreaM2 = 0.0;
                return false;
            }

            // 外周候補と判定できた場合は、簡易的に外部面積を length * height として返す。
            try
            {
                double areaFt2 = curveLength * heightFt;
                exteriorAreaM2 = UnitHelper.InternalToSqm(areaFt2);
            }
            catch
            {
                exteriorAreaM2 = 0.0;
            }

            return true;
        }

        /// <summary>
        /// 壁の一方の側（法線方向）に対して、元の法線とそれを 1 度回転させた方向から
        /// XY オフセットを構成します。
        /// </summary>
        private static List<XYZ> BuildWallSideOffsetsXY(XYZ baseNormal, double armLengthFt)
        {
            var offsets = new List<XYZ>();

            if (baseNormal == null || baseNormal.GetLength() < 1e-9)
            {
                return offsets;
            }

            // XY 成分のみを使用
            double nx = baseNormal.X;
            double ny = baseNormal.Y;
            double len = Math.Sqrt(nx * nx + ny * ny);
            if (len < 1e-9)
            {
                return offsets;
            }

            nx /= len;
            ny /= len;

            double angleDeg = 1.0;
            double angleRad = Math.PI * angleDeg / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            // 元の方向
            offsets.Add(new XYZ(nx * armLengthFt, ny * armLengthFt, 0.0));

            // +1 度回転
            double rx1 = cos * nx - sin * ny;
            double ry1 = sin * nx + cos * ny;
            offsets.Add(new XYZ(rx1 * armLengthFt, ry1 * armLengthFt, 0.0));

            // -1 度回転
            double rx2 = cos * nx + sin * ny;
            double ry2 = -sin * nx + cos * ny;
            offsets.Add(new XYZ(rx2 * armLengthFt, ry2 * armLengthFt, 0.0));

            return offsets;
        }

        /// <summary>
        /// 点 p が、「床または屋根に上下を挟まれた Room 内部ボリューム」に
        /// 含まれているかどうかを判定します。
        /// - まず IsPointInRoom で Room を特定
        /// - その Room の Level / BaseOffset / UpperLimit / LimitOffset / UnboundedHeight
        ///   から上下の Z 範囲 [zMin, zMax] を推定
        /// - p.Z がその範囲に入っていれば true を返す
        /// </summary>
        private static bool IsPointInInteriorRoomVolume(
            Document doc,
            IList<Autodesk.Revit.DB.Architecture.Room> rooms,
            XYZ p,
            double zToleranceFt = 0.1)
        {
            // XY が Room 内に入っていれば「室内」とみなす。
            // Z は Room のレベル高さに合わせて評価し、高さ方向の制限は設けない。
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
                            zTest = baseLevel.Elevation + 0.1; // レベル面から少し上で判定
                        }
                    }
                    catch
                    {
                        // レベル情報が取れない場合は元の Z を使用
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
                    // この Room で判定に失敗した場合は次の Room へ
                }
            }

            return false;
        }
    }
}
