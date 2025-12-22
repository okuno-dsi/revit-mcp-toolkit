// ================================================================
// File: Commands/ViewOps/SetCategoryOverrideCommand.cs (2023/2024 safe)
// カテゴリに色（線色＋任意で塗りつぶし）を適用。利用可能な API だけ使う。
// ビューテンプレートが適用されているビューは安全にスキップ（既定）。
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class SetCategoryOverrideCommand : IRevitCommandHandler
    {
        public string CommandName => "set_category_override";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            // ビュー解決（viewId 未指定/0 → アクティブグラフィックビュー）
            int reqViewId = p.Value<int?>("viewId") ?? 0;
            View view = null;
            ElementId viewId = ElementId.InvalidElementId;
            if (reqViewId > 0)
            {
                viewId = Autodesk.Revit.DB.ElementIdCompat.From(reqViewId);
                view = doc.GetElement(viewId) as View;
            }
            if (view == null)
            {
                view = uiapp.ActiveUIDocument?.ActiveGraphicalView
                    ?? (uiapp.ActiveUIDocument?.ActiveView is View av && av.ViewType != ViewType.ProjectBrowser ? av : null);
                if (view != null) viewId = view.Id;
            }
            // オプション: 自動で安全な3D作業ビュー作成
            bool autoWorkingView = p.Value<bool?>("autoWorkingView") ?? true;
            if (view == null && autoWorkingView)
            {
                using (var tx = new Transaction(doc, "Create Working 3D (SetCategoryOverride)"))
                {
                    try
                    {
                        tx.Start();
                        var vtf = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.ThreeDimensional);
                        if (vtf == null) throw new InvalidOperationException("3D view family type not found");
                        var v3d = View3D.CreateIsometric(doc, vtf.Id);
                        v3d.Name = UniqueViewName(doc, "MCP_Working_3D");
                        tx.Commit();
                        view = v3d; viewId = v3d.Id;
                        try { uiapp.ActiveUIDocument?.RequestViewChange(v3d); } catch { }
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return new { ok = false, errorCode = "ERR_NO_VIEW", msg = "適用先ビューを解決できませんでした。viewId を指定してください。", detail = ex.Message };
                    }
                }
            }
            if (view == null) return new { ok = false, msg = "適用先ビューを解決できませんでした。viewId を指定してください。" };

            // シート/集計表など不可ビューは除外
            if (view is ViewSchedule || view.ViewType == ViewType.DrawingSheet)
                return new { ok = false, msg = $"このビュー（{view.ViewType}）ではグラフィックオーバーライドを使用できません。" };

            // ★ ビューテンプレート: detachViewTemplate:true で一時デタッチ可
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            if (view.ViewTemplateId != ElementId.InvalidElementId && detachTemplate)
            {
                using (var tx0 = new Transaction(doc, "Detach View Template (CategoryOverride)"))
                { try { tx0.Start(); view.ViewTemplateId = ElementId.InvalidElementId; tx0.Commit(); } catch { try { tx0.RollBack(); } catch { } } }
            }
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                if (autoWorkingView)
                {
                    using (var tx = new Transaction(doc, "Create Working 3D (Template Locked)"))
                    {
                        try
                        {
                            tx.Start();
                            var vtf = new FilteredElementCollector(doc)
                                .OfClass(typeof(ViewFamilyType))
                                .Cast<ViewFamilyType>()
                                .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.ThreeDimensional);
                            if (vtf == null) throw new InvalidOperationException("3D view family type not found");
                            var v3d = View3D.CreateIsometric(doc, vtf.Id);
                            v3d.Name = UniqueViewName(doc, "MCP_Working_3D");
                            tx.Commit();
                            view = v3d; viewId = v3d.Id;
                            try { uiapp.ActiveUIDocument?.RequestViewChange(v3d); } catch { }
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            var tmplId = view.ViewTemplateId.IntValue();
                            return new
                            {
                                ok = true,
                                viewId = viewId.IntValue(),
                                requested = 0,
                                overridden = 0,
                                skipped = 0,
                                templateApplied = true,
                                templateViewId = tmplId,
                                appliedTo = "skipped",
                                msg = "View has a template; set detachViewTemplate:true to proceed.",
                                detail = ex.Message
                            };
                        }
                    }
                }
                else
                {
                    var tmplId = view.ViewTemplateId.IntValue();
                    return new
                    {
                        ok = true,
                        viewId = viewId.IntValue(),
                        requested = 0,
                        overridden = 0,
                        skipped = 0,
                        templateApplied = true,
                        templateViewId = tmplId,
                        appliedTo = "skipped",
                        msg = "View has a template; set detachViewTemplate:true to proceed."
                    };
                }
            }

            // 入力の色
            var colorObj = p["color"];
            if (colorObj == null) return new { ok = false, msg = "color が指定されていません。" };
            byte r = colorObj.Value<byte>("r");
            byte g = colorObj.Value<byte>("g");
            byte b = colorObj.Value<byte>("b");
            var c = new Color(r, g, b);

            // 塗りつぶし適用（任意）・透過度（0..100）
            bool applyFill = p.Value<bool?>("applyFill") ?? false;
            int transparency = p.Value<int?>("transparency") ?? 40;
            if (transparency < 0) transparency = 0;
            if (transparency > 100) transparency = 100;

            // カテゴリID配列
            var catIds = new List<ElementId>();
            var catIdsToken = p["categoryIds"] as JArray;
            if (catIdsToken != null)
            {
                foreach (var t in catIdsToken)
                    catIds.Add(Autodesk.Revit.DB.ElementIdCompat.From((int)t));
            }
            if (catIds.Count == 0) return new { ok = false, msg = "categoryIds が空です。" };

            // OverrideGraphicSettings 構築（利用可能APIのみ）
            var ogs = new OverrideGraphicSettings();
            GraphicsOverrideHelper.TrySetLineColors(ogs, c);

            if (applyFill)
            {
                // Surface/Cut の前景・背景をソリッドで設定（Revit 2023/2024で安全に）
                GraphicsOverrideHelper.TrySetAllSurfaceAndCutPatterns(doc, ogs, c, visible: true);
            }

            // 透過度（Surface のみ）
            GraphicsOverrideHelper.TrySetSurfaceTransparency(ogs, transparency);

            int overridden = 0, skipped = 0;

            int batchSize = Math.Max(10, Math.Min(5000, p.Value<int?>("batchSize") ?? 200));
            int maxMillisPerTx = Math.Max(500, Math.Min(10000, p.Value<int?>("maxMillisPerTx") ?? 3000));
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            bool refreshView = p.Value<bool?>("refreshView") ?? true;

            var swAll = System.Diagnostics.Stopwatch.StartNew();
            int nextIndex = startIndex;
            while (nextIndex < catIds.Count)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (var tx = new Transaction(doc, "[MCP] Set Category Override (batched)"))
                {
                    try
                    {
                        tx.Start();
                        int end = Math.Min(catIds.Count, nextIndex + batchSize);
                        for (int i = nextIndex; i < end; i++)
                        {
                            var cid = catIds[i];
                            try
                            {
                                bool canHide = false; try { canHide = view.CanCategoryBeHidden(cid); } catch (Exception ex) { RevitLogger.Info($"CanCategoryBeHidden failed for {cid.IntValue()}: {ex.Message}"); }
                                if (!canHide) { skipped++; continue; }
                                view.SetCategoryOverrides(cid, ogs);
                                overridden++;
                            }
                            catch (Exception ex)
                            {
                                skipped++;
                                RevitLogger.Error($"SetCategoryOverrides failed: cid={cid.IntValue()}", ex);
                            }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { tx.RollBack(); } catch { }
                        RevitLogger.Error("SetCategoryOverride transaction failed", ex);
                        break;
                    }
                }
                if (refreshView)
                {
                    try { doc.Regenerate(); } catch { }
                    try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { }
                }
                nextIndex += batchSize;
                if (sw.ElapsedMilliseconds > maxMillisPerTx) break;
            }

            return new
            {
                ok = true,
                viewId = viewId.IntValue(),
                overridden,
                skipped,
                templateApplied = false,
                templateViewId = (int?)null,
                appliedTo = "view",
                completed = nextIndex >= catIds.Count,
                nextIndex,
                batchSize,
                elapsedMs = swAll.ElapsedMilliseconds
            };
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int i = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Any(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} {i++}";
            }
            return name;
        }
    }
}


