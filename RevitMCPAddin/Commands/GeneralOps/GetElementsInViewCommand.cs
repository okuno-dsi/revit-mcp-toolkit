using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.GeneralOps
{
    /// <summary>
    /// Returns elements visible in the given view, with Level and Category info.
    /// Default output: flat rows [{ levelName, levelId, categoryName, categoryId, elementId }]
    /// Optional grouped output when params.grouped == true:
    /// {
    ///   ok, totalCount,
    ///   levels: [{ levelId, levelName, categories: [{ categoryId, categoryName, elementIds: [..] }] }]
    /// }
    /// </summary>
    public class GetElementsInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_elements_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();

            bool usedActiveView = false;
            View view = null;
            int viewId = 0;
            if (p.TryGetValue("viewId", out var viewToken) && viewToken.Type != JTokenType.Null)
            {
                viewId = viewToken.Value<int>();
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
                if (view == null)
                    return new { ok = false, msg = $"View not found: {viewId}" };
            }
            else
            {
                view = doc.ActiveView;
                if (view == null)
                    return new { ok = false, msg = "Missing parameter: viewId (no active view available)" };
                viewId = view.Id.IntValue();
                usedActiveView = true;
            }

            bool grouped = p.TryGetValue("grouped", out var gTok) && gTok.Type == JTokenType.Boolean && gTok.Value<bool>();

            // shape / options
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = page?.Value<int?>("limit")
                        ?? p.Value<int?>("count")
                        ?? p.Value<int?>("limit")
                        ?? int.MaxValue;
            int skip = page?.Value<int?>("skip")
                       ?? page?.Value<int?>("offset")
                       ?? p.Value<int?>("skip")
                       ?? p.Value<int?>("offset")
                       ?? 0;

            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeIndependentTags = p.Value<bool?>("includeIndependentTags") ?? true;
            bool includeElementTypes = p.Value<bool?>("includeElementTypes") ?? false;

            // optional filters
            var categoryIdsFilter = (p["categoryIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
            var pf = p["_filter"] as JObject;
            // include/exclude category ids from _filter
            var includeCategoryIdsPf = (pf?["includeCategoryIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
            var excludeCategoryIdsFilter = (pf?["excludeCategoryIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
            foreach (var x in includeCategoryIdsPf) categoryIdsFilter.Add(x);
            var categoryNamesRaw = (p["categoryNames"] as JArray)?.Values<string>()?.Where(s => !string.IsNullOrWhiteSpace(s))?.ToList()
                                   ?? new List<string>();
            var categoryNamesFilter = new HashSet<string>(categoryNamesRaw, StringComparer.OrdinalIgnoreCase);
            var unresolvedCategoryNamesFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawName in categoryNamesRaw)
            {
                if (CategoryResolver.TryResolveCategory(rawName, out var bic))
                    categoryIdsFilter.Add((int)bic);
                else
                    unresolvedCategoryNamesFilter.Add(rawName);
            }
            string categoryNameContains = p.Value<string>("categoryNameContains");
            string familyNameContains = p.Value<string>("familyNameContains");
            string typeNameContains = p.Value<string>("typeNameContains");
            int? filterLevelId = p.Value<int?>("levelId");
            string filterLevelName = p.Value<string>("levelName");
            var onlyElementIds = (p["elementIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();

            // filter toggles in _filter
            bool modelOnly = pf?.Value<bool?>("modelOnly") ?? p.Value<bool?>("modelOnly") ?? false;
            bool excludeImports = pf?.Value<bool?>("excludeImports") ?? p.Value<bool?>("excludeImports") ?? true;
            var includeClasses = (pf?["includeClasses"] as JArray)?.Values<string>()?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excludeClasses = (pf?["excludeClasses"] as JArray)?.Values<string>()?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var includeLevelIds = (pf?["includeLevelIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();

            // 収集（可能ならカテゴリで事前に絞り込み、列挙コストを削減）
            var collector = new FilteredElementCollector(doc, view.Id);
            if (!includeElementTypes)
                collector = collector.WhereElementIsNotElementType();

            // 集合: 直接指定 categoryIds + _filter.includeCategoryIds の和集合
            var includeCatsAll = new HashSet<int>(categoryIdsFilter);
            foreach (var x in includeCategoryIdsPf) includeCatsAll.Add(x);
            bool usedCategoryFilter = false;
            if (includeCatsAll.Count > 0)
            {
                try
                {
                    var catFilters = includeCatsAll
                        .Select(i => new ElementCategoryFilter(Autodesk.Revit.DB.ElementIdCompat.From(i)))
                        .Cast<ElementFilter>()
                        .ToList();
                    if (catFilters.Count > 0)
                    {
                        var catOr = (catFilters.Count == 1) ? catFilters[0] : (ElementFilter)new LogicalOrFilter(catFilters);
                        collector = collector.WherePasses(catOr);
                        usedCategoryFilter = true;
                    }
                }
                catch { /* fallback to unfiltered when category filter construction fails */ }
            }

            // キャッシュ：LevelId -> (Name), CategoryId -> (Name)
            var levelNameById = new Dictionary<int, string>();
            var categoryNameById = new Dictionary<int, string>();

            // レベル名解決用のローカル関数（複数手段で取得を試みる）
            (int? levelId, string levelName) ResolveLevel(Element e)
            {
                // 1) Element.LevelId（Revit 2021+ 多くの要素で有効）
                try
                {
                    var lid = e.LevelId;
                    if (lid != null && lid != ElementId.InvalidElementId)
                    {
                        int k = lid.IntValue();
                        if (!levelNameById.TryGetValue(k, out var nm))
                        {
                            var lv = doc.GetElement(lid) as Level;
                            nm = lv?.Name ?? null;
                            levelNameById[k] = nm;
                        }
                        return (k, levelNameById[k]);
                    }
                }
                catch { /* 一部クラスでは LevelId 未実装のことがある */ }

                // 2) よくある組み込みパラメータを順に当てる
                ElementId TryParam(params BuiltInParameter[] bips)
                {
                    foreach (var bip in bips)
                    {
                        var pr = e.get_Parameter(bip);
                        if (pr != null && pr.StorageType == StorageType.ElementId)
                        {
                            var id = pr.AsElementId();
                            if (id != null && id != ElementId.InvalidElementId) return id;
                        }
                    }
                    return null;
                }

                // LEVEL_PARAM / SCHEDULE_LEVEL_PARAM / WALL_BASE_CONSTRAINT / STAIRS_BASE_LEVEL 等
                var tryIds = TryParam(
                    BuiltInParameter.LEVEL_PARAM,
                    BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                    BuiltInParameter.WALL_BASE_CONSTRAINT,
                    BuiltInParameter.STAIRS_BASE_LEVEL
                );

                if (tryIds != null)
                {
                    int k = tryIds.IntValue();
                    if (!levelNameById.TryGetValue(k, out var nm))
                    {
                        var lv = doc.GetElement(tryIds) as Level;
                        nm = lv?.Name ?? null;
                        levelNameById[k] = nm;
                    }
                    return (k, levelNameById[k]);
                }

                // 3) それでも取れない場合（Level未所属・ビュー依存等）は null
                return (null, null);
            }

            string ResolveCategoryName(Category cat)
            {
                if (cat == null) return null;
                int k = cat.Id.IntValue();
                if (!categoryNameById.TryGetValue(k, out var nm))
                {
                    nm = cat.Name;
                    categoryNameById[k] = nm;
                }
                return categoryNameById[k];
            }

            // 実データ組み立て（まず全要素を列挙し、必要に応じて整形）
            var allElems = collector.ToElements()
                                     .Where(e => includeIndependentTags || !(e is IndependentTag))
                                     .ToList();
            if (usedCategoryFilter && allElems.Count == 0)
            {
                // Fallback: category filter can be too strict for some views/locales.
                var fallback = new FilteredElementCollector(doc, view.Id);
                if (!includeElementTypes)
                    fallback = fallback.WhereElementIsNotElementType();
                allElems = fallback.ToElements()
                                   .Where(e => includeIndependentTags || !(e is IndependentTag))
                                   .ToList();
            }
            // helpers (family/type names)
            string ResolveFamilyName(Element e)
            {
                try
                {
                    if (e is FamilyInstance fi) return fi.Symbol?.Family?.Name;
                    var et = doc.GetElement(e.GetTypeId()) as ElementType; return et?.FamilyName;
                }
                catch { return null; }
            }

            string ResolveTypeName(Element e)
            {
                try
                {
                    if (e is FamilyInstance fi) return fi.Symbol?.Name;
                    var et = doc.GetElement(e.GetTypeId()) as ElementType; return et?.Name;
                }
                catch { return null; }
            }

            bool PassFilters(Element e, int? lvId, string lvName, int? catId, string catName)
            {
                int eid = e.Id.IntValue();
                if (onlyElementIds.Count > 0 && !onlyElementIds.Contains(eid)) return false;
                if (filterLevelId.HasValue && (!lvId.HasValue || lvId.Value != filterLevelId.Value)) return false;
                // model/annotation partition
                try
                {
                    if (modelOnly)
                    {
                        var c = e.Category; var ct = c != null ? c.CategoryType : CategoryType.Model;
                        if (ct != CategoryType.Model) return false;
                    }
                    if (excludeImports && e is ImportInstance) return false;
                }
                catch { /* ignore */ }
                if (!string.IsNullOrWhiteSpace(filterLevelName))
                {
                    if (string.IsNullOrWhiteSpace(lvName) || !lvName.Equals(filterLevelName, StringComparison.OrdinalIgnoreCase)) return false;
                }
                if (categoryIdsFilter.Count > 0)
                {
                    if (!catId.HasValue || !categoryIdsFilter.Contains(catId.Value)) return false;
                }
                if (excludeCategoryIdsFilter.Count > 0)
                {
                    if (catId.HasValue && excludeCategoryIdsFilter.Contains(catId.Value)) return false;
                }
                if (unresolvedCategoryNamesFilter.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(catName) || !unresolvedCategoryNamesFilter.Contains(catName)) return false;
                }
                if (!string.IsNullOrWhiteSpace(categoryNameContains))
                {
                    if (string.IsNullOrWhiteSpace(catName) || catName.IndexOf(categoryNameContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
                }
                if (!string.IsNullOrWhiteSpace(familyNameContains))
                {
                    var fn = ResolveFamilyName(e) ?? string.Empty;
                    if (fn.IndexOf(familyNameContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
                }
                if (!string.IsNullOrWhiteSpace(typeNameContains))
                {
                    var tn = ResolveTypeName(e) ?? string.Empty;
                    if (tn.IndexOf(typeNameContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
                }
                // include/exclude class names
                if (includeClasses.Count > 0)
                {
                    var tn = e.GetType().Name;
                    if (!includeClasses.Contains(tn)) return false;
                }
                if (excludeClasses.Count > 0)
                {
                    var tn = e.GetType().Name;
                    if (excludeClasses.Contains(tn)) return false;
                }
                if (includeLevelIds.Count > 0)
                {
                    if (!lvId.HasValue || !includeLevelIds.Contains(lvId.Value)) return false;
                }
                return true;
            }

            int totalCount = allElems.Count;

            if (idsOnly)
            {
                var idList = new List<int>();
                int skipped = 0;
                foreach (var e in allElems)
                {
                    var (lvIdF, lvNameF) = ResolveLevel(e);
                    var catF = e.Category; int? catIdF = (catF != null) ? (int?)catF.Id.IntValue() : null; string catNameF = ResolveCategoryName(catF);
                    if (!PassFilters(e, lvIdF, lvNameF, catIdF, catNameF)) continue;
                    if (skipped < skip) { skipped++; continue; }
                    idList.Add(e.Id.IntValue());
                    if (idList.Count >= limit) break;
                }
                return new { ok = true, totalCount, elementIds = idList, viewId, usedActiveView };
            }

            // rows（要約）を構築
            var rowsAll = new List<object>(totalCount);
            foreach (var e in allElems)
            {
                var (lvId, lvName) = ResolveLevel(e);
                var cat = e.Category;
                int? categoryId = (cat != null) ? (int?)cat.Id.IntValue() : null;
                string categoryName = ResolveCategoryName(cat);
                if (!PassFilters(e, lvId, lvName, categoryId, categoryName)) continue;
                rowsAll.Add(new { levelName = lvName, levelId = lvId, categoryName, categoryId, elementId = e.Id.IntValue() });
            }

            var rows = rowsAll.Skip(skip).Take(limit).ToList();

            if (!grouped || summaryOnly)
                return new { ok = true, totalCount, rows, items = rows, viewId, usedActiveView };

            // グルーピング出力（レベル→カテゴリ→elementIds）
            var groupedLevels = rows
                .GroupBy(r => new
                {
                    levelId = (int?)r.GetType().GetProperty("levelId").GetValue(r, null),
                    levelName = (string)r.GetType().GetProperty("levelName").GetValue(r, null)
                })
                .OrderBy(g => g.Key.levelName ?? string.Empty)
                .Select(g =>
                {
                    var cats = g.GroupBy(r => new
                    {
                        categoryId = (int?)r.GetType().GetProperty("categoryId").GetValue(r, null),
                        categoryName = (string)r.GetType().GetProperty("categoryName").GetValue(r, null)
                    })
                    .OrderBy(cg => cg.Key.categoryName ?? string.Empty)
                    .Select(cg => new
                    {
                        categoryId = cg.Key.categoryId,
                        categoryName = cg.Key.categoryName,
                        elementIds = cg.Select(r => (int)r.GetType().GetProperty("elementId").GetValue(r, null)).ToList()
                    })
                    .ToList();

                    return new
                    {
                        levelId = g.Key.levelId,
                        levelName = g.Key.levelName,
                        categories = cats
                    };
                })
                .ToList();

            return new { ok = true, totalCount, levels = groupedLevels, viewId, usedActiveView };
        }
    }
}


