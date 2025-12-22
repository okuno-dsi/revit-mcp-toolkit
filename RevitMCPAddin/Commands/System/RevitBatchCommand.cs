#nullable enable
// ================================================================
// File   : Commands/System/RevitBatchCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary:
//   Step 6: revit.batch — execute multiple ops in a single ExternalEvent.
// Notes:
//   - Sub-ops are executed via CommandRouter internal "no-gate/no-ledger" path to avoid deadlocks.
//   - Transaction strategy is best-effort; some operations may have non-transactional side effects.
// ================================================================
using System;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    [RpcCommand(
        "revit.batch",
        Aliases = new[] { "revit_batch" },
        Category = "Meta",
        Tags = new[] { "revit", "batch", "meta", "performance" },
        Risk = RiskLevel.High,
        Kind = "write",
        Importance = "high",
        Summary = "Execute multiple commands in a single Revit ExternalEvent (batch).",
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"revit.batch\", \"params\":{ \"ops\":[ {\"method\":\"doc.get_project_info\",\"params\":{}}, {\"method\":\"view.get_current_view\",\"params\":{}} ], \"transaction\":\"none\", \"dryRun\":false } }"
    )]
    public sealed class RevitBatchCommand : IRevitCommandHandler
    {
        // Legacy dispatch (kept for backward compatibility)
        public string CommandName => "revit_batch";

        private CommandRouter? _router;

        internal void BindRouter(CommandRouter router)
        {
            _router = router;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            if (_router == null)
                return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "Router is not bound (initialization order issue).");

            var p = cmd.Params as JObject ?? new JObject();

            var opsArr = TryGetOpsArray(p, out var opsErr);
            if (opsArr == null)
                return opsErr ?? RpcResultEnvelope.Fail("INVALID_PARAMS", "Missing params.ops (array).");

            var transaction = (p.Value<string>("transaction") ?? "single").Trim().ToLowerInvariant();
            if (transaction != "single" && transaction != "perop" && transaction != "per_op" && transaction != "none")
                return RpcResultEnvelope.Fail("INVALID_PARAMS", "params.transaction must be one of: single | perOp | none");
            if (transaction == "per_op") transaction = "perop";

            var dryRun = p.Value<bool?>("dryRun") ?? false;
            var stopOnError = p.Value<bool?>("stopOnError") ?? true;

            if (dryRun && transaction == "none")
                return RpcResultEnvelope.Fail("INVALID_PARAMS", "dryRun=true requires transaction=single or perOp (cannot guarantee rollback with transaction=none).");

            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
                return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document.");

            // For transaction modes that need rollback, disallow document switching inside the batch (safety).
            if (transaction != "none")
            {
                var docSwitchOp = FindFirstDocumentSwitchOp(opsArr);
                if (docSwitchOp != null)
                {
                    return RpcResultEnvelope.Fail(
                        "INVALID_PARAMS",
                        "Batch transaction modes do not allow switching documents (params.documentPath) inside ops. Use transaction='none' or split the batch by document.",
                        data: new { op = docSwitchOp });
                }
            }

            var results = new JArray();
            var warnings = new JArray();
            var opCount = opsArr.Count;
            int okCount = 0;
            int failCount = 0;
            bool rolledBack = false;

            var swBatch = Stopwatch.StartNew();

            TransactionGroup? batchGroup = null;
            bool batchGroupStarted = false;

            try
            {
                if (transaction == "single")
                {
                    batchGroup = new TransactionGroup(doc, "MCP Batch (single)");
                    try
                    {
                        batchGroup.Start();
                        batchGroupStarted = true;
                    }
                    catch (Exception ex)
                    {
                        // Best-effort fallback: continue without TransactionGroup.
                        warnings.Add($"WARN: Failed to start TransactionGroup for batch(single): {ex.Message}");
                        batchGroupStarted = false;
                        try { batchGroup.Dispose(); } catch { /* ignore */ }
                        batchGroup = null;
                    }
                }

                for (int i = 0; i < opCount; i++)
                {
                    var opTok = opsArr[i];
                    var opObj = opTok as JObject;
                    if (opObj == null)
                    {
                        var bad = RpcResultEnvelope.Fail("INVALID_PARAMS", "Each ops[i] must be an object.");
                        results.Add(BuildOpResult(i, opId: null, method: "", bad));
                        failCount++;
                        if (stopOnError) break;
                        continue;
                    }

                    var opId = opObj["opId"] ?? opObj["id"];
                    var method = (opObj.Value<string>("method") ?? opObj.Value<string>("command") ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(method))
                    {
                        var bad = RpcResultEnvelope.Fail("INVALID_PARAMS", "ops[i].method is required.");
                        results.Add(BuildOpResult(i, opId, method: "", bad));
                        failCount++;
                        if (stopOnError) break;
                        continue;
                    }

                    // Disallow recursive batch to avoid infinite recursion / unintended behavior.
                    if (string.Equals(method, "revit.batch", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(method, "revit_batch", StringComparison.OrdinalIgnoreCase))
                    {
                        var bad = RpcResultEnvelope.Fail("INVALID_PARAMS", "Nested revit.batch is not supported. Split the batch.");
                        results.Add(BuildOpResult(i, opId, method, bad));
                        failCount++;
                        if (stopOnError) break;
                        continue;
                    }

                    var opParams = opObj["params"] as JObject ?? new JObject();

                    // Inherit selected batch-level context keys (best-effort).
                    InheritIfMissing(p, opParams, "docGuid");
                    InheritIfMissing(p, opParams, "viewId");
                    InheritIfMissing(p, opParams, "documentPath");
                    InheritIfMissing(p, opParams, "agentId");

                    // Propagate dryRun to ops unless explicitly set.
                    if (dryRun && opParams["dryRun"] == null) opParams["dryRun"] = true;

                    var subCmd = BuildSubCommand(cmd, method, opParams, opId);

                    JObject opResult = new JObject();
                    TransactionGroup? perOpGroup = null;
                    bool perOpStarted = false;

                    if (transaction == "perop")
                    {
                        perOpGroup = new TransactionGroup(doc, $"MCP Batch Op {i + 1}/{opCount}");
                        try
                        {
                            perOpGroup.Start();
                            perOpStarted = true;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"WARN: Failed to start TransactionGroup for op[{i}] (perOp): {ex.Message}");
                            perOpStarted = false;
                            try { perOpGroup.Dispose(); } catch { /* ignore */ }
                            perOpGroup = null;
                        }
                    }

                    try
                    {
                        opResult = _router.RouteInternalNoGateNoLedger(uiapp, subCmd);
                    }
                    finally
                    {
                        // End per-op transaction group.
                        if (transaction == "perop" && perOpGroup != null && perOpStarted)
                        {
                            var opOk = opResult != null && (opResult.Value<bool?>("ok") ?? false);
                            try
                            {
                                if (dryRun || !opOk)
                                {
                                    perOpGroup.RollBack();
                                }
                                else
                                {
                                    perOpGroup.Assimilate();
                                }
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"WARN: Failed to finalize TransactionGroup for op[{i}] (perOp): {ex.Message}");
                            }
                            finally
                            {
                                try { perOpGroup.Dispose(); } catch { /* ignore */ }
                            }
                        }
                    }

                    var ok = opResult.Value<bool?>("ok") ?? false;
                    if (ok) okCount++; else failCount++;

                    results.Add(BuildOpResult(i, opId, method, opResult));

                    if (!ok && stopOnError)
                        break;
                }
            }
            finally
            {
                // End batch transaction group.
                if (transaction == "single" && batchGroup != null && batchGroupStarted)
                {
                    var allOk = failCount == 0;
                    try
                    {
                        if (dryRun || !allOk)
                        {
                            batchGroup.RollBack();
                            rolledBack = true;
                        }
                        else
                        {
                            batchGroup.Assimilate();
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"WARN: Failed to finalize TransactionGroup for batch(single): {ex.Message}");
                    }
                    finally
                    {
                        try { batchGroup.Dispose(); } catch { /* ignore */ }
                    }
                }
            }

            swBatch.Stop();

            var allOkFinal = failCount == 0;

            var msg = allOkFinal
                ? (dryRun ? "OK (DryRun: rolled back)." : "OK")
                : (dryRun ? "Failed (DryRun: rolled back)." : "Failed");

            var data = new JObject
            {
                ["transaction"] = transaction,
                ["dryRun"] = dryRun,
                ["stopOnError"] = stopOnError,
                ["rolledBack"] = rolledBack || (dryRun && transaction != "none"),
                ["opCount"] = opCount,
                ["okCount"] = okCount,
                ["failCount"] = failCount,
                ["results"] = results
            };

            // Provide batch-local timing (router will also add its own timings).
            var timings = new JObject { ["batchMs"] = swBatch.ElapsedMilliseconds };

            return new JObject
            {
                ["ok"] = allOkFinal,
                ["code"] = allOkFinal ? "OK" : "ERROR",
                ["msg"] = msg,
                ["warnings"] = warnings,
                ["timings"] = timings,
                ["data"] = data
            };
        }

        private static JArray? TryGetOpsArray(JObject p, out object? err)
        {
            err = null;
            if (p == null) return null;

            var tok = p["ops"];
            if (tok == null)
            {
                err = RpcResultEnvelope.Fail("INVALID_PARAMS", "Missing params.ops (array).");
                return null;
            }

            if (tok is JArray ja) return ja;

            if (tok.Type == JTokenType.String)
            {
                var s = (tok.Value<string>() ?? "").Trim();
                if (s.Length == 0)
                {
                    err = RpcResultEnvelope.Fail("INVALID_PARAMS", "params.ops is an empty string; expected an array.");
                    return null;
                }
                try
                {
                    var parsed = JToken.Parse(s);
                    if (parsed is JArray ja2) return ja2;
                }
                catch (Exception ex)
                {
                    err = RpcResultEnvelope.Fail("INVALID_PARAMS", "Failed to parse params.ops JSON string: " + ex.Message);
                    return null;
                }
            }

            err = RpcResultEnvelope.Fail("INVALID_PARAMS", "params.ops must be an array.");
            return null;
        }

        private static JObject BuildOpResult(int index, JToken? opId, string method, JObject standardizedResult)
        {
            var jo = new JObject
            {
                ["index"] = index,
                ["method"] = method ?? string.Empty,
                ["result"] = standardizedResult ?? new JObject()
            };
            if (opId != null)
                jo["opId"] = opId.DeepClone();
            return jo;
        }

        private static RequestCommand BuildSubCommand(RequestCommand parent, string method, JObject opParams, JToken? opId)
        {
            var sub = new RequestCommand();
            sub.Command = method ?? string.Empty;
            sub.Params = opParams ?? new JObject();

            // Preserve parent meta (agentId, expectedContextToken, etc.) but avoid sharing mutable instances.
            try
            {
                if (parent.MetaRaw is JObject metaJo)
                    sub.MetaRaw = metaJo.DeepClone();
            }
            catch { /* ignore */ }

            // Optional per-op id (for debug); does not affect router wrapping (batch contains per-op results).
            if (opId != null) sub.Id = opId.DeepClone();

            return sub;
        }

        private static void InheritIfMissing(JObject batchParams, JObject opParams, string key)
        {
            try
            {
                if (opParams[key] != null) return;
                var v = batchParams[key];
                if (v == null) return;
                opParams[key] = v.DeepClone();
            }
            catch { /* ignore */ }
        }

        private static object? FindFirstDocumentSwitchOp(JArray opsArr)
        {
            if (opsArr == null) return null;
            for (int i = 0; i < opsArr.Count; i++)
            {
                if (!(opsArr[i] is JObject opObj)) continue;
                if (!(opObj["params"] is JObject p)) continue;
                if (p["documentPath"] != null)
                {
                    var method = (opObj.Value<string>("method") ?? opObj.Value<string>("command") ?? "").Trim();
                    return new { index = i, method = method, documentPath = p.Value<string>("documentPath") ?? "" };
                }
            }
            return null;
        }
    }
}
