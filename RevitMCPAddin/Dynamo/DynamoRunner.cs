// ================================================================
// File: Dynamo/DynamoRunner.cs
// Purpose: Execute Dynamo graphs inside Revit via reflection (no compile-time refs).
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Notes  : - Dynamo must be installed (DynamoForRevit).
//          - Graph inputs are passed by editing the .dyn Inputs payload.
// ================================================================
#nullable enable
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace RevitMCPAddin.Dynamo
{
    internal static class DynamoRunner
    {
        private static readonly object Gate = new object();
        private static bool _resolverHooked;
        private static string _dynamoRoot = string.Empty;
        private static string _dynamoRevitDir = string.Empty;
        private static string _dynamoRevitDsPath = string.Empty;
        private static Assembly? _dynamoRevitAsm;

        internal static string GetScriptsRoot()
            => Path.Combine(Paths.AddinFolder, "Dynamo", "Scripts");

        internal static string GetMetadataRoot()
            => Path.Combine(Paths.AddinFolder, "Dynamo", "ScriptMetadata");

        internal static string EnsureScriptsRoot()
        {
            var p = GetScriptsRoot();
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }

        internal static string EnsureMetadataRoot()
        {
            var p = GetMetadataRoot();
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }

        internal static bool TryInitialize(UIApplication uiapp, out string error)
        {
            error = string.Empty;
            lock (Gate)
            {
                if (_dynamoRevitAsm != null) return true;

                if (!TryResolveDynamoRoot(uiapp, out var root, out error))
                    return false;

                _dynamoRoot = root;
                _dynamoRevitDir = Path.Combine(root, "Revit");
                _dynamoRevitDsPath = Path.Combine(_dynamoRevitDir, "DynamoRevitDS.dll");
                if (!File.Exists(_dynamoRevitDsPath))
                {
                    error = "DynamoRevitDS.dll was not found under DynamoForRevit\\Revit.";
                    return false;
                }

                EnsureAssemblyResolver(root);
                _dynamoRevitAsm = Assembly.LoadFrom(_dynamoRevitDsPath);
                return true;
            }
        }

        private static bool TryResolveDynamoRoot(UIApplication uiapp, out string root, out string error)
        {
            root = string.Empty;
            error = string.Empty;

            string? revitVersion = null;
            try { revitVersion = uiapp?.Application?.VersionNumber; } catch { /* ignore */ }

            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(revitVersion))
            {
                var cand = Path.Combine(baseDir, "Autodesk", "Revit " + revitVersion, "AddIns", "DynamoForRevit");
                candidates.Add(cand);
            }

            // Fallback: scan available Revit installs
            var autodeskRoot = Path.Combine(baseDir, "Autodesk");
            if (Directory.Exists(autodeskRoot))
            {
                foreach (var dir in Directory.GetDirectories(autodeskRoot, "Revit *", SearchOption.TopDirectoryOnly))
                {
                    var cand = Path.Combine(dir, "AddIns", "DynamoForRevit");
                    if (Directory.Exists(cand)) candidates.Add(cand);
                }
            }

            foreach (var cand in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var dyn = Path.Combine(cand, "Revit", "DynamoRevitDS.dll");
                if (File.Exists(dyn))
                {
                    root = cand;
                    return true;
                }
            }

            error = "DynamoForRevit was not found for this Revit version.";
            return false;
        }

        private static void EnsureAssemblyResolver(string root)
        {
            if (_resolverHooked) return;
            _resolverHooked = true;

            var revitDir = Path.Combine(root, "Revit");
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var name = new AssemblyName(args.Name).Name + ".dll";
                    var p1 = Path.Combine(revitDir, name);
                    if (File.Exists(p1)) return Assembly.LoadFrom(p1);
                    var p2 = Path.Combine(root, name);
                    if (File.Exists(p2)) return Assembly.LoadFrom(p2);
                }
                catch { /* ignore */ }
                return null;
            };
        }

        internal static bool TryExecuteGraph(
            UIApplication uiapp,
            string dynPath,
            IDictionary<string, string> journalData,
            int timeoutMs,
            out object? model,
            out string runResult,
            out long execMs,
            out string error)
        {
            error = string.Empty;
            runResult = string.Empty;
            execMs = 0;
            model = null;

            if (_dynamoRevitAsm == null)
            {
                error = "Dynamo runtime is not initialized.";
                return false;
            }

            var appType = _dynamoRevitAsm.GetType("Dynamo.Applications.DynamoRevitApp");
            if (appType == null)
            {
                error = "DynamoRevitApp type not found.";
                return false;
            }

            var execMethod = appType.GetMethod("ExecuteDynamoCommand", new[] { typeof(IDictionary<string, string>), typeof(UIApplication) });
            if (execMethod == null)
            {
                error = "ExecuteDynamoCommand method not found.";
                return false;
            }

            var appInstance = Activator.CreateInstance(appType);
            if (appInstance == null)
            {
                error = "Failed to create DynamoRevitApp instance.";
                return false;
            }

            // Try to get existing model (if already initialized)
            model = TryGetRevitDynamoModel();
            var wait = model != null ? new EvalWaiter(model) : null;

            var sw = Stopwatch.StartNew();
            object? rawResult;
            try
            {
                rawResult = execMethod.Invoke(appInstance, new object[] { journalData, uiapp });
            }
            catch (Exception ex)
            {
                error = "Dynamo execution failed: " + ex.GetType().Name;
                RevitLogger.Warn("Dynamo ExecuteDynamoCommand exception: " + ex);
                return false;
            }
            finally
            {
                sw.Stop();
                execMs = sw.ElapsedMilliseconds;
            }

            runResult = rawResult != null ? rawResult.ToString() : string.Empty;

            // Re-acquire model if it was not available before
            if (model == null)
            {
                model = TryGetRevitDynamoModel();
                if (model != null) wait = new EvalWaiter(model);
            }

            if (wait != null)
            {
                wait.Wait(timeoutMs);
                wait.Dispose();
            }

            return true;
        }

        internal static object? TryGetRevitDynamoModel()
        {
            try
            {
                if (_dynamoRevitAsm == null) return null;
                var dynRevitType = _dynamoRevitAsm.GetType("Dynamo.Applications.DynamoRevit");
                if (dynRevitType == null) return null;

                var prop = dynRevitType.GetProperty("RevitDynamoModel", BindingFlags.Public | BindingFlags.Static);
                return prop != null ? prop.GetValue(null) : null;
            }
            catch (Exception ex)
            {
                RevitLogger.Warn("Dynamo TryGetRevitDynamoModel failed: " + ex.Message);
                return null;
            }
        }

        internal static JArray ExtractOutputs(object? model, JArray? outputSpecs, out bool outputsUnavailable)
        {
            outputsUnavailable = false;
            var outputs = new JArray();
            if (model == null)
            {
                outputsUnavailable = true;
                return outputs;
            }

            try
            {
                var modelType = model.GetType();
                var workspaceProp = modelType.GetProperty("CurrentWorkspace");
                var workspace = workspaceProp != null ? workspaceProp.GetValue(model) : null;
                if (workspace == null)
                {
                    outputsUnavailable = true;
                    return outputs;
                }

                var nodesProp = workspace.GetType().GetProperty("Nodes");
                var nodesObj = nodesProp != null ? nodesProp.GetValue(workspace) : null;
                var engineProp = modelType.GetProperty("EngineController");
                var engine = engineProp != null ? engineProp.GetValue(model) : null;

                var nodeMap = new Dictionary<Guid, object>();
                var nodesEnumerable = nodesObj as System.Collections.IEnumerable;
                if (nodesEnumerable != null)
                {
                    foreach (var node in nodesEnumerable)
                    {
                        if (node == null) continue;
                        var guidProp = node.GetType().GetProperty("GUID");
                        if (guidProp == null) continue;
                        var guid = (Guid)guidProp.GetValue(node, null);
                        if (!nodeMap.ContainsKey(guid)) nodeMap.Add(guid, node);
                    }
                }

                if (outputSpecs == null || outputSpecs.Count == 0)
                {
                    // Fallback: scan output nodes
                    foreach (var kv in nodeMap)
                    {
                        var node = kv.Value;
                        var isOutputProp = node.GetType().GetProperty("IsSetAsOutput");
                        var isOutputNodeProp = node.GetType().GetProperty("IsOutputNode");
                        bool isOutput = isOutputProp != null && (bool)isOutputProp.GetValue(node, null);
                        if (!isOutput && isOutputNodeProp != null)
                            isOutput = (bool)isOutputNodeProp.GetValue(node, null);
                        if (!isOutput) continue;

                        var nameProp = node.GetType().GetProperty("Name");
                        var name = nameProp != null ? (nameProp.GetValue(node, null) as string ?? "") : "";
                        outputs.Add(BuildOutputPayload(node, engine, kv.Key.ToString(), name));
                    }
                    return outputs;
                }

                foreach (var entry in outputSpecs.OfType<JObject>())
                {
                    var id = entry.Value<string>("Id") ?? entry.Value<string>("id") ?? "";
                    var name = entry.Value<string>("Name") ?? entry.Value<string>("name") ?? "";
                    if (!Guid.TryParse(id, out var guid)) continue;
                    if (!nodeMap.TryGetValue(guid, out var node)) continue;
                    outputs.Add(BuildOutputPayload(node, engine, id, name));
                }
            }
            catch (Exception ex)
            {
                outputsUnavailable = true;
                RevitLogger.Warn("Dynamo ExtractOutputs failed: " + ex.Message);
            }

            return outputs;
        }

        private static JObject BuildOutputPayload(object node, object? engine, string id, string name)
        {
            var payload = new JObject
            {
                ["id"] = id ?? "",
                ["name"] = name ?? ""
            };

            try
            {
                var method = node.GetType().GetMethod("GetValue");
                if (method == null || engine == null) return payload;

                var mirror = method.Invoke(node, new[] { (object)0, engine });
                if (mirror == null) return payload;

                var mirrorType = mirror.GetType();
                var stringData = mirrorType.GetProperty("StringData")?.GetValue(mirror) as string;
                var isNull = mirrorType.GetProperty("IsNull")?.GetValue(mirror) as bool? ?? false;
                var isCollection = mirrorType.GetProperty("IsCollection")?.GetValue(mirror) as bool? ?? false;
                var isDictionary = mirrorType.GetProperty("IsDictionary")?.GetValue(mirror) as bool? ?? false;
                var isPointer = mirrorType.GetProperty("IsPointer")?.GetValue(mirror) as bool? ?? false;
                var data = mirrorType.GetProperty("Data")?.GetValue(mirror);

                payload["stringValue"] = stringData ?? "";
                payload["isNull"] = isNull;
                payload["isCollection"] = isCollection;
                payload["isDictionary"] = isDictionary;
                payload["isPointer"] = isPointer;

                if (data == null)
                    payload["value"] = JValue.CreateNull();
                else if (data is string || data is double || data is float || data is bool || data is int || data is long)
                    payload["value"] = JToken.FromObject(data);
                else
                    payload["value"] = data.ToString();
            }
            catch (Exception ex)
            {
                payload["valueError"] = ex.GetType().Name;
            }

            return payload;
        }

        internal static string PrepareRunFile(string originalPath, JObject dynJson)
        {
            var runRoot = Path.Combine(Paths.LocalRoot, "dynamo", "runs");
            if (!Directory.Exists(runRoot)) Directory.CreateDirectory(runRoot);

            var baseName = Path.GetFileNameWithoutExtension(originalPath);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var name = baseName + "_run_" + stamp + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".dyn";
            var outPath = Path.Combine(runRoot, name);

            File.WriteAllText(outPath, JsonNetCompat.ToIndentedJson(dynJson));
            return outPath;
        }

        private sealed class EvalWaiter : IDisposable
        {
            private readonly ManualResetEventSlim _evt = new ManualResetEventSlim(false);
            private readonly object _model;
            private readonly EventInfo? _eventInfo;
            private readonly Delegate? _handler;

            public EvalWaiter(object model)
            {
                _model = model;
                var t = model.GetType();
                _eventInfo = t.GetEvent("EvaluationCompleted");
                if (_eventInfo == null) return;

                var handlerMethod = typeof(EvalWaiter).GetMethod(nameof(OnEvalCompleted), BindingFlags.NonPublic | BindingFlags.Instance);
                if (handlerMethod == null) return;

                _handler = Delegate.CreateDelegate(_eventInfo.EventHandlerType, this, handlerMethod);
                _eventInfo.AddEventHandler(_model, _handler);
            }

            private void OnEvalCompleted(object sender, object args)
            {
                _evt.Set();
            }

            public bool Wait(int timeoutMs)
            {
                if (timeoutMs < 1) timeoutMs = 1;
                return _evt.Wait(timeoutMs);
            }

            public void Dispose()
            {
                try
                {
                    if (_eventInfo != null && _handler != null)
                        _eventInfo.RemoveEventHandler(_model, _handler);
                }
                catch { /* ignore */ }
                _evt.Dispose();
            }
        }
    }
}
