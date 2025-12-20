// ================================================================
// File: RevitMCPAddin/Commands/DatumOps/GridCommands.cs  (UnitHelper 統一版)
// Purpose : Grid(通り芯) 取得/作成/改名/移動/削除
// Target  : .NET Framework 4.8 / C# 8 / Revit 2023 API
// Depends : Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq,
//           RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand, UnitHelper)
// Notes   : mm 入出力 / 内部 ft。gridId/uniqueId 両対応。1リクエスト=1レスポンス厳守。
//           get_grids は “配列 + 辞書(gridsById)” を同時返却（両対応）
// 変更点  : 変換はすべて UnitHelper に統一（自前の定数/関数は削除）
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DatumOps
{
    // ------------------------------
    // 共通ユーティリティ（UnitHelper で mm↔ft を一本化）
    // ------------------------------
    internal static class GridUnit
    {
        public static XYZ Mm(double x, double y, double z = 0) => UnitHelper.MmToXyz(x, y, z);

        public static JObject Pt(XYZ p) => new JObject
        {
            ["x"] = Math.Round(UnitHelper.FtToMm(p.X), 3),
            ["y"] = Math.Round(UnitHelper.FtToMm(p.Y), 3),
            ["z"] = Math.Round(UnitHelper.FtToMm(p.Z), 3)
        };

        public static double ToDouble(JToken? t, double def = 0)
        {
            if (t == null) return def;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return t.Value<double>();
            double v; return double.TryParse(t.ToString(), out v) ? v : def;
        }

        public static int ToInt(JToken? t, int def = 0)
        {
            if (t == null) return def;
            if (t.Type == JTokenType.Integer) return t.Value<int>();
            int v; return int.TryParse(t.ToString(), out v) ? v : def;
        }

        public static bool ToBool(JToken? t, bool def = false)
        {
            if (t == null) return def;
            if (t.Type == JTokenType.Boolean) return t.Value<bool>();
            bool v; return bool.TryParse(t.ToString(), out v) ? v : def;
        }
    }

    internal static class GridFind
    {
        public static Grid? Find(Document doc, JObject p)
        {
            // gridId 優先 → uniqueId
            if (p.TryGetValue("gridId", StringComparison.OrdinalIgnoreCase, out var gidTok))
            {
                var id = new ElementId(GridUnit.ToInt(gidTok, 0));
                if (id.IntegerValue > 0)
                {
                    var g = doc.GetElement(id) as Grid;
                    if (g != null) return g;
                }
            }
            if (p.TryGetValue("uniqueId", StringComparison.OrdinalIgnoreCase, out var uidTok))
            {
                var uid = uidTok?.ToString();
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    var e = doc.GetElement(uid);
                    var g = e as Grid;
                    if (g != null) return g;
                }
            }
            return null;
        }

        public static IList<Grid> All(Document doc)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().ToList();
        }
    }

    internal static class GridNaming
    {
        public static void SafeRename(Grid grid, string desired)
        {
            if (string.IsNullOrWhiteSpace(desired)) return;
            var doc = grid.Document;
            if (doc == null) { grid.Name = desired; return; }

            bool dup = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                       .Any(g => g.Id != grid.Id && g.Name.Equals(desired, StringComparison.OrdinalIgnoreCase));
            grid.Name = dup ? $"{desired} (2)" : desired;
        }

        public static string AutoNameFor(Line line, ref int countX, ref int countY)
        {
            var d = line.Direction;
            var ax = Math.Abs(d.X);
            var ay = Math.Abs(d.Y);
            // 縦線（Y優勢）は X、横線（X優勢）は Y
            if (ax < ay) return $"X{++countX}";
            return $"Y{++countY}";
        }
    }

    // ================================================================
    // get_grids : 通り芯一覧（mm の start/end を返却、直線/円弧対応）
    // 両対応: grids（配列）に加え、gridsById（辞書）も同時返却
    // ================================================================
    public sealed class GetGridsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_grids";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, message = "アクティブドキュメントがありません。" };

            var grids = GridFind.All(doc);

            var items = grids.Select(g =>
            {
                var c = g.Curve;
                XYZ s, e;
                if (c is Line ln)
                {
                    s = ln.GetEndPoint(0);
                    e = ln.GetEndPoint(1);
                }
                else if (c is Arc arc)
                {
                    s = arc.GetEndPoint(0);
                    e = arc.GetEndPoint(1);
                }
                else
                {
                    s = c.GetEndPoint(0);
                    e = c.GetEndPoint(1);
                }

                int eid = g.Id.IntegerValue;
                return new
                {
                    gridId = eid,                 // 既存フィールド
                    elementId = eid,              // エージェント互換の別名
                    uniqueId = g.UniqueId,
                    name = g.Name,
                    start = GridUnit.Pt(s),
                    end = GridUnit.Pt(e)
                };
            })
            .ToList();

            // 両対応: dict ビューを追加
            var gridsById = items.ToDictionary(x => x.gridId, x => (object)x);

            return new
            {
                ok = true,
                totalCount = items.Count,
                // 単位明示（配列/辞書どちらも mm を返す）
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" },
                grids = items,       // 配列ビュー（従来）
                gridsById            // 追加：辞書ビュー
            };
        }
    }

    // ================================================================
    // create_grids : ① axis+positions（mm）/ ② segments（start/end mm）両対応
    // 追加オプション: defaultLengthMm（axis+positions時の片側長; 既定 30480mm）
    //                  names[]（命名指定、足りない分は自動命名）
    // ================================================================
    public sealed class CreateGridsCommand : IRevitCommandHandler
    {
        public string CommandName => "create_grids";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, message = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            // Accept alias 'id' for gridId for broader client compatibility
            if (!p.ContainsKey("gridId") && p.TryGetValue("id", StringComparison.OrdinalIgnoreCase, out var idTok))
            {
                p["gridId"] = idTok;
            }

            var created = new List<dynamic>();

            // --- 事前宣言＋安全な代入 ---
            JArray? segArr = null;
            bool hasSegments = false;
            if (p.TryGetValue("segments", StringComparison.OrdinalIgnoreCase, out var segTok))
            {
                if (segTok is JArray sa && sa.Count > 0) { segArr = sa; hasSegments = true; }
            }

            JArray? posArr = null;
            bool hasPositionsArray = false;
            if (p.TryGetValue("positions", StringComparison.OrdinalIgnoreCase, out var posTok))
            {
                if (posTok is JArray pa && pa.Count > 0) { posArr = pa; hasPositionsArray = true; }
            }

            JToken? axisTok = null;
            bool hasAxis = p.TryGetValue("axis", StringComparison.OrdinalIgnoreCase, out axisTok)
                           && !string.IsNullOrWhiteSpace(axisTok?.ToString());

            bool hasPositions = hasPositionsArray && hasAxis;

            if (!hasSegments && !hasPositions)
                return new { ok = false, message = "segments[] または axis+positions[] のいずれかが必要です。" };

            // 既存通し番号
            var all = GridFind.All(doc);
            int countX = all.Count(g => g.Name.StartsWith("X", StringComparison.OrdinalIgnoreCase));
            int countY = all.Count(g => g.Name.StartsWith("Y", StringComparison.OrdinalIgnoreCase));

            // 任意 names
            List<string>? names = null;
            if (p.TryGetValue("names", StringComparison.OrdinalIgnoreCase, out var namesTok) && namesTok is JArray namesArr)
                names = namesArr.Select(x => x.ToString()).ToList();

            using (var t = new Transaction(doc, "Create Grids"))
            {
                t.Start();
                try
                {
                    if (hasSegments)
                    {
                        // origin（相対原点）
                        double ox = 0, oy = 0, oz = 0;
                        if (p.TryGetValue("origin", StringComparison.OrdinalIgnoreCase, out var orgTok) && orgTok is JObject org)
                        {
                            ox = GridUnit.ToDouble(org["x"]);
                            oy = GridUnit.ToDouble(org["y"]);
                            oz = GridUnit.ToDouble(org["z"]);
                        }

                        for (int i = 0; i < segArr!.Count; i++)
                        {
                            var seg = segArr[i] as JObject;
                            if (seg == null) continue;

                            var st = seg["start"] as JObject;
                            var ed = seg["end"] as JObject;
                            if (st == null || ed == null)
                                return new { ok = false, message = $"segments[{i}] に start/end が必要です。" };

                            double sx = ox + GridUnit.ToDouble(st["x"]);
                            double sy = oy + GridUnit.ToDouble(st["y"]);
                            double sz = oz + GridUnit.ToDouble(st["z"]);

                            double ex = ox + GridUnit.ToDouble(ed["x"]);
                            double ey = oy + GridUnit.ToDouble(ed["y"]);
                            double ez = oz + GridUnit.ToDouble(ed["z"]);

                            var line = Line.CreateBound(GridUnit.Mm(sx, sy, sz), GridUnit.Mm(ex, ey, ez));
                            var grid = Grid.Create(doc, line);

                            string desiredName = names != null && i < names.Count ? names[i]
                                               : GridNaming.AutoNameFor(line, ref countX, ref countY);
                            GridNaming.SafeRename(grid, desiredName);

                            created.Add(new
                            {
                                gridId = grid.Id.IntegerValue,
                                elementId = grid.Id.IntegerValue,
                                uniqueId = grid.UniqueId,
                                name = grid.Name
                            });
                        }
                    }
                    else if (hasPositions)
                    {
                        string axis = axisTok!.ToString().Trim().ToUpperInvariant();
                        if (axis != "X" && axis != "Y")
                            return new { ok = false, message = "axis は 'X' または 'Y' です。" };

                        double half = 30480.0; // 既定（100ft ≒ 30480mm）…入力は mm 想定
                        if (p.TryGetValue("defaultLengthMm", StringComparison.OrdinalIgnoreCase, out var lenTok))
                        {
                            var v = GridUnit.ToDouble(lenTok, 30480.0);
                            if (v > 100.0) half = v;
                        }

                        for (int i = 0; i < posArr!.Count; i++)
                        {
                            double pos = GridUnit.ToDouble(posArr[i], 0);

                            Line line = (axis == "X")
                                ? Line.CreateBound(GridUnit.Mm(-half, pos), GridUnit.Mm(+half, pos))
                                : Line.CreateBound(GridUnit.Mm(pos, -half), GridUnit.Mm(pos, +half));

                            var grid = Grid.Create(doc, line);
                            string desiredName = (names != null && i < names.Count) ? names[i]
                                               : (axis == "X" ? $"X{++countX}" : $"Y{++countY}");
                            GridNaming.SafeRename(grid, desiredName);

                            created.Add(new
                            {
                                gridId = grid.Id.IntegerValue,
                                elementId = grid.Id.IntegerValue,
                                uniqueId = grid.UniqueId,
                                name = grid.Name
                            });
                        }
                    }

                    t.Commit();
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    t.RollBack();
                    return new { ok = false, message = $"Revit ArgumentException: {ex.Message}" };
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return new { ok = false, message = ex.Message };
                }
            }

            var gridIds = created.Select(x => (int)x.gridId).ToList();
            // ここは互換優先のまま（必要なら gridsById 追加も可能）
            return new { ok = true, grids = created, gridIds };
        }
    }

    // ================================================================
    // update_grid_name : 通り芯名の変更（gridId/uniqueId どちらでも）
    // ================================================================
    public sealed class UpdateGridNameCommand : IRevitCommandHandler
    {
        public string CommandName => "update_grid_name";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, message = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            string newName = p.Value<string>("name") ?? p.Value<string>("Name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newName))
                return new { ok = false, message = "name が必要です。" };

            var g = GridFind.Find(doc, p);
            if (g == null) return new { ok = false, message = "指定の通り芯が見つかりません（gridId/uniqueId を確認）。" };

            using (var t = new Transaction(doc, "Rename Grid"))
            {
                t.Start();
                try
                {
                    GridNaming.SafeRename(g, newName);
                    t.Commit();
                    return new { ok = true, gridId = g.Id.IntegerValue, elementId = g.Id.IntegerValue, uniqueId = g.UniqueId, name = g.Name };
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return new { ok = false, message = ex.Message };
                }
            }
        }
    }

    // ================================================================
    // move_grid : 平行移動（dx/dy/dz mm, gridId/uniqueId どちらでも）
    // ================================================================
    public sealed class MoveGridCommand : IRevitCommandHandler
    {
        public string CommandName => "move_grid";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, message = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            var g = GridFind.Find(doc, p);
            if (g == null) return new { ok = false, message = "指定の通り芯が見つかりません（gridId/uniqueId を確認）。" };

            double dx = GridUnit.ToDouble(p["dx"]);
            double dy = GridUnit.ToDouble(p["dy"]);
            double dz = GridUnit.ToDouble(p["dz"]);

            using (var t = new Transaction(doc, "Move Grid"))
            {
                t.Start();
                try
                {
                    var v = GridUnit.Mm(dx, dy, dz);
                    ElementTransformUtils.MoveElement(doc, g.Id, v);
                    t.Commit();
                    return new { ok = true, gridId = g.Id.IntegerValue, elementId = g.Id.IntegerValue, uniqueId = g.UniqueId };
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return new { ok = false, message = ex.Message };
                }
            }
        }
    }

    // ================================================================
    // delete_grid : 削除（gridId/uniqueId どちらでも）
    // ================================================================
    public sealed class DeleteGridCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_grid";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, message = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            ElementId? targetId = null;
            if (p.TryGetValue("gridId", StringComparison.OrdinalIgnoreCase, out var gidTok))
            {
                var eid = new ElementId(GridUnit.ToInt(gidTok, 0));
                if (eid.IntegerValue > 0) targetId = eid;
            }
            if (targetId == null && p.TryGetValue("uniqueId", StringComparison.OrdinalIgnoreCase, out var uidTok))
            {
                var uid = uidTok?.ToString();
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    var e = doc.GetElement(uid);
                    if (e != null) targetId = e.Id;
                }
            }

            if (targetId == null)
                return new { ok = false, code = "INVALID_PARAM", message = "gridId または uniqueId を指定してください。" };

            using (var t = new Transaction(doc, "Delete Grid"))
            {
                t.Start();
                try
                {
                    doc.Delete(targetId);
                    t.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return new { ok = false, code = "EXCEPTION", message = ex.Message };
                }
            }
        }
    }
}
