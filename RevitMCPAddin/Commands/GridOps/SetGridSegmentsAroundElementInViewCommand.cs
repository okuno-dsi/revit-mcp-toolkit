#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.GridOps
{
    /// <summary>
    /// JSON-RPC: set_grid_segments_around_element_in_view
    /// 指定ビュー内のグリッド(直線)を、要素中心（または指定中心点）を基準に
    /// 「中心±halfLengthMm」の短い2Dセグメントに更新する。
    /// </summary>
    public class SetGridSegmentsAroundElementInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "set_grid_segments_around_element_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null || uidoc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();
            var guard = ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            int viewIdInt = p.Value<int?>("viewId") ?? uidoc.ActiveView?.Id.IntValue() ?? -1;
            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdInt)) as View;
            if (view == null) return new { ok = false, msg = $"viewId={viewIdInt} のビューが見つかりません。" };

            if (!(view is ViewPlan))
                return new { ok = false, msg = "Plan/Ceiling/Engineering ビューのみ対象です。", viewType = view.ViewType.ToString() };

            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                if (!detachTemplate)
                {
                    return new
                    {
                        ok = false,
                        code = "VIEW_TEMPLATE_APPLIED",
                        msg = "ビューにテンプレートが適用されています。detachViewTemplate=true で再実行してください。",
                        viewTemplateId = view.ViewTemplateId.IntegerValue
                    };
                }

                using (var txDetach = new Transaction(doc, "[MCP] Detach View Template (Grid Segment)"))
                {
                    try
                    {
                        txDetach.Start();
                        view.ViewTemplateId = ElementId.InvalidElementId;
                        txDetach.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { txDetach.RollBack(); } catch { }
                        return new { ok = false, msg = "ViewTemplate の解除に失敗: " + ex.Message };
                    }
                }
            }

            // ---- center 解決 ----
            XYZ? center = null;
            int elementId = p.Value<int?>("elementId") ?? -1;
            if (elementId > 0)
            {
                var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
                if (e != null)
                {
                    var bb = e.get_BoundingBox(null) ?? e.get_BoundingBox(view);
                    if (bb != null)
                    {
                        center = new XYZ(
                            (bb.Min.X + bb.Max.X) * 0.5,
                            (bb.Min.Y + bb.Max.Y) * 0.5,
                            (bb.Min.Z + bb.Max.Z) * 0.5
                        );
                    }
                }
            }

            if (center == null && p["centerMm"] is JObject cm)
            {
                center = new XYZ(
                    UnitHelper.MmToFt(cm.Value<double?>("x") ?? 0),
                    UnitHelper.MmToFt(cm.Value<double?>("y") ?? 0),
                    UnitHelper.MmToFt(cm.Value<double?>("z") ?? 0)
                );
            }
            if (center == null && p["center"] is JObject c)
            {
                // center も mm 想定（このプロジェクトの運用に合わせる）
                center = new XYZ(
                    UnitHelper.MmToFt(c.Value<double?>("x") ?? 0),
                    UnitHelper.MmToFt(c.Value<double?>("y") ?? 0),
                    UnitHelper.MmToFt(c.Value<double?>("z") ?? 0)
                );
            }
            if (center == null)
                return new { ok = false, msg = "elementId または centerMm が必要です。" };

            double halfLengthMm = p.Value<double?>("halfLengthMm") ?? 1000.0;
            if (halfLengthMm <= 0) halfLengthMm = 1000.0;
            double halfFt = UnitHelper.MmToFt(halfLengthMm);
            bool dryRun = p.Value<bool?>("dryRun") ?? false;

            // 対象グリッド（view内）
            var idSet = new HashSet<int>();
            if (p.TryGetValue("gridIds", out var tok) && tok is JArray arr)
            {
                foreach (var t in arr)
                {
                    try { idSet.Add(t.Value<int>()); } catch { }
                }
            }

            var grids = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType()
                .Cast<Grid>()
                .Where(g => idSet.Count == 0 || idSet.Contains(g.Id.IntegerValue))
                .ToList();

            if (grids.Count == 0)
                return new { ok = false, code = "NO_GRIDS", msg = "対象グリッドがありません。" };

            int changed = 0;
            int skippedCurved = 0;
            int skippedHidden = 0;
            var errors = new List<object>();

            Action<Grid> applyGrid = (g) =>
            {
                try
                {
                    Curve curveInView = null;
                    try
                    {
                        var cs = g.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                        if (cs != null && cs.Count > 0) curveInView = cs[0];
                    }
                    catch { /* ignore */ }

                    if (curveInView == null)
                    {
                        try
                        {
                            var cs = g.GetCurvesInView(DatumExtentType.Model, view);
                            if (cs != null && cs.Count > 0) curveInView = cs[0];
                        }
                        catch { /* ignore */ }
                    }

                    var line = curveInView as Line;
                    if (line == null)
                    {
                        skippedCurved++;
                        return;
                    }

                    try
                    {
                        if (g.IsHidden(view))
                        {
                            skippedHidden++;
                            return;
                        }
                    }
                    catch { /* ignore */ }

                    XYZ p0 = line.GetEndPoint(0);
                    XYZ dir = (line.GetEndPoint(1) - p0).Normalize();
                    XYZ v = center - p0;
                    double t = v.DotProduct(dir);
                    XYZ proj = p0 + dir.Multiply(t);

                    XYZ s = proj - dir.Multiply(halfFt);
                    XYZ e = proj + dir.Multiply(halfFt);
                    var newLine = Line.CreateBound(s, e);

                    if (!dryRun)
                    {
                        g.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
                        g.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);
                        g.SetCurveInView(DatumExtentType.ViewSpecific, view, newLine);
                    }
                    changed++;
                }
                catch (Exception ex)
                {
                    errors.Add(new { gridId = g.Id.IntegerValue, error = ex.Message });
                }
            };

            if (!dryRun)
            {
                using (var tx = new Transaction(doc, "[MCP] Set Grid Segments Around Element In View"))
                {
                    try
                    {
                        tx.Start();
                        foreach (var g in grids) applyGrid(g);
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { tx.RollBack(); } catch { }
                        return new { ok = false, msg = ex.Message };
                    }
                }
            }
            else
            {
                foreach (var g in grids) applyGrid(g);
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                gridCount = grids.Count,
                changed,
                skippedCurved,
                skippedHidden,
                errors,
                halfLengthMm = Math.Round(halfLengthMm, 3),
                centerMm = new
                {
                    x = Math.Round(UnitHelper.FtToMm(center.X), 3),
                    y = Math.Round(UnitHelper.FtToMm(center.Y), 3),
                    z = Math.Round(UnitHelper.FtToMm(center.Z), 3)
                },
                dryRun
            };
        }
    }
}
