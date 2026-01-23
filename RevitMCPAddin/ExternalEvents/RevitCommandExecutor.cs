// ================================================================
// File: ExternalEvents/RevitCommandExecutor.cs  (logger unified)
// Purpose: ExternalEvent で Revit API コマンド実行。例外時も必ず post_result する
// Notes  :
//  - ログ出力は RevitLogger に一元化（addin_<port>.log）
//  - 旧シグネチャ (traceLogPath 引数あり) も後方互換で維持（引数は無視）
//  - ★ 次回 1 回だけ適用する可変タイムアウトに対応（SetNextTimeoutMs → UiEventPump へ橋渡し）
// ================================================================
#nullable enable
using System;
using System.Net.Http;
using System.Text;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.ExternalEvents
{
    public class RevitCommandExecutor : IExternalEventHandler
    {
        private readonly CommandRouter _router;
        private readonly HttpClient _client;

        private RequestCommand? _pending;

        // Optional: worker-provided callback to stop per-job heartbeat quickly after result is posted
        public Action<string>? StopHeartbeatCallback { get; set; }

        // --- 追加：次回 1 回だけ適用する待機時間（ms）。明示されなければ既定扱い ---
        private int _nextTimeoutMs = 0; // 0=未指定（UiEventPump 側の既定/従来どおり）

        // 新シグネチャ
        public RevitCommandExecutor(CommandRouter router, HttpClient client)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        // 旧シグネチャ（後方互換・traceLogPathは受け取るが使用しない）
        public RevitCommandExecutor(CommandRouter router, HttpClient client, string traceLogPath)
            : this(router, client)
        {
            // NO-OP: ログは RevitLogger に集約するため traceLogPath は使わない
        }

        public void SetCommand(RequestCommand cmd) => _pending = cmd;

        /// <summary>
        /// 次回 1 回だけの UiEventPump 待機時間を設定（Worker などから呼ぶ）
        /// </summary>
        public void SetNextTimeoutMs(int ms)
        {
            if (ms < 10_000) ms = 10_000;
            if (ms > 3_600_000) ms = 3_600_000;
            _nextTimeoutMs = ms;
            // ★ 即座に UiEventPump に橋渡し（以後、最初の Invoke 系呼び出しで消費される）
            UiEventPump.Instance.SetNextTimeoutMs(_nextTimeoutMs);
        }

        public void Execute(UIApplication app)
        {
            try { LongOpEngine.Initialize(app, baseAddress: null); } catch { /* ignore */ }

            SafeTrace("[EXEC] begin");

            if (_pending == null)
            {
                SafeTrace("[EXEC] no pending cmd");
                return;
            }

            var cmd = _pending;
            _pending = null;

            SafeTrace($"[EXEC] command={cmd.Command ?? "(null)"} id={(cmd.Id != null ? cmd.Id.ToString() : "null")}");

            object resultObj;
            try
            {
                if (string.IsNullOrWhiteSpace(cmd.Command))
                    throw new InvalidOperationException("Command name is empty (method/command not provided).");

                // ルーター実行
                SafeTrace("[EXEC] route start");
                resultObj = _router.Route(app, cmd);
                SafeTrace("[EXEC] route end");
            }
            catch (Exception ex)
            {
                SafeWarn("[EXEC] route exception: " + ex);
                var fail = RpcResultEnvelope.Fail("UNHANDLED_EXCEPTION", "Unhandled exception in command.", data: new { exception = ex.GetType().Name });
                fail["detail"] = ex.ToString();
                resultObj = RpcResultEnvelope.StandardizePayload(fail, app, cmd.Command, revitMs: 0);
            }

            // JSON-RPC result
            var payload = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = cmd.Id ?? JValue.CreateNull(),
                ["result"] = JToken.FromObject(resultObj)
            };

            try
            {
                SafeTrace("[EXEC] post_result start");
                string jsonBody = JsonNetCompat.ToCompactJson(payload);
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var res = _client.PostAsync("post_result", content).GetAwaiter().GetResult();
                SafeTrace($"[EXEC] post_result done: {(int)res.StatusCode}");

                // Proactively stop heartbeat for this rpcId once result is posted
                try { StopHeartbeatCallback?.Invoke(cmd.Id != null ? cmd.Id.ToString() : null); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                SafeWarn("[EXEC] post_result exception: " + ex);
            }

            // オプション: コマンド記録（失敗しても処理継続）
            try
            {
                if (CommandLogWriter.Enabled)
                {
                    var meta = resultObj as ICommandLogMeta;

                    var entry = new CommandLogEntry
                    {
                        Ts = DateTimeOffset.Now,
                        Session = System.Diagnostics.Process.GetCurrentProcess().Id.ToString(),
                        User = $"{Environment.MachineName}\\{Environment.UserName}",
                        Command = cmd.Command,
                        Params = (object)cmd.Params, // JObject 前提
                        Summary = meta?.Summary ?? BuildAutoSummary(cmd.Command, cmd.Params, resultObj),
                        AffectedElementIds = meta != null
                            ? new System.Collections.Generic.List<int>(meta.AffectedIds)
                            : ExtractAffectedIds(resultObj),
                        Result = resultObj,
                        Replay = new ReplaySnippet { Method = cmd.Command, Params = meta?.ReplayParams ?? (object)cmd.Params },
                        Before = meta?.Before
                    };

                    CommandLogWriter.Append(entry);
                    SafeTrace("[EXEC] command-log appended");
                }
            }
            catch (Exception ex)
            {
                SafeWarn("[EXEC] command-log exception: " + ex);
            }

            SafeTrace("[EXEC] end");
        }

        public string GetName() => "RevitMCP Command Executor";

        // ----------------- ログユーティリティ（RevitLogger へ集約） -----------------
        private static void SafeTrace(string msg) => RevitLogger.Info(msg);
        private static void SafeWarn(string msg) => RevitLogger.Warn(msg);
        private static void SafeError(string msg) => RevitLogger.Error(msg);

        // ----------------- 補助 -----------------
        private static System.Collections.Generic.List<int> ExtractAffectedIds(object result)
        {
            try
            {
                var jo = JObject.FromObject(result);
                var arr = jo["affectedIds"] as JArray;
                if (arr == null) return new System.Collections.Generic.List<int>();
                var ids = new System.Collections.Generic.List<int>();
                foreach (var x in arr)
                {
                    if (x.Type == JTokenType.Integer) ids.Add((int)x);
                }
                return ids;
            }
            catch { return new System.Collections.Generic.List<int>(); }
        }

        private static string BuildAutoSummary(string? command, JObject? @params, object result)
        {
            try
            {
                var p = @params ?? new JObject();
                var leaf = CommandNaming.Leaf(command ?? string.Empty).ToLowerInvariant();
                switch (leaf)
                {
                    case "get_project_info":
                        return "プロジェクト情報を取得";
                    case "get_wall_baseline":
                        return $"壁のベースラインを取得（elementId={p.Value<int?>("elementId") ?? 0}）";
                    case "move_element":
                        return $"要素 #{p.Value<int?>("elementId") ?? 0} を移動";
                    case "update_element":
                        return $"要素 #{p.Value<int?>("elementId") ?? 0} のパラメータ '{p.Value<string>("param")}' を更新";
                    default:
                        return command ?? "(unknown)";
                }
            }
            catch { return command ?? "(unknown)"; }
        }
    }
}
