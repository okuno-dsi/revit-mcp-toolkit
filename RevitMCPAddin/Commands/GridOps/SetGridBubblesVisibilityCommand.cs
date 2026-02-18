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
    /// JSON-RPC: set_grid_bubbles_visibility
    /// グリッド線そのものは残したまま、バブル表示のみをビュー単位で制御する。
    /// </summary>
    public class SetGridBubblesVisibilityCommand : IRevitCommandHandler
    {
        public string CommandName => "set_grid_bubbles_visibility";

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

                using (var txDetach = new Transaction(doc, "[MCP] Detach View Template (Grid Bubble)"))
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

            bool? bothVisible = p.Value<bool?>("bothVisible");
            bool end0Visible = p.Value<bool?>("end0Visible") ?? (bothVisible ?? false);
            bool end1Visible = p.Value<bool?>("end1Visible") ?? (bothVisible ?? false);
            bool dryRun = p.Value<bool?>("dryRun") ?? false;

            var idSet = new HashSet<int>();
            if (p.TryGetValue("gridIds", out var tok) && tok is JArray arr)
            {
                foreach (var t in arr)
                {
                    try { idSet.Add(t.Value<int>()); } catch { }
                }
            }

            var grids = new List<Grid>();
            if (idSet.Count > 0)
            {
                foreach (var id in idSet)
                {
                    var g = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as Grid;
                    if (g != null) grids.Add(g);
                }
            }
            else
            {
                // 指定が無ければ「このビューに見えているグリッド」を対象
                grids = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(BuiltInCategory.OST_Grids)
                    .WhereElementIsNotElementType()
                    .Cast<Grid>()
                    .ToList();
            }

            if (grids.Count == 0)
                return new { ok = false, code = "NO_GRIDS", msg = "対象グリッドがありません。" };

            int changed0 = 0, changed1 = 0, skipped = 0;
            var errors = new List<object>();

            Action<Grid, DatumEnds, bool> apply = (g, end, visible) =>
            {
                bool before;
                try { before = g.IsBubbleVisibleInView(end, view); }
                catch
                {
                    skipped++;
                    return;
                }

                if (before == visible) return;
                if (dryRun)
                {
                    if (end == DatumEnds.End0) changed0++; else changed1++;
                    return;
                }

                try
                {
                    if (visible) g.ShowBubbleInView(end, view);
                    else g.HideBubbleInView(end, view);
                    if (end == DatumEnds.End0) changed0++; else changed1++;
                }
                catch (Exception ex)
                {
                    errors.Add(new { gridId = g.Id.IntegerValue, end = end.ToString(), error = ex.Message });
                }
            };

            if (!dryRun)
            {
                using (var tx = new Transaction(doc, "[MCP] Set Grid Bubble Visibility"))
                {
                    try
                    {
                        tx.Start();
                        foreach (var g in grids)
                        {
                            apply(g, DatumEnds.End0, end0Visible);
                            apply(g, DatumEnds.End1, end1Visible);
                        }
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
                foreach (var g in grids)
                {
                    apply(g, DatumEnds.End0, end0Visible);
                    apply(g, DatumEnds.End1, end1Visible);
                }
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                gridCount = grids.Count,
                target = new { end0Visible, end1Visible },
                changedEnd0 = changed0,
                changedEnd1 = changed1,
                skipped,
                errors,
                dryRun
            };
        }
    }
}

