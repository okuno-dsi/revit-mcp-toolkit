#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Compare;

namespace RevitMCPAddin.Commands.CompareOps
{
    /// <summary>
    /// validate_compare_context (multi-port):
    /// Validates that the specified projects (ports) refer to the same document and comparable views.
    /// Remote-port resolution is deferred so the Revit UI thread is not blocked by loopback HTTP polling.
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
                var left = p["left"] as JObject;
                var right = p["right"] as JObject;
                arr = new JArray();
                if (left != null) arr.Add(left);
                if (right != null) arr.Add(right);
            }

            if (arr == null || arr.Count == 0) return new { ok = false, code = "NO_PROJECTS", msg = "projects is required." };
            if (arr.Count > 5) return new { ok = false, code = "TOO_MANY", msg = "projects must be <= 5." };

            bool requireSameProject = p.Value<bool?>("requireSameProject") ?? true;
            bool requireSameViewType = p.Value<bool?>("requireSameViewType") ?? true;
            bool resolveByName = p.Value<bool?>("resolveByName") ?? true;

            var projects = arr.OfType<JObject>().Select(x => (JObject)x.DeepClone()).ToList();
            bool hasRemote = projects.Any(NeedsRemoteResolution);
            if (!hasRemote)
            {
                return BuildValidationResult(
                    uiapp,
                    projects,
                    requireSameProject,
                    requireSameViewType,
                    resolveByName);
            }

            var seededItems = new JArray();
            var seededIssues = new JArray();
            var remoteProjects = new JArray();

            foreach (var it in projects)
            {
                if (NeedsRemoteResolution(it))
                {
                    remoteProjects.Add(it);
                    continue;
                }

                ResolveProjectSync(uiapp, it, resolveByName, seededItems, seededIssues);
            }

            try
            {
                DeferredRpcRunner.Start(uiapp, cmd, CommandName, async () =>
                {
                    var items = (JArray)seededItems.DeepClone();
                    var issues = (JArray)seededIssues.DeepClone();

                    foreach (var remote in remoteProjects.OfType<JObject>())
                    {
                        await ResolveProjectRemoteAsync(remote, resolveByName, items, issues, CancellationToken.None).ConfigureAwait(false);
                    }

                    return BuildValidationResultFromResolved(items, issues, requireSameProject, requireSameViewType);
                });

                return DeferredRpcResult.Instance;
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "ASYNC_SCHEDULE_FAILED", msg = ex.Message };
            }
        }

        private static bool NeedsRemoteResolution(JObject item)
        {
            int port = item?.Value<int?>("port") ?? 0;
            return port > 0 && !CompareRpcFacade.IsSelfPort(port);
        }

        private static JObject BuildValidationResult(
            UIApplication uiapp,
            System.Collections.Generic.IEnumerable<JObject> projects,
            bool requireSameProject,
            bool requireSameViewType,
            bool resolveByName)
        {
            var items = new JArray();
            var issues = new JArray();

            foreach (var it in projects)
            {
                ResolveProjectSync(uiapp, it, resolveByName, items, issues);
            }

            return BuildValidationResultFromResolved(items, issues, requireSameProject, requireSameViewType);
        }

        private static void ResolveProjectSync(UIApplication uiapp, JObject item, bool resolveByName, JArray items, JArray issues)
        {
            int port = item.Value<int?>("port") ?? 0;
            if (port <= 0)
            {
                issues.Add(new JObject
                {
                    ["code"] = "BAD_PORT",
                    ["msg"] = "port missing/invalid",
                    ["item"] = item.DeepClone()
                });
                return;
            }

            ResolveProjectCoreAsync(
                item,
                resolveByName,
                (method, @params, _) => Task.FromResult(CompareRpcFacade.Call(uiapp, port, method, @params)),
                items,
                issues,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        private static Task ResolveProjectRemoteAsync(JObject item, bool resolveByName, JArray items, JArray issues, CancellationToken cancellationToken)
        {
            int port = item.Value<int?>("port") ?? 0;
            return ResolveProjectCoreAsync(
                item,
                resolveByName,
                (method, @params, ct) => CompareRpcFacade.CallRemoteAsync(port, method, @params, ct),
                items,
                issues,
                cancellationToken);
        }

        private static async Task ResolveProjectCoreAsync(
            JObject item,
            bool resolveByName,
            Func<string, JObject, CancellationToken, Task<JObject>> callAsync,
            JArray items,
            JArray issues,
            CancellationToken cancellationToken)
        {
            int port = item.Value<int?>("port") ?? 0;
            try
            {
                var docs = await callAsync("get_open_documents", new JObject(), cancellationToken).ConfigureAwait(false);
                var documents = (docs["documents"] as JArray) ?? new JArray();
                JObject? active = documents.OfType<JObject>().FirstOrDefault(d => (d.Value<bool?>("active") ?? false) == true);
                if (active == null && documents.Count > 0) active = documents[0] as JObject;

                string guid = active?.Value<string>("guid") ?? string.Empty;
                string title = active?.Value<string>("title") ?? string.Empty;
                string path = active?.Value<string>("path") ?? string.Empty;

                int viewId = item.Value<int?>("viewId") ?? 0;
                string viewName = item.Value<string>("viewName") ?? string.Empty;
                string viewType = string.Empty;
                string resolvedName = viewName;

                if (viewId > 0)
                {
                    var gv = await callAsync("get_views", new JObject { ["includeTemplates"] = false, ["detail"] = true }, cancellationToken).ConfigureAwait(false);
                    var vs = (gv["views"] as JArray) ?? new JArray();
                    foreach (var view in vs.OfType<JObject>())
                    {
                        if ((view.Value<int?>("viewId") ?? 0) != viewId) continue;
                        resolvedName = view.Value<string>("name") ?? string.Empty;
                        viewType = view.Value<string>("viewType") ?? string.Empty;
                        break;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(viewName) && resolveByName)
                {
                    var gv = await callAsync("get_views", new JObject { ["includeTemplates"] = false, ["detail"] = true }, cancellationToken).ConfigureAwait(false);
                    var vs = (gv["views"] as JArray) ?? new JArray();
                    var target = NormalizeName(viewName);
                    JObject? hit = vs.OfType<JObject>().FirstOrDefault(v => NormalizeName(v.Value<string>("name") ?? string.Empty) == target)
                        ?? vs.OfType<JObject>().FirstOrDefault(v => NormalizeName(v.Value<string>("name") ?? string.Empty).Contains(target));

                    if (hit != null)
                    {
                        viewId = hit.Value<int?>("viewId") ?? 0;
                        resolvedName = hit.Value<string>("name") ?? string.Empty;
                        viewType = hit.Value<string>("viewType") ?? string.Empty;
                    }
                    else
                    {
                        issues.Add(new JObject
                        {
                            ["code"] = "VIEW_NOT_FOUND",
                            ["msg"] = "viewName '" + viewName + "' not found on port " + port,
                            ["port"] = port
                        });
                    }
                }

                items.Add(new JObject
                {
                    ["port"] = port,
                    ["document"] = new JObject
                    {
                        ["guid"] = guid,
                        ["title"] = title,
                        ["path"] = path
                    },
                    ["view"] = new JObject
                    {
                        ["id"] = viewId,
                        ["name"] = string.IsNullOrEmpty(resolvedName) ? viewName : resolvedName,
                        ["viewType"] = viewType
                    }
                });
            }
            catch (Exception ex)
            {
                issues.Add(new JObject
                {
                    ["code"] = "EX",
                    ["msg"] = ex.Message,
                    ["item"] = item.DeepClone()
                });
            }
        }

        private static JObject BuildValidationResultFromResolved(JArray items, JArray issues, bool requireSameProject, bool requireSameViewType)
        {
            bool allSameProject = false;
            bool allSameViewType = false;

            var itemList = items.OfType<JObject>().ToList();
            if (itemList.Count > 0)
            {
                var guids = itemList
                    .Select(i => i.SelectToken("document.guid")?.Value<string>() ?? string.Empty)
                    .ToList();
                if (guids.All(g => !string.IsNullOrEmpty(g)))
                {
                    allSameProject = guids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                }
                else
                {
                    var titles = itemList
                        .Select(i => NormalizeName(i.SelectToken("document.title")?.Value<string>() ?? string.Empty))
                        .ToList();
                    allSameProject = titles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                }

                var vtypes = itemList
                    .Select(i => i.SelectToken("view.viewType")?.Value<string>() ?? string.Empty)
                    .ToList();
                allSameViewType = vtypes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
            }

            if (requireSameProject && !allSameProject)
            {
                issues.Add(new JObject
                {
                    ["code"] = "MISMATCHED_PROJECT",
                    ["msg"] = "Documents differ across ports."
                });
            }

            if (requireSameViewType && !allSameViewType)
            {
                issues.Add(new JObject
                {
                    ["code"] = "MISMATCHED_VIEWTYPE",
                    ["msg"] = "ViewType differs across ports."
                });
            }

            bool ok = (!requireSameProject || allSameProject)
                && (!requireSameViewType || allSameViewType)
                && itemList.Count >= 2;

            return new JObject
            {
                ["ok"] = ok,
                ["count"] = itemList.Count,
                ["items"] = items,
                ["checks"] = new JObject
                {
                    ["allSameProject"] = allSameProject,
                    ["allSameViewType"] = allSameViewType
                },
                ["issues"] = issues
            };
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string n = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            n = n.Replace('\u3000', ' ');
            var sb = new StringBuilder(n.Length);
            foreach (var ch in n)
            {
                if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
