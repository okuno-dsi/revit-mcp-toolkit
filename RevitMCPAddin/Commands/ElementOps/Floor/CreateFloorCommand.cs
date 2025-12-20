// ================================================================
// File: RevitMCPAddin/Commands/ElementOps/FloorOps/CreateFloorCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: 柔軟 boundary 入力と親切エラー、Floor.Create を使用
// Depends: ResultUtil, UnitHelper, IRevitCommandHandler, RequestCommand
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using ARDB = Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using Autodesk.Revit.Exceptions; // AutoJoinFailedException はこちら

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class CreateFloorCommand : IRevitCommandHandler
    {
        public string CommandName => "create_floor";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            if (!(cmd.Params is JObject p))
                return ResultUtil.Err("params はオブジェクトで指定してください。");

            try
            {
                // -------- 1) Level 解決（id / name）
                var level = TryResolveLevel(doc, p, out string whyLevel);
                if (level == null) return ResultUtil.Err(whyLevel);

                // -------- 2) FloorType 解決（id / name）
                var floorType = TryResolveFloorType(doc, p, out string whyType);
                if (floorType == null) return ResultUtil.Err(whyType);

                // -------- 3) 境界（柔軟入力 → 正規化）
                var loops = TryParseBoundaryLoops(p, out string whyBoundary);
                if (loops == null || loops.Count == 0) return ResultUtil.Err(whyBoundary);

                // 簡易検証：各ループ3点以上・Z の整合
                foreach (var loop in loops)
                {
                    if (loop.NumberOfCurves() < 3)
                        return ResultUtil.Err("各ループは3点以上が必要です。");

                    // 代表 Z をとって誤差内に収まるかチェック
                    double? z0 = null;
                    foreach (ARDB.Curve c in loop)
                    {
                        var a = c.GetEndPoint(0).Z;
                        var b = c.GetEndPoint(1).Z;
                        z0 ??= a;
                        if (Math.Abs(a - z0.Value) > 1e-6 || Math.Abs(b - z0.Value) > 1e-6)
                            return ResultUtil.Err("境界の Z が混在しています。すべて同一レベルの Z にしてください。");
                    }
                }

                bool isStructural = p.Value<bool?>("isStructural") ?? false;

                using (var tx = new ARDB.Transaction(doc, "Create Floor"))
                {
                    tx.Start();

                    // Revit 2023+ API
                    var created = ARDB.Floor.Create(
                        doc,
                        loops,
                        floorType.Id,
                        level.Id,
                        isStructural,
                        null,   // normal（未使用）
                        0.0     // slope
                    );

                    tx.Commit();

                    return ResultUtil.Ok(new
                    {
                        elementId = created.Id.IntegerValue,
                        levelId = level.Id.IntegerValue,
                        floorTypeId = floorType.Id.IntegerValue
                    });
                }
            }
            catch (Autodesk.Revit.Exceptions.AutoJoinFailedException)
            {
                return ResultUtil.Err("自動結合に失敗しました。境界の交差/重複/近接や壁との干渉を確認してください。");
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return ResultUtil.Err($"引数エラー(Revit): {ex.Message}");
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                return ResultUtil.Err($"実行エラー(Revit): {ex.Message}");
            }
            catch (System.ArgumentException ex)
            {
                return ResultUtil.Err($"引数エラー(System): {ex.Message}");
            }
            catch (System.InvalidOperationException ex)
            {
                return ResultUtil.Err($"実行エラー(System): {ex.Message}");
            }
            catch (System.Exception ex)
            {
                return ResultUtil.Err($"Floor 作成中に例外: {ex.Message}");
            }
        }

        // -------------------------- Level 解決
        private ARDB.Level? TryResolveLevel(ARDB.Document doc, JObject p, out string why)
        {
            why = "";
            if (TryReadInt(p, "levelId", out int levelId))
            {
                var lvl = doc.GetElement(new ARDB.ElementId(levelId)) as ARDB.Level;
                if (lvl != null) return lvl;
                why = $"Level not found by id={levelId}.";
                return null;
            }
            var levelName = p.Value<string>("levelName") ?? p.Value<string>("level");
            if (!string.IsNullOrEmpty(levelName))
            {
                var lvl = new ARDB.FilteredElementCollector(doc)
                    .OfClass(typeof(ARDB.Level)).Cast<ARDB.Level>()
                    .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
                if (lvl != null) return lvl;
                why = $"Level not found by name='{levelName}'.";
                return null;
            }
            why = "Level を id か name で指定してください（levelId / levelName）。";
            return null;
        }

        // -------------------------- FloorType 解決
        private ARDB.FloorType? TryResolveFloorType(ARDB.Document doc, JObject p, out string why)
        {
            why = "";
            if (TryReadInt(p, "floorTypeId", out int typeId) || TryReadInt(p, "typeId", out typeId))
            {
                var ft = doc.GetElement(new ARDB.ElementId(typeId)) as ARDB.FloorType;
                if (ft != null) return ft;
                why = $"FloorType not found by id={typeId}.";
                return null;
            }
            var typeName = p.Value<string>("floorTypeName") ?? p.Value<string>("typeName");
            if (!string.IsNullOrEmpty(typeName))
            {
                var ft = new ARDB.FilteredElementCollector(doc)
                    .OfClass(typeof(ARDB.FloorType)).Cast<ARDB.FloorType>()
                    .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
                if (ft != null) return ft;
                why = $"FloorType not found by name='{typeName}'.";
                return null;
            }
            why = "FloorType を id か name で指定してください（floorTypeId / floorTypeName）。";
            return null;
        }

        // -------------------------- boundary（柔軟入力 → List<CurveLoop>）
        private List<ARDB.CurveLoop>? TryParseBoundaryLoops(JObject p, out string why)
        {
            why = "";
            JToken? token =
                p["boundary"] ??
                p["boundaryLoops"] ??
                p["loops"];

            if (token == null)
            {
                why = "boundary がありません。'boundary': [{ 'points': [ {x,y,z}, ... ] }] の形式で指定してください。";
                return null;
            }

            // 1) 既定：[{ "points":[...] }, ...]
            if (token is JArray arr && arr.Count > 0 && arr[0] is JObject && arr[0]["points"] != null)
            {
                var list = arr.Select(x => (JArray)((JObject)x)["points"]).ToList();
                return BuildCurveLoopsFromPointsArrays(list, out why);
            }

            // 2) 単一ループ：{ "points":[...] }
            if (token is JObject obj && obj["points"] is JArray singlePoints)
            {
                return BuildCurveLoopsFromPointsArrays(new List<JArray> { singlePoints }, out why);
            }

            // 3) 配列の配列：[[{x,y,z},...], ...]
            if (token is JArray arr2 && arr2.Count > 0 && arr2[0] is JArray)
            {
                var loops = new List<JArray>();
                foreach (var loop in arr2) loops.Add((JArray)loop);
                return BuildCurveLoopsFromPointsArrays(loops, out why);
            }

            why = "boundary の形式が不正です。許容形式: [{points:[..]}], {points:[..]}, [[{x,y,z}..]].";
            return null;
        }

        private List<ARDB.CurveLoop>? BuildCurveLoopsFromPointsArrays(List<JArray> loops, out string why)
        {
            why = "";
            var result = new List<ARDB.CurveLoop>();
            foreach (var pts in loops)
            {
                if (pts == null || pts.Count < 3)
                {
                    why = "各ループは3点以上が必要です。";
                    return null;
                }
                var cl = new ARDB.CurveLoop();
                for (int i = 0; i < pts.Count; i++)
                {
                    var whyA = ""; var whyB = ""; // ← 未初期化対策
                    var a = (JObject)pts[i];
                    var b = (JObject)pts[(i + 1) % pts.Count];

                    if (!TryReadPointMm(a, out var p0, out whyA) || !TryReadPointMm(b, out var p1, out whyB))
                    {
                        var whyBoth = string.IsNullOrEmpty(whyA) ? whyB : (string.IsNullOrEmpty(whyB) ? whyA : $"{whyA} / {whyB}");
                        why = string.IsNullOrEmpty(whyBoth) ? "点の読み取りに失敗しました。" : whyBoth;
                        return null;
                    }
                    cl.Append(ARDB.Line.CreateBound(p0, p1));
                }
                result.Add(cl);
            }
            return result;
        }

        private bool TryReadPointMm(JObject o, out ARDB.XYZ p, out string why)
        {
            p = default!;
            why = "";
            if (o == null) { why = "point が null です。"; return false; }

            bool okx = TryReadDouble(o, "x", out double x);
            bool oky = TryReadDouble(o, "y", out double y);
            bool okz = TryReadDouble(o, "z", out double z);
            if (!(okx && oky && okz))
            {
                why = "point は {x,y,z} を mm 単位で指定してください。";
                return false;
            }
            p = UnitHelper.MmToXyz(x, y, z);
            return true;
        }

        private bool TryReadInt(JObject o, string key, out int value)
        {
            value = 0;
            if (o[key] == null) return false;
            if (o[key]!.Type == JTokenType.Integer) { value = o.Value<int>(key); return true; }
            if (o[key]!.Type == JTokenType.String && int.TryParse((string)o[key]!, out value)) return true;
            return false;
        }

        private bool TryReadDouble(JObject o, string key, out double value)
        {
            value = 0;
            if (o[key] == null) return false;
            if (o[key]!.Type == JTokenType.Float || o[key]!.Type == JTokenType.Integer) { value = (double)o[key]!; return true; }
            if (o[key]!.Type == JTokenType.String && double.TryParse((string)o[key]!, out value)) return true;
            return false;
        }
    }
}
