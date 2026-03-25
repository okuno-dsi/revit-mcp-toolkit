#nullable enable
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core.ResultDelivery;

namespace RevitMCPAddin.Core
{
    internal static class DeferredRpcRunner
    {
        public static DeferredRpcResult Start(UIApplication? uiapp, RequestCommand cmd, string method, Func<Task<object>> work)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            if (work == null) throw new ArgumentNullException(nameof(work));

            var idToken = cmd.Id != null ? cmd.Id.DeepClone() : JValue.CreateNull();
            var rpcId = cmd.Id != null ? (cmd.Id.ToString() ?? string.Empty) : string.Empty;
            if (string.IsNullOrWhiteSpace(rpcId))
                throw new InvalidOperationException(method + ": deferred execution requires a JSON-RPC id.");

            var contextSeed = CaptureContextSeed(uiapp, method);

            Task.Run(async () =>
            {
                long elapsedMs = 0;
                object raw;
                var sw = Stopwatch.StartNew();
                try
                {
                    raw = await work().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var fail = RpcResultEnvelope.Fail("ASYNC_REMOTE_COMPARE_FAILED", ex.Message, new { exception = ex.GetType().Name });
                    fail["detail"] = ex.ToString();
                    raw = fail;
                }
                finally
                {
                    sw.Stop();
                    elapsedMs = sw.ElapsedMilliseconds;
                }

                await EnqueueResultAsync(idToken, rpcId, method, raw, elapsedMs, contextSeed).ConfigureAwait(false);
            });

            return DeferredRpcResult.Instance;
        }

        private static async Task EnqueueResultAsync(JToken idToken, string rpcId, string method, object raw, long revitMs, JObject contextSeed)
        {
            var standardized = RpcResultEnvelope.StandardizePayload(raw, uiapp: null, method: method, revitMs: revitMs);
            MergeContextSeed(standardized, contextSeed);

            var envelope = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = idToken ?? JValue.CreateNull(),
                ["result"] = standardized
            };

            var jsonBody = JsonNetCompat.ToCompactJson(envelope);
            var item = new PendingResultItem
            {
                RpcId = rpcId,
                HeartbeatKey = rpcId,
                JsonBody = jsonBody,
                CreatedAtUtc = DateTime.UtcNow
            };

            var delivery = AppServices.ResultDelivery;
            if (delivery != null)
            {
                delivery.Enqueue(item);
                RevitLogger.Info("[DEFERRED] queued result delivery method=" + method + " rpcId=" + rpcId);
                return;
            }

            await PostDirectAsync(item).ConfigureAwait(false);
        }

        private static async Task PostDirectAsync(PendingResultItem item)
        {
            var port = AppServices.CurrentPort > 0 ? AppServices.CurrentPort : PortLocator.GetCurrentPortOrDefault(5210);
            var baseAddress = "http://127.0.0.1:" + port + "/";
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(baseAddress, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(8) };
                using var content = new StringContent(item.JsonBody, Encoding.UTF8, "application/json");
                using var res = await client.PostAsync("post_result", content).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                    throw new HttpRequestException("post_result returned " + (int)res.StatusCode);

                RevitLogger.Info("[DEFERRED] delivered result directly rpcId=" + item.RpcId);
            }
            catch (Exception ex)
            {
                RevitLogger.Warn("[DEFERRED] direct post_result failed rpcId=" + item.RpcId + ": " + ex.Message);
                try
                {
                    var store = new FileBackedPendingResultStore(port);
                    store.Save(item);
                }
                catch (Exception saveEx)
                {
                    RevitLogger.Warn("[DEFERRED] fallback persist failed rpcId=" + item.RpcId + ": " + saveEx.Message);
                }
            }
        }

        private static JObject CaptureContextSeed(UIApplication? uiapp, string method)
        {
            var ctx = new JObject();
            if (!string.IsNullOrWhiteSpace(method)) ctx["method"] = method;

            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return ctx;

                if (ctx["docTitle"] == null) ctx["docTitle"] = doc.Title ?? string.Empty;
                try
                {
                    string source;
                    var docKey = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out source);
                    if (!string.IsNullOrWhiteSpace(docKey))
                    {
                        ctx["docKey"] = docKey.Trim();
                        ctx["docKeySource"] = source;
                        ctx["docGuid"] = docKey.Trim();
                    }
                }
                catch { /* ignore */ }

                var av = doc.ActiveView;
                if (av != null)
                {
                    ctx["activeViewId"] = av.Id.IntValue();
                    ctx["activeViewName"] = av.Name ?? string.Empty;
                    ctx["activeViewType"] = av.ViewType.ToString();
                    ctx["rawActiveViewType"] = av.GetType().Name;
                }
            }
            catch { /* ignore */ }

            return ctx;
        }

        private static void MergeContextSeed(JObject payload, JObject contextSeed)
        {
            if (payload == null || contextSeed == null || contextSeed.Count == 0) return;

            var ctx = payload["context"] as JObject;
            if (ctx == null)
            {
                payload["context"] = (JObject)contextSeed.DeepClone();
                return;
            }

            foreach (var prop in contextSeed.Properties())
            {
                if (ctx[prop.Name] == null)
                    ctx[prop.Name] = prop.Value.DeepClone();
            }
        }
    }
}
