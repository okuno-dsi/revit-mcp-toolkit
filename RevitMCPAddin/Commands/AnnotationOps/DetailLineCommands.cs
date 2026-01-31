// ================================================================
// File: Commands/AnnotationOps/DetailLineCommands.cs
// 詳細線(Detail Line) 一式: 取得/作成/移動/削除/線種変更/モデル線→詳細線の投影複製
// 対応: .NET Framework 4.8 / C# 8 / Revit 2023 API
// ポイント: mm I/O（UnitHelper へ統一）, 例外時 { ok:false, msg }, View SketchPlane 準拠
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    internal static class DLHelpers
    {
        public static View ResolveView(Document doc, int viewId)
        {
            var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            if (v == null)
                throw new InvalidOperationException("View not found: " + viewId);
            return v;
        }

        public static CurveElement ResolveDetailCurve(Document doc, int elementId)
        {
            var ce = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId)) as CurveElement;
            if (ce == null || !ce.ViewSpecific)
                throw new InvalidOperationException("Detail Line not found or not view-specific: " + elementId);
            return ce;
        }

        public static GraphicsStyle ResolveLineStyle(Document doc, JObject p)
        {
            // by styleId
            if (p.TryGetValue("styleId", out var tid))
            {
                var gs = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>())) as GraphicsStyle;
                if (gs != null) return gs;
            }

            // by styleName (case-insensitive)
            if (p.TryGetValue("styleName", out var tname))
            {
                var name = tname.Value<string>();
                if (!string.IsNullOrEmpty(name))
                {
                    var cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                    foreach (Category sub in cat.SubCategories)
                    {
                        var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                        if (gs != null && sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return gs;
                    }
                }
            }
            return null;
        }

        public static string CurveKind(Curve c)
        {
            if (c is Line) return "Line";
            if (c is Arc) return "Arc";
            if (c is Ellipse) return "Ellipse";
            if (c is NurbSpline) return "Spline";
            return "Curve";
        }

        public static XYZ ReadPointMm(JObject pt)
        {
            // 入力は mm → 内部(ft) へ
            var x = pt.Value<double>("x");
            var y = pt.Value<double>("y");
            var z = pt.Value<double>("z");
            return UnitHelper.MmToXyz(x, y, z);
        }

        public static GraphicsStyle FindLineStyleByName(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            foreach (Category sub in cat.SubCategories)
            {
                if (sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            return null;
        }

        // ビュー平面の Transform 作成
        public static Transform BuildPlaneTransform(Plane plane)
        {
            var t = Transform.Identity;
            t.Origin = plane.Origin;
            t.BasisX = plane.XVec;
            t.BasisY = plane.YVec;
            t.BasisZ = plane.Normal;
            return t;
        }

        public static bool IsParallel(XYZ a, XYZ b, double tol = 1e-6)
        {
            if (a == null || b == null) return false;
            var dot = Math.Abs(a.Normalize().DotProduct(b.Normalize()));
            return (1.0 - dot) <= tol;
        }
    }

    // ------------------------------------------------------------
    // 1) 線種一覧（別名コマンドも下に用意）
    // ------------------------------------------------------------
    public class GetDetailLineStylesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_detail_line_styles";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (cat == null) return new { ok = false, msg = "Category OST_Lines not found." };

                var styles = new List<object>();
                foreach (Category sub in cat.SubCategories)
                {
                    var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                    if (gs != null)
                    {
                        styles.Add(new
                        {
                            styleId = gs.Id.IntValue(),
                            styleName = sub.Name,
                            graphicsStyleType = gs.GraphicsStyleType.ToString()
                        });
                    }
                }
                return new { ok = true, styles = styles };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 1) 一覧（ビュー内の詳細線）
    public class GetDetailLinesInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_detail_lines_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int viewId = p.Value<int>("viewId"); 
            // shape/paging
            var shape = p["_shape"] as JObject; 
            int limit = Math.Max(0, (shape?["page"] as JObject)?.Value<int?>("limit") ?? int.MaxValue); 
            int skip = Math.Max(0, (shape?["page"] as JObject)?.Value<int?>("skip") ?? (shape?["page"] as JObject)?.Value<int?>("offset") ?? 0); 
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false; 
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false; 
            bool includeEndpoints = p.Value<bool?>("includeEndpoints") ?? true; 
            string styleNameContains = p.Value<string>("styleNameContains"); 
            var typeIdsFilter = (p["typeIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>(); 

            var view = DLHelpers.ResolveView(doc, viewId);

            var coll = new FilteredElementCollector(doc, view.Id) 
                .OfClass(typeof(CurveElement)) 
                .Cast<CurveElement>() 
                .Where(ce => ce.ViewSpecific && ce.Category != null && ce.Category.Id.IntValue() == (int)BuiltInCategory.OST_Lines); 
            if (typeIdsFilter.Count > 0) coll = coll.Where(ce => typeIdsFilter.Contains(ce.GetTypeId().IntValue())); 
            if (!string.IsNullOrWhiteSpace(styleNameContains)) coll = coll.Where(ce => (ce.LineStyle?.Name ?? string.Empty).IndexOf(styleNameContains, StringComparison.OrdinalIgnoreCase) >= 0); 
            var all = coll.ToList(); 
            int totalCount = all.Count; 
            if (summaryOnly) return new { ok = true, viewId, totalCount }; 
            IEnumerable<CurveElement> paged = all; 
            if (skip > 0) paged = paged.Skip(skip); 
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit); 
            if (idsOnly) 
            { 
                var ids = paged.Select(ce => ce.Id.IntValue()).ToList(); 
                return new { ok = true, viewId, totalCount, elementIds = ids }; 
            } 
            var items = paged.Select(ce => 
            { 
                var c = ce.GeometryCurve; 
                var style = ce.LineStyle as GraphicsStyle; 
                object start = null, end = null; 
                if (includeEndpoints && c != null) 
                { 
                    var p0 = c.GetEndPoint(0); 
                    var p1 = c.GetEndPoint(1); 
                    start = new { x = UnitHelper.FtToMm(p0.X), y = UnitHelper.FtToMm(p0.Y), z = UnitHelper.FtToMm(p0.Z) }; 
                    end = new { x = UnitHelper.FtToMm(p1.X), y = UnitHelper.FtToMm(p1.Y), z = UnitHelper.FtToMm(p1.Z) }; 
                } 
                return new 
                { 
                    elementId = ce.Id.IntValue(), 
                    curveKind = (c != null ? DLHelpers.CurveKind(c) : ""), 
                    styleId = style != null ? style.Id.IntValue() : 0, 
                    styleName = style != null ? style.GraphicsStyleCategory?.Name ?? string.Empty : string.Empty, 
                    start, 
                    end 
                }; 
            }).ToList(); 
            return new { ok = true, viewId, totalCount, items }; 
        }
    }

    // 2) 直線作成
    public class CreateDetailLineCommand : IRevitCommandHandler
    {
        public string CommandName => "create_detail_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int viewId = p.Value<int>("viewId");
            var v = DLHelpers.ResolveView(doc, viewId);

            var s = DLHelpers.ReadPointMm(p.Value<JObject>("start"));
            var e = DLHelpers.ReadPointMm(p.Value<JObject>("end"));

            GraphicsStyle gs = DLHelpers.ResolveLineStyle(doc, p);

            using (var tx = new Transaction(doc, "Create Detail Line"))
            {
                try
                {
                    tx.Start();
                    var line = Line.CreateBound(s, e);
                    var ce = doc.Create.NewDetailCurve(v, line);
                    if (gs != null) ce.LineStyle = gs;
                    tx.Commit();
                    return new { ok = true, elementId = ce.Id.IntValue() };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Failed to create detail line: " + ex.Message };
                }
            }
        }
    }

    // 3) 円弧作成（3点法 / 中心+半径+角度）
    public class CreateDetailArcCommand : IRevitCommandHandler
    {
        public string CommandName => "create_detail_arc";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int viewId = p.Value<int>("viewId");
            var v = DLHelpers.ResolveView(doc, viewId);

            GraphicsStyle gs = DLHelpers.ResolveLineStyle(doc, p);

            using (var tx = new Transaction(doc, "Create Detail Arc"))
            {
                try
                {
                    tx.Start();

                    Curve arcCurve = null;

                    var mode = p.Value<string>("mode");
                    if (string.Equals(mode, "three_point", StringComparison.OrdinalIgnoreCase))
                    {
                        var s = DLHelpers.ReadPointMm(p.Value<JObject>("start"));
                        var m = DLHelpers.ReadPointMm(p.Value<JObject>("mid"));
                        var e = DLHelpers.ReadPointMm(p.Value<JObject>("end"));
                        arcCurve = Arc.Create(s, e, m);
                    }
                    else
                    {
                        // center + radius + start/end angle (deg)
                        var center = DLHelpers.ReadPointMm(p.Value<JObject>("center"));
                        double r = UnitHelper.MmToFt(p.Value<double>("radiusMm"));
                        double a0 = UnitHelper.DegToInternal(p.Value<double>("startAngleDeg"));
                        double a1 = UnitHelper.DegToInternal(p.Value<double>("endAngleDeg"));

                        // ビュー平面の座標基底を使う
                        SketchPlane sp = v.SketchPlane;
                        if (sp == null)
                            throw new InvalidOperationException("View has no SketchPlane. Use three_point mode or pick a 2D view.");

                        var plane = sp.GetPlane();
                        // Arc.Create(Plane, radius, startAngle, endAngle) は plane の X/Y 基底に対する角度
                        arcCurve = Arc.Create(plane, r, a0, a1);
                        // 原点を center に移動（Plane原点基準の円弧になるため）
                        var delta = center - plane.Origin;
                        if (!delta.IsAlmostEqualTo(XYZ.Zero))
                        {
                            arcCurve = arcCurve.CreateTransformed(Transform.CreateTranslation(delta));
                        }
                    }

                    var ce = doc.Create.NewDetailCurve(v, arcCurve);
                    if (gs != null) ce.LineStyle = gs;

                    tx.Commit();
                    return new { ok = true, elementId = ce.Id.IntValue() };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Failed to create detail arc: " + ex.Message };
                }
            }
        }
    }

    // 4) 移動
    public class MoveDetailLineCommand : IRevitCommandHandler
    {
        public string CommandName => "move_detail_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("elementId");
            double dx = UnitHelper.MmToFt(p.Value<double>("dx"));
            double dy = UnitHelper.MmToFt(p.Value<double>("dy"));
            double dz = UnitHelper.MmToFt(p.Value<double>("dz"));

            using (var tx = new Transaction(doc, "Move Detail Line"))
            {
                try
                {
                    tx.Start();
                    ElementTransformUtils.MoveElement(doc, Autodesk.Revit.DB.ElementIdCompat.From(id), new XYZ(dx, dy, dz));
                    tx.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Failed to move detail line: " + ex.Message };
                }
            }
        }
    }

    // 5) 回転（ビュー法線軸）
    public class RotateDetailLineCommand : IRevitCommandHandler
    {
        public string CommandName => "rotate_detail_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("elementId");
            var ce = DLHelpers.ResolveDetailCurve(doc, id);

            var view = doc.GetElement(ce.OwnerViewId) as View;
            if (view == null)
                return new { ok = false, msg = "Owner view not found." };

            var origin = DLHelpers.ReadPointMm(p.Value<JObject>("origin"));
            double angleRad = UnitHelper.DegToInternal(p.Value<double>("angleDeg"));

            // ビューの法線方向で回転軸を構成
            var axis = Line.CreateUnbound(origin, view.ViewDirection);

            using (var tx = new Transaction(doc, "Rotate Detail Line"))
            {
                try
                {
                    tx.Start();
                    ElementTransformUtils.RotateElement(doc, ce.Id, axis, angleRad);
                    tx.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Failed to rotate detail line: " + ex.Message };
                }
            }
        }
    }

    // 6) 削除（単体）
    [RpcCommand(
        "view.delete_detail_line",
        Aliases = new[] { "delete_detail_lines" }
    )]
    public class DeleteDetailLineCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_detail_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // Backward-compatible:
            // - elementId (int): single
            // - elementIds (int[]): bulk
            var rawIds = new List<int>();
            if (p.TryGetValue("elementIds", out var arrTok) && arrTok is JArray arr && arr.Count > 0)
            {
                foreach (var t in arr)
                {
                    try { rawIds.Add(t.Value<int>()); }
                    catch { /* ignore */ }
                }
            }
            else
            {
                rawIds.Add(p.Value<int>("elementId"));
            }

            // Resolve only view-specific detail curves (OST_Lines) to be safe
            var targetIds = new List<ElementId>();
            var skipped = new List<object>();
            foreach (var iid in rawIds.Distinct())
            {
                try
                {
                    var eid = Autodesk.Revit.DB.ElementIdCompat.From(iid);
                    var ce = doc.GetElement(eid) as CurveElement;
                    if (ce == null || !ce.ViewSpecific || ce.Category == null || ce.Category.Id.IntValue() != (int)BuiltInCategory.OST_Lines)
                    {
                        skipped.Add(new { elementId = iid, msg = "Not a view-specific detail line (OST_Lines)." });
                        continue;
                    }
                    targetIds.Add(eid);
                }
                catch (Exception ex)
                {
                    skipped.Add(new { elementId = iid, msg = "Invalid elementId: " + ex.Message });
                }
            }

            if (targetIds.Count == 0)
                return new { ok = false, msg = "No valid detail lines to delete.", requested = rawIds.Count, skipped = skipped };

            using (var tx = new Transaction(doc, "Delete Detail Line(s)"))
            {
                try
                {
                    tx.Start();
                    var deleted = doc.Delete(targetIds);
                    int cnt = deleted != null ? deleted.Count : 0;
                    tx.Commit();
                    return new { ok = cnt > 0, requested = rawIds.Count, deletedCount = cnt, skipped = skipped };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Failed to delete detail line(s): " + ex.Message, requested = rawIds.Count, skipped = skipped };
                }
            }
        }
    }

    // 7) 線種一覧（短縮名）
    public class GetLineStylesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_line_styles";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            var styles = new List<object>();

            foreach (Category sub in cat.SubCategories)
            {
                var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs != null)
                {
                    styles.Add(new { styleId = gs.Id.IntValue(), name = sub.Name });
                }
            }
            return new { ok = true, styles = styles };
        }
    }

    // 8) 線種変更（単体）
    public class SetDetailLineStyleCommand : IRevitCommandHandler
    {
        public string CommandName => "set_detail_line_style";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int elementId = p.Value<int>("elementId");
            var ce = DLHelpers.ResolveDetailCurve(doc, elementId);

            var gs = DLHelpers.ResolveLineStyle(doc, p);
            if (gs == null)
                return new { ok = false, msg = "Line style not found (styleId/styleName)." };

            using (var tx = new Transaction(doc, "Set Detail Line Style"))
            {
                try
                {
                    tx.Start();
                    ce.LineStyle = gs;
                    tx.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Failed to set line style: " + ex.Message };
                }
            }
        }
    }

    // 9) 線種変更（複数）
    public class SetDetailLinesStyleCommand : IRevitCommandHandler
    {
        public string CommandName => "set_detail_lines_style";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var ids = p["elementIds"] != null
                ? p["elementIds"].Values<int>().ToList()
                : new List<int>();

            if (ids.Count == 0)
                return new { ok = false, msg = "elementIds is required" };

            var gs = DLHelpers.ResolveLineStyle(doc, p);
            if (gs == null)
                return new { ok = false, msg = "Line style not found (styleId/styleName)." };

            int updated = 0;
            using (var tx = new Transaction(doc, "Set Detail Lines Style (Bulk)"))
            {
                try
                {
                    tx.Start();
                    foreach (var iid in ids)
                    {
                        var ce = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(iid)) as CurveElement;
                        if (ce != null && ce.ViewSpecific)
                        {
                            ce.LineStyle = gs;
                            updated++;
                        }
                    }
                    tx.Commit();
                    return new { ok = true, updated = updated };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Failed to set bulk line style: " + ex.Message };
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 11) モデル線 → 詳細線（ビュー平面へ投影して複製）
    //     Params: viewId, elementIds[], (optional) styleId/styleName
    //     Line/Arcは厳密、他は Tessellate で折れ線化
    // ------------------------------------------------------------
    public class ExplodeModelLineToDetailCommand : IRevitCommandHandler
    {
        public string CommandName => "explode_model_line_to_detail";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;
            var p = (JObject)cmd.Params;

            try
            {
                int viewId = p.Value<int>("viewId");
                var view = DLHelpers.ResolveView(doc, viewId);

                var sp = view.SketchPlane;
                if (sp == null) return new { ok = false, msg = "Target view has no SketchPlane." };
                var plane = sp.GetPlane();
                var toWorld = DLHelpers.BuildPlaneTransform(plane);
                var toPlane = toWorld.Inverse;

                var ids = p["elementIds"] != null ? p["elementIds"].Values<int>().Distinct().ToList() : new List<int>();
                if (ids.Count == 0) return new { ok = false, msg = "elementIds is required." };

                var mappings = new List<object>();
                var failed = new List<object>();
                int createdCount = 0;

                using (var tx = new Transaction(doc, "Explode Model Line(s) to Detail"))
                {
                    tx.Start();

                    foreach (var sid in ids)
                    {
                        var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(sid)) as CurveElement;
                        if (e == null)
                        {
                            failed.Add(new { sourceId = sid, msg = "Element not found or not a CurveElement." });
                            continue;
                        }
                        if (e.ViewSpecific)
                        {
                            failed.Add(new { sourceId = sid, msg = "Source is already a detail curve." });
                            continue;
                        }

                        var src = e.GeometryCurve;
                        if (src == null)
                        {
                            failed.Add(new { sourceId = sid, msg = "Null geometry." });
                            continue;
                        }

                        var outGs = DLHelpers.ResolveLineStyle(doc, p);
                        if (outGs == null)
                        {
                            // 元のモデル線の線種名を可能なら踏襲
                            var srcGs = e.LineStyle as GraphicsStyle;
                            var name = srcGs?.GraphicsStyleCategory?.Name;
                            if (!string.IsNullOrEmpty(name))
                                outGs = DLHelpers.FindLineStyleByName(doc, name);
                        }

                        var createdForThis = new List<int>();
                        try
                        {
                            CreateProjectedDetailCurves(doc, view, src, toPlane, toWorld, outGs, createdForThis);
                            if (createdForThis.Count > 0)
                            {
                                createdCount += createdForThis.Count;
                                mappings.Add(new { sourceId = sid, detailIds = createdForThis });
                            }
                            else
                            {
                                failed.Add(new { sourceId = sid, msg = "Projection produced no drawable segments." });
                            }
                        }
                        catch (Exception ex1)
                        {
                            failed.Add(new { sourceId = sid, msg = ex1.Message });
                        }
                    }

                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    total = ids.Count,
                    createdCount = createdCount,
                    mappings = mappings,
                    failed = failed
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Explode failed: " + ex.Message };
            }
        }

        private static void CreateProjectedDetailCurves(
            Document doc,
            View view,
            Curve src,
            Transform toPlane,
            Transform toWorld,
            GraphicsStyle outGs,
            List<int> createdIds)
        {
            // Line: 厳密投影
            if (src is Line)
            {
                var p0 = toPlane.OfPoint(src.GetEndPoint(0));
                var p1 = toPlane.OfPoint(src.GetEndPoint(1));
                p0 = new XYZ(p0.X, p0.Y, 0);
                p1 = new XYZ(p1.X, p1.Y, 0);
                if (p0.IsAlmostEqualTo(p1)) return;

                var w0 = toWorld.OfPoint(p0);
                var w1 = toWorld.OfPoint(p1);
                var line = Line.CreateBound(w0, w1);
                var ce = doc.Create.NewDetailCurve(view, line);
                if (outGs != null) ce.LineStyle = outGs;
                createdIds.Add(ce.Id.IntValue());
                return;
            }

            // Arc: 平行な場合は厳密、それ以外は折れ線化
            if (src is Arc)
            {
                var a = (Arc)src;

                // Revit 2023 には Arc.GetPlane() が無い → 3点から平面を復元
                var a0 = a.GetEndPoint(0);
                var am = a.Evaluate(0.5, true);
                var a1 = a.GetEndPoint(1);
                var arcPlane = Plane.CreateByThreePoints(a0, am, a1);

                if (DLHelpers.IsParallel(arcPlane.Normal, view.ViewDirection))
                {
                    // ビュー平面座標系へ射影 → Z=0 に押し付け → ワールドへ戻す
                    var p0 = toPlane.OfPoint(a0);
                    var pm = toPlane.OfPoint(am);
                    var p1 = toPlane.OfPoint(a1);

                    p0 = new XYZ(p0.X, p0.Y, 0);
                    pm = new XYZ(pm.X, pm.Y, 0);
                    p1 = new XYZ(p1.X, p1.Y, 0);

                    if (p0.IsAlmostEqualTo(p1)) return;

                    var w0 = toWorld.OfPoint(p0);
                    var wm = toWorld.OfPoint(pm);
                    var w1 = toWorld.OfPoint(p1);

                    var arcProj = Arc.Create(w0, w1, wm);
                    var ce = doc.Create.NewDetailCurve(view, arcProj);
                    if (outGs != null) ce.LineStyle = outGs;
                    createdIds.Add(ce.Id.IntValue());
                    return;
                }
                // それ以外は Tessellate へ
            }

            // その他カーブは Tessellate → 折れ線化
            var pts = src.Tessellate();
            if (pts == null || pts.Count < 2) return;

            XYZ prevW = null;
            foreach (var wp in pts)
            {
                var pp = toPlane.OfPoint(wp);
                pp = new XYZ(pp.X, pp.Y, 0);
                var w = toWorld.OfPoint(pp);

                if (prevW != null && !prevW.IsAlmostEqualTo(w))
                {
                    var seg = Line.CreateBound(prevW, w);
                    var ce = doc.Create.NewDetailCurve(view, seg);
                    if (outGs != null) ce.LineStyle = outGs;
                    createdIds.Add(ce.Id.IntValue());
                }
                prevW = w;
            }
        }
    }
}


