// ================================================================
// File: Core/LongOpEngine.cs
// Purpose: 長時間処理を Revit の Idling イベントで小分け実行し、
//          /post_result へ進捗/完了を返す非同期ジョブ基盤。
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Notes  :
//  - Initialize(UIApplication, Uri) は何度呼んでも安全（多重フック防止）
//  - BaseAddress は Worker 側で設定済みなら null を渡して構いません
//  - 例として export_dwg_by_param_groups の tick 実行に対応
//  - ログは RevitLogger に集約（必要なら節度付き tick ログを有効化）
// ================================================================
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class LongOpEngine
    {
        // ------------------ Job モデル ------------------
        internal sealed class Job
        {
            public string JobId = Guid.NewGuid().ToString("N");
            public long RpcId;
            public string Method = "";
            public JObject Params = new JObject();
            public string Phase = "queued";
            public int NextIndex = 0;
            public int TotalGroups = 0;
            public int Processed = 0;
            public DateTime EnqueuedAt = DateTime.UtcNow;
            public string SessionKey = "";
            public string Error = "";
        }

        // ------------------ 状態 ------------------
        private static readonly ConcurrentQueue<Job> _queue = new ConcurrentQueue<Job>();
        private static readonly ConcurrentDictionary<string, Job> _active = new ConcurrentDictionary<string, Job>();
        private static volatile bool _idlingHooked = false;
        private static UIApplication _uiapp;

        // 節度付き tick ログ（有効にするなら true に）
        private const bool LOG_TICK = false;
        private static long _lastTickLogMs = 0;

        // /post_result 宛て HTTP クライアント（BaseAddress は Initialize で設定）
        private static readonly Lazy<HttpClient> _http = new Lazy<HttpClient>(() =>
        {
            var cli = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            cli.Timeout = TimeSpan.FromSeconds(30);
            return cli;
        });

        // ------------------ 初期化 ------------------
        /// <summary>
        /// 非同期実行基盤の初期化。uiapp は Idling をフックするために必要。
        /// baseAddress は Worker 側と同一 (例: http://127.0.0.1:{port}/) を推奨。
        /// </summary>
        public static void Initialize(UIApplication uiapp, Uri baseAddress)
        {
            try
            {
                // ★ 追加: 環境変数 or 引数で BaseAddress を確実に設定
                if (baseAddress == null)
                {
                    var fromEnv = Environment.GetEnvironmentVariable("REVIT_MCP_BASE");
                    if (!string.IsNullOrWhiteSpace(fromEnv))
                        baseAddress = new Uri(fromEnv, UriKind.Absolute);
                    else
                    {
                        var port = Environment.GetEnvironmentVariable("REVIT_MCP_PORT");
                        if (string.IsNullOrWhiteSpace(port)) port = "5210";
                        baseAddress = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
                    }
                }
                _http.Value.BaseAddress = baseAddress;
            }
            catch { /* ignore */ }

            if (uiapp != null && !_idlingHooked)
            {
                _uiapp = uiapp;
                uiapp.Idling += OnIdling;
                _idlingHooked = true;
                RevitLogger.Info("[ASYNC] LongOpEngine: Idling hooked.");
            }
        }

        // ------------------ キュー投入 ------------------
        /// <summary>
        /// 非同期ジョブをキューに投入（即時戻り）。戻り値は JobId。
        /// </summary>
        public static string Enqueue(long rpcId, string method, JObject @params, string sessionKey, int startIndex)
        {
            var job = new Job
            {
                RpcId = rpcId,
                Method = method ?? "",
                Params = (@params != null ? (JObject)@params.DeepClone() : new JObject()),
                NextIndex = startIndex,
                SessionKey = sessionKey ?? ""
            };
            _queue.Enqueue(job);
            _active[job.JobId] = job;

            RevitLogger.Info($"[ASYNC] Enqueued job {job.JobId} method={job.Method} nextIndex={job.NextIndex}");
            return job.JobId;
        }

        // ------------------ Idling 実行本体 ------------------
        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (_uiapp == null) return;
            var doc = (_uiapp.ActiveUIDocument != null) ? _uiapp.ActiveUIDocument.Document : null;
            if (doc == null) return;

            if (LOG_TICK)
            {
                long now = Environment.TickCount & int.MaxValue;
                if (now - _lastTickLogMs >= 1000)
                {
                    RevitLogger.Info("[ASYNC] LongOpEngine: Idling tick.");
                    _lastTickLogMs = now;
                }
            }

            Job job;
            if (!_queue.TryDequeue(out job)) return;

            // 取り出したことをログ（節度）
            RevitLogger.Info($"[ASYNC] Dequeued job {job.JobId} method={job.Method} nextIndex={job.NextIndex}");

            try
            {
                // この tick（フレーム）で使える時間上限（既定 20s、最低 5s）
                int maxMs = Math.Max(5000, job.Params.Value<int?>("maxMillisPerPass") ?? 20000);

                if (job.Method == "export_dwg_by_param_groups")
                {
                    var result = ExportDwgByParamGroupsTick.Run(_uiapp, job, maxMs);

                    job.Phase = result.phase ?? job.Phase;
                    if (result.nextIndex.HasValue) job.NextIndex = result.nextIndex.Value;
                    job.TotalGroups = result.totalGroups;
                    job.Processed += result.processed;

                    // 進捗または完了を返す
                    PostResult(job.RpcId, new
                    {
                        ok = result.ok,
                        outputs = result.outputs,
                        skipped = result.skipped,
                        done = result.done,
                        nextIndex = result.nextIndex,
                        totalGroups = result.totalGroups,
                        processed = result.processed,
                        elapsedMs = result.elapsedMs,
                        phase = result.phase,
                        msg = result.msg,
                        jobId = job.JobId
                    });

                    if (!result.done)
                    {
                        // まだ残っていれば再キュー
                        _queue.Enqueue(job);
                    }
                    else
                    {
                        // 完了なので取り外し
                        Job _; _active.TryRemove(job.JobId, out _);
                        RevitLogger.Info($"[ASYNC] Job done {job.JobId} processed={job.Processed}/{job.TotalGroups}");
                    }
                }
                else
                {
                    // 未対応メソッド
                    PostResult(job.RpcId, new { ok = false, msg = "Unsupported async method: " + job.Method, jobId = job.JobId });
                    Job _; _active.TryRemove(job.JobId, out _);
                }
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                PostResult(job.RpcId, new { ok = false, msg = ex.Message, jobId = job.JobId });
                Job _; _active.TryRemove(job.JobId, out _);
                RevitLogger.Warn($"[ASYNC] Job error {job.JobId}: {ex}");
            }
        }

        // ------------------ /post_result 送信 ------------------
        private static void PostResult(long rpcId, object payload)
        {
            try
            {
                var body = new JObject { ["jsonrpc"] = "2.0", ["id"] = rpcId, ["result"] = JToken.FromObject(payload) };
                var json = JsonConvert.SerializeObject(body, Formatting.None);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    // ★ 相対パスでOK（BaseAddress必須）
                    var res = _http.Value.PostAsync("post_result", content).GetAwaiter().GetResult();
                    RevitLogger.Info($"[ASYNC] post_result => {(int)res.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                RevitLogger.Warn("[ASYNC] post_result failed (fallback to store): " + ex.Message);
                // ★ フォールバック保存（pull用）
                var jobId = (payload as JObject)?["jobId"]?.ToString();
                if (!string.IsNullOrEmpty(jobId)) ResultStore.Put(jobId, payload);
            }
        }
    }
}
