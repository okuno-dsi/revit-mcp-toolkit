#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Revit Geometry → JSON 変換（C# 8対応）
    /// 単位変換は UnitHelper に統一（mm）。
    /// 常に {"x","y","z"} で座標を返します。
    /// </summary>
    public static class GeometryJsonHelper
    {
        public static JObject CurveToJson(Curve cv)
        {
            if (cv == null) return new JObject { ["kind"] = "null" };

            // Line
            var ln = cv as Line;
            if (ln != null)
            {
                var o = new JObject
                {
                    ["kind"] = "line",
                    ["start"] = PointToJson(ln.GetEndPoint(0)),
                    ["end"] = PointToJson(ln.GetEndPoint(1))
                };
                return o;
            }

            // Arc
            var ac = cv as Arc;
            if (ac != null)
            {
                var o = new JObject
                {
                    ["kind"] = "arc",
                    ["start"] = PointToJson(ac.GetEndPoint(0)),
                    ["end"] = PointToJson(ac.GetEndPoint(1)),
                    ["center"] = PointToJson(ac.Center),
                    ["radius"] = UnitHelper.InternalToMm(ac.Radius), // ft→mm
                    ["normal"] = VectorToJson(ac.Normal),
                    ["isCcw"] = ac.IsCyclic
                };
                return o;
            }

            // Nurbs / Hermite などはテッセレーション（折線化）
            try
            {
                var pts = cv.Tessellate();
                if (pts != null && pts.Count >= 2)
                {
                    var arr = new JArray();
                    foreach (var p in pts)
                        arr.Add(PointToJson(p)); // x/y/z

                    var o = new JObject
                    {
                        ["kind"] = "polyline",
                        ["points"] = arr
                    };
                    return o;
                }
            }
            catch { /* ignore */ }

            // フォールバック: 型名のみ
            return new JObject { ["kind"] = cv.GetType().Name };
        }

        // ----------------- JSON helpers (x/y/z) -----------------

        /// <summary>XYZ 座標（内部=ft）→ {"x","y","z"}（mm）</summary>
        public static JObject PointToJson(XYZ p)
        {
            var mm = UnitHelper.XyzToMm(p); // (x,y,z) in mm
            return new JObject
            {
                ["x"] = mm.x,
                ["y"] = mm.y,
                ["z"] = mm.z
            };
        }

        /// <summary>XYZ ベクトル（内部=ft相当）→ {"x","y","z"}（mm）</summary>
        public static JObject VectorToJson(XYZ v)
        {
            // ベクトルでも一貫して mm スケールに正規化（既存仕様踏襲）
            var mm = UnitHelper.XyzToMm(v);
            return new JObject
            {
                ["x"] = mm.x,
                ["y"] = mm.y,
                ["z"] = mm.z
            };
        }
    }
}
