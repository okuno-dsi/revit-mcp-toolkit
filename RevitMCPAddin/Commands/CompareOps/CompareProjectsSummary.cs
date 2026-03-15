#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Commands.AnalysisOps;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Compare;

namespace RevitMCPAddin.Commands.CompareOps
{
    /// <summary>
    /// compare_projects_summary: Multi-project summary-only comparator.
    /// Remote RPC sources are resolved on a background thread and the original server job
    /// is completed later via deferred post_result.
    /// </summary>
    public class CompareProjectsSummary : IRevitCommandHandler
    {
        public string CommandName => "compare_projects_summary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (cmd.Params as JObject) ?? new JObject();
            var arr = p["projects"] as JArray;
            if (arr == null || arr.Count < 2) return new { ok = false, code = "NEED_2+" };
            if (arr.Count > 5) return new { ok = false, code = "TOO_MANY" };

            var categories = (p["categories"] as JArray)?.Values<int>()?.ToArray() ?? new[] { -2001320, -2001330 };
            var keys = (p["keys"] as JArray)?.Values<string>()?.Where(s => !string.IsNullOrWhiteSpace(s))?.Select(s => s.Trim()).ToArray()
                ?? new[] { "familyName", "typeName" };
            double posTolMm = p.Value<double?>("posTolMm") ?? 600.0;
            bool includeEndpoints = p.Value<bool?>("includeEndpoints") ?? true;
            double endpointsTolMm = p.Value<double?>("endpointsTolMm") ?? 30.0;
            int baselineIndex = p.Value<int?>("baselineIndex") ?? 0;
            if (baselineIndex < 0 || baselineIndex >= arr.Count) baselineIndex = 0;

            var cmpSettings = new DeepCompareSettings
            {
                NumericEpsilon = p.Value<double?>("numericEpsilon") ?? 0.0,
                StringCaseInsensitive = p.Value<bool?>("stringCaseInsensitive") ?? false,
                StringTrim = p.Value<bool?>("stringTrim") ?? false,
                ArrayOrderInsensitive = p.Value<bool?>("arrayOrderInsensitive") ?? false,
                MaxDiffs = p.Value<int?>("maxDiffs") ?? 1000
            };

            var projectSpecs = arr.OfType<JObject>().Select(x => (JObject)x.DeepClone()).ToList();
            bool hasRemoteRpc = projectSpecs.Any(NeedsRemoteRpcResolution);
            if (!hasRemoteRpc)
            {
                return BuildSummarySync(uiapp, projectSpecs, categories, keys, posTolMm, includeEndpoints, endpointsTolMm, baselineIndex, cmpSettings);
            }

            var resolved = new JArray();
            var issues = new JArray();
            var remoteSpecs = new JArray();

            foreach (var spec in projectSpecs)
            {
                if (NeedsRemoteRpcResolution(spec))
                {
                    remoteSpecs.Add(spec);
                    continue;
                }

                ResolveProjectSourceSync(uiapp, spec, categories, resolved, issues);
            }

            try
            {
                DeferredRpcRunner.Start(uiapp, cmd, CommandName, async () =>
                {
                    var resolvedClone = (JArray)resolved.DeepClone();
                    var issuesClone = (JArray)issues.DeepClone();

                    foreach (var remote in remoteSpecs.OfType<JObject>())
                    {
                        await ResolveProjectSourceRemoteAsync(remote, categories, resolvedClone, issuesClone, CancellationToken.None).ConfigureAwait(false);
                    }

                    return BuildSummaryFromResolved(resolvedClone, issuesClone, keys, posTolMm, includeEndpoints, endpointsTolMm, baselineIndex, cmpSettings);
                });

                return DeferredRpcResult.Instance;
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "ASYNC_SCHEDULE_FAILED", msg = ex.Message };
            }
        }

        private static bool NeedsRemoteRpcResolution(JObject project)
        {
            string source = project.Value<string>("source") ?? "rpc";
            if (!source.Equals("rpc", StringComparison.OrdinalIgnoreCase)) return false;

            int port = project.Value<int?>("port") ?? 0;
            return port > 0 && !CompareRpcFacade.IsSelfPort(port);
        }

        private static JObject BuildSummarySync(
            UIApplication uiapp,
            IEnumerable<JObject> projectSpecs,
            int[] categories,
            string[] keys,
            double posTolMm,
            bool includeEndpoints,
            double endpointsTolMm,
            int baselineIndex,
            DeepCompareSettings cmpSettings)
        {
            var resolved = new JArray();
            var issues = new JArray();
            foreach (var spec in projectSpecs)
            {
                ResolveProjectSourceSync(uiapp, spec, categories, resolved, issues);
            }

            return BuildSummaryFromResolved(resolved, issues, keys, posTolMm, includeEndpoints, endpointsTolMm, baselineIndex, cmpSettings);
        }

        private static void ResolveProjectSourceSync(UIApplication uiapp, JObject spec, int[] categories, JArray resolved, JArray issues)
        {
            string source = spec.Value<string>("source") ?? "rpc";
            if (source.Equals("rpc", StringComparison.OrdinalIgnoreCase))
            {
                int port = spec.Value<int?>("port") ?? 0;
                ResolveRpcSourceCoreAsync(
                    spec,
                    categories,
                    (method, @params, _) => Task.FromResult(CompareRpcFacade.Call(uiapp, port, method, @params)),
                    resolved,
                    issues,
                    CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            ResolveFileSource(spec, resolved, issues);
        }

        private static Task ResolveProjectSourceRemoteAsync(JObject spec, int[] categories, JArray resolved, JArray issues, CancellationToken cancellationToken)
        {
            int port = spec.Value<int?>("port") ?? 0;
            return ResolveRpcSourceCoreAsync(
                spec,
                categories,
                (method, @params, ct) => CompareRpcFacade.CallRemoteAsync(port, method, @params, ct),
                resolved,
                issues,
                cancellationToken);
        }

        private static async Task ResolveRpcSourceCoreAsync(
            JObject spec,
            int[] categories,
            Func<string, JObject, CancellationToken, Task<JObject>> callAsync,
            JArray resolved,
            JArray issues,
            CancellationToken cancellationToken)
        {
            int port = spec.Value<int?>("port") ?? 0;
            int viewId = spec.Value<int?>("viewId") ?? 0;
            string viewName = spec.Value<string>("viewName") ?? string.Empty;
            string viewType = string.Empty;
            string resolvedViewName = viewName;

            try
            {
                if (viewId <= 0 && !string.IsNullOrWhiteSpace(viewName))
                {
                    var gv = await callAsync("get_views", new JObject { ["includeTemplates"] = false, ["detail"] = true }, cancellationToken).ConfigureAwait(false);
                    var vs = (gv["views"] as JArray) ?? new JArray();
                    var target = NormalizeName(viewName);
                    JObject? hit = vs.OfType<JObject>().FirstOrDefault(v => NormalizeName(v.Value<string>("name") ?? "") == target)
                        ?? vs.OfType<JObject>().FirstOrDefault(v => NormalizeName(v.Value<string>("name") ?? "").Contains(target));

                    if (hit != null)
                    {
                        viewId = hit.Value<int?>("viewId") ?? 0;
                        resolvedViewName = hit.Value<string>("name") ?? resolvedViewName;
                        viewType = hit.Value<string>("viewType") ?? string.Empty;
                    }
                    else
                    {
                        issues.Add(new JObject { ["code"] = "VIEW_NOT_FOUND", ["port"] = port, ["name"] = viewName });
                        return;
                    }
                }

                var snapParams = new JObject
                {
                    ["viewId"] = viewId,
                    ["categoryIds"] = new JArray(categories),
                    ["includeAnalytic"] = true,
                    ["includeHidden"] = false
                };
                var snap = await callAsync("snapshot_view_elements", snapParams, cancellationToken).ConfigureAwait(false);
                if (snap == null || snap.Value<bool?>("ok") == false)
                {
                    issues.Add(new JObject { ["code"] = "SNAPSHOT_FAIL", ["port"] = port, ["viewId"] = viewId });
                    return;
                }

                var rows = (snap["elements"] as JArray) ?? new JArray();
                var meta = new JObject
                {
                    ["source"] = "rpc",
                    ["port"] = port,
                    ["viewId"] = viewId,
                    ["viewName"] = resolvedViewName
                };

                resolved.Add(new JObject
                {
                    ["port"] = port,
                    ["viewId"] = viewId,
                    ["viewName"] = resolvedViewName,
                    ["viewType"] = viewType,
                    ["snapshot"] = snap,
                    ["rows"] = rows,
                    ["meta"] = meta
                });
            }
            catch (Exception ex)
            {
                issues.Add(new JObject
                {
                    ["code"] = "EX",
                    ["port"] = port,
                    ["msg"] = ex.Message
                });
            }
        }

        private static void ResolveFileSource(JObject spec, JArray resolved, JArray issues)
        {
            string source = spec.Value<string>("source") ?? string.Empty;
            string path = spec.Value<string>("path") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(new JObject { ["code"] = "BAD_SOURCE", ["source"] = source, ["msg"] = "path is required." });
                return;
            }

            try
            {
                if (source.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    var jt = JToken.Parse(File.ReadAllText(path, Encoding.UTF8));
                    var rows = (jt as JArray) ?? (jt as JObject)?["elements"] as JArray ?? new JArray(jt);
                    resolved.Add(new JObject
                    {
                        ["port"] = 0,
                        ["viewId"] = 0,
                        ["viewName"] = Path.GetFileName(path),
                        ["viewType"] = string.Empty,
                        ["snapshot"] = new JObject { ["ok"] = true, ["elements"] = rows },
                        ["rows"] = rows,
                        ["meta"] = new JObject { ["source"] = "json", ["path"] = path }
                    });
                    return;
                }

                if (source.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    var rows = new JArray();
                    foreach (var line in File.ReadLines(path, Encoding.UTF8))
                    {
                        var s = line.Trim();
                        if (s.Length == 0) continue;
                        try { rows.Add(JObject.Parse(s)); } catch { /* ignore broken line */ }
                    }

                    resolved.Add(new JObject
                    {
                        ["port"] = 0,
                        ["viewId"] = 0,
                        ["viewName"] = Path.GetFileName(path),
                        ["viewType"] = string.Empty,
                        ["snapshot"] = new JObject { ["ok"] = true, ["elements"] = rows },
                        ["rows"] = rows,
                        ["meta"] = new JObject { ["source"] = "jsonl", ["path"] = path }
                    });
                    return;
                }

                issues.Add(new JObject { ["code"] = "BAD_SOURCE", ["source"] = source });
            }
            catch (Exception ex)
            {
                issues.Add(new JObject
                {
                    ["code"] = "EX",
                    ["source"] = source,
                    ["path"] = path,
                    ["msg"] = ex.Message
                });
            }
        }

        private static JObject BuildSummaryFromResolved(
            JArray resolvedArray,
            JArray issues,
            string[] keys,
            double posTolMm,
            bool includeEndpoints,
            double endpointsTolMm,
            int baselineIndex,
            DeepCompareSettings cmpSettings)
        {
            var resolved = resolvedArray.OfType<JObject>().ToList();
            if (resolved.Count < 2)
            {
                return new JObject
                {
                    ["ok"] = false,
                    ["code"] = "NOT_ENOUGH",
                    ["issues"] = issues
                };
            }

            if (baselineIndex < 0 || baselineIndex >= resolved.Count) baselineIndex = 0;
            var baseline = resolved[baselineIndex];
            var items = new JArray();

            for (int i = 0; i < resolved.Count; i++)
            {
                if (i == baselineIndex) continue;

                var cur = resolved[i];
                int modifiedCount = 0;
                int leftOnly = 0;
                int rightOnly = 0;
                int nameChanged = 0;
                int total = 0;

                var diffCmd = new DiffElementsCommand();
                var diffParams = new JObject
                {
                    ["left"] = baseline["snapshot"] as JObject ?? new JObject(),
                    ["right"] = cur["snapshot"] as JObject ?? new JObject(),
                    ["keys"] = new JArray(keys),
                    ["posTolMm"] = posTolMm,
                    ["includeEndpoints"] = includeEndpoints,
                    ["endpointsTolMm"] = endpointsTolMm
                };
                var diffReq = new RequestCommand { Method = "diff_elements", Params = diffParams };
                var diffResult = JObject.FromObject(diffCmd.Execute(uiapp: null!, diffReq));
                if (diffResult.Value<bool?>("ok") == true)
                {
                    var mps = (diffResult["modifiedPairs"] as JArray) ?? new JArray();
                    modifiedCount = mps.Count;
                    leftOnly = ((diffResult["leftOnly"] as JArray)?.Count) ?? 0;
                    rightOnly = ((diffResult["rightOnly"] as JArray)?.Count) ?? 0;
                    foreach (var mp in mps.OfType<JObject>())
                    {
                        var diffs = (mp["diffs"] as JArray) ?? new JArray();
                        if (diffs.OfType<JObject>().Any(d =>
                            string.Equals(d.Value<string>("key"), "familyName", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(d.Value<string>("key"), "typeName", StringComparison.OrdinalIgnoreCase)))
                        {
                            nameChanged++;
                        }
                    }
                }

                total = ((baseline["rows"] as JArray)?.Count)
                    ?? ((baseline.SelectToken("snapshot.elements") as JArray)?.Count)
                    ?? (baseline.SelectToken("snapshot.count")?.Value<int?>() ?? 0);

                if (modifiedCount == 0)
                {
                    var elsBase = baseline.SelectToken("snapshot.elements") as JArray;
                    var elsCur = cur.SelectToken("snapshot.elements") as JArray;
                    if (elsBase != null && elsCur != null)
                    {
                        var fallback = CompareRows(elsBase, elsCur, keys, cmpSettings);
                        if (fallback.modified > 0 || fallback.leftOnly > 0 || fallback.rightOnly > 0)
                        {
                            modifiedCount = fallback.modified;
                            leftOnly = fallback.leftOnly;
                            rightOnly = fallback.rightOnly;
                            nameChanged = fallback.nameChanged;
                            if (total == 0) total = elsBase.Count;
                        }
                    }
                }

                double ratio = total > 0 ? (double)modifiedCount / total : 0.0;
                items.Add(new JObject
                {
                    ["port"] = cur.Value<int?>("port") ?? 0,
                    ["view"] = new JObject
                    {
                        ["id"] = cur.Value<int?>("viewId") ?? 0,
                        ["name"] = cur.Value<string>("viewName") ?? string.Empty,
                        ["type"] = cur.Value<string>("viewType") ?? string.Empty
                    },
                    ["total"] = total,
                    ["modifiedPairs"] = modifiedCount,
                    ["leftOnly"] = leftOnly,
                    ["rightOnly"] = rightOnly,
                    ["ratio"] = ratio,
                    ["nameChanged"] = nameChanged,
                    ["meta"] = cur["meta"]?.DeepClone() ?? new JObject()
                });
            }

            return new JObject
            {
                ["ok"] = true,
                ["baseline"] = new JObject
                {
                    ["port"] = baseline.Value<int?>("port") ?? 0,
                    ["view"] = new JObject
                    {
                        ["id"] = baseline.Value<int?>("viewId") ?? 0,
                        ["name"] = baseline.Value<string>("viewName") ?? string.Empty,
                        ["type"] = baseline.Value<string>("viewType") ?? string.Empty
                    },
                    ["meta"] = baseline["meta"]?.DeepClone() ?? new JObject()
                },
                ["items"] = items,
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

        private static (int modified, int leftOnly, int rightOnly, int nameChanged) CompareRows(JArray leftRows, JArray rightRows, string[] keys, DeepCompareSettings settings)
        {
            var lmap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var rmap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

            string KeyOf(JObject obj)
            {
                var uid = obj.Value<string>("uniqueId");
                if (!string.IsNullOrEmpty(uid)) return "uid:" + uid;

                var idInt = obj.Value<int?>("elementId") ?? 0;
                if (idInt > 0) return "id:" + idInt;

                if (keys != null && keys.Length > 0)
                {
                    var parts = new List<string>(keys.Length);
                    foreach (var key in keys)
                    {
                        var token = obj.SelectToken(key) ?? obj[key];
                        parts.Add(token != null ? JsonNetCompat.ToCompactJson(token) : string.Empty);
                    }
                    return "k:" + string.Join("|", parts);
                }

                return "h:" + JsonNetCompat.ToCompactJson(obj);
            }

            foreach (var row in leftRows.OfType<JObject>())
            {
                var key = KeyOf(row);
                if (!lmap.ContainsKey(key)) lmap[key] = row;
            }
            foreach (var row in rightRows.OfType<JObject>())
            {
                var key = KeyOf(row);
                if (!rmap.ContainsKey(key)) rmap[key] = row;
            }

            int modified = 0;
            int leftOnly = 0;
            int rightOnly = 0;
            int nameChanged = 0;

            var allKeys = new HashSet<string>(lmap.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in rmap.Keys) allKeys.Add(key);

            foreach (var key in allKeys)
            {
                lmap.TryGetValue(key, out var left);
                rmap.TryGetValue(key, out var right);
                if (left == null && right != null) { rightOnly++; continue; }
                if (left != null && right == null) { leftOnly++; continue; }
                if (left == null || right == null) continue;

                var diffs = new List<(string path, JToken l, JToken r)>();
                var equal = DeepJsonComparer.AreEqual(left, right, settings, "", diffs);
                if (equal || diffs.Count == 0) continue;

                modified++;
                if (diffs.Any(d => d.path.Equals("familyName", StringComparison.OrdinalIgnoreCase) || d.path.Equals("typeName", StringComparison.OrdinalIgnoreCase)))
                    nameChanged++;
            }

            return (modified, leftOnly, rightOnly, nameChanged);
        }
    }
}
