// ================================================================
// File: Commands/ViewOps/HideElementsInViewCommand.cs
// Purpose : JSON-RPC "hide_elements_in_view" の実体
// Notes   : UI の「Hide in View」相当。View Template 適用時は安全スキップ。
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // IRevitCommandHandler, RequestCommand, ResultUtil, RevitLogger

namespace RevitMCPAddin.Commands.ViewOps
{
    public class HideElementsInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "hide_elements_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject?)cmd.Params ?? new JObject();

            // ---- 1) 対象ビューの決定（未指定ならアクティブビュー）
            View? view = null;
            var viewId = p.Value<int?>("viewId");
            if (viewId.HasValue && viewId.Value > 0)
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
            else
                view = uidoc?.ActiveGraphicalView ?? uidoc?.ActiveView;

            if (view == null) return ResultUtil.Err("対象ビューが見つかりません。");

            // View Template 判定とオプションのデタッチ
            var templateApplied = view.ViewTemplateId != ElementId.InvalidElementId;
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            if (templateApplied && !detachTemplate)
            {
                return ResultUtil.Ok(new JObject
                {
                    ["viewId"] = view.Id.IntValue(),
                    ["hiddenCount"] = 0,
                    ["skipped"] = new JArray(),
                    ["errors"] = new JArray(),
                    ["templateApplied"] = true,
                    ["templateViewId"] = view.ViewTemplateId.IntValue(),
                    ["msg"] = "View has a template; set detachViewTemplate:true to detach before operation."
                });
            }

            // ---- 2) 要素解決（elementId / elementIds / uniqueIds）
            var targetIds = new HashSet<int>();
            var singleId = p.Value<int?>("elementId");
            if (singleId.HasValue && singleId.Value > 0) targetIds.Add(singleId.Value);

            var arr = p["elementIds"] as JArray;
            if (arr != null)
            {
                foreach (var t in arr)
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        var id = t.Value<int>();
                        if (id > 0) targetIds.Add(id);
                    }
                }
            }

            var uniques = p["uniqueIds"] as JArray;
            if (uniques != null)
            {
                foreach (var u in uniques.Values<string?>())
                {
                    if (string.IsNullOrWhiteSpace(u)) continue;
                    var e = doc.GetElement(u!);
                    if (e != null) targetIds.Add(e.Id.IntValue());
                }
            }

            if (targetIds.Count == 0)
                return ResultUtil.Err("非表示にする要素が指定されていません。'elementId' または 'elementIds' を指定してください。");

            // ---- 3) パラメータ（バッチ・タイム・リフレッシュ）
            int batchSize = Math.Max(50, Math.Min(5000, p.Value<int?>("batchSize") ?? 800));
            int maxMillisPerTx = Math.Max(500, Math.Min(20000, p.Value<int?>("maxMillisPerTx") ?? 4000));
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            bool refreshView = p.Value<bool?>("refreshView") ?? true;

            // ---- 4) HideElements 対象解決
            var toHide = new List<ElementId>();
            var skipped = new JArray();
            var errors = new JArray();

            foreach (var id in targetIds.OrderBy(x => x))
            {
                var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id));
                if (e == null)
                {
                    errors.Add(new JObject { ["elementId"] = id, ["reason"] = "element not found" });
                    continue;
                }
                // Hide可否の事前チェック（Internal Origin等の不可要素で例外→詰まりを避ける）
                bool canHideElem = false;
                try { canHideElem = e.CanBeHidden(view); } catch { canHideElem = false; }
                if (!canHideElem)
                {
                    skipped.Add(new JObject { ["elementId"] = id, ["reason"] = "cannot_hide_in_view" });
                    continue;
                }

                toHide.Add(e.Id);
            }

            int hiddenCount = 0;
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            int nextIndex = startIndex;
            bool completed = false;

            while (nextIndex < toHide.Count)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var tx = new Transaction(doc, "Hide Elements in View (batched)"))
                {
                    try
                    {
                        tx.Start();

                        // Optionally detach template once (first batch)
                        if (detachTemplate && templateApplied && view.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            try { view.ViewTemplateId = ElementId.InvalidElementId; templateApplied = false; } catch (Exception ex) { RevitLogger.Warn("ViewTemplate detach failed.", ex); }
                        }

                        int end = Math.Min(toHide.Count, nextIndex + batchSize);
                        var batch = toHide.GetRange(nextIndex, end - nextIndex);

                        if (batch.Count > 0)
                        {
                            try
                            {
                                view.HideElements(batch);
                                hiddenCount += batch.Count;
                            }
                            catch (Exception ex)
                            {
                                // fallback per-item
                                foreach (var id in batch)
                                {
                                    try { view.HideElements(new List<ElementId> { id }); hiddenCount++; }
                                    catch (Exception ex1)
                                    {
                                        errors.Add(new JObject { ["elementId"] = id.IntValue(), ["reason"] = ex1.Message });
                                    }
                                }
                                RevitLogger.Error($"HideElements batch failed; per-item fallback. reason={ex.Message}");
                            }
                        }

                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        errors.Add(new JObject { ["reason"] = $"transaction failed: {ex.Message}" });
                        break;
                    }
                }

                // Regenerate/refresh only when requested (avoid unnecessary UI invalidations)
                if (refreshView)
                {
                    try { doc.Regenerate(); } catch { }
                    try { uidoc?.RefreshActiveView(); } catch { }
                }

                nextIndex += batchSize;
                if (sw.ElapsedMilliseconds > maxMillisPerTx) break; // soft time-slice; let clients loop using nextIndex
            }
            completed = nextIndex >= toHide.Count;

            // ---- 4) 返却
            return ResultUtil.Ok(new JObject
            {
                ["viewId"] = view.Id.IntValue(),
                ["hiddenCount"] = hiddenCount,
                ["skipped"] = skipped,
                ["errors"] = errors,
                ["templateApplied"] = view.ViewTemplateId != ElementId.InvalidElementId,
                ["templateViewId"] = view.ViewTemplateId != ElementId.InvalidElementId ? view.ViewTemplateId.IntValue() : (int?)null,
                ["completed"] = completed,
                ["nextIndex"] = completed ? (int?)null : nextIndex,
                ["batchSize"] = batchSize,
                ["elapsedMs"] = swAll.ElapsedMilliseconds
            });
        }
    }
}


