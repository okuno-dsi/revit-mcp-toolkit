// ============================================================================
// File   : Core/Common/ResultShaper.cs
// Purpose: 出力整形（idsOnly / countsOnly / summaryOnly / paging / dedupe / saveToFile）
// ============================================================================
#nullable disable
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Common
{
    public static class ResultShaper
    {
        public static object ShapeAndReturn(
            int viewId,
            bool templateApplied,
            JArray rows,                   // [{ elementId, ... }, ...]
            int countHiddenInView,
            int countCategoryHidden,
            bool includeCategoryStates,
            JArray categoryStates,
            ShapeOptions s)
        {
            // dedupe
            if (s.Dedupe)
            {
                var seen = new System.Collections.Generic.HashSet<int>();
                var dedup = new JArray();
                foreach (var row in rows)
                {
                    int id = row.Value<int?>("elementId") ?? 0;
                    if (id > 0 && seen.Add(id)) dedup.Add(row);
                }
                rows = dedup;
            }

            // summary
            var summary = new JObject
            {
                ["totalHidden"] = rows.Count,
                ["byReason"] = new JObject
                {
                    ["hidden_in_view"] = countHiddenInView,
                    ["category_hidden"] = countCategoryHidden
                }
            };

            if (s.CountsOnly || s.SummaryOnly)
            {
                var resLite = new JObject
                {
                    ["ok"] = true,
                    ["viewId"] = viewId,
                    ["templateApplied"] = templateApplied,
                    ["summary"] = summary
                };
                if (includeCategoryStates) resLite["categoryStates"] = categoryStates ?? new JArray();
                return resLite;
            }

            // paging
            var rowsPaged = rows;
            if (s.PageOffset > 0 || s.PageLimit < rowsPaged.Count)
                rowsPaged = new JArray(rowsPaged.Skip(s.PageOffset).Take(s.PageLimit));

            // idsOnly
            JToken hiddenOut = s.IdsOnly
                ? (JToken)new JArray(rowsPaged.Select(x => x.Value<int>("elementId")))
                : (JToken)rowsPaged;

            // saveToFile
            if (s.SaveToFile)
            {
                var baseDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RevitMCP", "data", "audit");
                System.IO.Directory.CreateDirectory(baseDir);
                var file = System.IO.Path.Combine(baseDir, $"audit_{viewId}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                var payload = new JObject
                {
                    ["ok"] = true,
                    ["viewId"] = viewId,
                    ["templateApplied"] = templateApplied,
                    ["summary"] = summary,
                    ["hiddenElements"] = hiddenOut,
                    ["inputUnits"] = new JObject { ["Length"] = "mm", ["Angle"] = "deg" },
                    ["internalUnits"] = new JObject { ["Length"] = "ft", ["Angle"] = "rad" }
                };
                if (includeCategoryStates) payload["categoryStates"] = categoryStates ?? new JArray();

                System.IO.File.WriteAllText(file, RevitMCPAddin.Core.JsonNetCompat.ToIndentedJson(payload));

                return new JObject
                {
                    ["ok"] = true,
                    ["viewId"] = viewId,
                    ["templateApplied"] = templateApplied,
                    ["summary"] = summary,
                    ["savedTo"] = file
                };
            }

            // 通常返却
            var result = new JObject
            {
                ["ok"] = true,
                ["viewId"] = viewId,
                ["templateApplied"] = templateApplied,
                ["summary"] = summary,
                ["hiddenElements"] = hiddenOut,
                ["inputUnits"] = new JObject { ["Length"] = "mm", ["Angle"] = "deg" },
                ["internalUnits"] = new JObject { ["Length"] = "ft", ["Angle"] = "rad" }
            };
            if (includeCategoryStates) result["categoryStates"] = categoryStates ?? new JArray();
            return result;
        }
    }
}
