#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.CompareOps
{
    /// <summary>
    /// validate_compare_context (multi-port):
    /// Validates that the specified projects (ports) refer to the same document and comparable views.
    /// Params:
    ///   - projects: [{ port:int, viewId?:int, viewName?:string }], up to 5
    ///   - requireSameProject?: bool = true
    ///   - requireSameViewType?: bool = true
    ///   - resolveByName?: bool = true
    /// Returns:
    ///   { ok, count, items:[{ port, document:{ guid,title,path }, view:{ id,name,viewType } }],
    ///     checks:{ allSameProject, allSameViewType }, issues:[...] }
    /// </summary>
    public class ValidateCompareContextCommand : IRevitCommandHandler
    {
        public string CommandName => "validate_compare_context";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (cmd.Params as JObject) ?? new JObject();
            var arr = p["projects"] as JArray;
            if (arr == null || arr.Count == 0)
            {
                // Back-compat: left/right
                var left = p["left"] as JObject; var right = p["right"] as JObject;
                arr = new JArray(); if (left != null) arr.Add(left); if (right != null) arr.Add(right);
            }
            if (arr == null || arr.Count == 0) return new { ok = false, code = "NO_PROJECTS", msg = "projects is required." };
            if (arr.Count > 5) return new { ok = false, code = "TOO_MANY", msg = "projects must be <= 5." };

            bool requireSameProject = p.Value<bool?>("requireSameProject") ?? true;
            bool requireSameViewType = p.Value<bool?>("requireSameViewType") ?? true;
            bool resolveByName = p.Value<bool?>("resolveByName") ?? true;

            var items = new List<Dictionary<string, object>>();
            var issues = new List<object>();

            foreach (var it in arr.OfType<JObject>())
            {
                try
                {
                    int port = it.Value<int?>("port") ?? 0;
                    if (port <= 0) { issues.Add(new { code = "BAD_PORT", msg = "port missing/invalid", item = it }); continue; }

                    // 1) Document meta via get_open_documents
                    var docs = CallRpcAndGetResult(port, "get_open_documents", new JObject());
                    var documents = (docs["documents"] as JArray) ?? new JArray();
                    JObject active = null;
                    foreach (var d in documents.OfType<JObject>())
                    {
                        if ((d.Value<bool?>("active") ?? false) == true) { active = d; break; }
                    }
                    if (active == null && documents.Count > 0) active = documents[0] as JObject;

                    string guid = active?.Value<string>("guid") ?? string.Empty;
                    string title = active?.Value<string>("title") ?? string.Empty;
                    string path = active?.Value<string>("path") ?? string.Empty;

                    // 2) Resolve view (viewId preferred, else by name)
                    int viewId = it.Value<int?>("viewId") ?? 0;
                    string viewName = it.Value<string>("viewName") ?? string.Empty;
                    string viewType = string.Empty;
                    string resolvedName = string.Empty;
                    if (viewId > 0)
                    {
                        // try to find by id
                        var gv = CallRpcAndGetResult(port, "get_views", new JObject { ["includeTemplates"] = false, ["detail"] = true });
                        var vs = (gv["views"] as JArray) ?? new JArray();
                        foreach (var v in vs.OfType<JObject>())
                        {
                            if ((v.Value<int?>("viewId") ?? 0) == viewId)
                            {
                                resolvedName = v.Value<string>("name") ?? string.Empty;
                                viewType = v.Value<string>("viewType") ?? string.Empty; break;
                            }
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(viewName) && resolveByName)
                    {
                        var gv = CallRpcAndGetResult(port, "get_views", new JObject { ["includeTemplates"] = false, ["detail"] = true });
                        var vs = (gv["views"] as JArray) ?? new JArray();
                        string target = NormalizeName(viewName);
                        JObject hit = null;
                        foreach (var v in vs.OfType<JObject>())
                        {
                            var nm = NormalizeName(v.Value<string>("name") ?? string.Empty);
                            if (nm == target) { hit = v; break; }
                        }
                        if (hit == null)
                        {
                            foreach (var v in vs.OfType<JObject>())
                            {
                                var nm = NormalizeName(v.Value<string>("name") ?? string.Empty);
                                if (nm.Contains(target)) { hit = v; break; }
                            }
                        }
                        if (hit != null)
                        {
                            viewId = hit.Value<int?>("viewId") ?? 0;
                            resolvedName = hit.Value<string>("name") ?? string.Empty;
                            viewType = hit.Value<string>("viewType") ?? string.Empty;
                        }
                        else
                        {
                            issues.Add(new { code = "VIEW_NOT_FOUND", msg = $"viewName '{viewName}' not found on port {port}" });
                        }
                    }

                    var entry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["port"] = port,
                        ["document"] = new { guid, title, path },
                        ["view"] = new { id = viewId, name = string.IsNullOrEmpty(resolvedName) ? viewName : resolvedName, viewType }
                    };
                    items.Add(entry);
                }
                catch (Exception ex)
                {
                    issues.Add(new { code = "EX", msg = ex.Message, item = it });
                }
            }

            bool allSameProject = false, allSameViewType = false;
            if (items.Count > 0)
            {
                // Same project by guid (non-empty) else normalized title
                var guids = items.Select(i => (((dynamic)i["document"]).guid as string) ?? string.Empty).ToList();
                if (guids.All(g => !string.IsNullOrEmpty(g)))
                    allSameProject = guids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                else
                {
                    var titles = items.Select(i => NormalizeName((((dynamic)i["document"]).title as string) ?? string.Empty)).ToList();
                    allSameProject = titles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                }

                var vtypes = items.Select(i => (((dynamic)i["view"]).viewType as string) ?? string.Empty).ToList();
                allSameViewType = vtypes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
            }

            if (requireSameProject && !allSameProject)
                issues.Add(new { code = "MISMATCHED_PROJECT", msg = "Documents differ across ports." });
            if (requireSameViewType && !allSameViewType)
                issues.Add(new { code = "MISMATCHED_VIEWTYPE", msg = "ViewType differs across ports." });

            bool ok = (!requireSameProject || allSameProject) && (!requireSameViewType || allSameViewType) && items.Count >= 2;
            return new
            {
                ok,
                count = items.Count,
                items,
                checks = new { allSameProject, allSameViewType },
                issues = issues
            };
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string n = s.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
            n = n.Replace('\u3000', ' ');
            var sb = new System.Text.StringBuilder(n.Length);
            foreach (var ch in n) if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            return sb.ToString();
        }

        private static JObject CallRpcAndGetResult(int port, string method, JObject @params)
        {
            var payload = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = @params ?? new JObject(),
                ["id"] = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                var baseUri = new Uri($"http://localhost:{port}/");
                var enqueue = new Uri(baseUri, "enqueue");
                var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                var resp = client.PostAsync(enqueue, content).GetAwaiter().GetResult();
                var txt = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var jo = JObject.Parse(txt);

                // direct result path
                if (jo["result"] != null && jo["result"].Type != JTokenType.Null)
                {
                    var r = jo["result"] as JObject;
                    // Some implementations wrap under result.result
                    if (r?["result"] is JObject rr) return rr;
                    return r ?? new JObject();
                }

                // legacy ok shape
                if (jo["ok"] != null) return jo as JObject;

                // job path
                string jobId = jo.Value<string>("jobId") ?? jo.Value<string>("job_id") ?? string.Empty;
                if (!string.IsNullOrEmpty(jobId))
                {
                    var job = new Uri(baseUri, $"job/{jobId}");
                    var start = DateTime.UtcNow;
                    while ((DateTime.UtcNow - start).TotalSeconds < 60)
                    {
                        var jr = client.GetAsync(job).GetAwaiter().GetResult();
                        var jtxt = jr.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        var jrow = JObject.Parse(jtxt);
                        var state = jrow.Value<string>("state") ?? string.Empty;
                        if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                        {
                            var rjson = jrow.Value<string>("result_json") ?? "{}";
                            return JObject.Parse(rjson);
                        }
                        if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(jrow.Value<string>("error_msg") ?? "job failed");
                        System.Threading.Thread.Sleep(300);
                    }
                    throw new TimeoutException("job polling timeout");
                }

                // fallback
                return jo as JObject ?? new JObject();
            }
        }
    }
}

