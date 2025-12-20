// ================================================================
// File: Commands/AnnotationOps/ModelLineCommands.cs
// モデル線(Model Line) 一式: 一覧/作成(直線・円弧)/移動/回転/削除/線種変更
// 仕様:
//  - 入力座標は mm、内部で ft に変換（UnitHelper に統一）
//  - 線種は OST_Lines 配下の GraphicsStyle を styleId/styleName で解決
//  - 作成は SketchPlane 必須: 既定は World XY（Z法線）だが、viewId 指定時は
//    そのビューの SketchPlane を優先（無ければ World XY）
//  - 例外時は { ok:false, msg:"..." } を返却
// 対象: Revit 2023 / .NET Framework 4.8 / C# 8
// 依存: Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq, RevitMCPAddin.Core
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
    internal static class MLHelpers
    {
        /// <summary>viewId が指定されればその View を返す（無ければ null）</summary>
        public static View TryResolveView(Document doc, JObject p)
        {
            if (p.TryGetValue("viewId", out var vtok))
            {
                var v = doc.GetElement(new ElementId(vtok.Value<int>())) as View;
                return v;
            }
            return null;
        }

        /// <summary>モデル線用 SketchPlane を決定する。view.SketchPlane を優先、無ければ World XY。</summary>
        public static SketchPlane ResolveSketchPlane(Document doc, View view)
        {
            // 1) ビューが持つ SketchPlane を優先（平面ビュー等）
            var sp = view?.SketchPlane;
            if (sp != null) return sp;

            // 2) World XY (Z法線、原点=0)
            var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
            return SketchPlane.Create(doc, plane);
        }

        /// <summary>OST_Lines 配下の GraphicsStyle を styleId / styleName で解決</summary>
        public static GraphicsStyle ResolveLineStyle(Document doc, JObject p)
        {
            if (p.TryGetValue("styleId", out var tid))
            {
                var gs = doc.GetElement(new ElementId(tid.Value<int>())) as GraphicsStyle;
                if (gs != null) return gs;
            }

            if (p.TryGetValue("styleName", out var tname))
            {
                var name = tname.Value<string>();
                if (!string.IsNullOrWhiteSpace(name))
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

        /// <summary>カーブ種別名</summary>
        public static string CurveKind(Curve c)
        {
            if (c is Line) return "Line";
            if (c is Arc) return "Arc";
            if (c is Ellipse) return "Ellipse";
            if (c is NurbSpline) return "Spline";
            return "Curve";
        }

        /// <summary>入力(mm)の {x,y,z} を内部(ft)の XYZ へ</summary>
        public static XYZ ReadPointMm(JObject pt) =>
            UnitHelper.MmToXyz(pt.Value<double>("x"), pt.Value<double>("y"), pt.Value<double>("z"));
    }

    // ------------------------------------------------------------
    // 1) 一覧: get_model_lines_in_view
    // ------------------------------------------------------------
    public class GetModelLinesInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_model_lines_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)(cmd.Params ?? new JObject());
                int viewId = p.Value<int>("viewId");

                var view = doc.GetElement(new ElementId(viewId)) as View;
                if (view == null) return new { ok = false, msg = "View not found: " + viewId };

                // Shape / paging and filters
                var shape = p["_shape"] as JObject;
                bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
                var page = shape?["page"] as JObject;
                int limit = Math.Max(0, page?.Value<int?>("limit") ?? int.MaxValue);
                int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? 0);
                bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
                bool includeEndpoints = p.Value<bool?>("includeEndpoints") ?? true;
                var typeIdsFilter = (p["typeIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
                string styleNameContains = p.Value<string>("styleNameContains");

                var coll = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Where(ce => !ce.ViewSpecific)
                    .Where(ce => ce.Category != null && ce.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Lines);

                if (typeIdsFilter.Count > 0) coll = coll.Where(ce => typeIdsFilter.Contains(ce.GetTypeId().IntegerValue));
                if (!string.IsNullOrWhiteSpace(styleNameContains)) coll = coll.Where(ce => (ce.LineStyle?.Name ?? string.Empty).IndexOf(styleNameContains, StringComparison.OrdinalIgnoreCase) >= 0);

                var all = coll.ToList();
                int totalCount = all.Count;
                if (summaryOnly) return new { ok = true, viewId, totalCount };

                IEnumerable<CurveElement> paged = all;
                if (skip > 0) paged = paged.Skip(skip);
                if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

                if (idsOnly)
                {
                    var ids = paged.Select(ce => ce.Id.IntegerValue).ToList();
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
                        elementId = ce.Id.IntegerValue,
                        curveKind = (c != null ? MLHelpers.CurveKind(c) : ""),
                        styleId = style != null ? style.Id.IntegerValue : 0,
                        styleName = style != null ? style.GraphicsStyleCategory?.Name ?? "" : "",
                        start,
                        end
                    };
                }).ToList();

                return new { ok = true, viewId, totalCount, items };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 2) 作成(直線): create_model_line
    //    params: start{mm}, end{mm}, [viewId], [styleId|styleName]
    // ------------------------------------------------------------
    public class CreateModelLineCommand : IRevitCommandHandler
    {
        public string CommandName => "create_model_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;
            var p = (JObject)cmd.Params;

            try
            {
                var s = MLHelpers.ReadPointMm(p.Value<JObject>("start"));
                var e = MLHelpers.ReadPointMm(p.Value<JObject>("end"));

                var view = MLHelpers.TryResolveView(doc, p);
                using (var tx = new Transaction(doc, "Create Model Line"))
                {
                    tx.Start();

                    var sp = MLHelpers.ResolveSketchPlane(doc, view);
                    var line = Line.CreateBound(s, e);
                    var ce = doc.Create.NewModelCurve(line, sp);

                    // 線種
                    var gs = MLHelpers.ResolveLineStyle(doc, p);
                    if (gs != null) ce.LineStyle = gs;

                    tx.Commit();
                    return new { ok = true, elementId = ce.Id.IntegerValue };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to create model line: " + ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 3) 作成(円弧): create_model_arc
    //    モード:
    //      - three_point: start/mid/end (mm)
    //      - center_radius: center{mm}, radiusMm, startAngleDeg, endAngleDeg, [viewId]
    // ------------------------------------------------------------
    public class CreateModelArcCommand : IRevitCommandHandler
    {
        public string CommandName => "create_model_arc";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                var mode = (p.Value<string>("mode") ?? "three_point").Trim().ToLower();
                var view = MLHelpers.TryResolveView(doc, p);

                using (var tx = new Transaction(doc, "Create Model Arc"))
                {
                    tx.Start();

                    var sp = MLHelpers.ResolveSketchPlane(doc, view);
                    Curve arcCurve;

                    if (mode == "three_point")
                    {
                        var s = MLHelpers.ReadPointMm(p.Value<JObject>("start"));
                        var m = MLHelpers.ReadPointMm(p.Value<JObject>("mid"));
                        var e = MLHelpers.ReadPointMm(p.Value<JObject>("end"));
                        arcCurve = Arc.Create(s, e, m);
                    }
                    else
                    {
                        var center = MLHelpers.ReadPointMm(p.Value<JObject>("center"));
                        double r = UnitHelper.MmToFt(p.Value<double>("radiusMm"));
                        double a0 = UnitHelper.DegToInternal(p.Value<double>("startAngleDeg"));
                        double a1 = UnitHelper.DegToInternal(p.Value<double>("endAngleDeg"));

                        // SketchPlane の Plane 基底（X/Y）に沿って角度が定義される
                        var plane = sp.GetPlane();
                        arcCurve = Arc.Create(plane, r, a0, a1);

                        // 円弧は plane.Origin 基準 → center に平行移動
                        var delta = center - plane.Origin;
                        if (!delta.IsAlmostEqualTo(XYZ.Zero))
                            arcCurve = arcCurve.CreateTransformed(Transform.CreateTranslation(delta));
                    }

                    var ce = doc.Create.NewModelCurve(arcCurve, sp);

                    // 線種
                    var gs = MLHelpers.ResolveLineStyle(doc, p);
                    if (gs != null) ce.LineStyle = gs;

                    tx.Commit();
                    return new { ok = true, elementId = ce.Id.IntegerValue };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to create model arc: " + ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 4) 移動: move_model_line（dx/dy/dz は mm）
    // ------------------------------------------------------------
    public class MoveModelLineCommand : IRevitCommandHandler
    {
        public string CommandName => "move_model_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            try
            {
                int id = p.Value<int>("elementId");
                double dx = UnitHelper.MmToFt(p.Value<double?>("dx") ?? 0);
                double dy = UnitHelper.MmToFt(p.Value<double?>("dy") ?? 0);
                double dz = UnitHelper.MmToFt(p.Value<double?>("dz") ?? 0);

                using (var tx = new Transaction(doc, "Move Model Line"))
                {
                    tx.Start();
                    ElementTransformUtils.MoveElement(doc, new ElementId(id), new XYZ(dx, dy, dz));
                    tx.Commit();
                }
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to move model line: " + ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 5) 回転: rotate_model_line
    //    params: elementId, origin{mm}, angleDeg, [axis:"Z"|"X"|"Y"]
    // ------------------------------------------------------------
    public class RotateModelLineCommand : IRevitCommandHandler
    {
        public string CommandName => "rotate_model_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            try
            {
                int id = p.Value<int>("elementId");
                var origin = MLHelpers.ReadPointMm(p.Value<JObject>("origin"));
                double angleRad = UnitHelper.DegToInternal(p.Value<double?>("angleDeg") ?? 0.0);
                string axis = (p.Value<string>("axis") ?? "Z").Trim().ToUpper();

                XYZ dir = axis switch
                {
                    "X" => XYZ.BasisX,
                    "Y" => XYZ.BasisY,
                    _ => XYZ.BasisZ
                };

                var rotAxis = Line.CreateUnbound(origin, dir);

                using (var tx = new Transaction(doc, "Rotate Model Line"))
                {
                    tx.Start();
                    ElementTransformUtils.RotateElement(doc, new ElementId(id), rotAxis, angleRad);
                    tx.Commit();
                }
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to rotate model line: " + ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 6) 削除: delete_model_line / delete_model_lines
    // ------------------------------------------------------------
    public class DeleteModelLineCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_model_line";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            try
            {
                int id = ((JObject)cmd.Params).Value<int>("elementId");
                using (var tx = new Transaction(doc, "Delete Model Line"))
                {
                    tx.Start();
                    doc.Delete(new ElementId(id));
                    tx.Commit();
                }
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to delete model line: " + ex.Message };
            }
        }
    }

    public class DeleteModelLinesCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_model_lines";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            try
            {
                var ids = (p["elementIds"] as JArray)?.Values<int>().ToList()
                          ?? new List<int> { p.Value<int>("elementId") };

                using (var tx = new Transaction(doc, "Delete Model Lines"))
                {
                    tx.Start();
                    var deleted = doc.Delete(ids.Select(i => new ElementId(i)).ToList());
                    tx.Commit();
                    return new { ok = true, requested = ids.Count, deletedCount = deleted?.Count ?? 0 };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to delete model lines: " + ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 7) 線種変更: set_model_line_style（単体）
    // ------------------------------------------------------------
    public class SetModelLineStyleCommand : IRevitCommandHandler
    {
        public string CommandName => "set_model_line_style";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            try
            {
                int elementId = p.Value<int>("elementId");
                var ce = doc.GetElement(new ElementId(elementId)) as CurveElement;
                if (ce == null || ce.ViewSpecific)
                    return new { ok = false, msg = "Model Line not found (elementId) or it is a Detail Line." };

                var gs = MLHelpers.ResolveLineStyle(doc, p);
                if (gs == null) return new { ok = false, msg = "Line style not found (styleId/styleName)." };

                using (var tx = new Transaction(doc, "Set Model Line Style"))
                {
                    tx.Start();
                    ce.LineStyle = gs;
                    tx.Commit();
                }
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to set model line style: " + ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 8) 線種変更: set_model_lines_style（複数）
    // ------------------------------------------------------------
    public class SetModelLinesStyleCommand : IRevitCommandHandler
    {
        public string CommandName => "set_model_lines_style";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                var ids = p["elementIds"] != null
                    ? p["elementIds"].Values<int>().ToList()
                    : new List<int>();

                if (ids.Count == 0) return new { ok = false, msg = "elementIds is required." };

                var gs = MLHelpers.ResolveLineStyle(doc, p);
                if (gs == null) return new { ok = false, msg = "Line style not found (styleId/styleName)." };

                int updated = 0;
                using (var tx = new Transaction(doc, "Set Model Lines Style (Bulk)"))
                {
                    tx.Start();
                    foreach (var eid in ids)
                    {
                        var ce = doc.GetElement(new ElementId(eid)) as CurveElement;
                        if (ce != null && !ce.ViewSpecific) // モデル線に限定
                        {
                            ce.LineStyle = gs;
                            updated++;
                        }
                    }
                    tx.Commit();
                }

                return new { ok = true, updated };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to set bulk model line style: " + ex.Message };
            }
        }
    }
}
