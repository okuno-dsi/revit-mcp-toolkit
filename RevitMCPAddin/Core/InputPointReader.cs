// ================================================================
// File: Core/InputPointReader.cs
// Purpose: JSONパラメの座標・角度・オフセットを頑健に読み取り（mm/deg）
// Notes : x,y,z / location.{x,y,z} / point.* / pt.* / center.* / position.* /
//         start.* / end.* / from.* / to.* / offset{Mm}/{x,y,z} / [x,y(,z)] 配列 まで対応
//         角度は angle / rotation / degrees / radians なども許容
// ================================================================
#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    public static class InputPointReader
    {
        // 優先探索するベースパス候補（順番が優先度）
        private static readonly string[] DefaultBases = new[]
        {
            "", "location", "point", "pt", "center", "position",
            "start", "end", "from", "to", "head", "tail", "origin"
        };

        /// <summary>
        /// 任意のJObjectから mm の XYZ を読み取る（Z省略可）。true=成功。
        /// 例: {x:1000,y:2000}, {location:{x:..,y:..,z:..}}, {point:[1000,2000,0]}
        /// </summary>
        public static bool TryReadPointMm(JObject p, out XYZ xyzMm, params string[] basePaths)
        {
            xyzMm = XYZ.Zero;

            var bases = (basePaths != null && basePaths.Length > 0) ? basePaths : DefaultBases;

            // 1) 配列 [x,y(,z)]
            foreach (var b in bases)
            {
                var tok = SelectTokenSmart(p, PathJoin(b, "point")) ?? SelectTokenSmart(p, PathJoin(b, ""));
                if (tok != null && tok.Type == JTokenType.Array)
                {
                    var arr = (JArray)tok;
                    if (arr.Count >= 2 &&
                        TryToDouble(arr[0], out var x) &&
                        TryToDouble(arr[1], out var y))
                    {
                        var z = (arr.Count >= 3 && TryToDouble(arr[2], out var z0)) ? z0 : 0.0;
                        xyzMm = new XYZ(x, y, z);
                        return true;
                    }
                }
            }

            // 2) オブジェクト {x:.., y:.., z:?}
            foreach (var b in bases)
            {
                if (TryReadDoubleMm(p, PathJoin(b, "x"), out var x) &&
                    TryReadDoubleMm(p, PathJoin(b, "y"), out var y))
                {
                    double z = 0;
                    TryReadDoubleMm(p, PathJoin(b, "z"), out z);
                    xyzMm = new XYZ(x, y, z);
                    return true;
                }
            }

            // 3) ルート簡易 {x,y,z}
            if (TryReadDoubleMm(p, "x", out var rx) && TryReadDoubleMm(p, "y", out var ry))
            {
                double rz = 0; TryReadDoubleMm(p, "z", out rz);
                xyzMm = new XYZ(rx, ry, rz);
                return true;
            }

            // 4) offsetMm オブジェクト
            var off = p["offsetMm"] as JObject;
            if (off != null &&
                TryReadDoubleMm(off, "x", out var ox) &&
                TryReadDoubleMm(off, "y", out var oy))
            {
                double oz = 0; TryReadDoubleMm(off, "z", out oz);
                xyzMm = new XYZ(ox, oy, oz);
                return true;
            }

            return false;
        }

        /// <summary>
        /// XY(mm)だけ読めればよいケース（UVなど）。true=成功。
        /// </summary>
        public static bool TryReadXYMm(JObject p, out double xMm, out double yMm, params string[] basePaths)
        {
            xMm = 0; yMm = 0;
            if (TryReadPointMm(p, out var mm, basePaths))
            {
                xMm = mm.X; yMm = mm.Y;
                return true;
            }
            return false;
        }

        /// <summary>
        /// オフセット {dx,dy,dz} or {offset:{x,y,z}} or {offsetMm:{x,y,z}} を mm で読む。
        /// true=成功。どれもなければ false。
        /// </summary>
        public static bool TryReadOffsetMm(JObject p, out XYZ deltaMm)
        {
            deltaMm = XYZ.Zero;

            // offsetMm優先
            if (TryReadPointMm(p, out var offMm, "offsetMm")) { deltaMm = offMm; return true; }

            // offset{ x,y,z } は mm とみなす
            if (TryReadPointMm(p, out var off, "offset")) { deltaMm = off; return true; }

            // dx/dy/dz（数値/文字列）
            bool okx = TryReadDoubleMm(p, "dx", out var dx);
            bool oky = TryReadDoubleMm(p, "dy", out var dy);
            bool okz = TryReadDoubleMm(p, "dz", out var dz);
            if (okx || oky || okz) { deltaMm = new XYZ(dx, dy, dz); return true; }

            return false;
        }

        /// <summary>
        /// 角度を deg で読み取る（angle/rotation/degrees/radians/angleDeg/angleRad 等）。
        /// radians 入力なら自動で deg に変換。
        /// </summary>
        public static bool TryReadAngleDeg(JObject p, out double angleDeg)
        {
            angleDeg = 0;

            // 代表名
            string[] degKeys = { "angle", "angleDeg", "degrees", "rotation", "rotationDeg" };
            string[] radKeys = { "angleRad", "radians", "rotationRad" };

            foreach (var k in degKeys)
                if (TryReadDoubleMm(p, k, out var d)) { angleDeg = d; return true; }

            foreach (var k in radKeys)
                if (TryReadDoubleMm(p, k, out var r)) { angleDeg = 180.0 * r / Math.PI; return true; }

            return false;
        }

        // -------------------- 内部小物 --------------------

        private static bool TryReadDoubleMm(JObject p, string path, out double mm)
        {
            mm = 0;
            var tok = SelectTokenSmart(p, path);
            if (tok == null) return false;
            return TryToDouble(tok, out mm);
        }

        private static JToken SelectTokenSmart(JObject p, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return p;
            try { return p.SelectToken(path); } catch { return null; }
        }

        private static string PathJoin(string baseName, string name)
        {
            if (string.IsNullOrEmpty(baseName)) return name;
            if (string.IsNullOrEmpty(name)) return baseName;
            return $"{baseName}.{name}";
        }

        private static bool TryToDouble(JToken tok, out double v)
        {
            v = 0;
            if (tok == null) return false;

            if (tok.Type == JTokenType.Float || tok.Type == JTokenType.Integer)
            { v = tok.Value<double>(); return true; }

            if (tok.Type == JTokenType.String &&
                double.TryParse(tok.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            { v = d; return true; }

            return false;
        }
    }
}
