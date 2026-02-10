// ================================================================
// File: Core/CommandRouter.cs (Safe Router - Full Text)
// Purpose:
//  - params.documentPath が来たら該当ドキュメントをアクティブ化
//  - uniqueId / uniqueIds（および target / targets 内）を elementId / elementIds に自動変換
//  - roomId / areaId / spaceId / gridId / wallId / doorId / windowId / floorId 等の
//    "エイリアスID" を elementId に自動昇格（top, target, targets[] の各コンテナで）
//  - （後方互換）Area/Room/Space系メソッドでは elementId → areaId/roomId/spaceId を自動補完
//  - 想定外や未解決時は { ok:false, errorCode, msg } を返す
//  - NEW-A: ルータ横断のスコープ相互排他（doc/view/elem/agent/global）+ タイムアウト
//  - NEW-B: UI実行の一元化（UiEventPump.InvokeSmart）
//  - NEW-C: JSON-RPC 包装（method/agentId を必ずエコー）※既にJSON-RPC形は素通し
//  - NEW-D: すべての例外を JSON-RPC error で返却（HTTP 200）→ 致命化防止
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;  // NEW
using System.Threading;               // NEW
using System.Diagnostics;             // Step1 timings
using System.Reflection;              // Step2 command metadata (aliases)
using System.Security.Cryptography;   // Step9 confirmToken fingerprint
using System.Text;                    // Step9 confirmToken fingerprint
using RevitMCPAddin.Core.Failures;    // FailureHandling (whitelist)
using RevitMCPAddin.Core.Progress;    // ProgressHub (CodexGUI progress)

namespace RevitMCPAddin.Core
{
    public class CommandRouter
    {
        private readonly Dictionary<string, IRevitCommandHandler> _handlers;
        // Step 4: canonical alias -> legacy dispatch mapping (for handler branching safety)
        private readonly Dictionary<string, string> _aliasToDispatch;

        public CommandRouter(IEnumerable<IRevitCommandHandler> handlers)
        {
            var map = new Dictionary<string, IRevitCommandHandler>(StringComparer.OrdinalIgnoreCase);
            var dupList = new List<string>();
            var aliasToDispatch = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in handlers ?? Enumerable.Empty<IRevitCommandHandler>())
            {
                var name = h?.CommandName ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                var methodsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dispatchMethods = new List<string>();
                foreach (var s in name.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(x => x.Trim())
                                      .Where(x => !string.IsNullOrEmpty(x)))
                {
                    methodsSet.Add(s);
                    dispatchMethods.Add(s);
                }

                // Step 2: attribute-driven aliases/name (optional; no behavior change unless attribute is present)
                try
                {
                    var attr = h.GetType().GetCustomAttribute<RpcCommandAttribute>(inherit: true);
                    if (attr != null)
                    {
                        if (!string.IsNullOrWhiteSpace(attr.Name))
                            methodsSet.Add(attr.Name.Trim());
                        if (attr.Aliases != null)
                        {
                            foreach (var a in attr.Aliases)
                            {
                                var aa = (a ?? "").Trim();
                                if (!string.IsNullOrWhiteSpace(aa))
                                    methodsSet.Add(aa);
                            }
                        }
                    }
                }
                catch { /* best-effort */ }

                // Step 4: add domain-first canonical names as callable aliases, and remember how to dispatch them.
                try
                {
                    var t = h.GetType();
                    foreach (var d in dispatchMethods.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var canon = CommandNaming.GetCanonical(d, t);
                        if (string.IsNullOrWhiteSpace(canon)) continue;
                        if (string.Equals(canon, d, StringComparison.OrdinalIgnoreCase)) continue;

                        methodsSet.Add(canon);
                        if (!aliasToDispatch.ContainsKey(canon))
                            aliasToDispatch[canon] = d;
                    }

                    // Attribute-driven name/aliases can also be treated as aliases when the handler has a single dispatch method.
                    if (dispatchMethods.Count == 1)
                    {
                        var d0 = dispatchMethods[0];
                        var attr = t.GetCustomAttribute<RpcCommandAttribute>(inherit: true);
                        if (attr != null)
                        {
                            if (!string.IsNullOrWhiteSpace(attr.Name))
                            {
                                var a0 = attr.Name.Trim();
                                if (!string.IsNullOrWhiteSpace(a0) && !string.Equals(a0, d0, StringComparison.OrdinalIgnoreCase) && !aliasToDispatch.ContainsKey(a0))
                                    aliasToDispatch[a0] = d0;
                            }

                            if (attr.Aliases != null)
                            {
                                foreach (var a in attr.Aliases)
                                {
                                    var aa = (a ?? "").Trim();
                                    if (string.IsNullOrWhiteSpace(aa)) continue;
                                    if (string.Equals(aa, d0, StringComparison.OrdinalIgnoreCase)) continue;
                                    if (!aliasToDispatch.ContainsKey(aa))
                                        aliasToDispatch[aa] = d0;
                                }
                            }
                        }
                    }
                }
                catch { /* best-effort */ }

                foreach (var m in methodsSet)
                {
                    if (map.ContainsKey(m))
                    {
                        dupList.Add($"{m} -> [{map[m].GetType().Name}] & [{h.GetType().Name}]");
                        // 先勝ち（既存の割当を尊重）
                        continue;
                    }
                    map[m] = h;
                }
            }

            if (dupList.Count > 0)
            {
                var msg = string.Join("\r\n", dupList);
                System.Diagnostics.Debug.WriteLine("[CommandRouter] Duplicate method bindings:\r\n" + msg);
            }

            _handlers = map;
            _aliasToDispatch = aliasToDispatch;
        }

        // ----------------------------------------------------------------
        // ルータ横断の相互排他・包装ヘルパ
        // ----------------------------------------------------------------

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _scopeGates
            = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        private static SemaphoreSlim GetGate(string key)
            => _scopeGates.GetOrAdd(string.IsNullOrWhiteSpace(key) ? "global" : key, _ => new SemaphoreSlim(1, 1));

        // params/meta から最も具体的なスコープ鍵を選ぶ: docGuid → viewId → elementId → agentId → global
        private static string ResolveScopeKey(RequestCommand cmd)
        {
            var p = cmd?.Params as JObject;

            var docGuid = p?.Value<string>("docGuid");
            if (string.IsNullOrWhiteSpace(docGuid))
                docGuid = p?.Value<string>("docKey") ?? p?.Value<string>("doc_key");
            if (!string.IsNullOrWhiteSpace(docGuid)) return "doc:" + docGuid;

            var viewId = p?.Value<int?>("viewId");
            if (viewId.HasValue && viewId.Value > 0) return "view:" + viewId.Value.ToString();

            var elemId = p?.Value<int?>("elementId");
            if (elemId.HasValue && elemId.Value > 0) return "elem:" + elemId.Value.ToString();

            string? agentId = null;
            try { agentId = p?.Value<string>("agentId"); } catch { /* ignore */ }
            if (agentId.IsNullOrEmpty() && cmd?.MetaRaw is JObject metaJo)
            {
                try { agentId = metaJo.Value<string>("agentId"); } catch { /* ignore */ }
            }
            if (!string.IsNullOrWhiteSpace(agentId)) return "agent:" + agentId;

            return "global";
        }

        // エラー応答用の agentId 抽出
        private static string GetAgentIdEcho(RequestCommand cmd)
        {
            var p = cmd?.Params as JObject;
            var a = p?.Value<string>("agentId")
                 ?? (cmd?.MetaRaw as JObject)?.Value<string>("agentId")
                 ?? "";
            return a ?? "";
        }

        private static string ExtractIdempotencyKey(RequestCommand cmd)
        {
            try
            {
                var p = cmd?.Params as JObject;
                var key = p?.Value<string>("idempotencyKey") ?? p?.Value<string>("idempotency_key");
                if (string.IsNullOrWhiteSpace(key) && cmd?.MetaRaw is JObject meta)
                    key = meta.Value<string>("idempotencyKey") ?? meta.Value<string>("idempotency_key");
                return (key ?? string.Empty).Trim();
            }
            catch { return string.Empty; }
        }

        private static string BuildIdemCacheKey(UIApplication uiapp, RequestCommand cmd, string methodEcho, string idemKey)
        {
            if (string.IsNullOrWhiteSpace(idemKey)) return string.Empty;
            string docKey = string.Empty;
            try
            {
                var p = cmd?.Params as JObject;
                docKey = p?.Value<string>("docGuid")
                    ?? p?.Value<string>("docKey")
                    ?? p?.Value<string>("doc_key")
                    ?? string.Empty;
                if (string.IsNullOrWhiteSpace(docKey))
                {
                    var doc = uiapp?.ActiveUIDocument?.Document;
                    if (doc != null)
                    {
                        string source;
                        docKey = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out source);
                    }
                }
            }
            catch { /* ignore */ }

            var key = $"{methodEcho}|{docKey}|{idemKey}";
            return key;
        }

        // 既に JSON-RPC 形なら素通し。そうでない場合にだけ method/agentId を付けて包む
        // ledger は optional（McpLedgerEngine が返す）
        private static object WrapJsonRpcIfNeeded(RequestCommand cmd, object raw, object ledger)
        {
            try
            {
                // JSON-RPC detection must be strict: many legacy payloads use a top-level "result" property.
                // Only treat as JSON-RPC when "jsonrpc" exists.
                if (raw is JObject jo && jo["jsonrpc"] != null)
                    return raw; // 既に JSON-RPC 形を返すハンドラはそのまま
            }
            catch { /* best effort */ }

            string method = cmd?.Command ?? "";
            string agentId = GetAgentIdEcho(cmd);

            // ledger が null のときは payload に入れない（後方互換）
            if (ledger == null)
            {
                return new
                {
                    jsonrpc = "2.0",
                    id = cmd?.Id,
                    method = method,   // エコー: 取り違え検知
                    agentId = agentId, // エコー: マルチクライアント混線対策
                    result = raw       // 既存 { ok, ... } 等はそのまま内包（意味は不変）
                };
            }

            return new
            {
                jsonrpc = "2.0",
                id = cmd?.Id,
                method = method,   // エコー: 取り違え検知
                agentId = agentId, // エコー: マルチクライアント混線対策
                result = raw,      // 既存 { ok, ... } 等はそのまま内包（意味は不変）
                ledger = ledger
            };
        }

        // ----------------------------------------------------------------

        public object Route(UIApplication uiapp, RequestCommand cmd)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));

            if (string.IsNullOrWhiteSpace(cmd.Command))
            {
                return new
                {
                    ok = false,
                    errorCode = "ERR_NO_METHOD",
                    msg = "Command(method) 名が空です。JSON-RPC の 'method' で送ってください。",
                    fix = new
                    {
                        exampleRight = new { jsonrpc = "2.0", method = "get_project_info", @params = new { }, id = 1 }
                    }
                };
            }

            // NEW: params 教示ガード（空 {} は許容。存在しない/オブジェクト以外はNG）
            if (cmd.Params == null || cmd.Params.Type != JTokenType.Object)
            {
                var example = new
                {
                    jsonrpc = "2.0",
                    method = cmd.Command ?? "(method)",
                    @params = new { },
                    id = 1
                };
                return new
                {
                    ok = false,
                    errorCode = "ERR_MISSING_PARAMS",
                    msg = "このコマンドは JSON-RPC の params:{.} に引数を包んで送ってください。",
                    error = new
                    {
                        humanMessage = "やさしいお願い：引数はすべて params に入れてね。トップレベル直置きは拾えません。"
                    },
                    fix = new { exampleRight = example }
                };
            }

            // Step 4: canonical alias -> legacy dispatch translation (for handler branching safety)
            var invokedMethod = (cmd.Command ?? string.Empty).Trim();
            var dispatchMethod = invokedMethod;
            var translated = false;
            if (!string.IsNullOrWhiteSpace(invokedMethod) && _aliasToDispatch != null)
            {
                if (_aliasToDispatch.TryGetValue(invokedMethod, out var mapped) &&
                    !string.IsNullOrWhiteSpace(mapped) &&
                    !string.Equals(mapped, invokedMethod, StringComparison.OrdinalIgnoreCase))
                {
                    dispatchMethod = mapped.Trim();
                    cmd.Command = dispatchMethod;
                    translated = true;

                    // Preserve both names for logs/ledger (agent-friendly)
                    try
                    {
                        var meta = cmd.MetaRaw as JObject;
                        if (meta == null) { meta = new JObject(); cmd.MetaRaw = meta; }
                        if (meta["invokedMethod"] == null) meta["invokedMethod"] = invokedMethod;
                        if (meta["dispatchMethod"] == null) meta["dispatchMethod"] = dispatchMethod;
                    }
                    catch { /* ignore */ }
                }
            }

            var methodEcho = translated ? invokedMethod : (cmd.Command ?? string.Empty);

            try
            {
                // ① documentPath が来ていたらアクティブドキュメント切替
                TryActivateDocument(uiapp, cmd);

            // ② uniqueId / uniqueIds & 各種エイリアスID → elementId / elementIds の前処理（＋後方互換バックフィル）
            var early = PreprocessIdsAndAliases(uiapp, cmd);
            if (early != null) return early;

            // ③ よくあるキーのエイリアス正規化（軽微）＋配列正規化（elementIds/uniqueIds/categoryIds）
            var pObj = cmd.Params as JObject;
            NormalizeCommandSpecificAliases(cmd.Command, pObj);
            // Generic array normalization: tolerate scalar -> array for common keys
            try
            {
                if (pObj != null)
                {
                    NormalizeArray(pObj, "elementIds");
                    NormalizeArray(pObj, "uniqueIds");
                    NormalizeArray(pObj, "categoryIds");
                    // Also normalize nested 'params' when wrapped (e.g., smoke_test)
                    if (pObj["params"] is JObject inner)
                    {
                        NormalizeArray(inner, "elementIds");
                        NormalizeArray(inner, "uniqueIds");
                        NormalizeArray(inner, "categoryIds");
                    }
                }
            }
            catch { /* best-effort; never fail router normalization */ }

            // ④ コマンド別パラメータ検証（必須/OneOf/未知キー/エイリアス）
            var teachErr = CommandParamTeach.Guard(cmd.Command, cmd.Params as JObject);
            if (teachErr != null) return teachErr;

            // ④b Step 7: optional expectedContextToken guard (precondition)
            var ctxFail = ValidateExpectedContextToken(uiapp, cmd, methodEcho);
            if (ctxFail != null)
            {
                var standardized = RpcResultEnvelope.StandardizePayload(ctxFail, uiapp, methodEcho, revitMs: 0);
                if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                if (translated) cmd.Command = invokedMethod;
                return WrapJsonRpcIfNeeded(cmd, standardized, ledger: null);
            }

            // Step 8: idempotency key (duplicate suppression)
            string idemKey = ExtractIdempotencyKey(cmd);
            string idemCacheKey = string.Empty;
            if (!string.IsNullOrWhiteSpace(idemKey))
            {
                idemCacheKey = BuildIdemCacheKey(uiapp, cmd, methodEcho, idemKey);
                if (!string.IsNullOrWhiteSpace(idemCacheKey)
                    && IdempotencyResultCache.TryGet(idemCacheKey, out var cachedPayload, out var cachedLedger))
                {
                    var cached = cachedPayload ?? new JObject();
                    var ctx = cached["context"] as JObject ?? new JObject();
                    ctx["idempotencyKey"] = idemKey;
                    ctx["idempotencyCache"] = "hit";
                    cached["context"] = ctx;

                    if (translated) TryAnnotateDispatch(cached, invokedMethod, dispatchMethod);
                    if (translated) cmd.Command = invokedMethod;
                    return WrapJsonRpcIfNeeded(cmd, cached, cachedLedger);
                }
            }

            // ⑤ ディスパッチ（ここだけ“包む”。ハンドラは無改造）
            if (_handlers.TryGetValue(cmd.Command, out var handler))
            {
                var scopeKey = ResolveScopeKey(cmd);
                var gate = GetGate(scopeKey);

                // --- 相互排他 with タイムアウト（デッドロック予防）---
                const int LOCK_TIMEOUT_MS = 8000; // 必要なら設定化
                if (!gate.Wait(millisecondsTimeout: LOCK_TIMEOUT_MS))
                {
                    var fail = RpcResultEnvelope.Fail(
                        code: "CONCURRENCY_LOCK_TIMEOUT",
                        msg: $"{methodEcho}: concurrency lock timeout on scope '{scopeKey}' (>{LOCK_TIMEOUT_MS}ms)"
                    );
                    var standardized = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                    if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                    if (translated) cmd.Command = invokedMethod;
                    return WrapJsonRpcIfNeeded(cmd, standardized, ledger: null);
                }

                try
                {
                    // NOTE:
                    // CommandRouter.Route is invoked from RevitCommandExecutor (ExternalEvent) and therefore already runs
                    // in a valid Revit API context (UI thread). Using UiEventPump.InvokeSmart here can mask real exceptions
                    // and cause long timeouts due to nested ExternalEvent waits. Execute directly and surface errors.

                    var pObjLocal = cmd.Params as JObject ?? new JObject();
                    bool dryRunRequested = IsDryRunRequested(pObjLocal);

                    RpcCommandMeta meta = null;
                    try { CommandMetadataRegistry.TryGet(methodEcho, out meta); } catch { meta = null; }
                    bool isHighRisk = (meta != null && string.Equals(meta.risk, "high", StringComparison.OrdinalIgnoreCase))
                        || LooksLikeHighRiskMethod(methodEcho);
                    string confirmMethod = meta != null && !string.IsNullOrWhiteSpace(meta.name) ? meta.name : methodEcho;
                    string confirmFingerprint = isHighRisk ? ComputeConfirmFingerprint(pObjLocal) : string.Empty;

                    bool requireConfirmToken = isHighRisk && IsRequireConfirmToken(pObjLocal);
                    string confirmToken = isHighRisk ? ExtractConfirmToken(pObjLocal) : string.Empty;
                    bool confirmTokenAccepted = false;

                    // Optional confirmation handshake for high-risk commands.
                    if (!dryRunRequested && requireConfirmToken && string.IsNullOrWhiteSpace(confirmToken))
                    {
                        var fail = RpcResultEnvelope.Fail(
                            code: "CONFIRMATION_REQUIRED",
                            msg: $"{confirmMethod}: confirmToken is required. Call with dryRun=true to obtain a token, then retry with requireConfirmToken=true and confirmToken."
                        );
                        fail["data"] = new JObject
                        {
                            ["method"] = confirmMethod
                        };
                        fail["nextActions"] = new JArray
                        {
                            new JObject
                            {
                                ["method"] = confirmMethod,
                                ["reason"] = "Preview and obtain confirmToken via dryRun.",
                                ["params"] = new JObject { ["dryRun"] = true }
                            }
                        };
                        var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                        if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                        if (translated) cmd.Command = invokedMethod;
                        return WrapJsonRpcIfNeeded(cmd, stdFail, ledger: null);
                    }

                    if (!dryRunRequested && !string.IsNullOrWhiteSpace(confirmToken))
                    {
                        var docForConfirm = uiapp?.ActiveUIDocument?.Document;
                        if (docForConfirm == null)
                        {
                            var fail = RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document for confirmToken validation.");
                            var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                            if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                            if (translated) cmd.Command = invokedMethod;
                            return WrapJsonRpcIfNeeded(cmd, stdFail, ledger: null);
                        }

                        if (!ConfirmTokenService.TryValidate(confirmToken, confirmMethod, docForConfirm, confirmFingerprint, out var ctErr))
                        {
                            var fail = RpcResultEnvelope.Fail("CONFIRMATION_INVALID", ctErr, data: new { method = confirmMethod });
                            var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                            if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                            if (translated) cmd.Command = invokedMethod;
                            return WrapJsonRpcIfNeeded(cmd, stdFail, ledger: null);
                        }
                        confirmTokenAccepted = true;
                    }

                    // Step 10: batch failure-handling confirmation gate (>= 5 targets)
                    const int FAILURE_HANDLING_CONFIRM_THRESHOLD = 5;
                    int targetCount = EstimateProcessingTargetCount(pObjLocal);
                    var fhReq = FailureHandlingRequest.Parse(pObjLocal);
                    if (!dryRunRequested && targetCount >= FAILURE_HANDLING_CONFIRM_THRESHOLD && !fhReq.ExplicitProvided)
                    {
                        var st = FailureWhitelistService.GetStatus();

                        var fail = RpcResultEnvelope.Fail(
                            code: "FAILURE_HANDLING_CONFIRMATION_REQUIRED",
                            msg: $"{methodEcho}: processing {targetCount} targets. Confirm failureHandling before execution (default=rollback)."
                        );
                        fail["data"] = new JObject
                        {
                            ["targetCount"] = targetCount,
                            ["threshold"] = FAILURE_HANDLING_CONFIRM_THRESHOLD,
                            ["defaultMode"] = "rollback",
                            ["whitelist"] = JObject.FromObject(st)
                        };
                        fail["nextActions"] = new JArray
                        {
                            new JObject
                            {
                                ["method"] = methodEcho,
                                ["reason"] = "Run without failureHandling (not recommended for large batches).",
                                ["params"] = new JObject { ["failureHandling"] = new JObject { ["enabled"] = false } }
                            },
                            new JObject
                            {
                                ["method"] = methodEcho,
                                ["reason"] = "Enable failureHandling: rollback on any non-whitelisted failure (recommended default).",
                                ["params"] = new JObject { ["failureHandling"] = new JObject { ["enabled"] = true, ["mode"] = "rollback" } }
                            },
                            new JObject
                            {
                                ["method"] = methodEcho,
                                ["reason"] = "Enable failureHandling: delete whitelisted warnings; rollback on others.",
                                ["params"] = new JObject { ["failureHandling"] = new JObject { ["enabled"] = true, ["mode"] = "delete" } }
                            },
                            new JObject
                            {
                                ["method"] = methodEcho,
                                ["reason"] = "Enable failureHandling: try resolve (whitelist); fallback rollback when unresolved.",
                                ["params"] = new JObject { ["failureHandling"] = new JObject { ["enabled"] = true, ["mode"] = "resolve" } }
                            }
                        };

                        var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                        if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                        if (translated) cmd.Command = invokedMethod;
                        return WrapJsonRpcIfNeeded(cmd, stdFail, ledger: null);
                    }

                    // DryRun wrapper (high-risk): execute within an outer TransactionGroup and rollback.
                    TransactionGroup dryGroup = null;
                    bool dryRunApplied = false;
                    object raw = null;
                    object ledger = null;
                    long revitMs = 0;
                    FailureHandlingScope fhScope = null;
                    try
                    {
                        // Always capture failures/dialogs for this command.
                        // When mode=Off (failureHandling disabled), the scope is "capture-only" and does not alter behavior.
                        if (uiapp != null)
                            fhScope = new FailureHandlingScope(uiapp, fhReq.Mode);

                        if (dryRunRequested && isHighRisk)
                        {
                            var docForDryRun = uiapp?.ActiveUIDocument?.Document;
                            if (docForDryRun == null)
                            {
                                var fail = RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document for dryRun.");
                                var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                                if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                                if (translated) cmd.Command = invokedMethod;
                                return WrapJsonRpcIfNeeded(cmd, stdFail, ledger: null);
                            }

                            dryGroup = new TransactionGroup(docForDryRun, "[MCP] DryRun");
                            dryGroup.Start();
                            dryRunApplied = true;
                        }

                        var sw = Stopwatch.StartNew();
                        var exec = McpLedgerEngine.ExecuteWithLedger(uiapp, cmd, handler);

                        if (dryRunApplied)
                        {
                            try { dryGroup.RollBack(); } catch { /* ignore */ }
                        }

                        sw.Stop();
                        revitMs = sw.ElapsedMilliseconds;

                        raw = exec != null ? exec.RawResult : null;
                        ledger = exec != null ? (object)exec.LedgerInfo : null;
                    }
                    finally
                    {
                        try { fhScope?.Dispose(); } catch { /* ignore */ }
                        try { dryGroup?.Dispose(); } catch { /* ignore */ }
                    }

                    // Step 7: bump context revision after successful *write* execution (prevents drift).
                    try
                    {
                        var okFlag = ExtractOkFlag(raw);
                        if (!dryRunRequested && okFlag != false && ShouldBumpContextRevision(methodEcho))
                        {
                            var doc = uiapp?.ActiveUIDocument?.Document;
                            if (doc != null) ContextTokenService.BumpRevision(doc, "CommandExecuted");
                        }
                    }
                    catch { /* best-effort */ }

                    // Step 1: Standardize payload (non-breaking additive fields)
                    var standardized = RpcResultEnvelope.StandardizePayload(raw, uiapp, methodEcho, revitMs);

                    // Step 10: attach failureHandling diagnostics (agent-friendly, additive)
                    if (fhReq.ExplicitProvided)
                    {
                        standardized["failureHandling"] = new JObject
                        {
                            ["enabled"] = fhReq.Enabled,
                            ["mode"] = fhReq.Enabled ? fhReq.Mode.ToString().ToLowerInvariant() : "off",
                            ["targetCount"] = targetCount,
                            ["threshold"] = FAILURE_HANDLING_CONFIRM_THRESHOLD,
                            ["whitelist"] = JObject.FromObject(FailureWhitelistService.GetStatus())
                        };
                        if (fhReq.Enabled && fhScope != null)
                        {
                            try { ((JObject)standardized["failureHandling"])["issues"] = JObject.FromObject(fhScope.Issues); } catch { /* ignore */ }
                        }
                    }

                    // Always record failure details when rollback (or tx-not-committed) occurs.
                    TryAttachFailureIssuesOnRollback(standardized, methodEcho, fhReq, targetCount, FAILURE_HANDLING_CONFIRM_THRESHOLD, fhScope);

                    // Step 9: annotate dryRun + confirmation details for agents.
                    if (dryRunRequested)
                    {
                        standardized["dryRunRequested"] = true;
                        if (dryRunApplied)
                        {
                            standardized["dryRun"] = true;
                            standardized["dryRunApplied"] = "transactionGroup.rollback";
                            try { (standardized["warnings"] as JArray)?.Add("DryRun: executed and rolled back (no model changes persisted)."); } catch { /* ignore */ }
                        }
                        else if (standardized.Value<bool?>("dryRun") != true)
                        {
                            try { (standardized["warnings"] as JArray)?.Add("dryRun=true was requested, but no rollback was applied (command may not support dryRun)."); } catch { /* ignore */ }
                        }
                    }

                    if (confirmTokenAccepted)
                        standardized["confirmTokenAccepted"] = true;

                    if (dryRunRequested && isHighRisk && standardized.Value<bool?>("ok") == true)
                    {
                        var doc = uiapp?.ActiveUIDocument?.Document;
                        if (doc != null)
                        {
                            var issued = ConfirmTokenService.Issue(confirmMethod, doc, confirmFingerprint);
                            standardized["confirmToken"] = issued.Token;
                            standardized["confirmTokenExpiresAtUtc"] = issued.ExpiresAtUtc.ToString("o");
                            standardized["confirmTokenTtlSec"] = issued.TtlSec;
                            standardized["confirmTokenMethod"] = confirmMethod;
                        }
                    }

                    // Step 8: cache idempotent results (short-lived, process-local)
                    if (!string.IsNullOrWhiteSpace(idemCacheKey))
                    {
                        try
                        {
                            var ctx = standardized["context"] as JObject ?? new JObject();
                            ctx["idempotencyKey"] = idemKey;
                            ctx["idempotencyCache"] = "miss";
                            standardized["context"] = ctx;

                            IdempotencyResultCache.Set(idemCacheKey, standardized, ledger);
                        }
                        catch { /* ignore */ }
                    }
                    if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                    if (translated) cmd.Command = invokedMethod;

                    // JSON-RPC ラップ（既に JSON-RPC で返すハンドラは素通し）
                    return WrapJsonRpcIfNeeded(cmd, standardized, ledger);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    var fail = RpcResultEnvelope.Fail(code: "INVALID_PARAMS", msg: $"{methodEcho}: {ex.Message}", data: new { exception = ex.GetType().Name });
                    var standardized = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                    if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                    if (translated) cmd.Command = invokedMethod;
                    return WrapJsonRpcIfNeeded(cmd, standardized, ledger: null);
                }
                catch (InvalidOperationException ex)
                {
                    var fail = RpcResultEnvelope.Fail(code: "INVALID_OPERATION", msg: $"{methodEcho}: {ex.Message}", data: new { exception = ex.GetType().Name });
                    var standardized = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                    if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                    if (translated) cmd.Command = invokedMethod;
                    return WrapJsonRpcIfNeeded(cmd, standardized, ledger: null);
                }
                catch (Exception ex)
                {
                    var fail = RpcResultEnvelope.Fail(code: "UNHANDLED_EXCEPTION", msg: $"{methodEcho}: {ex.Message}", data: new { exception = ex.GetType().Name });
                    // Keep full detail only for logs; avoid bloating the response.
                    fail["detail"] = ex.ToString();
                    var standardized = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                    if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                    if (translated) cmd.Command = invokedMethod;
                    return WrapJsonRpcIfNeeded(cmd, standardized, ledger: null);
                }
                finally
                {
                    gate.Release();
                }
            }

            // Step 1: Prefer fail-result over throwing (consistent for agents)
            var unknown = RpcResultEnvelope.Fail(code: "UNKNOWN_COMMAND", msg: $"Unknown command: {methodEcho}");
            unknown["nextActions"] = new JArray
            {
                new JObject { ["method"] = "list_commands", ["reason"] = "List available commands and retry with a valid name." }
            };
            var unknownStd = RpcResultEnvelope.StandardizePayload(unknown, uiapp, methodEcho, revitMs: 0);
            if (translated) TryAnnotateDispatch(unknownStd, invokedMethod, dispatchMethod);
            if (translated) cmd.Command = invokedMethod;
            return WrapJsonRpcIfNeeded(cmd, unknownStd, ledger: null);
            }
            finally
            {
                // Ensure cmd.Command is restored for the caller (even when we dispatched to legacy name).
                if (translated) cmd.Command = invokedMethod;
            }
        }

        /// <summary>
        /// Internal execution path for batch-like orchestrators.
        /// - No concurrency gate (assumes caller already serialized execution).
        /// - No ledger wrapping (caller controls ledger scope).
        /// - No JSON-RPC wrapping (returns standardized payload only).
        /// </summary>
        internal JObject RouteInternalNoGateNoLedger(UIApplication uiapp, RequestCommand cmd)
        {
            if (cmd == null)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("INVALID_PARAMS", "Command is null."), uiapp, method: "", revitMs: 0);

            // Step 4: canonical alias -> legacy dispatch translation (for handler branching safety)
            var invokedMethod = (cmd.Command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(invokedMethod))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("ERR_NO_METHOD", "Command(method) is empty."), uiapp, method: "", revitMs: 0);

            var dispatchMethod = invokedMethod;
            var translated = false;
            if (_aliasToDispatch != null && _aliasToDispatch.TryGetValue(invokedMethod, out var mapped) &&
                !string.IsNullOrWhiteSpace(mapped) &&
                !string.Equals(mapped, invokedMethod, StringComparison.OrdinalIgnoreCase))
            {
                dispatchMethod = mapped.Trim();
                cmd.Command = dispatchMethod;
                translated = true;

                try
                {
                    var meta = cmd.MetaRaw as JObject;
                    if (meta == null) { meta = new JObject(); cmd.MetaRaw = meta; }
                    if (meta["invokedMethod"] == null) meta["invokedMethod"] = invokedMethod;
                    if (meta["dispatchMethod"] == null) meta["dispatchMethod"] = dispatchMethod;
                }
                catch { /* ignore */ }
            }

            var methodEcho = translated ? invokedMethod : (cmd.Command ?? string.Empty);

            try
            {
                // ① documentPath が来ていたらアクティブドキュメント切替
                TryActivateDocument(uiapp, cmd);

                // ② uniqueId / uniqueIds & 各種エイリアスID → elementId / elementIds の前処理（＋後方互換バックフィル）
                var early = PreprocessIdsAndAliases(uiapp, cmd);
                if (early != null)
                {
                    var stdEarly = RpcResultEnvelope.StandardizePayload(early, uiapp, methodEcho, revitMs: 0);
                    if (translated) TryAnnotateDispatch(stdEarly, invokedMethod, dispatchMethod);
                    return stdEarly;
                }

                // ③ 軽微正規化 + 配列正規化（elementIds/uniqueIds/categoryIds）
                var pObj = cmd.Params as JObject;
                NormalizeCommandSpecificAliases(cmd.Command, pObj);
                try
                {
                    if (pObj != null)
                    {
                        NormalizeArray(pObj, "elementIds");
                        NormalizeArray(pObj, "uniqueIds");
                        NormalizeArray(pObj, "categoryIds");
                        if (pObj["params"] is JObject inner)
                        {
                            NormalizeArray(inner, "elementIds");
                            NormalizeArray(inner, "uniqueIds");
                            NormalizeArray(inner, "categoryIds");
                        }
                    }
                }
                catch { /* ignore */ }

                // ④ コマンド別パラメータ検証
                var teachErr = CommandParamTeach.Guard(cmd.Command, cmd.Params as JObject);
                if (teachErr != null)
                {
                    var stdTeach = RpcResultEnvelope.StandardizePayload(teachErr, uiapp, methodEcho, revitMs: 0);
                    if (translated) TryAnnotateDispatch(stdTeach, invokedMethod, dispatchMethod);
                    return stdTeach;
                }

                // ④b Step 7: optional expectedContextToken guard (precondition)
                var ctxFail = ValidateExpectedContextToken(uiapp, cmd, methodEcho);
                if (ctxFail != null)
                {
                    var standardized = RpcResultEnvelope.StandardizePayload(ctxFail, uiapp, methodEcho, revitMs: 0);
                    if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                    return standardized;
                }

                // Step 8: idempotency key (duplicate suppression) for internal route
                string idemKey = ExtractIdempotencyKey(cmd);
                string idemCacheKey = string.Empty;
                if (!string.IsNullOrWhiteSpace(idemKey))
                {
                    idemCacheKey = BuildIdemCacheKey(uiapp, cmd, methodEcho, idemKey);
                    if (!string.IsNullOrWhiteSpace(idemCacheKey)
                        && IdempotencyResultCache.TryGet(idemCacheKey, out var cachedPayload, out var _))
                    {
                        var cached = cachedPayload ?? new JObject();
                        var ctx = cached["context"] as JObject ?? new JObject();
                        ctx["idempotencyKey"] = idemKey;
                        ctx["idempotencyCache"] = "hit";
                        cached["context"] = ctx;
                        if (translated) TryAnnotateDispatch(cached, invokedMethod, dispatchMethod);
                        return cached;
                    }
                }

                // ⑤ ディスパッチ（No gate / No ledger）
                if (_handlers.TryGetValue(cmd.Command, out var handler))
                {
                    var pObjLocal = cmd.Params as JObject ?? new JObject();
                    bool dryRunRequested = IsDryRunRequested(pObjLocal);

                    RpcCommandMeta meta = null;
                    try { CommandMetadataRegistry.TryGet(methodEcho, out meta); } catch { meta = null; }
                    bool isHighRisk = (meta != null && string.Equals(meta.risk, "high", StringComparison.OrdinalIgnoreCase))
                        || LooksLikeHighRiskMethod(methodEcho);
                    string confirmMethod = meta != null && !string.IsNullOrWhiteSpace(meta.name) ? meta.name : methodEcho;
                    string confirmFingerprint = isHighRisk ? ComputeConfirmFingerprint(pObjLocal) : string.Empty;

                    bool requireConfirmToken = isHighRisk && IsRequireConfirmToken(pObjLocal);
                    string confirmToken = isHighRisk ? ExtractConfirmToken(pObjLocal) : string.Empty;
                    bool confirmTokenAccepted = false;

                    if (!dryRunRequested && requireConfirmToken && string.IsNullOrWhiteSpace(confirmToken))
                    {
                        var fail = RpcResultEnvelope.Fail(
                            code: "CONFIRMATION_REQUIRED",
                            msg: $"{confirmMethod}: confirmToken is required. Call with dryRun=true to obtain a token, then retry with requireConfirmToken=true and confirmToken."
                        );
                        fail["data"] = new JObject { ["method"] = confirmMethod };
                        var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                        if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                        return stdFail;
                    }

                    if (!dryRunRequested && !string.IsNullOrWhiteSpace(confirmToken))
                    {
                        var docForConfirm = uiapp?.ActiveUIDocument?.Document;
                        if (docForConfirm == null)
                        {
                            var fail = RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document for confirmToken validation.");
                            var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                            if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                            return stdFail;
                        }

                        if (!ConfirmTokenService.TryValidate(confirmToken, confirmMethod, docForConfirm, confirmFingerprint, out var ctErr))
                        {
                            var fail = RpcResultEnvelope.Fail("CONFIRMATION_INVALID", ctErr, data: new { method = confirmMethod });
                            var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                            if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                            return stdFail;
                        }

                        confirmTokenAccepted = true;
                    }

                    // Step 10: batch failure-handling confirmation gate (>= 5 targets)
                    const int FAILURE_HANDLING_CONFIRM_THRESHOLD = 5;
                    int targetCount = EstimateProcessingTargetCount(pObjLocal);
                    var fhReq = FailureHandlingRequest.Parse(pObjLocal);
                    if (!dryRunRequested && targetCount >= FAILURE_HANDLING_CONFIRM_THRESHOLD && !fhReq.ExplicitProvided)
                    {
                        var st = FailureWhitelistService.GetStatus();
                        var fail = RpcResultEnvelope.Fail(
                            code: "FAILURE_HANDLING_CONFIRMATION_REQUIRED",
                            msg: $"{methodEcho}: processing {targetCount} targets. Confirm failureHandling before execution (default=rollback)."
                        );
                        fail["data"] = new JObject
                        {
                            ["targetCount"] = targetCount,
                            ["threshold"] = FAILURE_HANDLING_CONFIRM_THRESHOLD,
                            ["defaultMode"] = "rollback",
                            ["whitelist"] = JObject.FromObject(st)
                        };
                        fail["nextActions"] = new JArray
                        {
                            new JObject
                            {
                                ["method"] = methodEcho,
                                ["reason"] = "Run without failureHandling (not recommended for large batches).",
                                ["params"] = new JObject { ["failureHandling"] = new JObject { ["enabled"] = false } }
                            },
                            new JObject
                            {
                                ["method"] = methodEcho,
                                ["reason"] = "Enable failureHandling: rollback on any non-whitelisted failure (recommended default).",
                                ["params"] = new JObject { ["failureHandling"] = new JObject { ["enabled"] = true, ["mode"] = "rollback" } }
                            },
                            new JObject
                            {
                                ["method"] = methodEcho,
                                ["reason"] = "Enable failureHandling: delete whitelisted warnings; rollback on others.",
                                ["params"] = new JObject { ["failureHandling"] = new JObject { ["enabled"] = true, ["mode"] = "delete" } }
                            },
                            new JObject
                            {
                                ["method"] = methodEcho,
                                ["reason"] = "Enable failureHandling: try resolve (whitelist); fallback rollback when unresolved.",
                                ["params"] = new JObject { ["failureHandling"] = new JObject { ["enabled"] = true, ["mode"] = "resolve" } }
                            }
                        };
                        var stdFail = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                        if (translated) TryAnnotateDispatch(stdFail, invokedMethod, dispatchMethod);
                        return stdFail;
                    }

                    // Progress (best-effort): show indeterminate "busy" for all commands,
                    // and allow specific handlers to publish determinate progress via the same jobId.
                    string progressJobId = "";
                    try { progressJobId = cmd?.Id != null ? cmd.Id.ToString() : ""; } catch { progressJobId = ""; }
                    if (string.IsNullOrWhiteSpace(progressJobId))
                    {
                        try { progressJobId = Guid.NewGuid().ToString("N"); } catch { progressJobId = ""; }
                    }

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(progressJobId))
                            ProgressHub.Start(progressJobId, methodEcho, total: 0, tick: TimeSpan.FromSeconds(1));
                    }
                    catch { /* ignore */ }

                    FailureHandlingScope fhScope = null;
                    var sw = Stopwatch.StartNew();
                    object raw;
                    Exception handlerEx = null;
                    bool handlerThrew = false;
                    try
                    {
                        // Always capture failures/dialogs for this command.
                        // When mode=Off (failureHandling disabled), the scope is "capture-only" and does not alter behavior.
                        if (uiapp != null)
                            fhScope = new FailureHandlingScope(uiapp, fhReq.Mode);
                        raw = handler.Execute(uiapp, cmd);
                    }
                    catch (Exception ex)
                    {
                        handlerThrew = true;
                        handlerEx = ex;
                        throw;
                    }
                    finally
                    {
                        try { fhScope?.Dispose(); } catch { /* ignore */ }
                        sw.Stop();
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(progressJobId))
                            {
                                if (handlerThrew)
                                {
                                    var msg = handlerEx != null ? handlerEx.Message : "failed";
                                    ProgressHub.Finish(progressJobId, "failed: " + msg);
                                }
                                else
                                {
                                    ProgressHub.Finish(progressJobId, "done");
                                }
                            }
                        }
                        catch { /* ignore */ }
                    }

                    // Step 7: bump context revision after successful *write* execution (prevents drift).
                    try
                    {
                        var okFlag = ExtractOkFlag(raw);
                        if (!dryRunRequested && okFlag != false && ShouldBumpContextRevision(methodEcho))
                        {
                            var doc = uiapp?.ActiveUIDocument?.Document;
                            if (doc != null) ContextTokenService.BumpRevision(doc, "CommandExecuted");
                        }
                    }
                    catch { /* ignore */ }

                    var standardized = RpcResultEnvelope.StandardizePayload(raw, uiapp, methodEcho, sw.ElapsedMilliseconds);
                    if (fhReq.ExplicitProvided)
                    {
                        standardized["failureHandling"] = new JObject
                        {
                            ["enabled"] = fhReq.Enabled,
                            ["mode"] = fhReq.Enabled ? fhReq.Mode.ToString().ToLowerInvariant() : "off",
                            ["targetCount"] = targetCount,
                            ["threshold"] = FAILURE_HANDLING_CONFIRM_THRESHOLD,
                            ["whitelist"] = JObject.FromObject(FailureWhitelistService.GetStatus())
                        };
                        if (fhReq.Enabled && fhScope != null)
                        {
                            try { ((JObject)standardized["failureHandling"])["issues"] = JObject.FromObject(fhScope.Issues); } catch { /* ignore */ }
                        }
                    }

                    // Always record failure details when rollback (or tx-not-committed) occurs.
                    TryAttachFailureIssuesOnRollback(standardized, methodEcho, fhReq, targetCount, FAILURE_HANDLING_CONFIRM_THRESHOLD, fhScope);
                    if (dryRunRequested) standardized["dryRunRequested"] = true;
                    if (confirmTokenAccepted) standardized["confirmTokenAccepted"] = true;
                    if (!string.IsNullOrWhiteSpace(idemCacheKey))
                    {
                        try
                        {
                            var ctx = standardized["context"] as JObject ?? new JObject();
                            ctx["idempotencyKey"] = idemKey;
                            ctx["idempotencyCache"] = "miss";
                            standardized["context"] = ctx;

                            IdempotencyResultCache.Set(idemCacheKey, standardized, ledger: null);
                        }
                        catch { /* ignore */ }
                    }
                    if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                    return standardized;
                }

                var unknown = RpcResultEnvelope.Fail(code: "UNKNOWN_COMMAND", msg: $"Unknown command: {methodEcho}");
                unknown["nextActions"] = new JArray
                {
                    new JObject { ["method"] = "list_commands", ["reason"] = "List available commands and retry with a valid name." }
                };
                var unknownStd = RpcResultEnvelope.StandardizePayload(unknown, uiapp, methodEcho, revitMs: 0);
                if (translated) TryAnnotateDispatch(unknownStd, invokedMethod, dispatchMethod);
                return unknownStd;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                var fail = RpcResultEnvelope.Fail(code: "INVALID_PARAMS", msg: $"{methodEcho}: {ex.Message}", data: new { exception = ex.GetType().Name });
                var standardized = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                return standardized;
            }
            catch (InvalidOperationException ex)
            {
                var fail = RpcResultEnvelope.Fail(code: "INVALID_OPERATION", msg: $"{methodEcho}: {ex.Message}", data: new { exception = ex.GetType().Name });
                var standardized = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                return standardized;
            }
            catch (Exception ex)
            {
                var fail = RpcResultEnvelope.Fail(code: "UNHANDLED_EXCEPTION", msg: $"{methodEcho}: {ex.Message}", data: new { exception = ex.GetType().Name });
                fail["detail"] = ex.ToString();
                var standardized = RpcResultEnvelope.StandardizePayload(fail, uiapp, methodEcho, revitMs: 0);
                if (translated) TryAnnotateDispatch(standardized, invokedMethod, dispatchMethod);
                return standardized;
            }
            finally
            {
                // Ensure cmd.Command is restored for the caller.
                if (translated) cmd.Command = invokedMethod;
            }
        }

        private static bool IsContextTokenBypassMethod(string method)
        {
            var m = (method ?? string.Empty).Trim();
            if (m.Length == 0) return false;
            var leaf = CommandNaming.Leaf(m).ToLowerInvariant();
            // Allow recovery even when caller mistakenly includes expectedContextToken.
            return leaf == "get_context";
        }

        private static string GetExpectedContextToken(RequestCommand cmd)
        {
            try
            {
                var p = cmd != null ? (cmd.Params as JObject) : null;
                string t = p?.Value<string>("expectedContextToken") ?? p?.Value<string>("__expectedContextToken") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(t) && p?["params"] is JObject inner)
                    t = inner.Value<string>("expectedContextToken") ?? inner.Value<string>("__expectedContextToken") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(t) && cmd != null && cmd.MetaRaw is JObject meta)
                    t = meta.Value<string>("expectedContextToken") ?? meta.Value<string>("__expectedContextToken") ?? string.Empty;

                return (t ?? string.Empty).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static JObject? ValidateExpectedContextToken(UIApplication uiapp, RequestCommand cmd, string methodEcho)
        {
            var expected = GetExpectedContextToken(cmd);
            if (string.IsNullOrWhiteSpace(expected)) return null;

            if (IsContextTokenBypassMethod(methodEcho) || IsContextTokenBypassMethod(cmd != null ? cmd.Command : string.Empty))
                return null;

            var snap = ContextTokenService.Capture(uiapp, includeSelectionIds: false, maxSelectionIds: 0);
            var actual = (snap != null ? snap.contextToken : string.Empty) ?? string.Empty;
            if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                return null;

            var fail = RpcResultEnvelope.Fail("PRECONDITION_FAILED", "Context token mismatch. Call help.get_context (or get_context) and retry.");
            fail["data"] = new JObject
            {
                ["expectedContextToken"] = expected,
                ["actualContextToken"] = actual,
                ["tokenVersion"] = snap != null ? snap.tokenVersion : ContextTokenService.TokenVersion,
                ["contextRevision"] = snap != null ? snap.revision : 0,
                ["docGuid"] = snap != null ? snap.docGuid : "",
                ["docTitle"] = snap != null ? snap.docTitle : "",
                ["activeViewId"] = snap != null ? snap.activeViewId : 0,
                ["selectionCount"] = snap != null ? snap.selectionCount : 0
            };
            fail["nextActions"] = new JArray
            {
                new JObject { ["method"] = "help.get_context", ["reason"] = "Get the current contextToken." },
                new JObject { ["method"] = "get_context", ["reason"] = "Legacy alias for help.get_context." },
                new JObject { ["method"] = methodEcho ?? (cmd != null ? (cmd.Command ?? "") : ""), ["reason"] = "Retry with expectedContextToken set to the latest token." }
            };
            return fail;
        }

        private static bool? ExtractOkFlag(object raw)
        {
            try
            {
                if (raw == null) return null;

                JObject jo = raw as JObject;
                if (jo == null)
                    jo = JObject.FromObject(raw);

                // If handler returned JSON-RPC shape, try to unwrap.
                if (jo["jsonrpc"] != null && jo["result"] is JObject inner)
                    jo = inner;

                var okTok = jo["ok"];
                if (okTok != null && okTok.Type == JTokenType.Boolean) return okTok.Value<bool>();
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static void TryAttachFailureIssuesOnRollback(
            JObject standardized,
            string methodEcho,
            FailureHandlingRequest fhReq,
            int targetCount,
            int threshold,
            FailureHandlingScope fhScope)
        {
            if (standardized == null) return;
            if (fhScope == null) return;

            var issues = fhScope.Issues;
            if (issues == null) return;

            int failureCount = 0;
            int dialogCount = 0;
            try { failureCount = (issues.failures != null ? issues.failures.Count : 0); } catch { failureCount = 0; }
            try { dialogCount = (issues.dialogs != null ? issues.dialogs.Count : 0); } catch { dialogCount = 0; }
            if (failureCount == 0 && dialogCount == 0) return;

            bool rollbackLike = false;
            bool attachForDialogs = dialogCount > 0;
            try
            {
                if (issues.rollbackRequested) rollbackLike = true;
            }
            catch { /* ignore */ }

            string code = string.Empty;
            try { code = (standardized.Value<string>("code") ?? string.Empty).Trim(); } catch { code = string.Empty; }

            if (!rollbackLike)
            {
                if (string.Equals(code, "TX_NOT_COMMITTED", StringComparison.OrdinalIgnoreCase))
                    rollbackLike = true;
                else if (code.IndexOf("ROLLBACK", StringComparison.OrdinalIgnoreCase) >= 0)
                    rollbackLike = true;
            }

            if (!rollbackLike)
            {
                try
                {
                    // Detect common "transactionStatus != Committed" shape.
                    if (standardized["detail"] is JObject detailObj)
                    {
                        var ts = (detailObj.Value<string>("transactionStatus") ?? string.Empty).Trim();
                        if (ts.Length > 0 && !string.Equals(ts, "Committed", StringComparison.OrdinalIgnoreCase))
                            rollbackLike = true;
                    }
                }
                catch { /* ignore */ }
            }

            if (!rollbackLike)
            {
                // Best-effort inference: if an Error severity failure was captured, attach diagnostics.
                try
                {
                    for (int i = 0; i < issues.failures.Count; i++)
                    {
                        var s = (issues.failures[i].severity ?? string.Empty).Trim();
                        if (string.Equals(s, "Error", StringComparison.OrdinalIgnoreCase))
                        {
                            rollbackLike = true;
                            break;
                        }
                    }
                }
                catch { /* ignore */ }
            }

            if (!rollbackLike && !attachForDialogs) return;

            // Ensure failureHandling block exists even when not explicitly requested,
            // so rollback/dialog diagnostics are always recorded in the job result.
            JObject fhObj = standardized["failureHandling"] as JObject;
            if (fhObj == null)
            {
                fhObj = new JObject();
                standardized["failureHandling"] = fhObj;
            }

            try { if (fhObj["enabled"] == null) fhObj["enabled"] = fhReq.Enabled; } catch { /* ignore */ }
            try { if (fhObj["mode"] == null) fhObj["mode"] = fhReq.Enabled ? fhReq.Mode.ToString().ToLowerInvariant() : "off"; } catch { /* ignore */ }
            try { if (fhObj["targetCount"] == null) fhObj["targetCount"] = targetCount; } catch { /* ignore */ }
            try { if (fhObj["threshold"] == null) fhObj["threshold"] = threshold; } catch { /* ignore */ }
            try { if (fhObj["whitelist"] == null) fhObj["whitelist"] = JObject.FromObject(FailureWhitelistService.GetStatus()); } catch { /* ignore */ }

            try
            {
                if (fhObj["issues"] == null)
                {
                    fhObj["issues"] = JObject.FromObject(issues);
                }
                else
                {
                    var issuesObj = fhObj["issues"] as JObject;
                    if (issuesObj != null && issuesObj["dialogs"] == null && issues.dialogs != null && issues.dialogs.Count > 0)
                        issuesObj["dialogs"] = JArray.FromObject(issues.dialogs);
                }
            }
            catch { /* ignore */ }

            if (rollbackLike)
            {
                try { if (fhObj["rollbackDetected"] == null) fhObj["rollbackDetected"] = true; } catch { /* ignore */ }
            }

            if (rollbackLike)
            {
                try
                {
                    if (fhObj["rollbackReason"] == null)
                    {
                        string reason = string.Empty;
                        try { reason = (issues.rollbackReason ?? string.Empty).Trim(); } catch { reason = string.Empty; }
                        if (string.IsNullOrWhiteSpace(reason)) reason = (string.IsNullOrWhiteSpace(code) ? "RollbackDetected" : code);
                        fhObj["rollbackReason"] = reason;
                    }
                }
                catch { /* ignore */ }
            }

            if (attachForDialogs)
            {
                try { if (fhObj["dialogCount"] == null) fhObj["dialogCount"] = dialogCount; } catch { /* ignore */ }
                try
                {
                    bool allDismissed = false;
                    try { allDismissed = issues.dialogs != null && issues.dialogs.Count > 0 && issues.dialogs.All(d => d.dismissed); } catch { allDismissed = false; }
                    if (fhObj["dialogsDismissed"] == null) fhObj["dialogsDismissed"] = allDismissed;
                }
                catch { /* ignore */ }
                try
                {
                    var warnArr = standardized["warnings"] as JArray;
                    if (warnArr == null)
                    {
                        warnArr = new JArray();
                        standardized["warnings"] = warnArr;
                    }
                    warnArr.Add("Dialogs auto-dismissed: " + dialogCount + ". See failureHandling.issues.dialogs.");
                }
                catch { /* ignore */ }
            }

            try
            {
                if (fhReq.ExplicitProvided)
                {
                    // Keep explicit mode as-is.
                }
                else
                {
                    if (fhObj["autoCaptured"] == null) fhObj["autoCaptured"] = true;
                }
            }
            catch { /* ignore */ }

            // Also emit a concise line to addin log (easy to find without digging jobs.db).
            try
            {
                RevitLogger.Warn(
                    "Rollback detected: method={0} code={1} failures={2} dialogs={3}",
                    methodEcho ?? "",
                    code,
                    failureCount,
                    dialogCount);
            }
            catch { /* ignore */ }
        }

        private static bool ShouldBumpContextRevision(string method)
        {
            try
            {
                var m = (method ?? string.Empty).Trim();
                if (m.Length == 0) return true;

                if (CommandMetadataRegistry.TryGet(m, out var meta))
                {
                    var kind = (meta != null ? (meta.kind ?? string.Empty) : string.Empty).Trim();
                    if (kind.Length > 0)
                        return string.Equals(kind, "write", StringComparison.OrdinalIgnoreCase);
                }

                // Fallback heuristic when metadata is unavailable.
                var leaf = CommandNaming.Leaf(m).ToLowerInvariant();
                if (leaf.StartsWith("get_")) return false;
                if (leaf.StartsWith("list_")) return false;
                if (leaf.StartsWith("search_")) return false;
                if (leaf.StartsWith("describe_")) return false;
                if (leaf == "ping_server") return false;
                if (leaf == "get_context") return false;
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static void TryAnnotateDispatch(JObject payload, string invoked, string dispatch)
        {
            if (payload == null) return;
            try
            {
                var ctx = payload["context"] as JObject;
                if (ctx == null)
                {
                    ctx = new JObject();
                    payload["context"] = ctx;
                }

                if (ctx["invokedMethod"] == null) ctx["invokedMethod"] = invoked ?? "";
                if (ctx["dispatchMethod"] == null) ctx["dispatchMethod"] = dispatch ?? "";
            }
            catch { /* ignore */ }
        }

        // ----------------------------------------------------------------
        // Step 9 helpers: dryRun + confirmToken (high-risk safety)
        // ----------------------------------------------------------------

        private static bool IsDryRunRequested(JObject p)
        {
            try { return (p?.Value<bool?>("dryRun") ?? p?.Value<bool?>("dry_run") ?? false); }
            catch { return false; }
        }

        private static int EstimateProcessingTargetCount(JObject p)
        {
            // Conservative heuristic: count elementIds/elementId in top-level and in target/targets containers.
            // (Designed only for the ">=5" confirmation gate, so false positives are acceptable but false negatives should be minimized.)
            int c = 0;
            if (p == null) return 0;

            c += CountIdsToken(p["elementIds"]);
            c += CountIdsToken(p["elementId"]);

            try
            {
                if (p["target"] is JObject tgt)
                {
                    c += CountIdsToken(tgt["elementIds"]);
                    c += CountIdsToken(tgt["elementId"]);
                }
            }
            catch { /* ignore */ }

            try
            {
                if (p["targets"] is JArray arr)
                {
                    foreach (var item in arr)
                    {
                        if (item == null) continue;
                        if (item.Type == JTokenType.Integer || item.Type == JTokenType.Float)
                        {
                            c += 1;
                            continue;
                        }
                        if (item is JObject jo)
                        {
                            c += CountIdsToken(jo["elementIds"]);
                            c += CountIdsToken(jo["elementId"]);
                        }
                    }
                }
            }
            catch { /* ignore */ }

            return c;
        }

        private static int CountIdsToken(JToken tok)
        {
            if (tok == null) return 0;
            try
            {
                if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float) return 1;
                if (tok.Type == JTokenType.String)
                {
                    var s = (tok.Value<string>() ?? string.Empty).Trim();
                    if (s.Length == 0) return 0;
                    // Support comma-separated ids: "1,2,3"
                    var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int n = 0;
                    foreach (var p in parts)
                    {
                        if (int.TryParse(p.Trim(), out var _)) n++;
                    }
                    return n;
                }
                var arr = tok as JArray;
                if (arr != null)
                {
                    int n = 0;
                    foreach (var t in arr)
                    {
                        if (t == null) continue;
                        if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) { n++; continue; }
                        if (t.Type == JTokenType.String && int.TryParse((t.Value<string>() ?? "").Trim(), out var _)) n++;
                    }
                    return n;
                }
            }
            catch { /* ignore */ }
            return 0;
        }

        private static bool IsRequireConfirmToken(JObject p)
        {
            try { return (p?.Value<bool?>("requireConfirmToken") ?? p?.Value<bool?>("require_confirm_token") ?? false); }
            catch { return false; }
        }

        private static bool LooksLikeHighRiskMethod(string method)
        {
            try
            {
                var leaf = CommandNaming.Leaf(method ?? string.Empty).ToLowerInvariant();
                return leaf.StartsWith("delete_")
                    || leaf.StartsWith("remove_")
                    || leaf.StartsWith("reset_")
                    || leaf.StartsWith("clear_")
                    || leaf.StartsWith("purge_");
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractConfirmToken(JObject p)
        {
            try { return ((p?.Value<string>("confirmToken") ?? p?.Value<string>("confirm_token")) ?? string.Empty).Trim(); }
            catch { return string.Empty; }
        }

        private static string ComputeConfirmFingerprint(JObject p)
        {
            try
            {
                if (p == null) return string.Empty;

                var clone = (JObject)p.DeepClone();
                clone.Remove("confirmToken");
                clone.Remove("confirm_token");
                clone.Remove("requireConfirmToken");
                clone.Remove("require_confirm_token");
                clone.Remove("dryRun");
                clone.Remove("dry_run");

                var normalized = SortJToken(clone);
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(normalized);

                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                    var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    if (hex.Length > 32) hex = hex.Substring(0, 32);
                    return "fp-" + hex;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static JToken SortJToken(JToken token)
        {
            if (token == null) return JValue.CreateNull();

            if (token is JObject obj)
            {
                var ordered = new JObject();
                foreach (var prop in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    ordered[prop.Name] = SortJToken(prop.Value);
                }
                return ordered;
            }

            if (token is JArray arr)
            {
                var a = new JArray();
                foreach (var item in arr)
                {
                    a.Add(SortJToken(item));
                }
                return a;
            }

            return token;
        }

        // ----------------------------------------------------------------
        // 以降は既存の内部ヘルパー（内容は変更なし）
        // ----------------------------------------------------------------

        private static int ResolveUiTimeoutMs(string? command)
        {
            // 既定は 4s（UiEventPump 既定）だが、以下は重くなり得る
            const int DEFAULT_MS = 4000;
            if (string.IsNullOrWhiteSpace(command)) return DEFAULT_MS;
            var m = command.Trim().ToLowerInvariant();

            // 重い/ブロッキングが起きやすい UI コマンド群
            switch (m)
            {
                // Heaviest read
                case "get_floors":
                    return 240_000; // 240s for large models
                // Heavy reads that can iterate many elements
                case "get_roofs":
                case "delete_view":
                case "open_views":
                case "close_inactive_views":
                case "duplicate_view":
                case "export_dwg":
                case "isolate_by_filter_in_view":
                case "set_visual_override":
                case "clear_visual_override":
                // Parameter updates can trigger regeneration and be slow
                case "update_wall_parameter":
                case "update_wall_type_parameter":
                case "update_curtain_wall_parameter":
                case "update_door_parameter":
                case "update_window_parameter":
                case "set_ceiling_parameter":
                case "set_ceiling_type_parameter":
                case "set_floor_parameter":
                case "set_floor_type_parameter":
                case "set_roof_parameter":
                case "set_roof_type_parameter":
                case "update_material_parameter":
                case "update_text_note_parameter":
                case "update_tag_parameter":
                case "create_text_note":
                    return 240_000; // allow longer time for UI creation
                case "set_text":
                case "move_text_note":
                case "delete_text_note":
                case "create_tag":
                case "move_tag":
                case "delete_tag":
                case "set_railing_parameter":
                case "set_railing_type_parameter":
                case "update_parameters_batch":
                case "set_stair_parameter":
                case "set_stair_type_parameter":
                case "set_stair_flight_parameters":
                case "set_family_type_parameter":
                case "update_family_instance_parameter":
                case "update_level_parameter":
                case "set_revision_cloud_parameter":
                case "set_revision_cloud_type_parameter":
                case "set_view_parameter":
                    return 120_000; // 120s まで待つ
                default:
                    return DEFAULT_MS;
            }
        }

        private static void TryActivateDocument(UIApplication uiapp, RequestCommand cmd)
        {
            if (uiapp?.Application == null) return;
            var p = cmd?.Params as JObject;
            if (p == null) return;

            if (p.TryGetValue("documentPath", out JToken token) && token.Type == JTokenType.String)
            {
                var path = (string)token;
                if (string.IsNullOrWhiteSpace(path)) return;

                var active = uiapp.ActiveUIDocument?.Document;
                var target = uiapp.Application.Documents
                    .Cast<Document>()
                    .FirstOrDefault(d => string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase));

                if (active != null && target != null && active.PathName == target.PathName)
                    return; // 既にアクティブ

                uiapp.OpenAndActivateDocument(path);
            }
        }

        private static object PreprocessIdsAndAliases(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var p = cmd?.Params as JObject;
            if (doc == null || p == null) return null;

            // 上位直下の補完
            var err = MapContainer(doc, p);
            if (err != null) return err;
            BackfillDomainSpecificIds(cmd.Command, p);
            TryAutoCorrectSpatialSelection(doc, cmd.Command, p);

            // target: { . }
            if (p.TryGetValue("target", out var tgtTok) && tgtTok is JObject tgtObj)
            {
                err = MapContainer(doc, tgtObj);
                if (err != null) return err;
                BackfillDomainSpecificIds(cmd.Command, tgtObj);
                TryAutoCorrectSpatialSelection(doc, cmd.Command, tgtObj);
            }

            // targets: [ { . }, . ]
            if (p.TryGetValue("targets", out var tgtsTok) && tgtsTok is JArray tgtsArr)
            {
                foreach (var item in tgtsArr.OfType<JObject>())
                {
                    err = MapContainer(doc, item);
                    if (err != null) return err;
                    BackfillDomainSpecificIds(cmd.Command, item);
                    TryAutoCorrectSpatialSelection(doc, cmd.Command, item);
                }
            }

            return null;
        }

        private static object MapContainer(Document doc, JObject container)
        {
            // 1) uniqueId -> elementId
            if (container.TryGetValue("uniqueId", out var uidTok)
                && uidTok.Type == JTokenType.String
                && !container.ContainsKey("elementId"))
            {
                var uid = uidTok.Value<string>();
                var e = string.IsNullOrWhiteSpace(uid) ? null : doc.GetElement(uid);
                if (e == null)
                {
                    return new
                    {
                        ok = false,
                        errorCode = "NOT_FOUND",
                        msg = $"Element not found for uniqueId: {uid}"
                    };
                }
                container["elementId"] = e.Id.IntValue();
            }

            // 2) uniqueIds -> elementIds
            if (container.TryGetValue("uniqueIds", out var uidsTok)
                && uidsTok is JArray uidsArr
                && !container.ContainsKey("elementIds"))
            {
                var outIds = new JArray();
                foreach (var s in uidsArr.Values<string>())
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var e = doc.GetElement(s);
                    if (e != null) outIds.Add(e.Id.IntValue());
                }

                if (outIds.Count == 0)
                {
                    return new
                    {
                        ok = false,
                        errorCode = "NOT_FOUND",
                        msg = "No elements found for given uniqueIds."
                    };
                }

                container["elementIds"] = outIds;
            }

            // 3) 各種 "XXId" エイリアスを elementId に昇格（elementId未指定時のみ）
            PromoteAliasIdToElementId(container, "roomId");
            PromoteAliasIdToElementId(container, "areaId");
            PromoteAliasIdToElementId(container, "spaceId");
            PromoteAliasIdToElementId(container, "gridId");
            PromoteAliasIdToElementId(container, "wallId");
            PromoteAliasIdToElementId(container, "doorId");
            PromoteAliasIdToElementId(container, "windowId");
            PromoteAliasIdToElementId(container, "floorId");
            // Site系は elementId を直接必要としないため昇格しない

            // 注意: viewId はビューIDとして利用。elementId へは昇格しない
            return null;
        }

        private static void PromoteAliasIdToElementId(JObject container, string elemKey)
        {
            if (!container.ContainsKey("elementId") &&
                container.TryGetValue(elemKey, out var tok) &&
                (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float))
            {
                container["elementId"] = tok.Value<int>();
            }
        }

        private static void BackfillDomainSpecificIds(string method, JObject container)
        {
            if (container == null || string.IsNullOrEmpty(method)) return;

            var elem = container["elementId"];
            if (elem == null) return;

            var m = method.ToLowerInvariant();

            // Area 系
            if (m.StartsWith("get_area") || m.StartsWith("update_area") ||
                m.StartsWith("move_area") || m.StartsWith("delete_area") ||
                m.Contains("_area"))
            {
                if (container["areaId"] == null) container["areaId"] = elem;
            }

            // Room 系
            if (m.StartsWith("get_room") || m.StartsWith("set_room") ||
                m.StartsWith("delete_room") || m.Contains("_room"))
            {
                if (container["roomId"] == null) container["roomId"] = elem;
            }

            // Space 系
            if (m.StartsWith("get_space") || m.StartsWith("update_space") ||
                m.StartsWith("move_space") || m.StartsWith("delete_space") ||
                m.Contains("_space"))
            {
                if (container["spaceId"] == null) container["spaceId"] = elem;
            }
        }

        // Auto-correct a common mismatch: Room/Space/Area IDs are swapped by human selection.
        // This is a "read-only" correction: it only rewrites ids in params, not the Revit model.
        private static void TryAutoCorrectSpatialSelection(Document doc, string method, JObject container)
        {
            try
            {
                if (doc == null || container == null) return;
                if (string.IsNullOrWhiteSpace(method)) return;

                var desired = InferDesiredSpatialKind(method, container);
                if (desired == null) return;

                var desiredKey = desired.Value == SpatialKind.Room ? "roomId"
                              : desired.Value == SpatialKind.Space ? "spaceId"
                              : "areaId";

                var inputId = GetIdBestEffort(container, desiredKey);
                if (inputId <= 0) inputId = GetIdBestEffort(container, "elementId");
                if (inputId <= 0) return;

                var inputElemId = Autodesk.Revit.DB.ElementIdCompat.From(inputId);
                var e = doc.GetElement(inputElemId);
                if (e == null) return;

                // Already correct (fast path)
                if (IsSpatialKindMatch(e, desired.Value)) return;

                // Only attempt correction when the input is likely spatial-ish (Room/Space/Area/Tags).
                if (!IsSpatialLike(e)) return;

                var opt = SpatialResolveOptions.CreateDefaultMeters(0.5);
                SpatialResolveResult rr;
                if (!SpatialElementResolver.TryResolve(doc, inputElemId, desired.Value, opt, out rr) || !rr.Ok)
                    return;

                var resolvedId = rr.ResolvedId != null ? rr.ResolvedId.IntValue() : 0;
                if (resolvedId <= 0 || resolvedId == inputId) return;

                // Update the desired key always.
                container[desiredKey] = resolvedId;

                // Update elementId only when it is missing or was derived from the same input id.
                var currentElemId = GetIdBestEffort(container, "elementId");
                if (currentElemId <= 0 || currentElemId == inputId)
                    container["elementId"] = resolvedId;

                // If the "wrong kind" alias id is present (e.g. spaceId for a room command), do not overwrite it;
                // keep as-is for debugging. The corrected ids are in desiredKey/elementId now.

                try
                {
                    var distM = UnitUtils.ConvertFromInternalUnits(rr.DistanceInternal, UnitTypeId.Meters);
                    RevitLogger.Info("Spatial selection corrected method='{0}' {1}:{2} => {3} ({4}, {5:0.###}m)",
                        method ?? "",
                        desiredKey,
                        inputId,
                        resolvedId,
                        rr.ByContainment ? "containment" : "nearest",
                        distM);
                }
                catch { /* ignore */ }
            }
            catch
            {
                // Never fail routing due to auto-correction.
            }
        }

        private static int GetIdBestEffort(JObject container, string key)
        {
            try
            {
                if (container == null || string.IsNullOrWhiteSpace(key)) return 0;
                var tok = container[key];
                if (tok == null) return 0;
                if (tok.Type == JTokenType.Integer) return tok.Value<int>();
                if (tok.Type == JTokenType.Float) return (int)Math.Round(tok.Value<double>());
                if (tok.Type == JTokenType.String)
                {
                    var s = (tok.Value<string>() ?? string.Empty).Trim();
                    if (int.TryParse(s, out var v)) return v;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static SpatialKind? InferDesiredSpatialKind(string method, JObject container)
        {
            try
            {
                var m = (method ?? string.Empty).Trim().ToLowerInvariant();
                if (m.StartsWith("get_room") || m.StartsWith("set_room") || m.StartsWith("delete_room") || m.Contains("_room"))
                    return SpatialKind.Room;
                if (m.StartsWith("get_space") || m.StartsWith("update_space") || m.StartsWith("move_space") || m.StartsWith("delete_space") || m.Contains("_space"))
                    return SpatialKind.Space;
                if (m.StartsWith("get_area") || m.StartsWith("update_area") || m.StartsWith("move_area") || m.StartsWith("delete_area") || m.Contains("_area"))
                    return SpatialKind.Area;

                // Fallback (unknown method naming): infer by provided id keys.
                if (container != null)
                {
                    if (GetIdBestEffort(container, "roomId") > 0) return SpatialKind.Room;
                    if (GetIdBestEffort(container, "spaceId") > 0) return SpatialKind.Space;
                    if (GetIdBestEffort(container, "areaId") > 0) return SpatialKind.Area;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSpatialKindMatch(Element e, SpatialKind desired)
        {
            if (e == null) return false;
            switch (desired)
            {
                case SpatialKind.Room:
                    return e is Autodesk.Revit.DB.Architecture.Room;
                case SpatialKind.Space:
                    return e is Autodesk.Revit.DB.Mechanical.Space;
                case SpatialKind.Area:
                    return e is Autodesk.Revit.DB.Area;
                default:
                    return false;
            }
        }

        private static bool IsSpatialLike(Element e)
        {
            if (e == null) return false;
            if (e is Autodesk.Revit.DB.Architecture.Room) return true;
            if (e is Autodesk.Revit.DB.Mechanical.Space) return true;
            if (e is Autodesk.Revit.DB.Area) return true;
            // Tags (best-effort; resolver will unwrap)
            if (e is Autodesk.Revit.DB.Architecture.RoomTag) return true;
            if (e is Autodesk.Revit.DB.Mechanical.SpaceTag) return true;
            if (e is Autodesk.Revit.DB.AreaTag) return true;
            return false;
        }

        // 追加の軽微エイリアス正規化
        private static void NormalizeCommandSpecificAliases(string method, JObject p)
        {
            if (p == null || string.IsNullOrEmpty(method)) return;
            var m = method.Trim().ToLowerInvariant();

            // Generic: if no location object is provided, but top-level x/y/z exist,
            // synthesize location = { x, y, z } for backward compatibility.
            try
            {
                var hasLocationObj = p["location"] is JObject;
                var hasX = p["x"] != null && (p["x"].Type == JTokenType.Integer || p["x"].Type == JTokenType.Float);
                var hasY = p["y"] != null && (p["y"].Type == JTokenType.Integer || p["y"].Type == JTokenType.Float);
                var hasZ = p["z"] != null && (p["z"].Type == JTokenType.Integer || p["z"].Type == JTokenType.Float);
                if (!hasLocationObj && hasX && hasY && hasZ)
                {
                    p["location"] = new JObject
                    {
                        ["x"] = p["x"],
                        ["y"] = p["y"],
                        ["z"] = p["z"]
                    };
                }
            }
            catch { /* best effort fallback; never fail normalization */ }

            // rename_wall_type: newTypeName / name / renameTo → newName
            if (m == "rename_wall_type")
            {
                if (p["newName"] == null)
                {
                    if (p["newTypeName"] != null) p["newName"] = p["newTypeName"];
                    else if (p["name"] != null) p["newName"] = p["name"];
                    else if (p["renameTo"] != null) p["newName"] = p["renameTo"];
                }
            }

            // create_text_note: pointMm/posMm → positionMm、textNoteTypeName → typeName
            if (m == "create_text_note")
            {
                if (p["positionMm"] == null && p["pointMm"] is JObject pt) p["positionMm"] = pt;
                if (p["positionMm"] == null && p["posMm"] is JObject pt2) p["positionMm"] = pt2;
                if (p["typeName"] == null && p["textNoteTypeName"] != null) p["typeName"] = p["textNoteTypeName"];
            }

            // get_wall_type_parameters: typeId -> typeIds[], elementId -> elementIds[]
            if (m == "get_wall_type_parameters")
            {
                if (p["typeIds"] == null && (p["typeId"]?.Type == JTokenType.Integer || p["typeId"]?.Type == JTokenType.Float))
                    p["typeIds"] = new JArray(p.Value<int>("typeId"));
                if (p["elementIds"] == null && (p["elementId"]?.Type == JTokenType.Integer || p["elementId"]?.Type == JTokenType.Float))
                    p["elementIds"] = new JArray(p.Value<int>("elementId"));
            }

            // Site 系軽微エイリアス
            if (m == "create_toposurface_from_points" || m == "append_toposurface_points" || m == "replace_toposurface_points")
            {
                if (p["pointsMm"] == null && p["points"] is JArray arr) p["pointsMm"] = arr;
            }
            if (m == "create_site_subregion_from_boundary")
            {
                if (p["boundaryMm"] == null && p["boundary"] is JArray arr2) p["boundaryMm"] = arr2;
            }
            if (m == "place_site_component" || m == "place_parking_spot")
            {
                if (p["angleDeg"] == null && (p["angleDegrees"]?.Type == JTokenType.Integer || p["angleDegrees"]?.Type == JTokenType.Float))
                    p["angleDeg"] = p["angleDegrees"];
                if (p["locationMm"] == null && p["positionMm"] is JObject pp) p["locationMm"] = pp;
            }
        }

        private static void NormalizeArray(JObject obj, string key)
        {
            if (obj[key] == null) return;
            var t = obj[key];
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.String)
            {
                // Wrap scalar into array
                obj[key] = new JArray(t);
            }
            // leave if already array/object/null
        }

        // ============================================================
        // コマンド別の“優しく叱る”検証器（このファイル内完結版）
        // （Profiles 内容は簡略ダミー。あなたの既存定義がある場合は置換不要）
        // ============================================================
        private static class CommandParamTeach
        {
                private static readonly HashSet<string> CommonOptional = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "documentPath", "debug",
                    "dryRun", "dry_run",
                    "idempotencyKey", "idempotency_key",
                    "confirmToken", "confirm_token",
                    "requireConfirmToken", "require_confirm_token",
                    "failureHandling", "failure_handling",
                "failureHandlingMode", "failure_handling_mode",
                "scope", "where", "target", "targets",
                "expectedContextToken", "__expectedContextToken"
            };

            private static readonly Dictionary<string, ParamProfile> Profiles =
                new Dictionary<string, ParamProfile>(StringComparer.OrdinalIgnoreCase)
                {
                    { "get_open_documents", new ParamProfile("get_open_documents").Allow().Example(_ => new JObject()) },
                    { "get_project_info",   new ParamProfile("get_project_info").Allow().Example(_ => new JObject()) },
                    { "ping_server",        new ParamProfile("ping_server").Allow().Example(_ => new JObject()) },
                    // TODO: ここに既存の詳細プロファイルを移植（必要に応じて）
                };

            public static object? Guard(string method, JObject? p)
            {
                if (string.IsNullOrWhiteSpace(method)) return null;
                p ??= new JObject();

                if (!Profiles.TryGetValue(method, out var prof))
                {
                    // 未登録メソッドは寛容に素通し（互換優先）
                    return null;
                }

                return prof.Validate(p, CommonOptional);
            }

            // ---- ParamProfile ----
            private sealed class ParamProfile
            {
                private readonly string _name;
                private readonly HashSet<string> _require = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                private readonly List<HashSet<string>> _oneOf = new List<HashSet<string>>();
                private readonly Dictionary<string, HashSet<string>> _alias = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                private readonly HashSet<string> _allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                private Func<JObject?, JObject>? _example;

                public ParamProfile(string name) { _name = name; }

                public ParamProfile Require(params string[] keys) { foreach (var k in keys) _require.Add(k); return this; }
                public ParamProfile OneOf(params string[] keys) { _oneOf.Add(new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase)); return this; }
                public ParamProfile Alias(string canonical, params string[] alts)
                {
                    if (!_alias.TryGetValue(canonical, out var set))
                        _alias[canonical] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var a in alts) set.Add(a);
                    return this;
                }
                public ParamProfile Allow(params string[] keys) { foreach (var k in keys) _allow.Add(k); return this; }
                public ParamProfile Example(Func<JObject?, JObject> maker) { _example = maker; return this; }

                public object? Validate(JObject p, HashSet<string> commonOptional)
                {
                    // 1) エイリアス正規化
                    foreach (var kv in _alias)
                    {
                        var canon = kv.Key;
                        if (p[canon] != null) continue;
                        foreach (var alt in kv.Value)
                        {
                            if (p[alt] != null) { p[canon] = p[alt]; break; }
                        }
                    }

                    // 2) 必須
                    foreach (var req in _require)
                    {
                        if (p[req] == null)
                        {
                            return new
                            {
                                ok = false,
                                errorCode = "ERR_PARAM_REQUIRED",
                                msg = $"Parameter '{req}' is required for '{_name}'.",
                                exampleRight = _example?.Invoke(p) ?? new JObject()
                            };
                        }
                    }

                    // 3) OneOf
                    foreach (var group in _oneOf)
                    {
                        if (!group.Any(k => p[k] != null))
                        {
                            return new
                            {
                                ok = false,
                                errorCode = "ERR_PARAM_ONEOF",
                                msg = $"One of [{string.Join(", ", group)}] is required for '{_name}'.",
                                exampleRight = _example?.Invoke(p) ?? new JObject()
                            };
                        }
                    }

                    // 4) 未知キー（共通黙認 + Allow 以外は黙認：互換優先）
                    var allowed = new HashSet<string>(_allow, StringComparer.OrdinalIgnoreCase);
                    foreach (var c in commonOptional) allowed.Add(c);
                    foreach (var k in p.Properties().Select(x => x.Name))
                    {
                        if (!allowed.Contains(k))
                        {
                            // 厳格に拒否せず黙認（ヒント返却に切替も可）
                        }
                    }

                    return null;
                }
            }
        }
    }
}

// 文字列拡張（.NET 4.8 互換向け）
internal static class _StrEx
{
    public static bool IsNullOrEmpty(this string? s) => string.IsNullOrEmpty(s);
}

