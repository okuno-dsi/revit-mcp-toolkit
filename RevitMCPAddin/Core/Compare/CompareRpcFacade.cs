#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Commands.AnalysisOps;
using RevitMCPAddin.Commands.DocumentOps;
using RevitMCPAddin.Commands.ViewOps;

namespace RevitMCPAddin.Core.Compare
{
    internal static class CompareRpcFacade
    {
        public static bool IsSelfPort(int targetPort) => targetPort > 0 && targetPort == GetSelfPort();

        public static JObject Call(UIApplication uiapp, int targetPort, string method, JObject @params)
        {
            if (targetPort <= 0) throw new ArgumentException("targetPort must be > 0", nameof(targetPort));
            var normalizedMethod = NormalizeMethod(method);
            var selfPort = GetSelfPort();

            if (targetPort == selfPort)
            {
                return CallLocal(uiapp, normalizedMethod, @params ?? new JObject());
            }

            return CallRemote(targetPort, normalizedMethod, @params ?? new JObject());
        }

        public static async Task<JObject> CallRemoteAsync(int port, string method, JObject @params, CancellationToken cancellationToken = default)
        {
            if (port <= 0) throw new ArgumentException("targetPort must be > 0", nameof(port));
            var normalizedMethod = NormalizeMethod(method);
            return await CallRemoteCoreAsync(port, normalizedMethod, @params ?? new JObject(), cancellationToken).ConfigureAwait(false);
        }

        private static int GetSelfPort()
        {
            var p = AppServices.CurrentPort;
            if (p <= 0) p = PortLocator.GetCurrentPortOrDefault(5210);
            return p > 0 ? p : 5210;
        }

        private static string NormalizeMethod(string method)
        {
            var m = (method ?? string.Empty).Trim();
            if (m.Length == 0) return m;
            var i = m.LastIndexOf('.');
            return i >= 0 ? m.Substring(i + 1) : m;
        }

        private static JObject CallLocal(UIApplication uiapp, string method, JObject @params)
        {
            if (uiapp == null) throw new InvalidOperationException("uiapp is required for local compare RPC call.");

            var req = new RequestCommand { Method = method, Params = @params ?? new JObject() };
            object result;
            switch (method.ToLowerInvariant())
            {
                case "get_open_documents":
                    result = new GetOpenDocumentsCommand().Execute(uiapp, req);
                    break;
                case "get_views":
                    result = new GetViewsCommand().Execute(uiapp, req);
                    break;
                case "snapshot_view_elements":
                    result = new SnapshotViewElementsCommand().Execute(uiapp, req);
                    break;
                case "diff_elements":
                    result = new DiffElementsCommand().Execute(uiapp, req);
                    break;
                default:
                    throw new NotSupportedException("Unsupported local compare RPC method: " + method);
            }
            return ExtractPayload(JToken.FromObject(result));
        }

        private static JObject CallRemote(int port, string method, JObject @params)
            => CallRemoteCoreAsync(port, method, @params, CancellationToken.None).GetAwaiter().GetResult();

        private static async Task<JObject> CallRemoteCoreAsync(int port, string method, JObject @params, CancellationToken cancellationToken)
        {
            var payload = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = @params ?? new JObject(),
                ["id"] = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var baseUri = new Uri($"http://localhost:{port}/");
            var enqueue = new Uri(baseUri, "enqueue");
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(enqueue, content, cancellationToken).ConfigureAwait(false);
            var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var first = JObject.Parse(txt);

            var jobId = ResolveJobId(first);
            if (!string.IsNullOrEmpty(jobId))
            {
                var job = new Uri(baseUri, $"job/{jobId}");
                var start = DateTime.UtcNow;
                while ((DateTime.UtcNow - start).TotalSeconds < 90)
                {
                    using var jr = await client.GetAsync(job, cancellationToken).ConfigureAwait(false);
                    if (jr.StatusCode == HttpStatusCode.Accepted || jr.StatusCode == HttpStatusCode.NoContent)
                    {
                        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var jtxt = await jr.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var jrow = JObject.Parse(jtxt);
                    var state = jrow.Value<string>("state") ?? string.Empty;
                    if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                    {
                        var rjson = jrow.Value<string>("result_json") ?? "{}";
                        try { return ExtractPayload(JObject.Parse(rjson)); } catch { return new JObject(); }
                    }
                    if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "DEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(jrow.Value<string>("error_msg") ?? ("job failed: " + state));
                    }
                    await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                }
                throw new TimeoutException("job polling timeout");
            }

            if (first["result"] != null && first["result"]!.Type != JTokenType.Null)
                return ExtractPayload(first["result"]!);

            if (first["ok"] != null || first["elements"] != null || first["project"] != null || first["views"] != null)
                return first;

            return first;
        }

        private static string ResolveJobId(JObject first)
        {
            string id = first.Value<string>("jobId") ?? first.Value<string>("job_id") ?? string.Empty;
            if (!string.IsNullOrEmpty(id)) return id;

            if (first["result"] is JObject r)
            {
                id = r.Value<string>("jobId") ?? r.Value<string>("job_id") ?? string.Empty;
                if (!string.IsNullOrEmpty(id)) return id;

                if (r["result"] is JObject rr)
                {
                    id = rr.Value<string>("jobId") ?? rr.Value<string>("job_id") ?? string.Empty;
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            return string.Empty;
        }

        private static JObject ExtractPayload(JToken node)
        {
            if (node == null || node.Type == JTokenType.Null) return new JObject();
            if (node is JObject o)
            {
                if (o["ok"] != null || o["elements"] != null || o["project"] != null || o["views"] != null || o["documents"] != null)
                    return o;
                if (o["result"] != null) return ExtractPayload(o["result"]!);
                if (o["data"] != null) return ExtractPayload(o["data"]!);
                if (o["payload"] != null) return ExtractPayload(o["payload"]!);
                return o;
            }
            return new JObject();
        }
    }
}
