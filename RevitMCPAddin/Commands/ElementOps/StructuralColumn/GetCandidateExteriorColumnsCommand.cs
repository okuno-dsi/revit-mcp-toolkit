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
    /// get_candidate_exterior_columns
    /// 部屋(Room) 情報と柱まわりのアームサンプリングのみを用いて、
    /// 「室内」に属さない柱を「外周柱候補」として抽出します。
    /// - XY 平面で Room.IsPointInRoom を用い、Z 方向はレベル高さ付近で評価
    /// - 柱の高さ方向に複数点、その各点から ±X / ±Y を ±1 度回転させた方向にアームを伸ばし、
    ///   端点が Room 内に入っている割合 (insideCount / totalEndpoints) が 0.9 未満のものを exterior とみなす
    /// 旧ロジックで使用していた外壁芯線との距離や ShellLayerType には依存しません。
    /// </summary>
    public class GetCandidateExteriorColumnsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_candidate_exterior_columns";

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

            // オプション: viewId / levelId / levelIds / includeArchitecturalColumns / includeStructuralColumns
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
                    foreach (var t in levelArr.OfType<JValue>())
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

            bool includeArchitectural = p.Value<bool?>("includeArchitecturalColumns") ?? true;
            bool includeStructural = p.Value<bool?>("includeStructuralColumns") ?? true;

            // 任意: elementIds で柱候補を限定（テストやピンポイント判定用）
            HashSet<int> elementFilterIds = null;
            try
            {
                if (p["elementIds"] is JArray elemArr && elemArr.Count > 0)
                {
                    elementFilterIds = new HashSet<int>();
                    foreach (var t in elemArr.OfType<JValue>())
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

            var resultColumns = new List<object>();

            if (includeStructural)
            {
                CollectCandidateColumnsForCategory(
                    doc,
                    view,
                    BuiltInCategory.OST_StructuralColumns,
                    "OST_StructuralColumns",
                    levelFilterIds,
                    elementFilterIds,
                    rooms,
                    resultColumns);
            }

            if (includeArchitectural)
            {
                CollectCandidateColumnsForCategory(
                    doc,
                    view,
                    BuiltInCategory.OST_Columns,
                    "OST_Columns",
                    levelFilterIds,
                    elementFilterIds,
                    rooms,
                    resultColumns);
            }

            return new
            {
                ok = true,
                msg = (string)null,
                totalCount = resultColumns.Count,
                columns = resultColumns,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }

        private static void CollectCandidateColumnsForCategory(
            Document doc,
            View view,
            BuiltInCategory bic,
            string categoryLabel,
            HashSet<int> levelFilterIds,
            HashSet<int> elementFilterIds,
            IList<Autodesk.Revit.DB.Architecture.Room> rooms,
            List<object> output)
        {
            var collector = (view != null)
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);

            var cols = collector
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in cols)
            {
                if (e == null) continue;

                int eid = e.Id.IntegerValue;
                if (elementFilterIds != null && !elementFilterIds.Contains(eid))
                {
                    continue;
                }

                int levelId = 0;
                try
                {
                    var lid = e.LevelId;
                    if (lid != null && lid != ElementId.InvalidElementId)
                        levelId = lid.IntegerValue;
                }
                catch
                {
                    levelId = 0;
                }

                if (levelFilterIds != null && levelId != 0 && !levelFilterIds.Contains(levelId))
                {
                    continue;
                }

                // 柱の代表点とバウンディングボックス
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
                    bbCol = e.get_BoundingBox(view);
                    if (bbCol == null) bbCol = e.get_BoundingBox(null);
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

                // アーム方式で室内/外を判定
                double ratio = 0.0;
                bool isInterior = false;
                if (rooms != null && rooms.Count > 0)
                {
                    int totalEndpoints;
                    int insideCount;
                    EvaluateColumnArms(doc, rooms, pt, bbCol, out totalEndpoints, out insideCount);

                    if (totalEndpoints > 0)
                    {
                        ratio = (double)insideCount / totalEndpoints;
                        isInterior = ratio >= 0.9;
                    }
                }

                if (isInterior)
                {
                    // 室内寄りの柱は外周候補から除外
                    continue;
                }

                int typeId = 0;
                string typeName = "";
                try
                {
                    var tid = e.GetTypeId();
                    if (tid != null && tid != ElementId.InvalidElementId)
                    {
                        typeId = tid.IntegerValue;
                        var t = doc.GetElement(tid) as ElementType;
                        if (t != null) typeName = t.Name ?? "";
                    }
                }
                catch
                {
                    typeId = 0;
                    typeName = "";
                }

                output.Add(new
                {
                    elementId = eid,
                    id = eid,
                    uniqueId = e.UniqueId,
                    levelId,
                    category = categoryLabel,
                    typeId,
                    typeName,
                    interiorRatio = ratio
                });
            }
        }

        /// <summary>
        /// 柱の高さ方向・周囲のアーム端点について、室内に入っている数を評価します。
        /// insideCount / totalEndpoints >= 0.9 を interior とみなします（判定ロジック自体は呼び出し側）。
        /// </summary>
        private static void EvaluateColumnArms(
            Document doc,
            IList<Autodesk.Revit.DB.Architecture.Room> rooms,
            XYZ pt,
            BoundingBoxXYZ bbCol,
            out int totalEndpoints,
            out int insideCount)
        {
            totalEndpoints = 0;
            insideCount = 0;

            if (rooms == null || rooms.Count == 0 || pt == null) return;

            double zMin, zMax;
            if (bbCol != null)
            {
                zMin = bbCol.Min.Z;
                zMax = bbCol.Max.Z;
            }
            else
            {
                zMin = zMax = pt.Z;
            }

            var sampleZs = new List<double>();
            if (Math.Abs(zMax - zMin) < 1e-6)
            {
                sampleZs.Add(zMin);
            }
            else
            {
                sampleZs.Add(zMin + (zMax - zMin) * 0.25);
                sampleZs.Add(zMin + (zMax - zMin) * 0.50);
                sampleZs.Add(zMin + (zMax - zMin) * 0.75);
            }

            double baseArmLengthFt = UnitHelper.MmToFt(300.0);
            double armLengthFt = baseArmLengthFt;
            if (bbCol != null)
            {
                try
                {
                    double dx = bbCol.Max.X - bbCol.Min.X;
                    double dy = bbCol.Max.Y - bbCol.Min.Y;
                    double halfMax = 0.5 * Math.Max(Math.Abs(dx), Math.Abs(dy));
                    double minArm = halfMax * 1.2;
                    if (minArm > armLengthFt) armLengthFt = minArm;
                }
                catch
                {
                    armLengthFt = baseArmLengthFt;
                }
            }

            var offsets = BuildArmOffsetsXY(armLengthFt);

            foreach (double z in sampleZs)
            {
                var basePt = new XYZ(pt.X, pt.Y, z);

                foreach (var off in offsets)
                {
                    var pEnd = new XYZ(basePt.X + off.X, basePt.Y + off.Y, basePt.Z);
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

                    if (inside) insideCount++;
                }
            }
        }

        /// <summary>
        /// XY 平面で ±X / ±Y とその ±1 度回転方向のアームオフセットを構成します。
        /// </summary>
        private static List<XYZ> BuildArmOffsetsXY(double armLengthFt)
        {
            var offsets = new List<XYZ>();

            var baseDirs = new[]
            {
                new XYZ( 1.0,  0.0, 0.0),
                new XYZ(-1.0,  0.0, 0.0),
                new XYZ( 0.0,  1.0, 0.0),
                new XYZ( 0.0, -1.0, 0.0)
            };

            double angleRad = Math.PI * 1.0 / 180.0;
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

