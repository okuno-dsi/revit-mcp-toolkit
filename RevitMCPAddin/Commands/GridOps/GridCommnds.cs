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
                var id = Autodesk.Revit.DB.ElementIdCompat.From(GridUnit.ToInt(gidTok, 0));
                if (id.IntValue() > 0)
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

                int eid = g.Id.IntValue();
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
                                gridId = grid.Id.IntValue(),
                                elementId = grid.Id.IntValue(),
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
                                gridId = grid.Id.IntValue(),
                                elementId = grid.Id.IntValue(),
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
                    return new { ok = true, gridId = g.Id.IntValue(), elementId = g.Id.IntValue(), uniqueId = g.UniqueId, name = g.Name };
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
                    return new { ok = true, gridId = g.Id.IntValue(), elementId = g.Id.IntValue(), uniqueId = g.UniqueId };
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
                var eid = Autodesk.Revit.DB.ElementIdCompat.From(GridUnit.ToInt(gidTok, 0));
                if (eid.IntValue() > 0) targetId = eid;
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

    // ================================================================
    // delete_grids : 複数通り芯の安全削除
    //   - gridIds[] / uniqueIds[] / elementIds[] に対応
    //   - dryRun=true では rollback で削除影響だけ確認
    //   - 実削除は confirm=true が必須
    // ================================================================
    public sealed class DeleteGridsCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_grids";

        private sealed class GridDeleteTarget
        {
            public ElementId Id = ElementId.InvalidElementId;
            public string UniqueId = string.Empty;
            public string Name = string.Empty;
        }

        private sealed class GridDeletePreview
        {
            public int gridId { get; set; }
            public int elementId { get; set; }
            public string uniqueId { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public int deletedElementCount { get; set; }
            public List<int> deletedElementIds { get; set; } = new List<int>();
            public List<int> dependentElementIds { get; set; } = new List<int>();
            public string? error { get; set; }
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, message = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            bool dryRun = GridUnit.ToBool(p["dryRun"], false);
            bool confirm = GridUnit.ToBool(p["confirm"], false);
            int maxCount = Math.Max(1, GridUnit.ToInt(p["maxCount"], 20));

            var targets = ResolveTargets(doc, p, out var invalidTargets);
            if (targets.Count == 0)
            {
                return new
                {
                    ok = false,
                    code = "INVALID_PARAM",
                    message = "gridIds / uniqueIds / elementIds のいずれかで削除対象の通り芯を指定してください。",
                    invalidTargets
                };
            }

            if (targets.Count > maxCount)
            {
                return new
                {
                    ok = false,
                    code = "TOO_MANY_TARGETS",
                    message = $"削除対象が maxCount={maxCount} を超えています。必要な場合は maxCount を明示してください。",
                    targetCount = targets.Count,
                    maxCount,
                    targets = targets.Select(ToTargetInfo).ToList(),
                    invalidTargets
                };
            }

            var preview = PreviewDeletes(doc, targets);
            bool hasPreviewError = preview.Any(x => x.error != null);
            int wouldDeleteElementCount = preview.Sum(x => x.deletedElementCount);
            int wouldDeleteNonGridElementCount = preview.Sum(x => Math.Max(0, x.deletedElementCount - 1));

            if (dryRun || !confirm)
            {
                return new
                {
                    ok = dryRun && !hasPreviewError,
                    code = dryRun ? (hasPreviewError ? "DRY_RUN_HAS_ERRORS" : "DRY_RUN") : "CONFIRM_REQUIRED",
                    message = dryRun
                        ? "dryRun のため削除していません。"
                        : "実削除には confirm=true を指定してください。dryRun 結果を確認してください。",
                    dryRun = true,
                    confirmRequired = !confirm,
                    targetCount = targets.Count,
                    maxCount,
                    wouldDeleteElementCount,
                    wouldDeleteNonGridElementCount,
                    targets = preview,
                    invalidTargets
                };
            }

            if (hasPreviewError)
            {
                return new
                {
                    ok = false,
                    code = "PREVIEW_FAILED",
                    message = "削除前確認で失敗した通り芯があるため、実削除を中止しました。",
                    dryRun = false,
                    targetCount = targets.Count,
                    maxCount,
                    wouldDeleteElementCount,
                    wouldDeleteNonGridElementCount,
                    targets = preview,
                    invalidTargets
                };
            }

            using (var t = new Transaction(doc, "Delete Grids"))
            {
                t.Start();
                try
                {
                    var deleted = new HashSet<int>();
                    foreach (var target in targets)
                    {
                        var ids = doc.Delete(target.Id);
                        foreach (var id in ids)
                        {
                            try { deleted.Add(id.IntValue()); } catch { }
                        }
                    }

                    t.Commit();
                    return new
                    {
                        ok = true,
                        code = "OK",
                        message = "OK",
                        dryRun = false,
                        targetCount = targets.Count,
                        deletedGridIds = targets.Select(x => x.Id.IntValue()).ToList(),
                        deletedElementCount = deleted.Count,
                        deletedElementIds = deleted.OrderBy(x => x).ToList(),
                        invalidTargets
                    };
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    return new
                    {
                        ok = false,
                        code = "EXCEPTION",
                        message = ex.Message,
                        dryRun = false,
                        targetCount = targets.Count,
                        targets = targets.Select(ToTargetInfo).ToList(),
                        invalidTargets
                    };
                }
            }
        }

        private static List<GridDeleteTarget> ResolveTargets(Document doc, JObject p, out List<object> invalidTargets)
        {
            invalidTargets = new List<object>();
            var targets = new List<GridDeleteTarget>();
            var seen = new HashSet<int>();

            foreach (int id in ReadIntArray(p, "gridIds")
                         .Concat(ReadIntArray(p, "elementIds"))
                         .Concat(ReadSingleInt(p, "gridId"))
                         .Concat(ReadSingleInt(p, "elementId")))
            {
                AddTargetById(doc, id, targets, seen, invalidTargets);
            }

            foreach (string uid in ReadStringArray(p, "uniqueIds").Concat(ReadSingleString(p, "uniqueId")))
            {
                AddTargetByUniqueId(doc, uid, targets, seen, invalidTargets);
            }

            return targets;
        }

        private static IEnumerable<int> ReadSingleInt(JObject p, string name)
        {
            if (p.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var tok))
            {
                int id = GridUnit.ToInt(tok, 0);
                if (id > 0) yield return id;
            }
        }

        private static IEnumerable<int> ReadIntArray(JObject p, string name)
        {
            if (!p.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var tok) || !(tok is JArray arr))
                yield break;

            foreach (var item in arr)
            {
                int id = GridUnit.ToInt(item, 0);
                if (id > 0) yield return id;
            }
        }

        private static IEnumerable<string> ReadSingleString(JObject p, string name)
        {
            if (p.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var tok))
            {
                string s = tok?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(s)) yield return s.Trim();
            }
        }

        private static IEnumerable<string> ReadStringArray(JObject p, string name)
        {
            if (!p.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var tok) || !(tok is JArray arr))
                yield break;

            foreach (var item in arr)
            {
                string s = item?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(s)) yield return s.Trim();
            }
        }

        private static void AddTargetById(
            Document doc,
            int id,
            List<GridDeleteTarget> targets,
            HashSet<int> seen,
            List<object> invalidTargets)
        {
            if (!seen.Add(id)) return;

            var elemId = Autodesk.Revit.DB.ElementIdCompat.From(id);
            var elem = doc.GetElement(elemId);
            var grid = elem as Grid;
            if (grid == null)
            {
                invalidTargets.Add(new
                {
                    input = id,
                    reason = elem == null ? "not_found" : "not_grid",
                    className = elem?.GetType().Name,
                    categoryName = elem?.Category?.Name
                });
                return;
            }

            targets.Add(new GridDeleteTarget
            {
                Id = grid.Id,
                UniqueId = grid.UniqueId ?? string.Empty,
                Name = grid.Name ?? string.Empty
            });
        }

        private static void AddTargetByUniqueId(
            Document doc,
            string uniqueId,
            List<GridDeleteTarget> targets,
            HashSet<int> seen,
            List<object> invalidTargets)
        {
            var elem = doc.GetElement(uniqueId);
            var grid = elem as Grid;
            if (grid == null)
            {
                invalidTargets.Add(new
                {
                    input = uniqueId,
                    reason = elem == null ? "not_found" : "not_grid",
                    className = elem?.GetType().Name,
                    categoryName = elem?.Category?.Name
                });
                return;
            }

            int id = grid.Id.IntValue();
            if (!seen.Add(id)) return;

            targets.Add(new GridDeleteTarget
            {
                Id = grid.Id,
                UniqueId = grid.UniqueId ?? string.Empty,
                Name = grid.Name ?? string.Empty
            });
        }

        private static List<GridDeletePreview> PreviewDeletes(Document doc, List<GridDeleteTarget> targets)
        {
            var preview = new List<GridDeletePreview>(targets.Count);
            foreach (var target in targets)
            {
                using (var t = new Transaction(doc, "Preview Delete Grid"))
                {
                    t.Start();
                    try
                    {
                        var deleted = doc.Delete(target.Id);
                        var deletedIds = deleted.Select(x => x.IntValue()).Distinct().OrderBy(x => x).ToList();
                        t.RollBack();
                        preview.Add(new GridDeletePreview
                        {
                            gridId = target.Id.IntValue(),
                            elementId = target.Id.IntValue(),
                            uniqueId = target.UniqueId,
                            name = target.Name,
                            deletedElementCount = deletedIds.Count,
                            deletedElementIds = deletedIds,
                            dependentElementIds = deletedIds.Where(x => x != target.Id.IntValue()).ToList(),
                            error = null
                        });
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        preview.Add(new GridDeletePreview
                        {
                            gridId = target.Id.IntValue(),
                            elementId = target.Id.IntValue(),
                            uniqueId = target.UniqueId,
                            name = target.Name,
                            deletedElementCount = 0,
                            deletedElementIds = new List<int>(),
                            dependentElementIds = new List<int>(),
                            error = ex.Message
                        });
                    }
                }
            }
            return preview;
        }

        private static object ToTargetInfo(GridDeleteTarget target)
        {
            return new
            {
                gridId = target.Id.IntValue(),
                elementId = target.Id.IntValue(),
                uniqueId = target.UniqueId,
                name = target.Name
            };
        }
    }
}


