#nullable enable
using System;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture; // Room
using Autodesk.Revit.DB.Mechanical;   // Space
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Spatial
{
    /// <summary>点群の室内外判定（Room/Space対応）</summary>
    public class ClassifyPointsInRoomHandler : IRevitCommandHandler
    {
        public string CommandName => "classify_points_in_room";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd.Params as JObject ?? new JObject();
            var roomId = ReadElemId(p["roomId"]);
            var pts = p["points"] as JArray; // [[x_mm,y_mm,z_mm], ...]
            bool projectZToLevel = p.Value<bool?>("projectZToLevel") ?? true; // デフォルト: Zをレベル標高に投影して判定

            if (roomId == null || pts == null || pts.Count == 0)
                return new { ok = false, msg = "roomId と points(mm) を指定してください。" };

            var se = doc.GetElement(roomId) as SpatialElement;
            if (se == null) return new { ok = false, msg = "指定IDは Room/Space ではありません。" };

            // レベル標高の取得（Room/Space 共通で LevelId を見る）
            double? levelElevation = null;
            try
            {
                var levelId = se.LevelId;
                if (levelId != null && levelId != ElementId.InvalidElementId)
                {
                    var lvl = doc.GetElement(levelId) as Level;
                    levelElevation = lvl?.Elevation;
                }
            }
            catch { /* ignore */ }

            bool IsInside(XYZ pft)
            {
                XYZ targetPt = pft;

                if (projectZToLevel && levelElevation.HasValue)
                {
                    // XYはそのまま、Zだけレベル標高に投影
                    targetPt = new XYZ(pft.X, pft.Y, levelElevation.Value);
                }

                if (se is Autodesk.Revit.DB.Architecture.Room r) return r.IsPointInRoom(targetPt);
                if (se is Autodesk.Revit.DB.Mechanical.Space s) return s.IsPointInSpace(targetPt);
                return false; // その他SpatialElementは未対応
            }

            var inside = new JArray();
            var outside = new JArray();

            foreach (var j in pts)
            {
                var a = (JArray)j;
                var pft = new XYZ(
                    UnitHelper.MmToFt(a[0].Value<double>()),
                    UnitHelper.MmToFt(a[1].Value<double>()),
                    UnitHelper.MmToFt(a[2].Value<double>()));
                (IsInside(pft) ? inside : outside).Add(j);
            }

            // すべて外側だった場合は、要素ベースの詳細解析コマンドを案内するメッセージを付与する
            var messages = new JArray();
            if (inside.Count == 0)
            {
                messages.Add("指定された全ての点は Room/Space の外側と判定されました。柱や壁など要素として室内を通過しているかを調べる場合は、get_spatial_context_for_element / get_spatial_context_for_elements などの要素ベースのコマンドと併用してください。");
            }

            return new { ok = true, inside, outside, messages };
        }

        private static ElementId? ReadElemId(JToken? t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Integer) return Autodesk.Revit.DB.ElementIdCompat.From(t.Value<int>());
            if (t.Type == JTokenType.String && int.TryParse((string)t, out var i)) return Autodesk.Revit.DB.ElementIdCompat.From(i);
            return null;
        }
    }
}

