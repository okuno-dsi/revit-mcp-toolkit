// ============================================================================
// File   : Core/Common/CommandCommonOptions.cs
// Purpose: すべてのコマンドで使える共通パラメータの読み取り（_filter / _shape）
// Notes  : 互換のため includeKinds/idsOnly/countsOnly/page/saveToFile 等の旧キーも読む
// ============================================================================
#nullable disable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Common
{
    public sealed class FilterOptions
    {
        // ビュー系
        public string ViewTypeFilter;                // "Section" など
        public bool ModelOnly = true;                // 注釈/凡例を除外
        public bool ExcludeImports = true;           // DWG/DXF 等の ImportInstance を除外
        public HashSet<int> IncludeCategoryIds;      // null → 無指定
        public HashSet<int> ExcludeCategoryIds;
        public HashSet<string> IncludeClasses;       // 要素クラス名（"Wall","FamilyInstance" など）
        public HashSet<string> ExcludeClasses;       // 既定 "ImportInstance" 相当
        public HashSet<int> IncludeLevelIds;         // LEVEL_PARAM による軽フィルタ
        public HashSet<string> ReasonFilter;         // "hidden_in_view", "category_hidden"
        public HashSet<string> IncludeKinds;         // "explicit","category","temporary"
        public bool OnlyRevealables = true;          // 電球相当（explicit/category）
    }

    public sealed class ShapeOptions
    {
        public int PageLimit = 10000;
        public int PageOffset = 0;
        public bool IdsOnly = false;
        public bool CountsOnly = false;
        public bool SummaryOnly = false;
        public bool Dedupe = true;
        public bool SaveToFile = false;
    }

    public static class CommandCommonOptions
    {
        public static (FilterOptions filter, ShapeOptions shape) Read(JObject p)
        {
            var f = new FilterOptions();
            var s = new ShapeOptions();

            var pf = p["_filter"] as JObject;  // 推奨：新スキーマ
            var ps = p["_shape"] as JObject;

            // ------- Filter -------
            f.ViewTypeFilter = pf?.Value<string>("viewTypeFilter") ?? p.Value<string>("viewTypeFilter");
            f.ModelOnly = GetBool(pf, "modelOnly", defaultVal: f.ModelOnly, fallbackParent: p, fbName: "modelOnly");
            f.ExcludeImports = GetBool(pf, "excludeImports", defaultVal: f.ExcludeImports, fallbackParent: p, fbName: "skipImports");

            f.IncludeCategoryIds = ToIntSet(ReadIntArray(pf, "includeCategoryIds") ?? ReadIntArray(p, "includeCategoryIds"));
            f.ExcludeCategoryIds = ToIntSet(ReadIntArray(pf, "excludeCategoryIds") ?? ReadIntArray(p, "excludeCategoryIds"));
            f.IncludeClasses = ToStrSet(ReadStrArray(pf, "includeClasses"));
            f.ExcludeClasses = ToStrSet(ReadStrArray(pf, "excludeClasses"));

            f.IncludeLevelIds = ToIntSet(ReadIntArray(p, "includeLevelIds") ?? ReadIntArray(p["_filter"] as JObject, "includeLevelIds"));

            var rf = ReadStrArray(pf, "reasonFilter") ?? ReadStrArray(p, "reasonFilter");
            f.ReasonFilter = ToStrSet(rf);

            var kinds = ReadStrArray(pf, "includeKinds") ?? ReadStrArray(p, "includeKinds");
            f.IncludeKinds = ToStrSet(kinds ?? new[] { "explicit" });

            f.OnlyRevealables = GetBool(pf, "onlyRevealables", defaultVal: f.OnlyRevealables, fallbackParent: p, fbName: "onlyRevealables");

            // ------- Shape -------
            var pageObj = ps?["page"] as JObject ?? p["page"] as JObject;
            s.PageLimit = pageObj != null ? GetInt(pageObj, "limit", 10000) : GetInt(ps, "limit", GetInt(p, "limit", 10000));
            s.PageOffset = pageObj != null ? GetInt(pageObj, "offset", 0) : GetInt(ps, "offset", GetInt(p, "offset", 0));

            s.IdsOnly = GetBool(ps, "idsOnly", defaultVal: false, fallbackParent: p, fbName: "idsOnly");
            s.CountsOnly = GetBool(ps, "countsOnly", defaultVal: false, fallbackParent: p, fbName: "countsOnly");
            s.SummaryOnly = GetBool(ps, "summaryOnly", defaultVal: false, fallbackParent: p, fbName: "summaryOnly");
            s.Dedupe = GetBool(ps, "dedupe", defaultVal: true, fallbackParent: p, fbName: "dedupe");
            s.SaveToFile = GetBool(ps, "saveToFile", defaultVal: false, fallbackParent: p, fbName: "saveToFile");

            return (f, s);
        }

        // ---- helpers ----
        private static bool GetBool(JObject o, string name, bool defaultVal, JObject fallbackParent = null, string fbName = null)
        {
            if (o != null && o.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var tok))
            {
                if (tok.Type == JTokenType.Boolean) return tok.Value<bool>();
                if (tok.Type == JTokenType.Integer) return tok.Value<int>() != 0;
                if (tok.Type == JTokenType.String && bool.TryParse(tok.Value<string>(), out var b)) return b;
            }
            if (fallbackParent != null && !string.IsNullOrEmpty(fbName))
            {
                var t = fallbackParent[fbName];
                if (t != null)
                {
                    if (t.Type == JTokenType.Boolean) return t.Value<bool>();
                    if (t.Type == JTokenType.Integer) return t.Value<int>() != 0;
                    if (t.Type == JTokenType.String && bool.TryParse(t.Value<string>(), out var b2)) return b2;
                }
            }
            return defaultVal;
        }

        private static int GetInt(JObject o, string name, int def)
        {
            if (o == null) return def;
            var t = o[name];
            if (t == null) return def;
            if (t.Type == JTokenType.Integer) return t.Value<int>();
            if (t.Type == JTokenType.String && int.TryParse(t.Value<string>(), out var v)) return v;
            return def;
        }

        private static string[] ReadStrArray(JObject o, string name)
        {
            if (o == null) return null;
            var a = o[name] as JArray; if (a == null) return null;
            var list = new List<string>();
            foreach (var t in a) if (t.Type == JTokenType.String) list.Add(t.Value<string>());
            return list.ToArray();
        }

        private static int[] ReadIntArray(JObject o, string name)
        {
            if (o == null) return null;
            var a = o[name] as JArray; if (a == null) return null;
            var list = new List<int>();
            foreach (var t in a) if (t.Type == JTokenType.Integer) list.Add(t.Value<int>());
            return list.ToArray();
        }

        private static HashSet<int> ToIntSet(int[] arr) => (arr != null && arr.Length > 0) ? new HashSet<int>(arr) : null;
        private static HashSet<string> ToStrSet(string[] arr)
            => (arr != null && arr.Length > 0) ? new HashSet<string>(arr, StringComparer.OrdinalIgnoreCase) : null;
    }
}
