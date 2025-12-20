// ================================================================
// File: McpGuiBridge.cs – GUIブリッジ（ロード表示・進捗表示・旧新シグネチャ両対応）
// Target: net8.0-windows, AutoCAD GUI (acmgd + acdbmgd)
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application; // GUI Application
using AcRt = Autodesk.AutoCAD.Runtime;                         // IExtensionApplication / CommandMethod

namespace MergeDwgsPlugin
{
    /// <summary>
    /// AutoCAD GUI に NETLOAD されたときに起動するブリッジ。
    /// - ロード時に "loaded successfully" を表示
    /// - サーバーを定期ポーリング（/pending_request）
    /// - 取得ジョブの実行を MergeDwgsGuiCommand.* に移譲
    /// - 実行進捗を Editor とログへ出力
    /// - 実行結果を /post_result に返却
    /// </summary>
    public class McpGuiBridge : AcRt.IExtensionApplication
    {
        private static readonly HttpClient _http = new HttpClient();
        private static readonly JsonSerializerOptions _jsonOpt = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private static Timer? _pollTimer;
        private static volatile bool _busy;
        private static string _serverBase = "http://127.0.0.1:5210"; // 環境変数 AUTOCAD_MCP_GUI_SERVER で上書き可

        public void Initialize()
        {
            try
            {
                var ver = typeof(McpGuiBridge).Assembly.GetName().Version?.ToString() ?? "unknown";
                GuiLog.Info($"AutoCadGuiAddin loaded successfully (v{ver}).");
                GuiLog.Info($"Log: {GuiLog.LogPath}");

                // Idle の存在チェック（GUI前提 acmgd）
                try
                {
                    var appType = Type.GetType("Autodesk.AutoCAD.ApplicationServices.Application, acmgd", throwOnError: false);
                    var idleEv = appType?.GetEvent("Idle", BindingFlags.Public | BindingFlags.Static);
                    if (idleEv != null)
                        GuiLog.Info("Application.Idle is available (acmgd). GUI polling ready.");
                    else
                        GuiLog.Warn("Application.Idle not found on acmgd Application. (If this is CoreConsole, Idle is unavailable.)");
                }
                catch (System.Exception ex)
                {
                    GuiLog.Warn("Idle probing failed: " + ex.Message);
                }

                var env = Environment.GetEnvironmentVariable("AUTOCAD_MCP_GUI_SERVER");
                if (!string.IsNullOrWhiteSpace(env)) _serverBase = env.Trim();
                GuiLog.Info($"MCP server base = {_serverBase}");

                // 2秒間隔でポーリング
                _pollTimer = new Timer(_ => PollOnceSafe(), null, 2000, 2000);

                GuiLog.Info("McpGuiBridge is running. Waiting for pending jobs...");
            }
            catch (System.Exception ex)
            {
                GuiLog.Error("Initialize failed: " + ex);
                throw; // AutoCAD に初期化失敗を伝える
            }
        }

        public void Terminate()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            GuiLog.Info("McpGuiBridge terminated.");
        }

        // 手動確認用コマンド（Editor に現在状態を出力）
        [AcRt.CommandMethod("MCPSTATUS")]
        public void CmdStatus()
        {
            GuiLog.Info("MCPSTATUS:");
            GuiLog.Info($"  server = {_serverBase}");
            GuiLog.Info($"  busy   = {_busy}");
            GuiLog.Info("  See log file for details.");
        }

        // ──────────────────────────────────────────────────────────
        // Poller
        // ──────────────────────────────────────────────────────────
        private static async void PollOnceSafe()
        {
            if (_busy) return;
            try
            {
                _busy = true;
                await PollOnceCore();
            }
            catch (System.Exception ex)
            {
                GuiLog.Error("Polling error: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private static async Task PollOnceCore()
        {
            JsonNode? job = null;
            try
            {
                var res = await _http.GetAsync($"{_serverBase}/pending_request?agent=acad&accept=merge_dwgs_perfile_rename");
                if (!res.IsSuccessStatusCode) return;
                var txt = await res.Content.ReadAsStringAsync();
                job = JsonNode.Parse(txt);
            }
            catch (System.Exception ex)
            {
                GuiLog.Warn("Pending request fetch failed: " + ex.Message);
                return;
            }
            if (job is null) return;

            var id = job?["id"]?.GetValue<string>() ?? "(no-id)";
            var meth = job?["method"]?.GetValue<string>() ?? "(no-method)";
            var p = job?["params"] as JsonObject ?? new JsonObject();

            GuiLog.Step($"Job claimed: id={id}, method={meth}");

            try
            {
                if (string.Equals(meth, "merge_dwgs_perfile_rename", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(meth, "merge_dwgs", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleMergeDwgsAsync(id, p);
                }
                else
                {
                    GuiLog.Warn($"Unsupported method: {meth}. Posting error...");
                    await PostResultAsync(id, new { ok = false, error = new { code = "E_UNSUPPORTED", message = $"Unsupported method {meth}" } });
                }
            }
            catch (System.Exception ex)
            {
                GuiLog.Error("Job failed: " + ex);
                await PostResultAsync(id, new { ok = false, error = new { code = "E_UNEXPECTED", message = ex.Message } });
            }
        }

        // ──────────────────────────────────────────────────────────
        // Merge dispatcher（旧／新シグネチャ両対応）
        // ──────────────────────────────────────────────────────────
        private static async Task HandleMergeDwgsAsync(string id, JsonObject p)
        {
            GuiLog.Step("Preparing GUI merge...");
            string seed = p?["seed"]?.GetValue<string>() ?? "";
            string output = p?["output"]?.GetValue<string>() ?? "";
            var inputsArr = p?["inputs"] as JsonArray ?? new JsonArray();

            GuiLog.Step($"Seed:   {seed}");
            GuiLog.Step($"Output: {output}");
            GuiLog.Step($"Inputs: {inputsArr.Count} file(s)");

            bool ok = false;
            string? msg = null;

            try
            {
                ok = InvokeMergeDwgsCommand(p, s => GuiLog.Step(s), out msg);
            }
            catch (System.Exception ex)
            {
                msg = ex.Message;
                ok = false;
            }

            if (ok)
            {
                GuiLog.Step("Merge finished successfully.");
                await PostResultAsync(id, new { ok = true, output });
            }
            else
            {
                GuiLog.Error("Merge failed: " + (msg ?? "unknown"));
                await PostResultAsync(id, new { ok = false, error = new { code = "E_MERGE_FAIL", message = msg ?? "unknown" } });
            }
        }

        /// <summary>
        /// MergeDwgsGuiCommand.DoMergePublic の **新旧両方のシグネチャ**をリフレクションで探して呼び出す。
        /// - 新: bool DoMergePublic(JsonObject p, Action&lt;string&gt;? progress, out string? message)
        /// - 旧: bool DoMergePublic(object rawParams, string[] inputs, string seed, string output, IEnumerable&lt;string&gt; include, HashSet&lt;string&gt; exclude)
        /// </summary>
        private static bool InvokeMergeDwgsCommand(JsonObject p, Action<string>? progress, out string? message)
        {
            message = null;

            // 1) アセンブリ内から "MergeDwgsGuiCommand" を探す（名前空間が異なる場合に備え suffix マッチ）
            var asm = typeof(McpGuiBridge).Assembly;
            var type = asm.GetTypes().FirstOrDefault(t => t.Name.Equals("MergeDwgsGuiCommand", StringComparison.Ordinal))
                    ?? asm.GetTypes().FirstOrDefault(t => t.Name.EndsWith("MergeDwgsGuiCommand", StringComparison.Ordinal));

            if (type == null)
                throw new System.MissingMemberException("MergeDwgsGuiCommand type not found in current add-in assembly.");

            // 2) 新シグネチャ優先
            var miNew = type.GetMethod("DoMergePublic", BindingFlags.Public | BindingFlags.Static,
                                       binder: null,
                                       types: new[] { typeof(JsonObject), typeof(Action<string>), typeof(string).MakeByRefType() },
                                       modifiers: null);
            if (miNew != null)
            {
                object?[] args = new object?[] { p, progress, null };
                var ok = (bool)miNew.Invoke(null, args)!;
                message = (string?)args[2];
                return ok;
            }

            // 3) 旧シグネチャにフォールバック
            var miOld = type.GetMethod("DoMergePublic", BindingFlags.Public | BindingFlags.Static,
                                       binder: null,
                                       types: new[]
                                       {
                                           typeof(object),
                                           typeof(string[]),
                                           typeof(string),
                                           typeof(string),
                                           typeof(System.Collections.Generic.IEnumerable<string>),
                                           typeof(System.Collections.Generic.HashSet<string>)
                                       },
                                       modifiers: null);
            if (miOld != null)
            {
                // p から旧パラメータを起こす
                string seed = p?["seed"]?.GetValue<string>() ?? "";
                string output = p?["output"]?.GetValue<string>() ?? "";
                var inputs = (p?["inputs"] as JsonArray ?? new JsonArray())
                                .Select(n => n?["path"]?.GetValue<string>() ?? "")
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToArray();

                // rename.include / exclude をざっくり吸収（無ければデフォルト）
                var include = new[] { "*" };
                var exclude = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "0", "DEFPOINTS"
                };

                var ok = (bool)miOld.Invoke(null, new object?[] { (object)p, inputs, seed, output, include, exclude })!;
                message = null; // 旧には out msg が無い前提
                // 進捗を吐けないのでざっくり通知だけ
                if (progress != null)
                {
                    progress("Executed legacy DoMergePublic(...) overload.");
                }
                return ok;
            }

            throw new System.MissingMethodException("MergeDwgsGuiCommand.DoMergePublic overloads not found (neither new nor legacy).");
        }

        private static async Task PostResultAsync(string id, object payload)
        {
            var url = $"{_serverBase}/post_result?id={Uri.EscapeDataString(id)}";
            var jo = JsonSerializer.SerializeToNode(payload, _jsonOpt) as JsonObject ?? new JsonObject();
            jo["id"] = id;
            var json = jo.ToJsonString(_jsonOpt);
            var res = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            GuiLog.Info($"post_result[{id}] => {(int)res.StatusCode}");
        }
    }
}
