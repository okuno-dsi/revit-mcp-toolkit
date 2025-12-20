// ============================================================================
// File   : Core/Common/ElementFilterUtil.cs
// Purpose: 共通の要素フィルタ（カテゴリ/クラス/レベル/インポート/理由/ビュー種別）
// ============================================================================
#nullable disable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core.Common
{
    public static class ElementFilterUtil
    {
        public static bool IsImport(Element e) => (e is ImportInstance);

        public static bool PassesCategoryFilter(
            Element e,
            View view,
            bool modelOnly, bool excludeImports,
            HashSet<int> incCat, HashSet<int> excCat)
        {
            var cat = e.Category;
            if (cat == null) return false;
            if (excludeImports && IsImport(e)) return false;
            if (modelOnly && cat.CategoryType != CategoryType.Model) return false;

            int cid = cat.Id.IntegerValue;
            if (incCat != null && incCat.Count > 0 && !incCat.Contains(cid)) return false;
            if (excCat != null && excCat.Contains(cid)) return false;
            return true;
        }

        public static bool PassesClassFilter(Element e, HashSet<string> inc, HashSet<string> exc)
        {
            string cls = e.GetType().Name;
            if (exc != null && exc.Contains(cls)) return false;
            if (inc != null && inc.Count > 0 && !inc.Contains(cls)) return false;
            return true;
        }

        public static bool PassesLevelFilter(Element e, HashSet<int> incLevelIds)
        {
            if (incLevelIds == null || incLevelIds.Count == 0) return true;
            try
            {
                Parameter p = e.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    int lid = p.AsElementId().IntegerValue;
                    return incLevelIds.Contains(lid);
                }
            }
            catch { }
            return false; // レベル不明は除外
        }

        public static bool PassesViewType(View view, string viewTypeFilter)
        {
            if (string.IsNullOrEmpty(viewTypeFilter)) return true;
            return string.Equals(view.ViewType.ToString(), viewTypeFilter, StringComparison.OrdinalIgnoreCase);
        }
    }
}
