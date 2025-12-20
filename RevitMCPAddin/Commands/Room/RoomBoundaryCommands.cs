// ================================================================
// File: RevitMCPAddin/Commands/RoomOps/RoomBoundaryCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: Room Separation Lines（部屋境界線）の作成・削除・移動・トリム・延長・クリーニング・一覧
// Policy : すべての座標入出力は mm。内部⇄mm はこのファイル内の変換ヘルパで完結。
// Error  : 失敗時は { ok:false, msg:"..." [,reason/hint] } を返す。
// Notes  : トリム/延長/クリーニングは Line のみ安全対応（Arc/スプラインは拒否）。
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // ResultUtil, IRevitCommandHandler, RequestCommand

namespace RevitMCPAddin.Commands.RoomOps
{
    // -------- 単位変換（このファイル内で自己完結） ---------------------------
    internal static class LengthConv
    {
        public static double MmToInternal(double mm)
            => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        public static double InternalToMm(double internalFeet)
            => UnitUtils.ConvertFromInternalUnits(internalFeet, UnitTypeId.Millimeters);
    }

    // -------- JSON⇄XYZ 変換（タプル不使用・安全） ---------------------------
    internal static class PointConv
    {
        public static bool TryParsePointMm(JToken? tok, out XYZ xyz, out string reason)
        {
            xyz = null!;
            reason = "";
            try
            {
                if (tok == null || tok.Type != JTokenType.Object) { reason = "座標は {x,y,z} のオブジェクトで指定してください。"; return false; }
                var o = (JObject)tok;
                if (!o.ContainsKey("x") || !o.ContainsKey("y") || !o.ContainsKey("z")) { reason = "x,y,z が必要です。"; return false; }
                var x = (double)o["x"]!;
                var y = (double)o["y"]!;
                var z = (double)o["z"]!;
                xyz = new XYZ(LengthConv.MmToInternal(x), LengthConv.MmToInternal(y), LengthConv.MmToInternal(z));
                return true;
            }
            catch
            {
                reason = "座標の数値解釈に失敗しました（mmの数値）。";
                return false;
            }
        }

        public static JObject PointToMm(XYZ p)
        {
            return new JObject
            {
                ["x"] = Math.Round(LengthConv.InternalToMm(p.X), 3),
                ["y"] = Math.Round(LengthConv.InternalToMm(p.Y), 3),
                ["z"] = Math.Round(LengthConv.InternalToMm(p.Z), 3),
            };
        }
    }

    internal static class RoomBoundaryUtil
    {
        public static bool TryGetView(UIApplication uiapp, JToken? paramViewId, out View view, out string reason)
        {
            view = null!;
            reason = "";
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) { reason = "アクティブドキュメントがありません。"; return false; }

            if (paramViewId != null && paramViewId.Type != JTokenType.Null)
            {
                if (!int.TryParse(paramViewId.ToString(), out var vid))
                {
                    reason = "viewId は整数で指定してください。";
                    return false;
                }
                var v = doc.GetElement(new ElementId(vid)) as View;
                if (v == null) { reason = $"viewId={vid} のビューが見つかりません。"; return false; }
                if (v.IsTemplate) { reason = "ビュー テンプレートには操作できません。"; return false; }
                view = v;
                return true;
            }
            view = doc.ActiveView;
            if (view == null) { reason = "アクティブビューがありません。"; return false; }
            if (view.IsTemplate) { reason = "アクティブビューがテンプレートです。"; return false; }
            return true;
        }

        public static bool TryGetSketchPlane(Document doc, View view, out SketchPlane sp, out string reason)
        {
            sp = null!;
            reason = "";
            try
            {
                if (view.SketchPlane != null) { sp = view.SketchPlane; return true; }

                using var tx = new Transaction(doc, "Create SketchPlane for RoomBoundary");
                tx.Start();

                var dir = view.ViewDirection; // 法線
                var origin = XYZ.Zero;
                try
                {
                    var bb = view.CropBox;
                    if (bb != null) origin = (bb.Min + bb.Max) * 0.5;
                }
                catch { /* no-op */ }

                var plane = Plane.CreateByNormalAndOrigin(dir, origin);
                sp = SketchPlane.Create(doc, plane);

                tx.Commit();
                return true;
            }
            catch (Exception ex)
            {
                reason = $"SketchPlane の取得/作成に失敗: {ex.Message}";
                return false;
            }
        }

        public static bool IsRoomSeparationLine(Element e)
            => e is CurveElement ce
               && ce.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_RoomSeparationLines;

        public static IEnumerable<CurveElement> GetRoomSeparationLines(Document doc)
        {
            var fec = new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .WherePasses(new ElementCategoryFilter(BuiltInCategory.OST_RoomSeparationLines));
            foreach (var e in fec)
                if (e is CurveElement ce) yield return ce;
        }

        public static bool TryGetCurveElement(Document doc, int elementId, out CurveElement ce, out string reason)
        {
            ce = null!;
            reason = "";
            var e = doc.GetElement(new ElementId(elementId));
            if (e == null) { reason = $"elementId={elementId} が見つかりません。"; return false; }
            if (!IsRoomSeparationLine(e)) { reason = $"elementId={elementId} は Room Separation Line ではありません。"; return false; }
            ce = (CurveElement)e;
            return true;
        }

        public static JObject CurveToMm(Curve c)
        {
            var s = c.GetEndPoint(0);
            var e = c.GetEndPoint(1);
            return new JObject
            {
                ["start"] = PointConv.PointToMm(s),
                ["end"] = PointConv.PointToMm(e),
                ["lengthMm"] = Math.Round(LengthConv.InternalToMm(s.DistanceTo(e)), 3),
                ["curveType"] = c.GetType().Name
            };
        }

        public static bool TryIntersect(Curve a, Curve b, out XYZ p)
        {
            p = null!;
            try
            {
                var result = a.Intersect(b, out var ira);
                if (result != SetComparisonResult.Overlap || ira == null || ira.Size == 0) return false;
                var item = ira.get_Item(0);
                p = item?.XYZPoint;
                return p != null;
            }
            catch { return false; }
        }

        public static bool ReplaceLineGeometry(CurveElement ce, XYZ newStart, XYZ newEnd, out string reason)
        {
            reason = "";
            try
            {
                var line = Line.CreateBound(newStart, newEnd);
                ce.GeometryCurve = line;
                return true;
            }
            catch (Exception ex)
            {
                reason = $"GeometryCurve の更新に失敗: {ex.Message}";
                return false;
            }
        }
    }

    // ----------------------------------------------------------------
    // 1) create_room_boundary_line
    // ----------------------------------------------------------------
    public class CreateRoomBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "create_room_boundary_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            if (!RoomBoundaryUtil.TryGetView(uiapp, p["viewId"], out var view, out var why))
                return ResultUtil.Err(why);
            var doc = view.Document;

            XYZ s, e;
            string whyS, whyE;
            bool okS = PointConv.TryParsePointMm(p["start"], out s, out whyS);
            bool okE = PointConv.TryParsePointMm(p["end"], out e, out whyE);

            if (!okS || !okE)
            {
                var errs = new List<string>();
                if (!okS && !string.IsNullOrEmpty(whyS)) errs.Add(whyS);
                if (!okE && !string.IsNullOrEmpty(whyE)) errs.Add(whyE);
                var msg = errs.Count > 0 ? string.Join(" / ", errs) : "start/end の座標解釈に失敗しました（mm想定: {x,y,z}）。";
                return ResultUtil.Err(msg);
            }

            if (!RoomBoundaryUtil.TryGetSketchPlane(doc, view, out var sp, out var reason))
                return ResultUtil.Err(reason);

            try
            {
                using var tx = new Transaction(doc, "Create Room Boundary Line");
                tx.Start();

                // Revit 2023: CurveArray を使用し、戻りは ModelCurveArray
                var ca = new CurveArray();
                ca.Append(Line.CreateBound(s, e));
                ModelCurveArray created = doc.Create.NewRoomBoundaryLines(sp, ca, view);

                var ids = new List<int>();
                foreach (ModelCurve mc in created) ids.Add(mc.Id.IntegerValue);

                tx.Commit();
                return ResultUtil.Ok(new { created = ids, count = ids.Count });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"境界線の作成に失敗: {ex.Message}");
            }
        }
    }

    // ----------------------------------------------------------------
    // 2) delete_room_boundary_line
    // ----------------------------------------------------------------
    public class DeleteRoomBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_room_boundary_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var targets = new List<int>();
            if (p["elementId"] != null && int.TryParse(p["elementId"]!.ToString(), out var one)) targets.Add(one);
            if (p["elementIds"] is JArray arr)
                foreach (var t in arr) if (int.TryParse(t.ToString(), out var v)) targets.Add(v);

            if (targets.Count == 0) return ResultUtil.Err("elementId もしくは elementIds が必要です。");

            var okIds = new List<ElementId>();
            var skipped = new List<object>();
            foreach (var id in targets.Distinct())
            {
                if (RoomBoundaryUtil.TryGetCurveElement(doc, id, out var ce, out var why))
                    okIds.Add(ce.Id);
                else
                    skipped.Add(new { elementId = id, reason = why });
            }
            if (okIds.Count == 0) return ResultUtil.Err("削除可能な Room 境界線が見つかりません。");

            try
            {
                using var tx = new Transaction(doc, "Delete Room Boundary Lines");
                tx.Start();
                var deleted = new HashSet<int>();
                foreach (var id in okIds)
                {
                    var res = doc.Delete(id);
                    foreach (var rid in res) deleted.Add(rid.IntegerValue);
                }
                tx.Commit();

                return ResultUtil.Ok(new
                {
                    requested = targets.Count,
                    deletedCount = deleted.Count,
                    deletedElementIds = deleted.ToList(),
                    skipped
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"削除に失敗: {ex.Message}");
            }
        }
    }

    // ----------------------------------------------------------------
    // 3) move_room_boundary_line
    // ----------------------------------------------------------------
    public class MoveRoomBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "move_room_boundary_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            if (p["elementId"] == null || !int.TryParse(p["elementId"]!.ToString(), out var id))
                return ResultUtil.Err("elementId が必要です（整数）。");

            if (!RoomBoundaryUtil.TryGetCurveElement(doc, id, out var ce, out var why))
                return ResultUtil.Err(why);

            XYZ delta;
            if (p["offset"] is JObject off)
            {
                if (!PointConv.TryParsePointMm(off, out delta, out var whyOff))
                    return ResultUtil.Err(whyOff);
            }
            else
            {
                // dx/dy/dz を mm として受ける
                double dx = p.Value<double?>("dx") ?? 0.0;
                double dy = p.Value<double?>("dy") ?? 0.0;
                double dz = p.Value<double?>("dz") ?? 0.0;
                delta = new XYZ(LengthConv.MmToInternal(dx), LengthConv.MmToInternal(dy), LengthConv.MmToInternal(dz));
            }

            try
            {
                using var tx = new Transaction(doc, "Move Room Boundary Line");
                tx.Start();
                ElementTransformUtils.MoveElement(doc, ce.Id, delta);
                tx.Commit();
                return ResultUtil.Ok(new { elementId = id });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"移動に失敗: {ex.Message}");
            }
        }
    }

    // ----------------------------------------------------------------
    // 4) trim_room_boundary_line（Line のみ）
    // ----------------------------------------------------------------
    public class TrimRoomBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "trim_room_boundary_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            if (!int.TryParse(p["lineId"]?.ToString(), out var lineId) ||
                !int.TryParse(p["targetLineId"]?.ToString(), out var targetLineId))
                return ResultUtil.Err("lineId と targetLineId（整数）が必要です。");

            if (!RoomBoundaryUtil.TryGetCurveElement(doc, lineId, out var a, out var whyA)) return ResultUtil.Err(whyA);
            if (!RoomBoundaryUtil.TryGetCurveElement(doc, targetLineId, out var b, out var whyB)) return ResultUtil.Err(whyB);

            var ca = a.GeometryCurve;
            var cb = b.GeometryCurve;
            if (!(ca is Line) || !(cb is Line))
                return ResultUtil.Err("現在の実装は Line 形状にのみ対応しています（Arc/スプラインは不可）。");

            if (!RoomBoundaryUtil.TryIntersect(ca, cb, out var ip))
                return ResultUtil.Err("2線は交差しません（平行/スキュー）。");

            var s = ca.GetEndPoint(0);
            var e = ca.GetEndPoint(1);
            XYZ newS, newE;
            if (s.DistanceTo(ip) <= e.DistanceTo(ip)) { newS = ip; newE = e; }
            else { newS = s; newE = ip; }

            try
            {
                using var tx = new Transaction(doc, "Trim Room Boundary Line");
                tx.Start();
                if (!RoomBoundaryUtil.ReplaceLineGeometry(a, newS, newE, out var reason))
                    return ResultUtil.Err(reason);
                tx.Commit();

                return ResultUtil.Ok(new
                {
                    lineId,
                    newStart = PointConv.PointToMm(newS),
                    newEnd = PointConv.PointToMm(newE)
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"トリムに失敗: {ex.Message}");
            }
        }
    }

    // ----------------------------------------------------------------
    // 5) extend_room_boundary_line（Line のみ、maxExtendMm ガード）
    // ----------------------------------------------------------------
    public class ExtendRoomBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "extend_room_boundary_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            if (!int.TryParse(p["lineId"]?.ToString(), out var lineId) ||
                !int.TryParse(p["targetLineId"]?.ToString(), out var targetLineId))
                return ResultUtil.Err("lineId と targetLineId（整数）が必要です。");

            var maxExtendMm = p.Value<double?>("maxExtendMm") ?? 5000.0;

            if (!RoomBoundaryUtil.TryGetCurveElement(doc, lineId, out var a, out var whyA)) return ResultUtil.Err(whyA);
            if (!RoomBoundaryUtil.TryGetCurveElement(doc, targetLineId, out var b, out var whyB)) return ResultUtil.Err(whyB);

            var ca = a.GeometryCurve;
            var cb = b.GeometryCurve;
            if (!(ca is Line) || !(cb is Line))
                return ResultUtil.Err("現在の実装は Line 形状にのみ対応しています。");

            if (!RoomBoundaryUtil.TryIntersect(ca, cb, out var ip))
                return ResultUtil.Err("2線は交差しません（平行/スキュー）。");

            var s = ca.GetEndPoint(0);
            var e = ca.GetEndPoint(1);

            var distMm = LengthConv.InternalToMm(Math.Min(s.DistanceTo(ip), e.DistanceTo(ip)));
            if (distMm > maxExtendMm)
                return ResultUtil.Err($"延長距離が上限 {maxExtendMm} mm を超えます（約 {Math.Round(distMm, 1)} mm）。");

            XYZ newS, newE;
            if (s.DistanceTo(ip) <= e.DistanceTo(ip)) { newS = ip; newE = e; }
            else { newS = s; newE = ip; }

            try
            {
                using var tx = new Transaction(doc, "Extend Room Boundary Line");
                tx.Start();
                if (!RoomBoundaryUtil.ReplaceLineGeometry(a, newS, newE, out var reason))
                    return ResultUtil.Err(reason);
                tx.Commit();

                return ResultUtil.Ok(new
                {
                    lineId,
                    newStart = PointConv.PointToMm(newS),
                    newEnd = PointConv.PointToMm(newE)
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"延長に失敗: {ex.Message}");
            }
        }
    }

    // ----------------------------------------------------------------
    // 6) clean_room_boundaries（Line のみ）
    // ----------------------------------------------------------------
    public class CleanRoomBoundariesCommand : IRevitCommandHandler
    {
        public string CommandName => "clean_room_boundaries";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            if (!RoomBoundaryUtil.TryGetView(uiapp, p["viewId"], out var view, out var why))
                return ResultUtil.Err(why);

            var doc = view.Document;
            double extendTolMm = p.Value<double?>("extendToleranceMm") ?? 50.0;
            double mergeTolMm = p.Value<double?>("mergeToleranceMm") ?? 5.0;
            bool deleteIsolated = p.Value<bool?>("deleteIsolated") ?? false;

            var lines = RoomBoundaryUtil.GetRoomSeparationLines(doc)
                .Where(ce => ce.GeometryCurve is Line)
                .Cast<CurveElement>()
                .ToList();

            int adjusted = 0, merged = 0, deleted = 0;

            try
            {
                using var tx = new Transaction(doc, "Clean Room Boundaries");
                tx.Start();

                // 1) 交点へ端点寄せ（微小ギャップ解消）
                for (int i = 0; i < lines.Count; i++)
                {
                    var a = lines[i];
                    var la = (Line)a.GeometryCurve;

                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        var b = lines[j];
                        var lb = (Line)b.GeometryCurve;

                        if (!RoomBoundaryUtil.TryIntersect(la, lb, out var ip)) continue;

                        var a_s = la.GetEndPoint(0); var a_e = la.GetEndPoint(1);
                        var b_s = lb.GetEndPoint(0); var b_e = lb.GetEndPoint(1);

                        bool aNeeds = Math.Min(a_s.DistanceTo(ip), a_e.DistanceTo(ip)) > LengthConv.MmToInternal(extendTolMm);
                        bool bNeeds = Math.Min(b_s.DistanceTo(ip), b_e.DistanceTo(ip)) > LengthConv.MmToInternal(extendTolMm);

                        if (aNeeds)
                        {
                            var ds = a_s.DistanceTo(ip); var de = a_e.DistanceTo(ip);
                            a.GeometryCurve = Line.CreateBound(ds <= de ? ip : a_s, ds <= de ? a_e : ip);
                            adjusted++;
                        }
                        if (bNeeds)
                        {
                            var ds = b_s.DistanceTo(ip); var de = b_e.DistanceTo(ip);
                            b.GeometryCurve = Line.CreateBound(ds <= de ? ip : b_s, ds <= de ? b_e : ip);
                            adjusted++;
                        }
                    }
                }

                // 2) 近接重複の統合
                var toDelete = new HashSet<ElementId>();
                double tolInt = LengthConv.MmToInternal(mergeTolMm);

                for (int i = 0; i < lines.Count; i++)
                {
                    if (toDelete.Contains(lines[i].Id)) continue;
                    var li = (Line)lines[i].GeometryCurve;
                    var i_s = li.GetEndPoint(0); var i_e = li.GetEndPoint(1);

                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        if (toDelete.Contains(lines[j].Id)) continue;
                        var lj = (Line)lines[j].GeometryCurve;
                        var j_s = lj.GetEndPoint(0); var j_e = lj.GetEndPoint(1);

                        bool close =
                            (i_s.DistanceTo(j_s) < tolInt && i_e.DistanceTo(j_e) < tolInt) ||
                            (i_s.DistanceTo(j_e) < tolInt && i_e.DistanceTo(j_s) < tolInt);

                        if (close)
                        {
                            toDelete.Add(lines[j].Id);
                            merged++;
                        }
                    }
                }

                if (toDelete.Count > 0)
                {
                    doc.Delete(toDelete.ToList());
                    deleted += toDelete.Count;
                }

                // 3) 孤立線の削除（任意）
                if (deleteIsolated)
                {
                    var survivors = RoomBoundaryUtil.GetRoomSeparationLines(doc)
                        .Where(ce => ce.GeometryCurve is Line)
                        .Cast<CurveElement>()
                        .ToList();

                    var endpointMap = new Dictionary<string, int>();
                    string Key(XYZ p) => $"{Math.Round(p.X, 6)}|{Math.Round(p.Y, 6)}|{Math.Round(p.Z, 6)}";

                    foreach (var ce in survivors)
                    {
                        var l = (Line)ce.GeometryCurve;
                        var ks = Key(l.GetEndPoint(0));
                        var ke = Key(l.GetEndPoint(1));
                        endpointMap[ks] = endpointMap.TryGetValue(ks, out var c1) ? c1 + 1 : 1;
                        endpointMap[ke] = endpointMap.TryGetValue(ke, out var c2) ? c2 + 1 : 1;
                    }

                    var iso = survivors
                        .Where(ce =>
                        {
                            var l = (Line)ce.GeometryCurve;
                            return (endpointMap[Key(l.GetEndPoint(0))] == 1 &&
                                    endpointMap[Key(l.GetEndPoint(1))] == 1);
                        })
                        .Select(ce => ce.Id)
                        .ToList();

                    if (iso.Count > 0)
                    {
                        doc.Delete(iso);
                        deleted += iso.Count;
                    }
                }

                tx.Commit();

                return ResultUtil.Ok(new
                {
                    viewId = view.Id.IntegerValue,
                    adjusted,
                    merged,
                    deleted,
                    units = new { Length = "mm" }
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"クリーニングでエラー: {ex.Message}");
            }
        }
    }

    // ----------------------------------------------------------------
    // 7) get_room_boundary_lines_in_view
    // ----------------------------------------------------------------
    public class GetRoomBoundaryLinesInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_boundary_lines_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            if (!RoomBoundaryUtil.TryGetView(uiapp, p["viewId"], out var view, out var why))
                return ResultUtil.Err(why);

            var doc = view.Document;
            var list = new List<object>();

            foreach (var ce in RoomBoundaryUtil.GetRoomSeparationLines(doc))
            {
                var c = ce.GeometryCurve;
                if (c is Line line)
                {
                    var s = line.GetEndPoint(0);
                    var e = line.GetEndPoint(1);
                    list.Add(new
                    {
                        elementId = ce.Id.IntegerValue,
                        kind = "Line",
                        start = PointConv.PointToMm(s),
                        end = PointConv.PointToMm(e),
                        lengthMm = Math.Round(LengthConv.InternalToMm(s.DistanceTo(e)), 3)
                    });
                }
                else
                {
                    list.Add(new
                    {
                        elementId = ce.Id.IntegerValue,
                        kind = c.GetType().Name,
                        curve = RoomBoundaryUtil.CurveToMm(c)
                    });
                }
            }

            return ResultUtil.Ok(new
            {
                viewId = view.Id.IntegerValue,
                total = list.Count,
                lines = list,
                units = new { Length = "mm" }
            });
        }
    }
}
