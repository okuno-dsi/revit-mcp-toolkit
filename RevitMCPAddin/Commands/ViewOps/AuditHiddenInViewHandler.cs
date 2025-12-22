// ============================================================================
// File   : Commands/ViewOps/AuditHiddenInViewHandler.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: 「非表示要素の一時表示（電球ボタン）」相当の監査を最小構成で返す。
//          再表示（unhide）は本コマンドでは行わない → 既存の unhide_elements_in_view を利用。
// Notes  : 共通ユーティリティ Core/Common/* を使用して極小化。
// ============================================================================
#nullable disable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Common; // CommandCommonOptions / ViewScopeUtil / ElementFilterUtil / ResultShaper
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class AuditHiddenInViewHandler : IRevitCommandHandler
    {
        public string CommandName => "audit_hidden_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return Fail("NO_ACTIVE_DOC", "アクティブドキュメントがありません。");

                var p = cmd.Params as JObject ?? new JObject();

                // ---- View 解決 ----
                View view = null; int viewId = 0;
                if (TryGetInt(p, "viewId", out var vId) && vId > 0)
                {
                    view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vId)) as View;
                    viewId = vId;
                }
                else
                {
                    view = doc.ActiveView;
                    viewId = view?.Id?.IntValue() ?? 0;
                }
                if (view == null) return Fail("NOT_FOUND_VIEW", "ビューが見つかりません。");

                // ---- 共通オプション ----
                var (filter, shape) = CommandCommonOptions.Read(p);
                bool includeCategoryStates = p.Value<bool?>("includeCategoryStates") ?? false;

                // ViewType フィルタ（指定時）
                if (!ElementFilterUtil.PassesViewType(view, filter.ViewTypeFilter))
                    return ResultShaper.ShapeAndReturn(viewId, TemplateApplied(view), new JArray(), 0, 0, includeCategoryStates, BuildCategoryStates(view, includeCategoryStates), shape);

                // ビューの実スコープ
                var viewBox = ViewScopeUtil.TryGetViewScopeBox(view);

                // 欲しい種別（既定: explicit のみ）
                bool wantExplicit = filter.IncludeKinds == null || filter.IncludeKinds.Contains("explicit", StringComparer.OrdinalIgnoreCase);
                bool wantCategory = filter.IncludeKinds != null && filter.IncludeKinds.Contains("category", StringComparer.OrdinalIgnoreCase);

                // 収集（最小）：全要素1パスで判定、rowsに積む
                var rows = new List<JObject>();
                int cntExplicit = 0, cntCategory = 0;

                // 重複防止（カウント用）
                var seenExplicit = new HashSet<int>();
                var seenCategory = new HashSet<int>();

                foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    // カテゴリ/クラス/レベル/インポート除外
                    if (!ElementFilterUtil.PassesCategoryFilter(e, view, filter.ModelOnly, filter.ExcludeImports, filter.IncludeCategoryIds, filter.ExcludeCategoryIds)) continue;
                    if (!ElementFilterUtil.PassesClassFilter(e, filter.IncludeClasses, filter.ExcludeClasses)) continue;
                    if (!ElementFilterUtil.PassesLevelFilter(e, filter.IncludeLevelIds)) continue;

                    // ビュースコープ交差
                    if (!ViewScopeUtil.IntersectsViewScope(e, view, viewBox)) continue;

                    // 要素レベル（explicit）
                    if (wantExplicit)
                    {
                        bool elemHidden = false; try { elemHidden = e.IsHidden(view); } catch { }
                        if (elemHidden && ReasonAllowed(filter.ReasonFilter, "hidden_in_view"))
                        {
                            int id = e.Id.IntValue();
                            if (seenExplicit.Add(id))
                            {
                                rows.Add(BuildRow(e, "hidden_in_view", TemplateApplied(view)));
                                cntExplicit++;
                            }
                            continue; // explicit が取れたら category 判定は不要
                        }
                    }

                    // カテゴリ非表示（category）
                    if (wantCategory && filter.OnlyRevealables)
                    {
                        var cat = e.Category;
                        if (cat != null)
                        {
                            bool catHidden = false; try { catHidden = view.GetCategoryHidden(cat.Id); } catch { }
                            if (catHidden && ReasonAllowed(filter.ReasonFilter, "category_hidden"))
                            {
                                int id = e.Id.IntValue();
                                if (seenCategory.Add(id))
                                {
                                    // explicit で既に rows に載っていれば ResultShaper の dedupe に任せて二重登録OK。
                                    rows.Add(BuildRow(e, "category_hidden", TemplateApplied(view)));
                                    cntCategory++;
                                }
                            }
                        }
                    }
                }

                // 整形して返す（idsOnly / countsOnly / paging / saveToFile など）
                var catStates = BuildCategoryStates(view, includeCategoryStates);
                return ResultShaper.ShapeAndReturn(
                    viewId,
                    TemplateApplied(view),
                    new JArray(rows),
                    cntExplicit,
                    cntCategory,
                    includeCategoryStates,
                    catStates,
                    shape
                );
            }
            catch (Exception ex)
            {
                return Fail("EXCEPTION", "audit_hidden_in_view 実行中に例外: " + ex.Message);
            }
        }

        // ---- JSON行の構築（最小） ----
        private static JObject BuildRow(Element e, string reason, bool templateApplied)
        {
            int catId = 0; string catName = null;
            try { var c = e.Category; if (c != null) { catId = c.Id.IntValue(); catName = c.Name; } } catch { }

            bool canUnhideNow = (reason == "hidden_in_view" && !templateApplied);
            string suggestedFix = (reason == "hidden_in_view") ? "unhide_element" : "set_category_visibility:true";

            return new JObject
            {
                ["elementId"] = e.Id.IntValue(),
                ["uniqueId"] = e.UniqueId != null ? (JToken)new JValue(e.UniqueId) : JValue.CreateNull(),
                ["categoryId"] = (catId != 0) ? (JToken)new JValue(catId) : JValue.CreateNull(),
                ["categoryName"] = (catName != null) ? (JToken)new JValue(catName) : JValue.CreateNull(),
                ["reason"] = reason,
                ["canUnhideNow"] = canUnhideNow,
                ["suggestedFix"] = suggestedFix
            };
        }

        private static bool TemplateApplied(View v)
            => (v?.ViewTemplateId != null && v.ViewTemplateId != ElementId.InvalidElementId);

        private static bool ReasonAllowed(HashSet<string> reasonFilter, string reason)
            => (reasonFilter == null || reasonFilter.Count == 0 || reasonFilter.Contains(reason, StringComparer.OrdinalIgnoreCase));

        private static JArray BuildCategoryStates(View view, bool include)
        {
            if (!include) return null;
            var arr = new JArray();
            foreach (Category c in view.Document.Settings.Categories)
            {
                if (c == null) continue;
                bool isHidden; try { isHidden = view.GetCategoryHidden(c.Id); } catch { continue; }
                arr.Add(new JObject { ["categoryId"] = c.Id.IntValue(), ["name"] = c.Name, ["visible"] = !isHidden });
            }
            return arr;
        }

        // ---- 汎用（最小） ----
        private static object Fail(string code, string msg)
            => new JObject { ["ok"] = false, ["code"] = code, ["msg"] = msg };

        private static bool TryGetInt(JObject obj, string name, out int value)
        {
            value = 0; if (obj == null) return false;
            var t = obj[name]; if (t == null) return false;
            if (t.Type == JTokenType.Integer) { value = t.Value<int>(); return true; }
            if (t.Type == JTokenType.String && int.TryParse(t.Value<string>(), out value)) return true;
            return false;
        }
    }
}


