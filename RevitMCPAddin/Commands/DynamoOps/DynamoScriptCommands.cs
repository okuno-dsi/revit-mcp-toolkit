// ================================================================
// File: Commands/DynamoOps/DynamoScriptCommands.cs
// Purpose: List and run Dynamo scripts (.dyn) from a controlled folder.
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Ledger;
using RevitMCPAddin.Core.ViewWorkspace;
using RevitMCPAddin.Dynamo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace RevitMCPAddin.Commands.DynamoOps
{
    internal static class DynamoScriptUtil
    {
        internal static bool TryLoadDynJson(string path, out JObject dyn, out string error)
        {
            error = string.Empty;
            dyn = new JObject();
            try
            {
                var text = File.ReadAllText(path);
                dyn = JObject.Parse(text);
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to parse .dyn: " + ex.GetType().Name;
                return false;
            }
        }

        internal static bool TryResolveScriptPath(string script, out string scriptPath, out string error)
        {
            error = string.Empty;
            scriptPath = string.Empty;

            if (string.IsNullOrWhiteSpace(script))
            {
                error = "script is required.";
                return false;
            }

            var root = DynamoRunner.EnsureScriptsRoot();
            var name = script.Trim();
            if (!name.EndsWith(".dyn", StringComparison.OrdinalIgnoreCase))
                name += ".dyn";

            var full = Path.IsPathRooted(name)
                ? Path.GetFullPath(name)
                : Path.GetFullPath(Path.Combine(root, name));

            var rootFull = Path.GetFullPath(root);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                error = "Script must be under Dynamo/Scripts.";
                return false;
            }

            if (!File.Exists(full))
            {
                error = "Script not found: " + full;
                return false;
            }

            scriptPath = full;
            return true;
        }

        internal static JArray ExtractInputs(JObject dyn)
        {
            var arr = new JArray();
            var inputs = dyn["Inputs"] as JArray;
            if (inputs == null) return arr;

            foreach (var it in inputs.OfType<JObject>())
            {
                var name = it.Value<string>("Name") ?? "";
                var id = it.Value<string>("Id") ?? "";
                var type = it.Value<string>("Type") ?? "";
                var value = it.Value<string>("Value") ?? "";
                var obj = new JObject
                {
                    ["name"] = name,
                    ["id"] = id,
                    ["type"] = type,
                    ["defaultValue"] = value
                };
                arr.Add(obj);
            }
            return arr;
        }

        internal static JArray ExtractOutputs(JObject dyn)
        {
            var arr = new JArray();
            var outputs = dyn["Outputs"] as JArray;
            if (outputs == null) return arr;

            foreach (var it in outputs.OfType<JObject>())
            {
                var name = it.Value<string>("Name") ?? "";
                var id = it.Value<string>("Id") ?? "";
                var type = it.Value<string>("Type") ?? "";
                var obj = new JObject
                {
                    ["name"] = name,
                    ["id"] = id,
                    ["type"] = type
                };
                arr.Add(obj);
            }
            return arr;
        }

        internal static bool ApplyInputOverrides(JObject dyn, JObject inputs, out JArray updated, out JArray warnings, out string error)
        {
            error = string.Empty;
            updated = new JArray();
            warnings = new JArray();

            if (inputs == null || !inputs.Properties().Any())
                return true;

            var inputsArray = dyn["Inputs"] as JArray;
            if (inputsArray == null)
            {
                error = "Script has no Inputs section.";
                return false;
            }

            var map = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in inputsArray.OfType<JObject>())
            {
                var name = it.Value<string>("Name") ?? "";
                var id = it.Value<string>("Id") ?? "";
                if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name))
                    map.Add(name, it);
                if (!string.IsNullOrWhiteSpace(id) && !map.ContainsKey(id))
                    map.Add(id, it);
            }

            foreach (var prop in inputs.Properties())
            {
                var key = prop.Name ?? "";
                if (!map.TryGetValue(key, out var target))
                {
                    error = "Input not found: " + key;
                    return false;
                }

                if (!TryFormatInputValue(prop.Value, target, out var value, out var formatError))
                {
                    error = formatError;
                    return false;
                }

                target["Value"] = value;

                // Dropdown helper
                var type = (target.Value<string>("Type") ?? "").Trim().ToLowerInvariant();
                if (type == "dropdownselection")
                {
                    if (prop.Value.Type == JTokenType.Integer)
                        target["SelectedIndex"] = prop.Value.Value<int>();
                    else if (prop.Value.Type == JTokenType.String)
                    {
                        if (int.TryParse(prop.Value.Value<string>(), out var idx))
                            target["SelectedIndex"] = idx;
                    }
                }

                var updatedItem = new JObject
                {
                    ["name"] = target.Value<string>("Name") ?? "",
                    ["id"] = target.Value<string>("Id") ?? "",
                    ["type"] = target.Value<string>("Type") ?? "",
                    ["value"] = value
                };
                updated.Add(updatedItem);
            }

            return true;
        }

        private static bool TryFormatInputValue(JToken token, JObject inputDef, out string value, out string error)
        {
            error = string.Empty;
            value = string.Empty;

            var type = (inputDef.Value<string>("Type") ?? "").Trim().ToLowerInvariant();
            var numberType = (inputDef.Value<string>("NumberType") ?? "").Trim().ToLowerInvariant();

            switch (type)
            {
                case "number":
                    if (TryGetNumber(token, out var numberValue, out error))
                    {
                        if (numberType == "integer")
                        {
                            var iv = (long)Math.Round(numberValue);
                            value = iv.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            value = numberValue.ToString(CultureInfo.InvariantCulture);
                        }
                        return true;
                    }
                    return false;

                case "boolean":
                    if (token.Type == JTokenType.Boolean)
                    {
                        value = token.Value<bool>() ? "true" : "false";
                        return true;
                    }
                    if (token.Type == JTokenType.String)
                    {
                        var s = token.Value<string>() ?? "";
                        if (bool.TryParse(s, out var b))
                        {
                            value = b ? "true" : "false";
                            return true;
                        }
                    }
                    error = "Input expects boolean.";
                    return false;

                case "string":
                    value = token.Type == JTokenType.String
                        ? (token.Value<string>() ?? "")
                        : JsonNetCompat.ToCompactJson(token);
                    return true;

                case "point":
                    if (token is JArray arr && arr.Count >= 3)
                    {
                        if (TryGetNumber(arr[0], out var x, out error) &&
                            TryGetNumber(arr[1], out var y, out error) &&
                            TryGetNumber(arr[2], out var z, out error))
                        {
                            value = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", x, y, z);
                            return true;
                        }
                        return false;
                    }
                    error = "Input expects [x,y,z] array for point.";
                    return false;

                default:
                    value = JsonNetCompat.ToCompactJson(token);
                    if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                        value = string.Empty;
                    return true;
            }
        }

        private static bool TryGetNumber(JToken token, out double value, out string error)
        {
            error = string.Empty;
            value = 0;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
                return true;
            }
            if (token.Type == JTokenType.String)
            {
                var s = token.Value<string>() ?? "";
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return true;
            }
            error = "Input expects a number.";
            return false;
        }
    }

    [RpcCommand("dynamo.list_scripts",
        Category = "Dynamo",
        Tags = new[] { "Dynamo", "Scripts", "Discovery" },
        Kind = "read",
        Risk = RiskLevel.Low,
        Summary = "List available Dynamo .dyn scripts from the controlled Scripts folder.",
        Constraints = new[]
        {
            "Only scripts under RevitMCPAddin/Dynamo/Scripts are listed.",
            "ScriptMetadata/<name>.json overrides description/inputs if present."
        })]
    public class DynamoListScriptsCommand : IRevitCommandHandler
    {
        public string CommandName => "dynamo.list_scripts";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var scriptsDir = DynamoRunner.EnsureScriptsRoot();
            var metaDir = DynamoRunner.EnsureMetadataRoot();

            bool dynamoReady = DynamoRunner.TryInitialize(uiapp, out var dynError);

            var scripts = new JArray();
            if (Directory.Exists(scriptsDir))
            {
                foreach (var path in Directory.GetFiles(scriptsDir, "*.dyn", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    var info = new JObject
                    {
                        ["name"] = name,
                        ["fileName"] = Path.GetFileName(path),
                        ["relativePath"] = path.Substring(scriptsDir.Length).TrimStart(Path.DirectorySeparatorChar),
                        ["description"] = "",
                        ["inputs"] = new JArray(),
                        ["outputs"] = new JArray()
                    };

                    if (DynamoScriptUtil.TryLoadDynJson(path, out var dyn, out _))
                    {
                        info["inputs"] = DynamoScriptUtil.ExtractInputs(dyn);
                        info["outputs"] = DynamoScriptUtil.ExtractOutputs(dyn);
                    }

                    var metaPath = Path.Combine(metaDir, name + ".json");
                    if (File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = JObject.Parse(File.ReadAllText(metaPath));
                            if (meta["description"] != null) info["description"] = meta["description"];
                            if (meta["inputs"] != null) info["inputs"] = meta["inputs"];
                            if (meta["outputs"] != null) info["outputs"] = meta["outputs"];
                        }
                        catch { /* best-effort */ }
                    }

                    scripts.Add(info);
                }
            }

            var payload = new JObject
            {
                ["ok"] = true,
                ["msg"] = "",
                ["scripts"] = scripts,
                ["scriptsRoot"] = scriptsDir,
                ["metadataRoot"] = metaDir,
                ["dynamoReady"] = dynamoReady,
                ["dynamoError"] = dynamoReady ? "" : dynError
            };
            return RpcResultEnvelope.StandardizePayload(payload, uiapp, cmd.Command, sw.ElapsedMilliseconds);
        }
    }

    [RpcCommand("dynamo.run_script",
        Category = "Dynamo",
        Tags = new[] { "Dynamo", "Scripts", "Execution" },
        Kind = "write",
        Risk = RiskLevel.Medium,
        Summary = "Run a Dynamo .dyn script from the controlled Scripts folder with input overrides.",
        Constraints = new[]
        {
            "Only scripts under RevitMCPAddin/Dynamo/Scripts are executable.",
            "Inputs are matched by name or Id from the .dyn Inputs section."
        })]
    public class DynamoRunScriptCommand : IRevitCommandHandler
    {
        public string CommandName => "dynamo.run_script";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = cmd.Params as JObject ?? new JObject();

            var script = p.Value<string>("script") ?? "";
            if (!DynamoScriptUtil.TryResolveScriptPath(script, out var scriptPath, out var resolveErr))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("BAD_SCRIPT", resolveErr), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            if (!DynamoRunner.TryInitialize(uiapp, out var initErr))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("DYNAMO_NOT_READY", initErr), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            if (!DynamoScriptUtil.TryLoadDynJson(scriptPath, out var dyn, out var loadErr))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("BAD_DYN", loadErr), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var inputs = p["inputs"] as JObject ?? new JObject();
            var outputsSpec = dyn["Outputs"] as JArray;

            if (!DynamoScriptUtil.ApplyInputOverrides(dyn, inputs, out var updatedInputs, out var warnings, out var inputErr))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("BAD_INPUT", inputErr), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var runPath = DynamoRunner.PrepareRunFile(scriptPath, dyn);

            var showUi = p.Value<bool?>("showUi") ?? false;
            var forceManualRun = p.Value<bool?>("forceManualRun") ?? false;
            var checkExisting = p.Value<bool?>("checkExisting") ?? true;
            var shutdownModel = p.Value<bool?>("shutdownModel") ?? false;
            var timeoutMs = p.Value<int?>("timeoutMs") ?? 120000;
            if (timeoutMs < 1000) timeoutMs = 1000;

            var hardKillRevit = p.Value<bool?>("hardKillRevit") ?? false;
            var hardKillDelayMs = p.Value<int?>("hardKillDelayMs") ?? 5000;
            if (hardKillDelayMs < 1000) hardKillDelayMs = 1000;
            if (hardKillDelayMs > 600000) hardKillDelayMs = 600000;

            var journal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dynShowUI"] = showUi ? "true" : "false",
                ["dynAutomation"] = "true",
                ["dynPath"] = runPath,
                ["dynPathExecute"] = "true",
                ["dynForceManualRun"] = forceManualRun ? "true" : "false",
                ["dynModelShutDown"] = shutdownModel ? "true" : "false",
                ["dynPathCheckExisting"] = checkExisting ? "true" : "false"
            };

            if (!DynamoRunner.TryExecuteGraph(uiapp, runPath, journal, timeoutMs, out var model, out var runResult, out var execMs, out var runErr))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("DYNAMO_RUN_FAILED", runErr), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var outputs = DynamoRunner.ExtractOutputs(model, outputsSpec, out var outputsUnavailable);

            JObject hardKillReport = new JObject();
            if (hardKillRevit)
            {
                hardKillReport["enabled"] = true;
                hardKillReport["delayMs"] = hardKillDelayMs;

                Document? doc = null;
                try { doc = uiapp?.ActiveUIDocument?.Document; } catch { doc = null; }

                string docTitle = string.Empty;
                string docPath = string.Empty;
                bool isWorkshared = false;
                bool isCloud = false;
                bool isReadOnly = false;
                try { docTitle = doc?.Title ?? ""; } catch { docTitle = ""; }
                try { docPath = doc?.PathName ?? ""; } catch { docPath = ""; }
                try { isWorkshared = doc != null && doc.IsWorkshared; } catch { isWorkshared = false; }
                try { isCloud = doc != null && doc.IsModelInCloud; } catch { isCloud = false; }
                try { isReadOnly = doc != null && doc.IsReadOnly; } catch { isReadOnly = false; }

                hardKillReport["docTitle"] = docTitle;
                hardKillReport["docPath"] = docPath;
                hardKillReport["isWorkshared"] = isWorkshared;
                hardKillReport["isCloud"] = isCloud;
                hardKillReport["isReadOnly"] = isReadOnly;

                // 1) Best-effort view workspace snapshot
                bool snapshotAttempted = false;
                bool snapshotOk = false;
                string snapshotPath = "";
                string snapshotErr = "";
                try
                {
                    if (doc != null)
                    {
                        snapshotAttempted = true;
                        if (LedgerDocKeyProvider.TryGetOrCreateDocKey(doc, createIfMissing: true, out var docKey, out _, out var docKeyErr))
                        {
                            var s = ViewWorkspaceService.CurrentSettings;
                            if (ViewWorkspaceCapture.TryCapture(uiapp, doc, docKey, s.IncludeZoom, s.Include3dOrientation, out var snap, out var capWarn, out var capErr))
                            {
                                if (ViewWorkspaceStore.TrySaveToFile(snap!, s.Retention, out var savedPath, out var storeWarn, out var storeErr))
                                {
                                    snapshotOk = true;
                                    snapshotPath = savedPath ?? "";
                                    RevitLogger.Info($"Dynamo hardKill: workspace snapshot saved. docKey={docKey} path={snapshotPath}");
                                    foreach (var w in capWarn.Concat(storeWarn))
                                        RevitLogger.Info("Dynamo hardKill: workspace snapshot warn: " + w);
                                }
                                else
                                {
                                    snapshotErr = storeErr ?? "snapshot save failed";
                                    RevitLogger.Warn("Dynamo hardKill: snapshot save failed: " + snapshotErr);
                                }
                            }
                            else
                            {
                                snapshotErr = capErr ?? "snapshot capture failed";
                                RevitLogger.Warn("Dynamo hardKill: snapshot capture failed: " + snapshotErr);
                            }
                        }
                        else
                        {
                            snapshotErr = docKeyErr ?? "docKey resolve failed";
                            RevitLogger.Warn("Dynamo hardKill: docKey resolve failed: " + snapshotErr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    snapshotErr = ex.GetType().Name + ": " + ex.Message;
                    RevitLogger.Warn("Dynamo hardKill: snapshot exception: " + snapshotErr);
                }

                hardKillReport["snapshotAttempted"] = snapshotAttempted;
                hardKillReport["snapshotOk"] = snapshotOk;
                hardKillReport["snapshotPath"] = snapshotPath;
                hardKillReport["snapshotError"] = snapshotErr;

                // 2) Workshared sync (best-effort)
                bool syncAttempted = false;
                bool syncOk = false;
                string syncErr = "";
                try
                {
                    if (doc != null && isWorkshared && !isReadOnly)
                    {
                        syncAttempted = true;
                        var twc = new TransactWithCentralOptions();
                        var syncOpts = new SynchronizeWithCentralOptions();
                        doc.SynchronizeWithCentral(twc, syncOpts);
                        syncOk = true;
                        RevitLogger.Info("Dynamo hardKill: SynchronizeWithCentral succeeded.");
                    }
                }
                catch (Exception ex)
                {
                    syncErr = ex.GetType().Name + ": " + ex.Message;
                    RevitLogger.Warn("Dynamo hardKill: SynchronizeWithCentral failed: " + syncErr);
                }

                hardKillReport["syncAttempted"] = syncAttempted;
                hardKillReport["syncOk"] = syncOk;
                hardKillReport["syncError"] = syncErr;

                // 3) Save (best-effort)
                bool saveAttempted = false;
                bool saveOk = false;
                string saveErr = "";
                try
                {
                    if (doc != null && !isReadOnly && !string.IsNullOrWhiteSpace(docPath))
                    {
                        saveAttempted = true;
                        doc.Save();
                        saveOk = true;
                        RevitLogger.Info("Dynamo hardKill: Document.Save succeeded.");
                    }
                }
                catch (Exception ex)
                {
                    saveErr = ex.GetType().Name + ": " + ex.Message;
                    RevitLogger.Warn("Dynamo hardKill: Document.Save failed: " + saveErr);
                }

                hardKillReport["saveAttempted"] = saveAttempted;
                hardKillReport["saveOk"] = saveOk;
                hardKillReport["saveError"] = saveErr;

                // Retry save/sync on UI thread if blocked by open transaction
                if (!saveOk && doc != null && !isReadOnly && !string.IsNullOrWhiteSpace(docPath))
                {
                    var retryWindowMs = hardKillDelayMs - 2000;
                    if (retryWindowMs > 5000)
                    {
                        bool uiPumpReady = false;
                        try
                        {
                            UiEventPump.Initialize();
                            uiPumpReady = true;
                        }
                        catch (Exception ex)
                        {
                            RevitLogger.Warn("Dynamo hardKill: UiEventPump initialize failed: " + ex.Message);
                        }

                        if (uiPumpReady)
                        {
                            hardKillReport["saveRetryScheduled"] = true;
                            hardKillReport["saveRetryWindowMs"] = retryWindowMs;
                            hardKillReport["saveRetryIntervalMs"] = 3000;

                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                try
                                {
                                    var deadline = DateTime.UtcNow.AddMilliseconds(retryWindowMs);
                                    int attempt = 0;
                                    while (DateTime.UtcNow < deadline)
                                    {
                                        attempt++;
                                        Thread.Sleep(3000);
                                        bool ok = UiEventPump.Instance.InvokeSmartSafe(uiapp, app =>
                                        {
                                            var d = app?.ActiveUIDocument?.Document;
                                            if (d == null) throw new InvalidOperationException("No active document.");
                                            if (d.IsReadOnly) throw new InvalidOperationException("Document is read-only.");
                                            if (d.IsModifiable) throw new InvalidOperationException("Document is modifiable (open transaction).");

                                            if (d.IsWorkshared)
                                            {
                                                var twc = new TransactWithCentralOptions();
                                                var syncOpts = new SynchronizeWithCentralOptions();
                                                d.SynchronizeWithCentral(twc, syncOpts);
                                            }
                                            d.Save();
                                        }, timeoutMs: 10000);

                                        if (ok)
                                        {
                                            RevitLogger.Info($"Dynamo hardKill: save retry succeeded (attempt {attempt}).");
                                            break;
                                        }
                                        else
                                        {
                                            RevitLogger.Warn($"Dynamo hardKill: save retry failed (attempt {attempt}).");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    RevitLogger.Warn("Dynamo hardKill: save retry scheduling failed: " + ex.Message);
                                }
                            });
                        }
                        else
                        {
                            hardKillReport["saveRetryScheduled"] = false;
                            hardKillReport["saveRetryError"] = "UiEventPump not initialized.";
                        }
                    }
                }

                // 4) Schedule restart (best-effort)
                bool restartScheduled = false;
                string restartErr = "";
                string restartCmdPath = "";
                string restartExe = "";
                string restartTarget = docPath;

                try
                {
                    if (!string.IsNullOrWhiteSpace(restartTarget) && File.Exists(restartTarget) && !isCloud)
                    {
                        restartExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                        if (!string.IsNullOrWhiteSpace(restartExe) && File.Exists(restartExe))
                        {
                            var delayMs = hardKillDelayMs + 2000;
                            var delaySec = Math.Max(2, delayMs / 1000);
                            var tempDir = Path.Combine(Paths.LocalRoot, "temp");
                            Directory.CreateDirectory(tempDir);
                            restartCmdPath = Path.Combine(tempDir, $"revit_restart_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}.cmd");
                            var sb = new StringBuilder();
                            sb.AppendLine("@echo off");
                            sb.AppendLine($"timeout /t {delaySec} /nobreak >nul");
                            sb.AppendLine($"start \"\" \"{restartExe}\" \"{restartTarget}\"");
                            sb.AppendLine("del \"%~f0\" >nul 2>&1");
                            File.WriteAllText(restartCmdPath, sb.ToString(), Encoding.ASCII);

                            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + restartCmdPath + "\"")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            Process.Start(psi);
                            restartScheduled = true;
                            RevitLogger.Info($"Dynamo hardKill: restart scheduled. exe={restartExe} target={restartTarget}");
                        }
                        else
                        {
                            restartErr = "Revit.exe path not found.";
                        }
                    }
                    else
                    {
                        restartErr = isCloud ? "Cloud model: auto-reopen skipped." : "Document path not found.";
                    }
                }
                catch (Exception ex)
                {
                    restartErr = ex.GetType().Name + ": " + ex.Message;
                    RevitLogger.Warn("Dynamo hardKill: restart schedule failed: " + restartErr);
                }

                hardKillReport["restartScheduled"] = restartScheduled;
                hardKillReport["restartExe"] = restartExe;
                hardKillReport["restartTarget"] = restartTarget;
                hardKillReport["restartCmdPath"] = restartCmdPath;
                hardKillReport["restartError"] = restartErr;

                // 4.1) Schedule external server stop check (after kill)
                bool stopCheckScheduled = false;
                string stopCheckErr = "";
                string stopCheckCmdPath = "";
                string stopCheckScriptPath = "";
                string stopCheckLogPath = "";
                string stopCheckExe = "";
                try
                {
                    var delayMs = hardKillDelayMs + 3000;
                    var delaySec = Math.Max(2, delayMs / 1000);
                    var logDir = Path.Combine(Paths.LocalRoot, "logs");
                    Directory.CreateDirectory(logDir);

                    stopCheckLogPath = Path.Combine(logDir, $"server_stop_check_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    try
                    {
                        File.WriteAllText(stopCheckLogPath, "scheduled" + Environment.NewLine, Encoding.ASCII);
                    }
                    catch
                    {
                        // ignore log pre-write errors
                    }
                    stopCheckExe = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        @"WindowsPowerShell\v1.0\powershell.exe");

                    var scriptDir = Path.Combine(Paths.LocalRoot, "temp");
                    Directory.CreateDirectory(scriptDir);
                    stopCheckScriptPath = Path.Combine(scriptDir, $"server_stop_check_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
                    stopCheckCmdPath = Path.Combine(scriptDir, $"server_stop_check_{DateTime.Now:yyyyMMdd_HHmmss}.cmd");
                    var scriptLines = new[]
                    {
                        $"Start-Sleep -Seconds {delaySec}",
                        "$state = 'free'",
                        $"try {{ $c=New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1',{AppServices.CurrentPort}); $c.Close(); $state = 'busy' }} catch {{ $state = 'free' }}",
                        $"$state | Add-Content -Path '{stopCheckLogPath}' -Encoding ASCII"
                    };
                    File.WriteAllText(stopCheckScriptPath, string.Join(Environment.NewLine, scriptLines), Encoding.ASCII);

                    var cmdLines = new[]
                    {
                        "@echo off",
                        $"echo started>> \"{stopCheckLogPath}\"",
                        $"start \"\" /b \"{stopCheckExe}\" -NoProfile -ExecutionPolicy Bypass -File \"{stopCheckScriptPath}\""
                    };
                    File.WriteAllText(stopCheckCmdPath, string.Join(Environment.NewLine, cmdLines), Encoding.ASCII);

                    var psi = new ProcessStartInfo("cmd.exe", "/c \"" + stopCheckCmdPath + "\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Paths.LocalRoot
                    };
                    var proc = Process.Start(psi);
                    stopCheckScheduled = true;
                    var pidInfo = proc != null ? $" pid={proc.Id}" : " pid=unknown";
                    RevitLogger.Info($"Dynamo hardKill: server stop check scheduled. log={stopCheckLogPath}{pidInfo}");
                }
                catch (Exception ex)
                {
                    stopCheckErr = ex.GetType().Name + ": " + ex.Message;
                    RevitLogger.Warn("Dynamo hardKill: server stop check scheduling failed: " + stopCheckErr);
                }

                hardKillReport["serverStopCheckScheduled"] = stopCheckScheduled;
                hardKillReport["serverStopCheckLogPath"] = stopCheckLogPath;
                hardKillReport["serverStopCheckCmdPath"] = stopCheckCmdPath;
                hardKillReport["serverStopCheckScriptPath"] = stopCheckScriptPath;
                hardKillReport["serverStopCheckExe"] = stopCheckExe;
                hardKillReport["serverStopCheckError"] = stopCheckErr;

                // 5) Force terminate after delay (async)
                var pid = Process.GetCurrentProcess().Id;
                var port = AppServices.CurrentPort;
                hardKillReport["serverStopCheckPlanned"] = true;
                hardKillReport["serverStopCheckWaitMs"] = 5000;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Thread.Sleep(hardKillDelayMs);
                        try
                        {
                            var stopRes = RevitMCPAddin.Core.Net.ServerProcessManager.StopByLock(pid, port);
                            RevitLogger.Info($"Dynamo hardKill: stop server result: {stopRes.ok} {stopRes.msg}");
                        }
                        catch (Exception ex)
                        {
                            RevitLogger.Warn("Dynamo hardKill: stop server failed: " + ex.Message);
                        }

                        try
                        {
                            var chk = RevitMCPAddin.Core.Net.ServerProcessManager.CheckServerStopped(port, 5000);
                            RevitLogger.Info($"Dynamo hardKill: server stop check: stopped={chk.stopped} lockExists={chk.lockExists} pid={chk.serverPid} msg={chk.msg}");
                        }
                        catch (Exception ex)
                        {
                            RevitLogger.Warn("Dynamo hardKill: server stop check failed: " + ex.Message);
                        }
                        try { Process.GetCurrentProcess().Kill(); } catch { /* ignore */ }
                    }
                    catch { /* ignore */ }
                });
            }

            var result = new JObject
            {
                ["script"] = Path.GetFileName(scriptPath),
                ["scriptPath"] = scriptPath,
                ["runPath"] = runPath,
                ["executionTimeMs"] = execMs,
                ["runResult"] = runResult,
                ["updatedInputs"] = updatedInputs,
                ["outputs"] = outputs,
                ["outputsUnavailable"] = outputsUnavailable
            };
            if (hardKillRevit)
            {
                result["hardKill"] = hardKillReport;
                result["hardKillNote"] = "Revit will be force-terminated after the delay. Unsaved changes may be lost if save/sync fails.";
            }

            if (warnings.Count > 0)
                result["warnings"] = warnings;

            var payload = new JObject
            {
                ["ok"] = true,
                ["msg"] = "",
                ["result"] = result
            };

            return RpcResultEnvelope.StandardizePayload(payload, uiapp, cmd.Command, sw.ElapsedMilliseconds);
        }
    }
}
