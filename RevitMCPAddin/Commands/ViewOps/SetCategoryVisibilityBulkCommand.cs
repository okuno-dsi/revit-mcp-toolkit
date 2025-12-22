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
    /// <summary>
    /// set_category_visibility_bulk
    /// ビュー内のカテゴリ表示を一括制御します。
    /// 典型用途:
    /// - keepOnly モード: 指定カテゴリだけ表示し、それ以外（Model など）をすべて非表示。
    /// - hideOnly モード: 指定カテゴリだけ非表示（既存 set_category_visibility のバルク版）。
    /// </summary>
    public class SetCategoryVisibilityBulkCommand : IRevitCommandHandler
    {
        public string CommandName => "set_category_visibility_bulk";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            Autodesk.Revit.DB.Document doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            JObject p = (JObject)(cmd.Params ?? new JObject());

            // 対象ビュー解決
            int reqViewId = p.Value<int?>("viewId") ?? 0;
            Autodesk.Revit.DB.View view = null;
            if (reqViewId > 0)
            {
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(reqViewId)) as Autodesk.Revit.DB.View;
            }
            if (view == null)
            {
                view = uiapp.ActiveUIDocument?.ActiveGraphicalView
                       ?? (uiapp.ActiveUIDocument?.ActiveView as Autodesk.Revit.DB.View);
            }
            if (view == null)
            {
                return new { ok = false, msg = "対象ビューを解決できませんでした。viewId を指定してください。" };
            }

            // ビューテンプレート対応
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            if (view.ViewTemplateId != ElementId.InvalidElementId && detachTemplate)
            {
                using (Transaction tx = new Transaction(doc, "[MCP] Detach View Template (VisibilityBulk)"))
                {
                    try
                    {
                        tx.Start();
                        view.ViewTemplateId = ElementId.InvalidElementId;
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { tx.RollBack(); } catch { }
                        RevitLogger.Warn("Detach view template failed in SetCategoryVisibilityBulkCommand.", ex);
                    }
                }
            }
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                int tmplId = view.ViewTemplateId.IntValue();
                return new
                {
                    ok = true,
                    viewId = view.Id.IntValue(),
                    changed = 0,
                    skipped = 0,
                    templateApplied = true,
                    templateViewId = tmplId,
                    appliedTo = "skipped",
                    msg = "View has a template; set detachViewTemplate:true to proceed."
                };
            }

            // モード: keep_only / hide_only （既定 keep_only）
            string mode = (p.Value<string>("mode") ?? "keep_only").Trim().ToLowerInvariant();
            if (mode != "keep_only" && mode != "hide_only")
            {
                mode = "keep_only";
            }

            // 対象カテゴリタイプ: Model / Annotation / All（既定 Model）
            string categoryTypeFilter = (p.Value<string>("categoryType") ?? "Model").Trim();
            CategoryType? ctFilter = null;
            if (string.Equals(categoryTypeFilter, "Model", StringComparison.OrdinalIgnoreCase))
            {
                ctFilter = CategoryType.Model;
            }
            else if (string.Equals(categoryTypeFilter, "Annotation", StringComparison.OrdinalIgnoreCase))
            {
                ctFilter = CategoryType.Annotation;
            }
            else if (string.Equals(categoryTypeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                ctFilter = null; // no filter
            }
            else
            {
                ctFilter = CategoryType.Model;
            }

            // keepCategoryIds / hideCategoryIds は BuiltInCategory(int) ベース
            HashSet<int> keepIds = new HashSet<int>();
            if (p.TryGetValue("keepCategoryIds", out JToken keepTok) && keepTok is JArray keepArr)
            {
                foreach (JToken t in keepArr)
                {
                    try { keepIds.Add(t.Value<int>()); } catch { }
                }
            }

            HashSet<int> hideIds = new HashSet<int>();
            if (p.TryGetValue("hideCategoryIds", out JToken hideTok) && hideTok is JArray hideArr)
            {
                foreach (JToken t in hideArr)
                {
                    try { hideIds.Add(t.Value<int>()); } catch { }
                }
            }

            if (mode == "keep_only" && keepIds.Count == 0)
            {
                return new { ok = false, msg = "keep_only モードでは keepCategoryIds を指定してください。" };
            }
            if (mode == "hide_only" && hideIds.Count == 0)
            {
                return new { ok = false, msg = "hide_only モードでは hideCategoryIds を指定してください。" };
            }

            IList<Category> allCats = doc.Settings.Categories.Cast<Category>().ToList();

            int changed = 0;
            int skipped = 0;
            List<object> errors = new List<object>();

            using (Transaction tx = new Transaction(doc, "[MCP] Set Category Visibility (Bulk)"))
            {
                try
                {
                    tx.Start();

                    foreach (Category cat in allCats)
                    {
                        if (cat == null) continue;
                        try
                        {
                            // タイプフィルタ
                            if (ctFilter.HasValue && cat.CategoryType != ctFilter.Value)
                            {
                                continue;
                            }

                            // ビューで非表示可能か
                            bool canHide = false;
                            try
                            {
                                canHide = view.CanCategoryBeHidden(cat.Id);
                            }
                            catch
                            {
                                // some categories throw; treat as cannot hide
                                canHide = false;
                            }
                            if (!canHide)
                            {
                                skipped++;
                                continue;
                            }

                            int bicInt = cat.Id.IntValue();
                            bool targetVisible;

                            if (mode == "keep_only")
                            {
                                // keepCategoryIds に含まれるものだけ非表示解除、それ以外を非表示
                                bool shouldKeep = keepIds.Contains(bicInt);
                                targetVisible = shouldKeep;
                            }
                            else
                            {
                                // hide_only: hideCategoryIds に含まれるものだけ非表示
                                bool shouldHide = hideIds.Contains(bicInt);
                                targetVisible = !shouldHide;
                            }

                            bool currentHidden = view.GetCategoryHidden(cat.Id);
                            bool currentVisible = !currentHidden;

                            if (currentVisible != targetVisible)
                            {
                                view.SetCategoryHidden(cat.Id, !targetVisible);
                                changed++;
                            }
                        }
                        catch (Exception exCat)
                        {
                            skipped++;
                            errors.Add(new { categoryId = cat.Id.IntValue(), name = cat.Name, error = exCat.Message });
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { }
                    return new { ok = false, msg = ex.Message };
                }
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                mode,
                categoryType = categoryTypeFilter,
                changed,
                skipped,
                errors
            };
        }
    }
}



