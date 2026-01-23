#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Commands.AnalysisOps; // DiffElementsCommand
using RevitMCPAddin.Core.Compare;
using RevitMCPAddin.Commands.ViewOps; // SnapshotViewElementsCommand

namespace RevitMCPAddin.Commands.CompareOps
{
    /// <summary>
    /// compare_projects_summary: Multi-project summary-only comparator.
    /// Supports RPC sources (snapshot_view_elements) and JSON/JSONL.
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
            var keys = (p["keys"] as JArray)?.Values<string>()?.Where(s => !string.IsNullOrWhiteSpace(s))?.Select(s => s.Trim()).ToArray() ?? new[] { "familyName", "typeName" };
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

            var issues = new List<object>();
            var resolved = new List<(int port, int viewId, string viewName, string viewType, JObject snapshot, JArray rows, JObject meta)>();
            int selfPort = PortLocator.GetCurrentPortOrDefault(5210);

            for (int i = 0; i < arr.Count; i++)
            {
                var it = (JObject)arr[i];
                string source = it.Value<string>("source") ?? "rpc";
                if (source.Equals("rpc", StringComparison.OrdinalIgnoreCase))
                {
                    int port = it.Value<int?>("port") ?? 0;
                    int viewId = it.Value<int?>("viewId") ?? 0;
                    string viewName = it.Value<string>("viewName") ?? string.Empty;
                    string vtype = string.Empty; string vnameResolved = viewName;

                    if (viewId <= 0 && !string.IsNullOrWhiteSpace(viewName))
                    {
                        var gv = CallRpcAndGetResult2(port, "get_views", new JObject { ["includeTemplates"] = false, ["detail"] = true });
                        var vs = (gv["views"] as JArray) ?? new JArray();
                        var target = NormalizeName(viewName);
                        var hit = vs.OfType<JObject>().FirstOrDefault(v => NormalizeName(v.Value<string>("name") ?? "") == target) ??
                                  vs.OfType<JObject>().FirstOrDefault(v => NormalizeName(v.Value<string>("name") ?? "").Contains(target));
                        if (hit != null)
                        {
                            viewId = hit.Value<int?>("viewId") ?? 0;
                            vnameResolved = hit.Value<string>("name") ?? vnameResolved;
                            vtype = hit.Value<string>("viewType") ?? string.Empty;
                        }
                        else { issues.Add(new { code = "VIEW_NOT_FOUND", port, name = viewName }); continue; }
                    }

                    JObject snap;
                    var snapParams = new JObject { ["viewId"] = viewId, ["categoryIds"] = new JArray(categories), ["includeAnalytic"] = true, ["includeHidden"] = false };
                    if (port == selfPort)
                    {
                        var handler = new SnapshotViewElementsCommand();
                        var rc = new RequestCommand { Method = "snapshot_view_elements", Params = snapParams };
                        snap = JObject.FromObject(handler.Execute(uiapp, rc));
                    }
                    else
                    {
                        snap = CallRpcAndGetResult2(port, "snapshot_view_elements", snapParams) as JObject;
                    }
                    if (snap == null || (snap.Value<bool?>("ok") == false)) { issues.Add(new { code = "SNAPSHOT_FAIL", port, viewId }); continue; }

                    var rows = (snap["elements"] as JArray) ?? new JArray();
                    var meta = new JObject { ["source"] = "rpc", ["port"] = port, ["viewId"] = viewId, ["viewName"] = vnameResolved };
                    resolved.Add((port, viewId, vnameResolved, vtype, snap, rows, meta));
                }
                else if (source.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    var path = it.Value<string>("path");
                    var jt = string.IsNullOrWhiteSpace(path) ? null : JToken.Parse(System.IO.File.ReadAllText(path, Encoding.UTF8));
                    var rows = (jt as JArray) ?? (jt as JObject)?["elements"] as JArray ?? new JArray(jt);
                    resolved.Add((0, 0, System.IO.Path.GetFileName(path), "", new JObject { ["ok"] = true, ["elements"] = rows }, rows, new JObject { ["source"] = "json", ["path"] = path }));
                }
                else if (source.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    var path = it.Value<string>("path");
                    var rows = new JArray();
                    foreach (var line in System.IO.File.ReadLines(path, Encoding.UTF8))
                    {
                        var s = line.Trim(); if (s.Length == 0) continue; try { rows.Add(JObject.Parse(s)); } catch { }
                    }
                    resolved.Add((0, 0, System.IO.Path.GetFileName(path), "", new JObject { ["ok"] = true, ["elements"] = rows }, rows, new JObject { ["source"] = "jsonl", ["path"] = path }));
                }
                else
                {
                    issues.Add(new { code = "BAD_SOURCE", idx = i, source });
                }
            }

            if (resolved.Count < 2) return new { ok = false, code = "NOT_ENOUGH", issues };
            if (baselineIndex >= resolved.Count) baselineIndex = 0;

            var baseline = resolved[baselineIndex];
            var items = new List<object>();

            for (int i = 0; i < resolved.Count; i++)
            {
                if (i == baselineIndex) continue;
                var cur = resolved[i];
                int modifiedCount = 0, leftOnly = 0, rightOnly = 0, nameChanged = 0, total = 0;

                // snapshot-based diff via DiffElementsCommand
                var diffCmd = new DiffElementsCommand();
                var diffParams = new JObject
                {
                    ["left"] = baseline.snapshot,
                    ["right"] = cur.snapshot,
                    ["keys"] = new JArray(keys),
                    ["posTolMm"] = posTolMm,
                    ["includeEndpoints"] = includeEndpoints,
                    ["endpointsTolMm"] = endpointsTolMm
                };
                var diffReq = new RequestCommand { Method = "diff_elements", Params = diffParams };
                var diffResult = JObject.FromObject(diffCmd.Execute(uiapp, diffReq));
                if (diffResult.Value<bool?>("ok") == true)
                {
                    var mps = (diffResult["modifiedPairs"] as JArray) ?? new JArray();
                    modifiedCount = mps.Count;
                    leftOnly = ((diffResult["leftOnly"] as JArray)?.Count) ?? 0;
                    rightOnly = ((diffResult["rightOnly"] as JArray)?.Count) ?? 0;
                    foreach (var mp in mps.OfType<JObject>())
                    {
                        var diffs = (mp["diffs"] as JArray) ?? new JArray();
                        if (diffs.OfType<JObject>().Any(d => string.Equals(d.Value<string>("key"), "familyName", StringComparison.OrdinalIgnoreCase) ||
                                                             string.Equals(d.Value<string>("key"), "typeName", StringComparison.OrdinalIgnoreCase)))
                            nameChanged++;
                    }
                }

                // Totals
                total = baseline.rows?.Count ?? (baseline.snapshot["elements"] as JArray)?.Count ?? (baseline.snapshot.Value<int?>("count") ?? 0);

                // Deep compare fallback using element arrays if needed
                if (modifiedCount == 0)
                {
                    var elsBase = baseline.snapshot["elements"] as JArray;
                    var elsCur = cur.snapshot["elements"] as JArray;
                    if (elsBase != null && elsCur != null)
                    {
                        var (m, lo, ro, nc) = CompareRows(elsBase, elsCur, keys, cmpSettings);
                        if (m > 0 || lo > 0 || ro > 0)
                        { modifiedCount = m; leftOnly = lo; rightOnly = ro; nameChanged = nc; total = total == 0 ? elsBase.Count : total; }
                    }
                }

                double ratio = (total > 0) ? (double)modifiedCount / (double)total : 0.0;
                items.Add(new { port = cur.port, view = new { id = cur.viewId, name = cur.viewName, type = cur.viewType }, total, modifiedPairs = modifiedCount, leftOnly, rightOnly, ratio, nameChanged, meta = cur.meta });
            }

            var baselineOut = new { port = baseline.port, view = new { id = baseline.viewId, name = baseline.viewName, type = baseline.viewType }, meta = baseline.meta };
            return new { ok = true, baseline = baselineOut, items, issues };
        }

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string n = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            n = n.Replace('\u3000', ' ');
            var sb = new StringBuilder(n.Length);
            foreach (var ch in n) if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            return sb.ToString();
        }

        private static (int modified, int leftOnly, int rightOnly, int nameChanged) CompareRows(JArray leftRows, JArray rightRows, string[] keys, DeepCompareSettings s)
        {
            var lmap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var rmap = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

            string KeyOf(JObject o)
            {
                var uid = o.Value<string>("uniqueId"); if (!string.IsNullOrEmpty(uid)) return "uid:" + uid;
                var idInt = o.Value<int?>("elementId") ?? 0; if (idInt > 0) return "id:" + idInt;
                if (keys != null && keys.Length > 0)
                {
                    var parts = new List<string>(keys.Length);
                    foreach (var k in keys) { var token = o.SelectToken(k) ?? o[k]; parts.Add((token != null) ? JsonNetCompat.ToCompactJson(token) : ""); }
                    return "k:" + string.Join("|", parts);
                }
                return "h:" + JsonNetCompat.ToCompactJson(o);
            }

            foreach (var j in leftRows?.OfType<JObject>() ?? Enumerable.Empty<JObject>()) { var k = KeyOf(j); if (!lmap.ContainsKey(k)) lmap[k] = j; }
            foreach (var j in rightRows?.OfType<JObject>() ?? Enumerable.Empty<JObject>()) { var k = KeyOf(j); if (!rmap.ContainsKey(k)) rmap[k] = j; }

            int modified = 0, leftOnly = 0, rightOnly = 0, nameChanged = 0;
            var allKeys = new HashSet<string>(lmap.Keys, StringComparer.OrdinalIgnoreCase); foreach (var k in rmap.Keys) allKeys.Add(k);
            foreach (var k in allKeys)
            {
                lmap.TryGetValue(k, out var lo); rmap.TryGetValue(k, out var ro);
                if (lo == null && ro != null) { rightOnly++; continue; }
                if (lo != null && ro == null) { leftOnly++; continue; }
                var diffs = new List<(string path, JToken l, JToken r)>();
                var ok = DeepJsonComparer.AreEqual(lo, ro, s, "", diffs);
                if (!ok && diffs.Count > 0)
                {
                    modified++;
                    if (diffs.Any(d => d.path.Equals("familyName", StringComparison.OrdinalIgnoreCase) || d.path.Equals("typeName", StringComparison.OrdinalIgnoreCase))) nameChanged++;
                }
            }
            return (modified, leftOnly, rightOnly, nameChanged);
        }

        // RPC helper that resolves enqueue ack -> job -> payload
        private static JObject CallRpcAndGetResult2(int port, string method, JObject @params)
        {
            var payload = new JObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params ?? new JObject(), ["id"] = DateTimeOffset.Now.ToUnixTimeMilliseconds() };
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var baseUri = new Uri($"http://localhost:{port}/");
            var enqueue = new Uri(baseUri, "enqueue");
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var resp = client.PostAsync(enqueue, content).GetAwaiter().GetResult();
            var txt = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var first = JObject.Parse(txt);
            var jobId = first.Value<string>("jobId") ?? first.Value<string>("job_id") ?? string.Empty;
            if (!string.IsNullOrEmpty(jobId))
            {
                var job = new Uri(baseUri, $"job/{jobId}");
                var start = DateTime.UtcNow;
                while ((DateTime.UtcNow - start).TotalSeconds < 90)
                {
                    var jr = client.GetAsync(job).GetAwaiter().GetResult();
                    var jtxt = jr.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var jrow = JObject.Parse(jtxt);
                    var state = jrow.Value<string>("state") ?? string.Empty;
                    if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                    {
                        var rjson = jrow.Value<string>("result_json") ?? "{}";
                        try { return ExtractPayload2(JObject.Parse(rjson)); } catch { return new JObject(); }
                    }
                    if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(jrow.Value<string>("error_msg") ?? "job failed");
                    System.Threading.Thread.Sleep(300);
                }
                throw new TimeoutException("job polling timeout");
            }
            if (first["result"] != null && first["result"].Type != JTokenType.Null) return ExtractPayload2(first["result"]!);
            if (first["ok"] != null || first["elements"] != null || first["project"] != null || first["views"] != null) return first;
            return first;
        }

        private static JObject ExtractPayload2(JToken node)
        {
            if (node == null || node.Type == JTokenType.Null) return new JObject();
            if (node is JObject o)
            {
                if (o["ok"] != null || o["elements"] != null || o["project"] != null || o["views"] != null) return o as JObject ?? new JObject();
                if (o["result"] != null) return ExtractPayload2(o["result"]!);
                if (o["data"] != null) return ExtractPayload2(o["data"]!);
                if (o["payload"] != null) return ExtractPayload2(o["payload"]!);
                return o as JObject ?? new JObject();
            }
            return new JObject();
        }
    }
}
