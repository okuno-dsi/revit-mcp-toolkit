// RevitMCPAddin/Commands/ElementOps/ElementQueryCommands.cs
// Implements:
//  - element.search_elements
//  - element.query_elements
#nullable enable
using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Models;

namespace RevitMCPAddin.Commands.ElementOps
{
    [RpcCommand("element.search_elements",
        Category = "ElementOps/Query",
        Tags = new[] { "ElementOps", "Query" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Summary = "Find elements by a forgiving keyword search (optionally scoped by category/view/level).",
        Requires = new[] { "keyword" },
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"element.search_elements\", \"params\":{ \"keyword\":\"door\", \"categories\":[\"Doors\",\"Windows\"], \"viewId\":12345, \"levelId\":67890, \"includeTypes\":false, \"caseSensitive\":false, \"maxResults\":50 } }")]
    public sealed class SearchElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "element.search_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = cmd?.Params as JObject ?? new JObject();
                var req = p.ToObject<SearchElementsRequest>() ?? new SearchElementsRequest();

                // Back-compat for alternate keys (best-effort).
                if (string.IsNullOrWhiteSpace(req.Keyword))
                    req.Keyword = (p.Value<string>("q") ?? p.Value<string>("text") ?? p.Value<string>("name") ?? "").Trim();

                if (req.Categories == null) req.Categories = new System.Collections.Generic.List<string>();
                if (req.Categories.Count == 0 && p["category"] != null)
                {
                    var one = (p.Value<string>("category") ?? "").Trim();
                    if (one.Length > 0) req.Categories.Add(one);
                }

                string jobId = "";
                try { jobId = cmd?.Id != null ? cmd.Id.ToString() : ""; } catch { jobId = ""; }
                return ElementQueryService.SearchElements(doc, req, jobId);
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "INTERNAL_ERROR", msg = ex.Message };
            }
        }
    }

    [RpcCommand("element.query_elements",
        Category = "ElementOps/Query",
        Tags = new[] { "ElementOps", "Query" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Summary = "Query elements using structured filters (categories, classNames, levelId, name, bbox, parameters).",
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":2, \"method\":\"element.query_elements\", \"params\":{ \"scope\":{ \"viewId\":12345, \"includeHiddenInView\":false }, \"filters\":{ \"categories\":[\"Walls\",\"Floors\"], \"classNames\":[\"Wall\",\"Floor\"], \"levelId\":67890, \"name\":{ \"mode\":\"contains\", \"value\":\"Basic Wall\", \"caseSensitive\":false }, \"bbox\":{ \"min\":{ \"x\":0, \"y\":0, \"z\":0, \"unit\":\"mm\" }, \"max\":{ \"x\":10000, \"y\":10000, \"z\":3000, \"unit\":\"mm\" }, \"mode\":\"intersects\" }, \"parameters\":[ { \"param\":{ \"kind\":\"name\", \"value\":\"Mark\" }, \"op\":\"contains\", \"value\":\"A-\", \"caseSensitive\":false } ] }, \"options\":{ \"includeElementType\":true, \"includeBoundingBox\":false, \"includeParameters\":[\"Mark\"], \"maxResults\":200, \"orderBy\":\"id\" } } }")]
    public sealed class QueryElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "element.query_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = cmd?.Params as JObject ?? new JObject();
                var req = p.ToObject<QueryElementsRequest>() ?? new QueryElementsRequest();

                string jobId = "";
                try { jobId = cmd?.Id != null ? cmd.Id.ToString() : ""; } catch { jobId = ""; }
                return ElementQueryService.QueryElements(doc, req, jobId);
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "INTERNAL_ERROR", msg = ex.Message };
            }
        }
    }
}
