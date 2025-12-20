// RevitMCPAddin/Core/ParamSanitizer.cs
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// “ゆるい”入力(JSON-RPC params)を各コマンドが期待する形へ正規化。
    /// - 大文字小文字/別表記のキー統一
    /// - 単数⇔配列の昇格
    /// - 数値/真偽の文字列→型変換
    /// - "3000mm" / "45deg" の単位付き文字列→数値(mm/deg)
    /// - "x,y,z" / [x,y(,z)] を {x,y,z} へ
    /// 変更点は issues に記録（string の簡易ログ）
    /// </summary>
    public static class ParamSanitizer
    {
        public static Tuple<JObject, List<string>> Normalize(string method, JObject src)
        {
            var p = (JObject)(src != null ? src.DeepClone() : new JObject());
            var issues = new List<string>();

            // 1) キーの正規化（代表例）
            var aliasMap = new[]
            {
                new { aliases = new []{ "ViewID","ViewId","viewID","view_id" }, canon = "viewId" },
                new { aliases = new []{ "ElementID","ElementId","elementID","element_id" }, canon = "elementId" },
                new { aliases = new []{ "ElementIDs","ElementIds","elementIDs","element_ids" }, canon = "elementIds" },
                new { aliases = new []{ "TypeID","TypeId","typeID","type_id","newTypeId" }, canon = "typeId" },

                // Level は levelId/levelName を“だけ”正規化
                new { aliases = new []{ "LevelID","LevelId","levelID","level_id" }, canon = "levelId" },
                new { aliases = new []{ "LevelName","level_name" }, canon = "levelName" },

                // BaseLevel は baseLevelId/baseLevelName を“維持”して正規化
                new { aliases = new []{ "BaseLevelID","BaseLevelId","base_level_id" }, canon = "baseLevelId" },
                new { aliases = new []{ "BaseLevelName","base_level_name" }, canon = "baseLevelName" },

                new { aliases = new []{ "TopLevelID","TopLevelId","topLevelID","top_level_id" }, canon = "topLevelId" },
                new { aliases = new []{ "TopLevelName","top_level_name" }, canon = "topLevelName" },

                new { aliases = new []{ "UniqueID","UniqueId","uniqueID","unique_id" }, canon = "uniqueId" },
                new { aliases = new []{ "HostID","HostId","hostID","host_id","hostWallId" }, canon = "hostWallId" },
                new { aliases = new []{ "Parameters","parameters","Params" }, canon = "params" },
                new { aliases = new []{ "angle","Angle","AngleDeg","angleDeg" }, canon = "angleDeg" },
                new { aliases = new []{ "baselinePts","baseLine","line","points2" }, canon = "baseline" }
            };

            foreach (var m in aliasMap)
            {
                foreach (var a in m.aliases)
                {
                    JToken v;
                    if (p.TryGetValue(a, StringComparison.OrdinalIgnoreCase, out v))
                    {
                        if (!p.ContainsKey(m.canon))
                        {
                            p[m.canon] = v;
                            issues.Add("alias:" + a + "→" + m.canon);
                        }
                        p.Remove(a);
                    }
                }
            }

            // 2) スカラーの強制変換（string→bool/int/double）
            CoerceScalars(p, issues);

            // 3) 単位付き文字列（"3000mm","3m","45deg","90°"）→数値(mm/deg)
            ConvertWithUnits(p, issues);

            // 4) 座標キーのゆるフォーマット補正
            FixPointObjects(p, "start", issues, null);
            FixPointObjects(p, "end", issues, null);
            FixPointObjects(p, "origin", issues, null);
            FixPointObjects(p, "center", issues, null);
            FixPointObjects(p, "location", issues, null);

            // baseline: [{x,y,z}, ...] / "x1,y1,z1; x2,y2,z2" も許可
            JToken baselineTok;
            if (p.TryGetValue("baseline", out baselineTok))
            {
                var fixedBaseline = TryFixPointArray(baselineTok, issues, "baseline");
                if (fixedBaseline != null) p["baseline"] = fixedBaseline;
            }

            // 5) 単数→配列の昇格
            ElevateToArray(p, "elementId", "elementIds", issues);
            ElevateToArray(p, "categoryId", "categoryIds", issues);
            ElevateToArray(p, "typeId", "typeIds", issues);

            // 6) メソッド別の軽い補助
            if (string.Equals(method, "create_grids", StringComparison.OrdinalIgnoreCase))
            {
                JToken segs;
                if (p.TryGetValue("segments", out segs) && segs is JArray)
                {
                    var arr = (JArray)segs;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var seg = arr[i] as JObject;
                        if (seg == null) continue;
                        FixPointObjects(seg, "start", issues, "segments[" + i + "].start");
                        FixPointObjects(seg, "end", issues, "segments[" + i + "].end");
                    }
                }
            }
            else if (string.Equals(method, "create_curtain_wall", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(method, "update_curtain_wall_geometry", StringComparison.OrdinalIgnoreCase))
            {
                JToken bl;
                if (p.TryGetValue("baseline", out bl) && bl is JArray && ((JArray)bl).Count < 2)
                    issues.Add("baseline: requires >= 2 points");
            }
            else if (string.Equals(method, "create_wall", StringComparison.OrdinalIgnoreCase))
            {
                // 壁は start/end を期待
                if (!p.ContainsKey("start") || !p.ContainsKey("end"))
                    issues.Add("create_wall: requires start and end points");
            }


            return Tuple.Create(p, issues);
        }

        // ----------------- helpers -----------------

        private static void ElevateToArray(JObject p, string singleKey, string arrayKey, List<string> issues)
        {
            if (p.ContainsKey(arrayKey)) return;
            JToken v;
            if (p.TryGetValue(singleKey, out v) && v != null && v.Type != JTokenType.Null)
            {
                p[arrayKey] = new JArray(v);
                issues.Add(singleKey + "→" + arrayKey + " (auto-wrap)");
            }
        }

        private static void CoerceScalars(JObject p, List<string> issues)
        {
            foreach (var prop in p.Properties().ToList())
            {
                var val = prop.Value;
                if (val is JObject) { CoerceScalars((JObject)val, issues); continue; }
                if (val is JArray)
                {
                    var a = (JArray)val;
                    for (int i = 0; i < a.Count; i++)
                    {
                        var it = a[i];
                        if (it is JObject) CoerceScalars((JObject)it, issues);
                    }
                    continue;
                }
                if (val.Type != JTokenType.String) continue;

                var s = val.Value<string>();
                if (string.IsNullOrEmpty(s)) continue;

                bool b;
                int iv;
                double dv;
                if (bool.TryParse(s, out b))
                {
                    prop.Value = new JValue(b);
                    issues.Add("coerce:boolean:" + prop.Name);
                    continue;
                }
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                {
                    prop.Value = new JValue(iv);
                    issues.Add("coerce:int:" + prop.Name);
                    continue;
                }
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                {
                    prop.Value = new JValue(dv);
                    issues.Add("coerce:double:" + prop.Name);
                    continue;
                }
            }
        }

        private static void ConvertWithUnits(JObject p, List<string> issues)
        {
            foreach (var prop in p.Properties().ToList())
            {
                var val = prop.Value;
                if (val is JObject) { ConvertWithUnits((JObject)val, issues); continue; }
                if (val is JArray)
                {
                    var a = (JArray)val;
                    for (int i = 0; i < a.Count; i++)
                    {
                        var it = a[i] as JObject;
                        if (it != null) ConvertWithUnits(it, issues);
                    }
                    continue;
                }
                if (val.Type != JTokenType.String) continue;

                var s = val.Value<string>().Trim();
                double num;
                if (TryParseWithUnit(s, out num))
                {
                    prop.Value = new JValue(num);
                    issues.Add("unit:" + s + "→" + num.ToString(CultureInfo.InvariantCulture) + " (" + prop.Name + ")");
                }
            }
        }

        // .NET 4.8 向け：range 演算子は使わない
        private static bool TryParseWithUnit(string s, out double value)
        {
            value = 0.0;
            if (string.IsNullOrEmpty(s)) return false;

            var lower = s.ToLowerInvariant();

            if (EndsWith(lower, "mm"))
            {
                double v;
                if (double.TryParse(SubNoRange(lower, 0, lower.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                { value = v; return true; }
            }
            if (lower.EndsWith("m") && !lower.EndsWith("mm"))
            {
                double v;
                if (double.TryParse(SubNoRange(lower, 0, lower.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                { value = v * 1000.0; return true; }
            }
            if (EndsWith(lower, "deg"))
            {
                double v;
                if (double.TryParse(SubNoRange(lower, 0, lower.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                { value = v; return true; }
            }
            if (EndsWith(lower, "°"))
            {
                double v;
                if (double.TryParse(SubNoRange(lower, 0, lower.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                { value = v; return true; }
            }
            return false;
        }

        private static bool EndsWith(string s, string suffix)
        {
            return s != null && suffix != null && s.Length >= suffix.Length
                   && s.Substring(s.Length - suffix.Length) == suffix;
        }

        private static string SubNoRange(string s, int start, int length)
        {
            if (s == null) return "";
            if (start < 0) start = 0;
            if (length < 0) length = 0;
            if (start + length > s.Length) length = s.Length - start;
            return s.Substring(start, length);
        }

        private static void FixPointObjects(JObject host, string key, List<string> issues, string path)
        {
            JToken tok;
            if (!host.TryGetValue(key, out tok) || tok == null) return;

            // 既に {x,y,z} なら Z を補完
            var jo = tok as JObject;
            if (jo != null)
            {
                if (!jo.ContainsKey("z"))
                {
                    jo["z"] = 0.0;
                    issues.Add((path ?? key) + ": z=0 default");
                }
                return;
            }

            // 配列 [x,y,(z)]
            var arr = tok as JArray;
            if (arr != null && (arr.Count == 2 || arr.Count == 3))
            {
                host[key] = new JObject
                {
                    ["x"] = arr[0],
                    ["y"] = arr[1],
                    ["z"] = (arr.Count >= 3 ? arr[2] : new JValue(0.0))
                };
                issues.Add((path ?? key) + ": array→object");
                return;
            }

            // "x,y,z" 文字列
            if (tok.Type == JTokenType.String)
            {
                var s = tok.Value<string>();
                var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                double x, y, z = 0.0;
                if (parts.Length >= 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                {
                    if (parts.Length >= 3) double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                    host[key] = new JObject { ["x"] = x, ["y"] = y, ["z"] = z };
                    issues.Add((path ?? key) + ": string→object");
                }
            }
        }

        private static JArray TryFixPointArray(JToken tok, List<string> issues, string key)
        {
            var outArr = new JArray();

            // 文字列 "x1,y1; x2,y2"
            if (tok != null && tok.Type == JTokenType.String)
            {
                var all = tok.Value<string>().Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < all.Length; i++)
                {
                    var s = all[i];
                    var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    double x, y, z = 0.0;
                    if (parts.Length >= 2
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                    {
                        if (parts.Length >= 3) double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                        outArr.Add(new JObject { ["x"] = x, ["y"] = y, ["z"] = z });
                    }
                }
                issues.Add(key + ": string-list→array");
                return outArr;
            }

            // 配列
            var ja = tok as JArray;
            if (ja != null)
            {
                for (int i = 0; i < ja.Count; i++)
                {
                    var t = ja[i];
                    if (t is JObject)
                    {
                        var jo = (JObject)t;
                        FixPointObjects(new JObject { [key] = jo }, key, issues, key);
                        outArr.Add(jo);
                    }
                    else if (t is JArray)
                    {
                        var a = (JArray)t;
                        var obj = new JObject();
                        if (a.Count > 0) obj["x"] = a[0];
                        if (a.Count > 1) obj["y"] = a[1];
                        obj["z"] = (a.Count > 2 ? a[2] : new JValue(0.0));
                        outArr.Add(obj);
                        issues.Add(key + "[" + i + "]: array→object");
                    }
                    else if (t.Type == JTokenType.String)
                    {
                        var s = t.Value<string>();
                        var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        double x, y, z = 0.0;
                        if (parts.Length >= 2
                            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                        {
                            if (parts.Length >= 3) double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
                            outArr.Add(new JObject { ["x"] = x, ["y"] = y, ["z"] = z });
                            issues.Add(key + "[" + i + "]: string→object");
                        }
                    }
                }
                return outArr;
            }

            return null;
        }
    }
}
