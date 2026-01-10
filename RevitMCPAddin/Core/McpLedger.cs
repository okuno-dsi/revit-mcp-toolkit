// ================================================================
// File: Core/McpLedger.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8.0
// Purpose:
//   MCP execution ledger (DataStorage + ExtensibleStorage) for
//   project authenticity and continuity tracking.
// Notes:
//   - Schema GUID must remain stable across versions (2024/2025).
//   - Sequence is treated as "next expected sequence" (1-based).
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class McpTrackingOptions
    {
        // Tracking is optional but enabled by default (per design doc)
        public static bool EnableLedger { get; set; } = true;
        public static bool EnableCommandLog { get; set; } = true;
        public static int MaxLogEntries { get; set; } = 200;

        // Compatibility: enforce expected token/sequence only when provided (default).
        // If set true, missing expectations will cause fail-closed.
        public static bool RequireExpectations { get; set; } = false;
    }

    internal sealed class McpLedgerExecutionResult
    {
        public object RawResult { get; set; }
        public JObject? LedgerInfo { get; set; }
    }

    internal static class McpLedgerEngine
    {
        // IMPORTANT: Do not change this GUID once deployed.
        private static readonly Guid LedgerSchemaGuid = new Guid("cffdf490-8b12-423f-8478-2a73147b6512");
        private const string LedgerSchemaName = "McpLedger";

        private const string F_ProjectToken = "ProjectToken";
        private const string F_Sequence = "Sequence";
        private const string F_LastCommandId = "LastCommandId";
        private const string F_LastCommandName = "LastCommandName";
        private const string F_LastCommandExecutedUtc = "LastCommandExecutedUtc";
        private const string F_LastCommandResult = "LastCommandResult";
        private const string F_ActiveSessionId = "ActiveSessionId";
        private const string F_ActiveSessionMode = "ActiveSessionMode";
        private const string F_ActiveSessionStartedUtc = "ActiveSessionStartedUtc";
        private const string F_CommandLogJson = "CommandLogJson";

        public static string LedgerSchemaGuidString => LedgerSchemaGuid.ToString();

        private static string BuildUndoGroupName(string commandName)
        {
            var raw = (commandName ?? string.Empty).Trim();
            if (raw.Length == 0) return "MCP Command";

            // Normalize to token-ish string.
            string norm = raw;
            try
            {
                norm = norm.Replace('.', '_').Replace('-', '_').Replace(' ', '_');
            }
            catch { /* ignore */ }

            var parts = new List<string>();
            try
            {
                foreach (var p in norm.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = (p ?? string.Empty).Trim().ToLowerInvariant();
                    if (t.Length == 0) continue;
                    parts.Add(t);
                }
            }
            catch { /* ignore */ }

            // Drop leading generic action verbs.
            var dropLeading = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "get","set","create","delete","update","apply","import","export","clear","reset","move","copy","rename","duplicate",
                "open","close","show","hide","select","stash","restore","prepare","simulate","build","ensure","validate","compare",
                "diagnose","debug","test","start","stop","sync","ping"
            };
            while (parts.Count > 0 && dropLeading.Contains(parts[0]))
                parts.RemoveAt(0);

            // Remove common stop-words but keep domain tokens.
            var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "revit","mcp","command","commands","ops","op","util","utils","by","from","to","in","with","and","for","of","all"
            };
            var tokens = new List<string>();
            foreach (var t in parts)
            {
                if (t.Length == 0) continue;
                if (stop.Contains(t)) continue;
                tokens.Add(t);
            }

            string shortLabel;
            if (tokens.Count == 0)
            {
                // Fall back to first 2 raw parts (even if verb-like).
                if (parts.Count == 0) shortLabel = raw;
                else if (parts.Count == 1) shortLabel = parts[0];
                else shortLabel = parts[0] + " " + parts[1];
            }
            else if (tokens.Count == 1) shortLabel = tokens[0];
            else shortLabel = tokens[0] + " " + tokens[1];

            // Title-ish casing (simple).
            try
            {
                var words = shortLabel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new StringBuilder();
                for (int i = 0; i < words.Length; i++)
                {
                    var w = words[i];
                    if (w.Length == 0) continue;
                    if (i > 0) sb.Append(' ');
                    if (w.Length == 1) sb.Append(w.ToUpperInvariant());
                    else sb.Append(char.ToUpperInvariant(w[0])).Append(w.Substring(1));
                }
                shortLabel = sb.ToString();
            }
            catch { /* ignore */ }

            var finalName = "MCP " + shortLabel;
            if (finalName.Length > 48) finalName = finalName.Substring(0, 48);
            return finalName;
        }

        public static object GetLedgerSummary(UIApplication uiapp, bool createIfMissing)
        {
            if (!McpTrackingOptions.EnableLedger)
                return new { ok = false, code = "LEDGER_DISABLED", msg = "Ledger is disabled by settings." };

            var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
            var doc = uidoc != null ? uidoc.Document : null;
            if (doc == null) return ResultUtil.Err("No active document.");

            var schema = GetOrCreateLedgerSchema();
            if (schema == null) return ResultUtil.Err(new { code = "LEDGER_SCHEMA_ERROR", msg = "Failed to resolve/create ledger schema." });

            DataStorage? storage = null;
            Entity entity;
            bool hasEntity = false;
            bool created = false;

            if (createIfMissing)
            {
                var ensureErr = EnsureLedger(doc, schema, out storage, out entity, out hasEntity, out created);
                if (ensureErr != null) return ResultUtil.Err(ensureErr);
                if (storage == null || !hasEntity) return ResultUtil.Err(new { code = "LEDGER_INIT_FAILED", msg = "Ledger storage/entity is null." });
            }
            else
            {
                var findErr = TryFindExistingLedger(doc, schema, out storage, out entity, out hasEntity);
                if (findErr != null) return ResultUtil.Err(findErr);
                if (storage == null || !hasEntity) return ResultUtil.Err(new { code = "LEDGER_NOT_FOUND", msg = "Ledger not found (createIfMissing=false)." });
            }

            var projectToken = SafeGetString(entity, schema, F_ProjectToken);
            var sequence = SafeGetInt(entity, schema, F_Sequence, fallback: 1);

            var last = new
            {
                CommandId = SafeGetString(entity, schema, F_LastCommandId),
                Name = SafeGetString(entity, schema, F_LastCommandName),
                ExecutedUtc = SafeGetString(entity, schema, F_LastCommandExecutedUtc),
                Result = SafeGetString(entity, schema, F_LastCommandResult)
            };

            var session = new
            {
                SessionId = SafeGetString(entity, schema, F_ActiveSessionId),
                Mode = SafeGetString(entity, schema, F_ActiveSessionMode),
                StartedUtc = SafeGetString(entity, schema, F_ActiveSessionStartedUtc)
            };

            string logJson = SafeGetString(entity, schema, F_CommandLogJson);
            int logCount = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(logJson))
                {
                    var arr = JArray.Parse(logJson);
                    logCount = arr != null ? arr.Count : 0;
                }
            }
            catch { /* ignore */ }

            return new
            {
                ok = true,
                schemaGuid = LedgerSchemaGuid.ToString(),
                dataStorageId = storage.Id.IntValue(),
                projectToken = projectToken,
                sequence = sequence,
                lastCommand = last,
                activeSession = session,
                commandLogCount = logCount,
                createdThisCall = created
            };
        }

        public static McpLedgerExecutionResult ExecuteWithLedger(UIApplication uiapp, RequestCommand cmd, IRevitCommandHandler handler)
        {
            // Always return an execution result object so the router can safely extract ledger info.
            var res = new McpLedgerExecutionResult();

            if (!McpTrackingOptions.EnableLedger)
            {
                res.RawResult = handler.Execute(uiapp, cmd);
                return res;
            }

            var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
            var doc = uidoc != null ? uidoc.Document : null;
            if (doc == null)
            {
                // No active document => cannot persist ledger.
                res.RawResult = handler.Execute(uiapp, cmd);
                return res;
            }

            // ---- Resolve expectations (optional, fail-closed when RequireExpectations=true) ----
            string? expectProjectToken = GetLedgerString(cmd, "ledgerProjectToken", "__ledgerProjectToken", nestedObjectKey: "ledger", nestedFieldKey: "projectToken");
            int? expectSequence = GetLedgerInt(cmd, "ledgerExpectedSequence", "__ledgerExpectedSequence", nestedObjectKey: "ledger", nestedFieldKey: "sequence");

            if (McpTrackingOptions.RequireExpectations)
            {
                if (string.IsNullOrWhiteSpace(expectProjectToken) || !expectSequence.HasValue)
                {
                    res.RawResult = ResultUtil.Err(new
                    {
                        code = "LEDGER_EXPECTATION_REQUIRED",
                        msg = "Ledger expectations are required (ledgerProjectToken and ledgerExpectedSequence).",
                        hint = "Call get_mcp_ledger_summary (or agent_bootstrap) first and pass the returned token/sequence in meta."
                    });
                    return res;
                }
            }

            // ---- Ensure ledger exists (create on first MCP execution) ----
            var schema = GetOrCreateLedgerSchema();
            if (schema == null)
            {
                res.RawResult = ResultUtil.Err(new { code = "LEDGER_SCHEMA_ERROR", msg = "Failed to resolve/create ledger schema." });
                return res;
            }

            DataStorage? storage = null;
            Entity entity;
            bool hasEntity = false;
            bool created = false;
            var ensureErr = EnsureLedger(doc, schema, out storage, out entity, out hasEntity, out created);
            if (ensureErr != null)
            {
                res.RawResult = ResultUtil.Err(ensureErr);
                return res;
            }

            if (storage == null || !hasEntity)
            {
                res.RawResult = ResultUtil.Err(new { code = "LEDGER_INIT_FAILED", msg = "Ledger storage/entity is null." });
                return res;
            }

            // ---- Read current ledger values ----
            string actualProjectToken = SafeGetString(entity, schema, F_ProjectToken);
            int actualSequence = SafeGetInt(entity, schema, F_Sequence, fallback: 1);

            // ---- Verify expectations when provided ----
            if (!string.IsNullOrWhiteSpace(expectProjectToken))
            {
                if (!string.Equals(expectProjectToken.Trim(), actualProjectToken.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    res.RawResult = ResultUtil.Err(new
                    {
                        code = "LEDGER_PROJECT_TOKEN_MISMATCH",
                        msg = "ProjectToken mismatch. Wrong project may be open.",
                        details = new { expected = expectProjectToken, actual = actualProjectToken, ledgerCreated = created },
                        ledger = BuildLedgerEcho(actualProjectToken, actualSequence, storage.Id.IntValue())
                    });
                    return res;
                }
            }

            if (expectSequence.HasValue)
            {
                if (actualSequence != expectSequence.Value)
                {
                    res.RawResult = ResultUtil.Err(new
                    {
                        code = "LEDGER_SEQUENCE_MISMATCH",
                        msg = "Sequence mismatch. Project state may have changed since last command.",
                        details = new { expected = expectSequence.Value, actual = actualSequence },
                        ledger = BuildLedgerEcho(actualProjectToken, actualSequence, storage.Id.IntValue())
                    });
                    return res;
                }
            }

            // ---- Execute command + update ledger atomically when possible ----
            // Sequence is treated as "next expected seq" (1-based).
            int assignedSeq = actualSequence;
            string commandId = Guid.NewGuid().ToString();
            string commandName = string.Empty;
            try
            {
                if (cmd != null && cmd.MetaRaw is JObject meta)
                {
                    var invoked = meta.Value<string>("invokedMethod");
                    if (!string.IsNullOrWhiteSpace(invoked))
                        commandName = invoked.Trim();
                }
            }
            catch { /* ignore */ }
            if (string.IsNullOrWhiteSpace(commandName))
                commandName = cmd != null ? (cmd.Command ?? string.Empty) : string.Empty;
            string executedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            string paramsHash = ComputeParamsHash(cmd);

            object raw;
            TransactionGroup tg = null;
            bool tgStarted = false;
            bool tgAssimilated = false;

            try
            {
                // Some orchestrator commands (e.g., revit.batch) manage their own TransactionGroup(s).
                // Avoid wrapping them in an outer TransactionGroup here to prevent nesting failures.
                bool skipOuterGroup = false;
                try
                {
                    var nameProbe = cmd != null ? (cmd.Command ?? string.Empty) : string.Empty;
                    if (cmd != null && cmd.MetaRaw is JObject meta)
                    {
                        var invoked = meta.Value<string>("invokedMethod");
                        if (!string.IsNullOrWhiteSpace(invoked))
                            nameProbe = invoked.Trim();
                    }
                    nameProbe = (nameProbe ?? string.Empty).Trim().ToLowerInvariant();
                    if (nameProbe == "revit.batch" || nameProbe == "revit_batch")
                        skipOuterGroup = true;
                }
                catch { /* ignore */ }

                if (skipOuterGroup)
                {
                    raw = ExecuteWithoutOuterGroupAndUpdateLedger(doc, uiapp, cmd, handler, schema, storage, entity, actualProjectToken, assignedSeq, commandId, commandName, executedUtc, paramsHash);
                    // NOTE: No outer TransactionGroup to dispose in this path.
                    tgStarted = false;
                    tgAssimilated = false;
                    tg = null;
                    goto AfterLedgerExec;
                }

                // Try atomic mode: wrap command and ledger update in a TransactionGroup.
                tg = new TransactionGroup(doc, BuildUndoGroupName(commandName));
                tg.Start();
                tgStarted = true;

                raw = handler.Execute(uiapp, cmd);

                // Ledger update (same group)
                using (var t = new Transaction(doc, "Update MCP Ledger"))
                {
                    t.Start();
                    var okFlag = ExtractOkFlag(raw);
                    var resultStr = okFlag.HasValue && okFlag.Value ? "OK" : "ERROR";

                    var newEntity = BuildUpdatedEntity(
                        schema,
                        entity,
                        projectToken: actualProjectToken,
                        nextSequence: assignedSeq + 1,
                        lastCommandId: commandId,
                        lastCommandName: commandName,
                        lastCommandExecutedUtc: executedUtc,
                        lastCommandResult: resultStr,
                        paramsHash: paramsHash);

                    storage.SetEntity(newEntity);
                    t.Commit();
                }

                tg.Assimilate();
                tgAssimilated = true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (tgStarted && !tgAssimilated && tg != null)
                        tg.RollBack();
                }
                catch { /* ignore */ }

                // Fallback strategy:
                // - If exception suggests nested TransactionGroup issue, retry without outer group.
                if (LooksLikeTransactionGroupNestingIssue(ex))
                {
                    raw = ExecuteWithoutOuterGroupAndUpdateLedger(doc, uiapp, cmd, handler, schema, storage, entity, actualProjectToken, assignedSeq, commandId, commandName, executedUtc, paramsHash);
                }
                else
                {
                    // Best-effort: record error stamp (no atomic guarantee if command partially modified the model)
                    raw = new { ok = false, code = "LEDGER_COMMAND_EXCEPTION", msg = ex.Message, detail = ex.ToString() };
                    TryStampLedgerError(doc, schema, storage, entity, actualProjectToken, assignedSeq, commandId, commandName, executedUtc, paramsHash, ex);
                }
            }
            finally
            {
                try { tg?.Dispose(); } catch { /* ignore */ }
            }

        AfterLedgerExec:
            // ---- Prepare ledger info for router wrapper ----
            res.RawResult = raw;
            res.LedgerInfo = new JObject
            {
                ["projectToken"] = actualProjectToken,
                ["assignedSeq"] = assignedSeq,
                ["nextSequence"] = assignedSeq + 1,
                ["dataStorageId"] = storage.Id.IntValue(),
                ["schemaGuid"] = LedgerSchemaGuid.ToString(),
                ["createdThisCall"] = created
            };
            return res;
        }

        private static object ExecuteWithoutOuterGroupAndUpdateLedger(
            Document doc,
            UIApplication uiapp,
            RequestCommand cmd,
            IRevitCommandHandler handler,
            Schema schema,
            DataStorage storage,
            Entity entity,
            string projectToken,
            int assignedSeq,
            string commandId,
            string commandName,
            string executedUtc,
            string paramsHash)
        {
            object raw;
            try
            {
                raw = handler.Execute(uiapp, cmd);
            }
            catch (Exception ex2)
            {
                raw = new { ok = false, code = "LEDGER_COMMAND_EXCEPTION", msg = ex2.Message, detail = ex2.ToString() };
            }

            try
            {
                using (var t = new Transaction(doc, "Update MCP Ledger"))
                {
                    t.Start();
                    var okFlag = ExtractOkFlag(raw);
                    var resultStr = okFlag.HasValue && okFlag.Value ? "OK" : "ERROR";

                    var newEntity = BuildUpdatedEntity(
                        schema,
                        entity,
                        projectToken: projectToken,
                        nextSequence: assignedSeq + 1,
                        lastCommandId: commandId,
                        lastCommandName: commandName,
                        lastCommandExecutedUtc: executedUtc,
                        lastCommandResult: resultStr,
                        paramsHash: paramsHash);

                    storage.SetEntity(newEntity);
                    t.Commit();
                }
            }
            catch
            {
                // Swallow: the command already executed. Ledger update failure is reported via log only.
            }

            return raw;
        }

        private static void TryStampLedgerError(
            Document doc,
            Schema schema,
            DataStorage storage,
            Entity entity,
            string projectToken,
            int assignedSeq,
            string commandId,
            string commandName,
            string executedUtc,
            string paramsHash,
            Exception ex)
        {
            try
            {
                using (var t = new Transaction(doc, "Update MCP Ledger (Error)"))
                {
                    t.Start();
                    var newEntity = BuildUpdatedEntity(
                        schema,
                        entity,
                        projectToken: projectToken,
                        nextSequence: assignedSeq + 1,
                        lastCommandId: commandId,
                        lastCommandName: commandName,
                        lastCommandExecutedUtc: executedUtc,
                        lastCommandResult: "ERROR",
                        paramsHash: paramsHash);
                    storage.SetEntity(newEntity);
                    t.Commit();
                }
            }
            catch
            {
                // best-effort
            }
        }

        private static JObject BuildLedgerEcho(string projectToken, int sequence, int storageId)
        {
            return new JObject
            {
                ["projectToken"] = projectToken,
                ["sequence"] = sequence,
                ["dataStorageId"] = storageId
            };
        }

        private static bool LooksLikeTransactionGroupNestingIssue(Exception ex)
        {
            // Revit exceptions can be localized; check common keywords.
            string msg = ex != null ? (ex.Message ?? string.Empty) : string.Empty;
            if (string.IsNullOrEmpty(msg)) msg = string.Empty;
            msg = msg.ToLowerInvariant();

            return msg.Contains("transactiongroup")
                || msg.Contains("transaction group")
                || msg.Contains("トランザクション グループ")
                || msg.Contains("トランザクショングループ");
        }

        private static Schema? GetOrCreateLedgerSchema()
        {
            try
            {
                var s = Schema.Lookup(LedgerSchemaGuid);
                if (s != null) return s;

                var sb = new SchemaBuilder(LedgerSchemaGuid);
                sb.SetSchemaName(LedgerSchemaName);
                sb.SetDocumentation("MCP Ledger (Project authenticity + command continuity).");
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Public);

                sb.AddSimpleField(F_ProjectToken, typeof(string));
                sb.AddSimpleField(F_Sequence, typeof(int));
                sb.AddSimpleField(F_LastCommandId, typeof(string));
                sb.AddSimpleField(F_LastCommandName, typeof(string));
                sb.AddSimpleField(F_LastCommandExecutedUtc, typeof(string));
                sb.AddSimpleField(F_LastCommandResult, typeof(string));
                sb.AddSimpleField(F_ActiveSessionId, typeof(string));
                sb.AddSimpleField(F_ActiveSessionMode, typeof(string));
                sb.AddSimpleField(F_ActiveSessionStartedUtc, typeof(string));
                sb.AddSimpleField(F_CommandLogJson, typeof(string));

                return sb.Finish();
            }
            catch
            {
                return null;
            }
        }

        private static object? EnsureLedger(Document doc, Schema schema, out DataStorage? storage, out Entity entity, out bool hasEntity, out bool created)
        {
            storage = null;
            entity = default(Entity);
            hasEntity = false;
            created = false;

            try
            {
                var matches = new List<DataStorage>();
                var all = new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).ToElements();
                foreach (var e in all)
                {
                    var ds = e as DataStorage;
                    if (ds == null) continue;
                    try
                    {
                        var ent = ds.GetEntity(schema);
                        if (ent.IsValid()) matches.Add(ds);
                    }
                    catch { /* ignore */ }
                }

                if (matches.Count > 1)
                {
                    return new
                    {
                        code = "LEDGER_DUPLICATE",
                        msg = "Multiple ledger DataStorage elements found. Aborting for safety.",
                        details = new { count = matches.Count, elementIds = matches.ConvertAll(x => x.Id.IntValue()).ToArray() }
                    };
                }

                if (matches.Count == 1)
                {
                    storage = matches[0];
                    entity = storage.GetEntity(schema);
                    hasEntity = true;
                    return null;
                }

                // Create new DataStorage ledger (must be inside a transaction)
                using (var t = new Transaction(doc, "Create MCP Ledger"))
                {
                    t.Start();

                    storage = DataStorage.Create(doc);
                    var entNew = new Entity(schema);

                    var token = Guid.NewGuid().ToString();
                    SafeSetString(ref entNew, schema, F_ProjectToken, token);
                    SafeSetInt(ref entNew, schema, F_Sequence, 1); // next expected seq
                    SafeSetString(ref entNew, schema, F_LastCommandId, "");
                    SafeSetString(ref entNew, schema, F_LastCommandName, "");
                    SafeSetString(ref entNew, schema, F_LastCommandExecutedUtc, "");
                    SafeSetString(ref entNew, schema, F_LastCommandResult, "");
                    SafeSetString(ref entNew, schema, F_ActiveSessionId, "");
                    SafeSetString(ref entNew, schema, F_ActiveSessionMode, "");
                    SafeSetString(ref entNew, schema, F_ActiveSessionStartedUtc, "");
                    SafeSetString(ref entNew, schema, F_CommandLogJson, "[]");

                    storage.SetEntity(entNew);
                    t.Commit();

                    entity = entNew;
                    hasEntity = true;
                    created = true;
                    return null;
                }
            }
            catch (Exception ex)
            {
                return new { code = "LEDGER_CREATE_FAILED", msg = ex.Message, detail = ex.ToString() };
            }
        }

        private static object? TryFindExistingLedger(Document doc, Schema schema, out DataStorage? storage, out Entity entity, out bool hasEntity)
        {
            storage = null;
            entity = default(Entity);
            hasEntity = false;

            try
            {
                var matches = new List<DataStorage>();
                var all = new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).ToElements();
                foreach (var e in all)
                {
                    var ds = e as DataStorage;
                    if (ds == null) continue;
                    try
                    {
                        var ent = ds.GetEntity(schema);
                        if (ent.IsValid()) matches.Add(ds);
                    }
                    catch { /* ignore */ }
                }

                if (matches.Count > 1)
                {
                    return new
                    {
                        code = "LEDGER_DUPLICATE",
                        msg = "Multiple ledger DataStorage elements found. Aborting for safety.",
                        details = new { count = matches.Count, elementIds = matches.ConvertAll(x => x.Id.IntValue()).ToArray() }
                    };
                }

                if (matches.Count == 1)
                {
                    storage = matches[0];
                    entity = storage.GetEntity(schema);
                    hasEntity = true;
                }

                return null;
            }
            catch (Exception ex)
            {
                return new { code = "LEDGER_READ_FAILED", msg = ex.Message, detail = ex.ToString() };
            }
        }

        private static Entity BuildUpdatedEntity(
            Schema schema,
            Entity existing,
            string projectToken,
            int nextSequence,
            string lastCommandId,
            string lastCommandName,
            string lastCommandExecutedUtc,
            string lastCommandResult,
            string paramsHash)
        {
            var ent = new Entity(schema);

            // Stable core fields
            SafeSetString(ref ent, schema, F_ProjectToken, projectToken ?? "");
            SafeSetInt(ref ent, schema, F_Sequence, nextSequence);
            SafeSetString(ref ent, schema, F_LastCommandId, lastCommandId ?? "");
            SafeSetString(ref ent, schema, F_LastCommandName, lastCommandName ?? "");
            SafeSetString(ref ent, schema, F_LastCommandExecutedUtc, lastCommandExecutedUtc ?? "");
            SafeSetString(ref ent, schema, F_LastCommandResult, lastCommandResult ?? "");

            // Preserve session fields (optional)
            SafeSetString(ref ent, schema, F_ActiveSessionId, SafeGetString(existing, schema, F_ActiveSessionId));
            SafeSetString(ref ent, schema, F_ActiveSessionMode, SafeGetString(existing, schema, F_ActiveSessionMode));
            SafeSetString(ref ent, schema, F_ActiveSessionStartedUtc, SafeGetString(existing, schema, F_ActiveSessionStartedUtc));

            // CommandLog (optional)
            string existingLog = SafeGetString(existing, schema, F_CommandLogJson);
            string newLog = existingLog;

            if (McpTrackingOptions.EnableCommandLog)
            {
                newLog = AppendLog(existingLog, seq: nextSequence - 1, commandId: lastCommandId, name: lastCommandName, paramsHash: paramsHash, executedUtc: lastCommandExecutedUtc, result: lastCommandResult);
            }

            SafeSetString(ref ent, schema, F_CommandLogJson, newLog ?? "[]");
            return ent;
        }

        private static string AppendLog(string existingJson, int seq, string commandId, string name, string paramsHash, string executedUtc, string result)
        {
            JArray arr;
            try
            {
                if (!string.IsNullOrWhiteSpace(existingJson))
                {
                    arr = JArray.Parse(existingJson);
                }
                else
                {
                    arr = new JArray();
                }
            }
            catch
            {
                arr = new JArray();
            }

            var entry = new JObject
            {
                ["Seq"] = seq,
                ["CommandId"] = commandId ?? "",
                ["Name"] = name ?? "",
                ["ParamsHash"] = paramsHash ?? "",
                ["ExecutedUtc"] = executedUtc ?? "",
                ["Result"] = result ?? ""
            };
            arr.Add(entry);

            int max = McpTrackingOptions.MaxLogEntries;
            if (max < 0) max = 0;
            while (max > 0 && arr.Count > max)
            {
                arr.RemoveAt(0);
            }
            if (max == 0)
            {
                // Keep empty if disabled via max=0
                arr = new JArray();
            }

            try
            {
                // Avoid JToken.ToString(Formatting) to be resilient against Json.NET binding quirks in Revit host.
                return JsonConvert.SerializeObject(arr, Formatting.None);
            }
            catch
            {
                // Last resort: an empty log (do not break command execution)
                return "[]";
            }
        }

        private static string ComputeParamsHash(RequestCommand cmd)
        {
            try
            {
                var p = cmd != null ? cmd.Params : null;
                string json;
                try
                {
                    // Avoid JToken.ToString(Formatting) to be resilient against Json.NET binding quirks in Revit host.
                    json = p != null ? JsonConvert.SerializeObject(p, Formatting.None) : "{}";
                }
                catch
                {
                    json = "{}";
                }
                using (var sha = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var hash = sha.ComputeHash(bytes);
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    return sb.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private static bool? ExtractOkFlag(object raw)
        {
            try
            {
                if (raw == null) return null;

                if (raw is JObject jo)
                {
                    var okTok = jo["ok"];
                    if (okTok != null && okTok.Type == JTokenType.Boolean) return okTok.Value<bool>();
                    return null;
                }

                var asJo = JObject.FromObject(raw);
                var ok2 = asJo["ok"];
                if (ok2 != null && ok2.Type == JTokenType.Boolean) return ok2.Value<bool>();
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeGetString(Entity e, Schema schema, string fieldName)
        {
            try
            {
                var f = schema.GetField(fieldName);
                if (f == null) return "";
                var v = e.Get<string>(f);
                return v ?? "";
            }
            catch { return ""; }
        }

        private static int SafeGetInt(Entity e, Schema schema, string fieldName, int fallback)
        {
            try
            {
                var f = schema.GetField(fieldName);
                if (f == null) return fallback;
                return e.Get<int>(f);
            }
            catch { return fallback; }
        }

        private static void SafeSetString(ref Entity e, Schema schema, string fieldName, string value)
        {
            try
            {
                var f = schema.GetField(fieldName);
                if (f == null) return;
                e.Set<string>(f, value ?? "");
            }
            catch { /* ignore */ }
        }

        private static void SafeSetInt(ref Entity e, Schema schema, string fieldName, int value)
        {
            try
            {
                var f = schema.GetField(fieldName);
                if (f == null) return;
                e.Set<int>(f, value);
            }
            catch { /* ignore */ }
        }

        private static string? GetLedgerString(RequestCommand cmd, string metaKey, string paramsKey, string nestedObjectKey, string nestedFieldKey)
        {
            try
            {
                // meta (preferred)
                if (cmd != null && cmd.MetaRaw is JObject meta)
                {
                    var v = meta.Value<string>(metaKey);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                    if (meta[nestedObjectKey] is JObject nested)
                    {
                        var v2 = nested.Value<string>(nestedFieldKey);
                        if (!string.IsNullOrWhiteSpace(v2)) return v2;
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                // params (fallback)
                var p = cmd != null ? cmd.Params : null;
                if (p != null)
                {
                    var v = p.Value<string>(paramsKey);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                    var v2 = p.Value<string>(metaKey);
                    if (!string.IsNullOrWhiteSpace(v2)) return v2;
                    if (p[nestedObjectKey] is JObject nested)
                    {
                        var v3 = nested.Value<string>(nestedFieldKey);
                        if (!string.IsNullOrWhiteSpace(v3)) return v3;
                    }
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static int? GetLedgerInt(RequestCommand cmd, string metaKey, string paramsKey, string nestedObjectKey, string nestedFieldKey)
        {
            try
            {
                if (cmd != null && cmd.MetaRaw is JObject meta)
                {
                    var v = meta.Value<int?>(metaKey);
                    if (v.HasValue) return v.Value;
                    if (meta[nestedObjectKey] is JObject nested)
                    {
                        var v2 = nested.Value<int?>(nestedFieldKey);
                        if (v2.HasValue) return v2.Value;
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                var p = cmd != null ? cmd.Params : null;
                if (p != null)
                {
                    var v = p.Value<int?>(paramsKey);
                    if (v.HasValue) return v.Value;
                    var v2 = p.Value<int?>(metaKey);
                    if (v2.HasValue) return v2.Value;
                    if (p[nestedObjectKey] is JObject nested)
                    {
                        var v3 = nested.Value<int?>(nestedFieldKey);
                        if (v3.HasValue) return v3.Value;
                    }
                }
            }
            catch { /* ignore */ }

            return null;
        }
    }
}

