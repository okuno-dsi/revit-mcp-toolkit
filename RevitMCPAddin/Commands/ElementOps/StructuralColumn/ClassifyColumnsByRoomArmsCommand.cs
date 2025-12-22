using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.StructuralColumn
{
    /// <summary>
    /// classify_columns_by_room_arms
    /// 柱について、「床または屋根に上下を挟まれた部屋の内部」に基づき、
    /// 四方向の短いアーム先端がすべて室内ボリュームに含まれているかどうかで
    /// 内部柱 / 外部柱を分類します。
    /// </summary>
    public class ClassifyColumnsByRoomArmsCommand : IRevitCommandHandler
    {
        public string CommandName => "classify_columns_by_room_arms";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return new
                {
                    ok = false,
                    msg = "アクティブドキュメントがありません。",
                    columns = new List<object>()
                };
            }

            var p = (JObject)(cmd.Params ?? new JObject());

            // elementIds が指定されている場合はそれを対象に、なければ全柱を対象
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

            double armLengthMm = p.Value<double?>("armLengthMm") ?? 300.0;
            if (armLengthMm <= 0) armLengthMm = 300.0;
            double baseArmLengthFt = UnitHelper.MmToFt(armLengthMm);

            int sampleCount = p.Value<int?>("sampleCount") ?? 3;
            if (sampleCount < 1) sampleCount = 1;
            if (sampleCount > 7) sampleCount = 7;

            // Room コレクション
            List<Autodesk.Revit.DB.Architecture.Room> rooms = null;
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

            var result = new List<object>();

            // 構造柱 + 建築柱を対象
            var categories = new[]
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Columns
            };

            foreach (var bic in categories)
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType();

                foreach (var e in collector)
                {
                    if (e == null) continue;

                    int eid = e.Id.IntValue();
                    if (elementFilterIds != null && !elementFilterIds.Contains(eid))
                    {
                        continue;
                    }

                    // 柱の代表位置とバウンディングボックス
                    XYZ pt = null;
                    try
                    {
                        var lp = e.Location as LocationPoint;
                        if (lp != null) pt = lp.Point;
                    }
                    catch
                    {
                        pt = null;
                    }

                    BoundingBoxXYZ bbCol = null;
                    try
                    {
                        bbCol = e.get_BoundingBox(null);
                    }
                    catch
                    {
                        bbCol = null;
                    }

                    if (pt == null && bbCol != null)
                    {
                        pt = (bbCol.Min + bbCol.Max) * 0.5;
                    }

                    if (pt == null) continue;

                    double zMin = pt.Z;
                    double zMax = pt.Z;
                    double armLengthFt = baseArmLengthFt;
                    if (bbCol != null)
                    {
                        zMin = bbCol.Min.Z;
                        zMax = bbCol.Max.Z;

                        // 柱幅以上のアーム長になるように調整（XY の最大寸法の 1.2 倍を下限とする）
                        try
                        {
                            double dx = bbCol.Max.X - bbCol.Min.X;
                            double dy = bbCol.Max.Y - bbCol.Min.Y;
                            double halfMax = 0.5 * Math.Max(Math.Abs(dx), Math.Abs(dy));
                            double minArm = halfMax * 1.2;
                            if (minArm > armLengthFt)
                            {
                                armLengthFt = minArm;
                            }
                        }
                        catch
                        {
                            // 失敗時は baseArmLengthFt のまま
                            armLengthFt = baseArmLengthFt;
                        }
                    }

                    // 高さ方向のサンプル Z を決定
                    var sampleZs = new List<double>();
                    if (zMax - zMin < 1e-6)
                    {
                        sampleZs.Add(zMin);
                    }
                    else
                    {
                        for (int i = 0; i < sampleCount; i++)
                        {
                            double t = (sampleCount == 1)
                                ? 0.5
                                : (i + 0.5) / sampleCount;
                            sampleZs.Add(zMin + (zMax - zMin) * t);
                        }
                    }

                    int totalEndpoints = 0;
                    int insideCount = 0;
                    int outsideCount = 0;

                    // 各高さで複数方向にアームを伸ばし、その端点を評価
                    // （±X / ±Y に加え、1 度回転させた方向も含めて境界誤差を吸収）
                    var armOffsets = BuildArmOffsetsXY(armLengthFt);
                    foreach (double z in sampleZs)
                    {
                        var basePt = new XYZ(pt.X, pt.Y, z);

                        foreach (var offset in armOffsets)
                        {
                            var pEnd = new XYZ(basePt.X + offset.X, basePt.Y + offset.Y, basePt.Z);
                            totalEndpoints++;
                            bool inside = false;
                            try
                            {
                                inside = IsPointInInteriorRoomVolume(doc, rooms, pEnd);
                            }
                            catch
                            {
                                inside = false;
                            }

                            if (inside)
                            {
                                insideCount++;
                            }
                            else
                            {
                                outsideCount++;
                            }
                        }
                    }

                    string classification;
                    if (totalEndpoints == 0)
                    {
                        classification = "unknown";
                    }
                    else
                    {
                        double ratio = (double)insideCount / totalEndpoints;
                        classification = (ratio >= 0.9) ? "interior" : "exterior";
                    }

                    int levelId = 0;
                    try
                    {
                        var lid = e.LevelId;
                        if (lid != null && lid != ElementId.InvalidElementId)
                            levelId = lid.IntValue();
                    }
                    catch
                    {
                        levelId = 0;
                    }

                    int typeId = 0;
                    string typeName = string.Empty;
                    try
                    {
                        var tid = e.GetTypeId();
                        if (tid != null && tid != ElementId.InvalidElementId)
                        {
                            typeId = tid.IntValue();
                            var tElem = doc.GetElement(tid) as ElementType;
                            if (tElem != null) typeName = tElem.Name ?? string.Empty;
                        }
                    }
                    catch
                    {
                        typeId = 0;
                        typeName = string.Empty;
                    }

                    result.Add(new
                    {
                        elementId = eid,
                        uniqueId = e.UniqueId,
                        levelId,
                        category = bic.ToString(),
                        typeId,
                        typeName,
                        armLengthMm = armLengthMm,
                        sampleCount,
                        totalEndpoints,
                        insideCount,
                        outsideCount,
                        classification
                    });
                }
            }

            return new
            {
                ok = true,
                msg = (string)null,
                totalCount = result.Count,
                columns = result,
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
            // XY が Room 内かどうかのみで室内判定を行う。
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

        /// <summary>
        /// XY 平面上のアーム方向ベクトル（±X, ±Y とそれぞれを 1 度回転させたもの）を、
        /// 指定された長さのオフセットとして返します。
        /// </summary>
        private static List<XYZ> BuildArmOffsetsXY(double armLengthFt)
        {
            var offsets = new List<XYZ>();

            // 基本方向（単位長）
            var baseDirs = new[]
            {
                new XYZ( 1.0,  0.0, 0.0),
                new XYZ(-1.0,  0.0, 0.0),
                new XYZ( 0.0,  1.0, 0.0),
                new XYZ( 0.0, -1.0, 0.0)
            };

            double angleDeg = 1.0;
            double angleRad = Math.PI * angleDeg / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            foreach (var d in baseDirs)
            {
                // 元の方向
                offsets.Add(new XYZ(d.X * armLengthFt, d.Y * armLengthFt, 0.0));

                // +1 度回転
                double rx1 = cos * d.X - sin * d.Y;
                double ry1 = sin * d.X + cos * d.Y;
                offsets.Add(new XYZ(rx1 * armLengthFt, ry1 * armLengthFt, 0.0));

                // -1 度回転
                double rx2 = cos * d.X + sin * d.Y;
                double ry2 = -sin * d.X + cos * d.Y;
                offsets.Add(new XYZ(rx2 * armLengthFt, ry2 * armLengthFt, 0.0));
            }

            return offsets;
        }
    }
}

