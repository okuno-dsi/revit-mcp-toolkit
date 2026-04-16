using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.UI.PythonRunner
{
    public partial class PythonRunnerWindow : Window
    {
        private static readonly Brush McpCommandBrush = CreateFrozenBrush(0x5B, 0x3A, 0x00); // dark brown
        private static readonly Regex McpMethodInRpcCallRx = new Regex(
            "\\b(?:rpc|jsonrpc|call|mcp_rpc)\\s*\\(\\s*(?:r)?['\\\"](?<method>[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+)['\\\"]",
            RegexOptions.Compiled);
        private static readonly Regex McpMethodInJsonRpcRx = new Regex(
            "(?i)(['\\\"]method['\\\"])\\s*:\\s*(?:r)?['\\\"](?<method>[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z0-9_]+)+)['\\\"]",
            RegexOptions.Compiled);

        private readonly string _defaultScriptsRoot;
        private string? _currentPath;
        private bool _isDirty;
        private Process? _proc;
        private TaskCompletionSource<int>? _exitTcs;
        private string _lastAutoPolledJobId = "";
        private bool _suppressTextChange;
        private bool _needsOutputHeader = true;
        private readonly DispatcherTimer _highlightTimer;
        private bool _suppressMetaChange;
        private ScriptLibraryWindow? _libraryWindow;
        private string? _lastRunHash;
        private ScrollViewer? _scriptScrollViewer;
        private bool _suppressLineNumbers;
        private string? _lastInboxPath;
        private readonly List<ScriptOptionBinding> _optionBindings = new List<ScriptOptionBinding>();
        private string _lastOptionProfileKey = string.Empty;
        private bool _stopRequested;
        private bool _isClosing;

        private static readonly Regex FeatureLineRx = new Regex("^\\s*#\\s*@feature\\s*:\\s*(?<feature>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex KeywordLineRx = new Regex("^\\s*#\\s*@keywords\\s*:\\s*(?<keywords>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FeatureInlineRx = new Regex("^\\s*#\\s*@feature\\s*:\\s*(?<feature>[^#|]*)(?:\\|\\s*keywords\\s*:\\s*(?<keywords>.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ArgHintLineRx = new Regex("^\\s*#\\s*@arg\\s*:\\s*(?<spec>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HashSet<string> DefaultHiddenOptionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--config"
        };

        private sealed class ScriptOptionDef
        {
            public string Name { get; set; } = string.Empty; // --port
            public string Label { get; set; } = string.Empty; // port
            public string Type { get; set; } = "string"; // string|int|float|bool|choice|file|path|dir
            public string DefaultValue { get; set; } = string.Empty;
            public string Hint { get; set; } = string.Empty;
            public string Example { get; set; } = string.Empty;
            public string[] Choices { get; set; } = Array.Empty<string>();
            public bool Required { get; set; }
            public string Action { get; set; } = string.Empty; // store_true/store_false
            public bool FromHint { get; set; }
            public bool Hidden { get; set; }
        }

        private sealed class ScriptOptionBinding
        {
            public ScriptOptionDef Def { get; set; } = new ScriptOptionDef();
            public FrameworkElement? Editor { get; set; }
        }

        public PythonRunnerWindow(string? docTitle = null, string? docKey = null)
        {
            _defaultScriptsRoot = ResolveScriptsRoot(docTitle, docKey);
            InitializeComponent();

            _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _highlightTimer.Tick += (_, __) =>
            {
                _highlightTimer.Stop();
                ApplyMcpCommandHighlight();
            };

            BtnOpen.Click += (_, __) => OpenScript();
            BtnLoadCodex.Click += (_, __) => LoadCodexScript();
            BtnSave.Click += (_, __) => SaveScript();
            BtnSaveAs.Click += (_, __) => SaveScriptAs();
            BtnSelectAll.Click += (_, __) => SelectAllScript();
            BtnClearScript.Click += (_, __) => ClearScript();
            BtnResetPaste.Click += (_, __) => ResetAndPaste();
            BtnReloadOptions.Click += (_, __) => ReloadScriptOptionEditors(preserveCurrentValues: true, addOutput: true);
            BtnCopyOptionSpec.Click += (_, __) => CopyOptionSpecToClipboard();
            BtnRun.Click += async (_, __) => await RunAsync();
            BtnStop.Click += (_, __) => StopProcess();
            BtnCopyOut.Click += (_, __) => CopyOutput();
            BtnClearOut.Click += (_, __) => ClearOutput();
            BtnLibrary.Click += (_, __) => OpenLibrary();

            DataObject.AddPastingHandler(ScriptBox, OnScriptBoxPasting);
            ScriptBox.TextChanged += (_, __) =>
            {
                if (_suppressTextChange) return;
                _isDirty = true;
                UpdateStatus();
                ScheduleHighlight();
                UpdateLineNumbers();
            };

            TxtFeature.TextChanged += (_, __) =>
            {
                if (_suppressMetaChange) return;
                _isDirty = true;
                UpdateStatus();
            };
            TxtKeywords.TextChanged += (_, __) =>
            {
                if (_suppressMetaChange) return;
                _isDirty = true;
                UpdateStatus();
            };

            UpdateStatus();
            ApplyMcpCommandHighlight();

            ScriptBox.Loaded += (_, __) =>
            {
                AttachScriptScrollSync();
                UpdateLineNumbers();
            };

            // Load last script on startup (if exists)
            try
            {
                var last = PythonRunnerScriptLibrary.LoadLastScript();
                if (!string.IsNullOrWhiteSpace(last) && File.Exists(last))
                {
                    LoadScriptFromPathInternal(last, addOutput: false);
                }
            }
            catch { /* ignore */ }

            ReloadScriptOptionEditors(preserveCurrentValues: false, addOutput: false);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            StopProcess(addOutput: false);
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            try { PersistOptionProfile(); } catch { /* ignore */ }
            base.OnClosed(e);
        }

        private void OpenScript()
        {
            StartOutputGroup();
            try
            {
                var folder = ResolveDialogFolder();
                var dlg = new OpenFileDialog
                {
                    Filter = "Python (*.py)|*.py|All files (*.*)|*.*"
                };
                if (!string.IsNullOrWhiteSpace(folder))
                    dlg.InitialDirectory = folder;

                if (dlg.ShowDialog(this) != true) return;
                LoadScriptFromPath(dlg.FileName);
            }
            catch (Exception ex)
            {
                AppendOutput("Open failed: " + ex.Message);
            }
        }

        private void LoadCodexScript()
        {
            StartOutputGroup();
            if (!TryLoadFromInbox(addOutput: true))
            {
                AppendOutput("Codex script not found. (inbox is empty or file missing)");
            }
        }

        private void SaveScript()
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                SaveScriptAs();
                return;
            }

            SaveToPath(_currentPath);
        }

        private void SaveScriptAs()
        {
            var folder = ResolveDialogFolder();

            var dlg = new SaveFileDialog
            {
                Filter = "Python (*.py)|*.py|All files (*.*)|*.*",
                InitialDirectory = string.IsNullOrWhiteSpace(folder) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : folder,
                FileName = $"script_{DateTime.Now:yyyyMMdd_HHmmss}.py"
            };

            if (dlg.ShowDialog(this) != true) return;
            SaveToPath(dlg.FileName);
        }

        private void SaveToPath(string path)
        {
            StartOutputGroup();
            try
            {
                var raw = GetScriptText();
                var normalized = NormalizeScriptForSave(raw);
                if (!string.Equals(raw, normalized, StringComparison.Ordinal))
                {
                    _suppressTextChange = true;
                    SetScriptText(normalized);
                    _suppressTextChange = false;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, GetScriptText(), new UTF8Encoding(false));
                _currentPath = path;
                _isDirty = false;
                UpdateStatus();
                ReloadScriptOptionEditors(preserveCurrentValues: true, addOutput: false);
                try { PythonRunnerScriptLibrary.SaveLastScript(path); } catch { /* ignore */ }
                AppendOutput("Saved: " + path);
            }
            catch (Exception ex)
            {
                AppendOutput("Save failed: " + ex.Message);
            }
        }

        private void SelectAllScript()
        {
            ScriptBox.Focus();
            ScriptBox.SelectAll();
        }

        private void ClearScript()
        {
            SetScriptText("");
            ReloadScriptOptionEditors(preserveCurrentValues: false, addOutput: false);
            _isDirty = true;
            UpdateStatus();
        }

        private void ResetAndPaste()
        {
            StartOutputGroup();
            if (!Clipboard.ContainsText())
            {
                AppendOutput("Clipboard has no text.");
                return;
            }

            var txt = NormalizePastedScriptText(Clipboard.GetText() ?? "");
            SetScriptText(txt);
            ApplyMetadataFromText(txt);
            ReloadScriptOptionEditors(preserveCurrentValues: false, addOutput: false);
            _isDirty = true;
            UpdateStatus();
        }

        private void CopyOutput()
        {
            StartOutputGroup();
            try
            {
                Clipboard.SetText(OutputBox.Text ?? "");
            }
            catch (Exception ex)
            {
                AppendOutput("Copy failed: " + ex.Message);
            }
        }

        private void ClearOutput()
        {
            OutputBox.Clear();
            _needsOutputHeader = true;
        }

        private void OnScriptBoxPasting(object sender, DataObjectPastingEventArgs e)
        {
            try
            {
                var data = e.SourceDataObject;
                if (data == null)
                    return;

                string? text = null;
                if (data.GetDataPresent(DataFormats.UnicodeText, true))
                    text = data.GetData(DataFormats.UnicodeText, true) as string;
                if (text == null && data.GetDataPresent(DataFormats.Text, true))
                    text = data.GetData(DataFormats.Text, true) as string;
                if (text == null)
                    return;

                e.CancelCommand();
                ScriptBox.BeginChange();
                try
                {
                    ScriptBox.Selection.Text = NormalizePastedScriptText(text);
                }
                finally
                {
                    ScriptBox.EndChange();
                }
            }
            catch
            {
                // Fall back to the built-in paste behavior if normalization fails.
            }
        }

        private async Task RunAsync()
        {
            StartOutputGroup();
            if (_proc != null && !_proc.HasExited)
            {
                AppendOutput("Run blocked: process is still running.");
                return;
            }

            BtnRun.IsEnabled = false;
            BtnStop.IsEnabled = true;
            _stopRequested = false;

            try
            {
                var port = PortSettings.GetPort();
                var scriptText = GetScriptText();
                var rewritten = NormalizeScriptForSave(scriptText);
                var rewrittenPorts = RewritePorts(rewritten, port, out var urlCount, out var argCount);
                var endpointCount = 0;
                rewrittenPorts = RewriteLegacyEndpoint(rewrittenPorts, out endpointCount);

                if (urlCount > 0 || argCount > 0 || endpointCount > 0)
                {
                    AppendOutput($"Rewrite: url={urlCount}, args={argCount}, endpoint={endpointCount} -> {port}");
                }

                ReloadScriptOptionEditors(preserveCurrentValues: true, addOutput: false);
                if (!ValidateRequiredOptions(out var requiredMsg))
                {
                    AppendOutput(requiredMsg);
                    return;
                }
                var runPath = SaveRunCopy(rewrittenPorts);
                AppendOutput("Run file: " + runPath);

                var pythonExe = ResolvePythonExe();
                if (string.IsNullOrWhiteSpace(pythonExe))
                {
                    AppendOutput("Python not found. Place python.exe in add-in \\python or set REVIT_MCP_PYTHON_EXE.");
                    return;
                }

                var profile = CollectCurrentOptionValues();
                profile["__extraArgs"] = (TxtArgs?.Text ?? string.Empty).Trim();
                var profileKey = GetOptionProfileKey(scriptText);
                if (!string.IsNullOrWhiteSpace(profileKey))
                {
                    PythonRunnerScriptLibrary.SaveArgsProfile(profileKey, profile);
                }

                var optionArgs = BuildArgsFromOptionEditors();
                var extraArgs = (TxtArgs?.Text ?? string.Empty).Trim();
                var mergedArgs = string.IsNullOrWhiteSpace(optionArgs)
                    ? extraArgs
                    : (string.IsNullOrWhiteSpace(extraArgs) ? optionArgs : (optionArgs + " " + extraArgs));
                var rewrittenArgs = RewritePortArgs(mergedArgs, port, out var argHits2);
                if (argHits2 > 0)
                {
                    AppendOutput($"Rewrite args: {argHits2} -> {port}");
                }
                var sanitizedArgs = RemoveBareBooleanTokens(rewrittenArgs, out var removedBoolTokens);
                if (removedBoolTokens > 0)
                {
                    AppendOutput($"Args cleanup: removed {removedBoolTokens} bare boolean token(s).");
                }
                await StartProcessAsync(pythonExe, runPath, port, sanitizedArgs);
            }
            catch (Exception ex)
            {
                AppendOutput("Run error: " + ex.Message);
            }
            finally
            {
                BtnRun.IsEnabled = true;
                BtnStop.IsEnabled = false;
            }
        }

        private async Task StartProcessAsync(string pythonExe, string scriptPath, int port, string argsText)
        {
            var scriptDir = Path.GetDirectoryName(scriptPath) ?? GetDefaultFolder();
            var extraArgs = string.IsNullOrWhiteSpace(argsText) ? "" : (" " + argsText);
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-u \"" + scriptPath + "\"" + extraArgs,
                WorkingDirectory = scriptDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            var pythonHome = Path.GetDirectoryName(pythonExe);
            if (!string.IsNullOrWhiteSpace(pythonHome))
            {
                psi.EnvironmentVariables["PYTHONHOME"] = pythonHome;
            }
            psi.EnvironmentVariables["REVIT_MCP_PORT"] = port.ToString(CultureInfo.InvariantCulture);
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            var sitePackages = Path.Combine(baseDir, "python", "Lib", "site-packages");
            if (Directory.Exists(sitePackages))
            {
                var existing = psi.EnvironmentVariables.ContainsKey("PYTHONPATH")
                    ? psi.EnvironmentVariables["PYTHONPATH"]
                    : null;
                psi.EnvironmentVariables["PYTHONPATH"] = string.IsNullOrWhiteSpace(existing)
                    ? sitePackages
                    : sitePackages + Path.PathSeparator + existing;
            }

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _proc = proc;
            _exitTcs = exitTcs;

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) AppendOutput(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) AppendOutput("ERR: " + e.Data);
            };
            proc.Exited += (_, __) =>
            {
                exitTcs.TrySetResult(GetProcessExitCode(proc, -1));
            };

            AppendOutput("Process start: " + pythonExe);
            if (!proc.Start())
            {
                AppendOutput("Process failed to start.");
                exitTcs.TrySetResult(-1);
                try { proc.Dispose(); } catch { }
                if (ReferenceEquals(_proc, proc)) _proc = null;
                if (ReferenceEquals(_exitTcs, exitTcs)) _exitTcs = null;
                return;
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var exitCode = await exitTcs.Task.ConfigureAwait(false);
            var stopped = _stopRequested || exitCode == -2;
            AppendOutput(stopped ? "Process stopped." : ("Process exit: " + exitCode));

            if (!stopped && !_isClosing)
            {
                await TryAutoPollQueuedJobAsync().ConfigureAwait(false);
            }

            try { proc.Dispose(); } catch { }
            if (ReferenceEquals(_proc, proc)) _proc = null;
            if (ReferenceEquals(_exitTcs, exitTcs)) _exitTcs = null;
        }

        private void StopProcess()
        {
            StopProcess(addOutput: true);
        }

        private void StopProcess(bool addOutput)
        {
            if (addOutput) StartOutputGroup();
            var proc = _proc;
            var exitTcs = _exitTcs;
            if (proc == null) return;

            _stopRequested = true;
            try
            {
                if (!proc.HasExited)
                {
                    var killed = TryKillProcessTree(proc, out var killMessage);
                    if (addOutput) AppendOutput(killMessage);
                    if (!killed)
                    {
                        exitTcs?.TrySetResult(GetProcessExitCode(proc, -2));
                    }
                }
                else
                {
                    exitTcs?.TrySetResult(GetProcessExitCode(proc, -2));
                }
            }
            catch (Exception ex)
            {
                if (addOutput) AppendOutput("Stop failed: " + ex.Message);
                exitTcs?.TrySetResult(-2);
            }
            finally
            {
                exitTcs?.TrySetResult(-2);
            }
        }

        private static int GetProcessExitCode(Process process, int fallback)
        {
            try
            {
                return process.HasExited ? process.ExitCode : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool TryKillProcessTree(Process process, out string message)
        {
            var pid = 0;
            try { pid = process.Id; } catch { }

            if (pid > 0 && Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = "/PID " + pid.ToString(CultureInfo.InvariantCulture) + " /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var killer = Process.Start(psi))
                    {
                        if (killer != null)
                        {
                            if (!killer.WaitForExit(5000))
                            {
                                try { killer.Kill(); } catch { }
                                message = "Stop requested; taskkill timed out.";
                                return false;
                            }

                            if (killer.ExitCode == 0 || SafeHasExited(process))
                            {
                                message = "Process tree stopped.";
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    // Fall back to Process.Kill below.
                }
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
                message = "Process stopped.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Stop failed: " + ex.Message;
                return false;
            }
        }

        private static bool SafeHasExited(Process process)
        {
            try { return process.HasExited; }
            catch { return true; }
        }

        private void ReloadScriptOptionEditors(bool preserveCurrentValues, bool addOutput)
        {
            try
            {
                var scriptText = GetScriptText();
                var defs = BuildOptionDefinitions(scriptText);
                var currentValues = preserveCurrentValues
                    ? CollectCurrentOptionValues()
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var profileKey = GetOptionProfileKey(scriptText);
                var savedValues = string.IsNullOrWhiteSpace(profileKey)
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : PythonRunnerScriptLibrary.LoadArgsProfile(profileKey);

                RenderOptionEditors(defs, currentValues, savedValues);

                if (!preserveCurrentValues && savedValues.TryGetValue("__extraArgs", out var extra))
                {
                    TxtArgs.Text = extra ?? string.Empty;
                }

                _lastOptionProfileKey = profileKey;
                if (addOutput)
                {
                    AppendOutput($"Options loaded: {defs.Count}");
                }
            }
            catch (Exception ex)
            {
                if (addOutput)
                {
                    AppendOutput("Options parse failed: " + ex.Message);
                }
            }
        }

        private void PersistOptionProfile()
        {
            var values = CollectCurrentOptionValues();
            values["__extraArgs"] = (TxtArgs?.Text ?? string.Empty).Trim();
            var key = !string.IsNullOrWhiteSpace(_lastOptionProfileKey)
                ? _lastOptionProfileKey
                : GetOptionProfileKey(GetScriptText());
            if (string.IsNullOrWhiteSpace(key)) return;
            PythonRunnerScriptLibrary.SaveArgsProfile(key, values);
        }

        private string GetOptionProfileKey(string scriptText)
        {
            if (!string.IsNullOrWhiteSpace(_currentPath))
            {
                try { return Path.GetFullPath(_currentPath).ToLowerInvariant(); }
                catch { return _currentPath; }
            }
            var hash = ComputeSha256(NormalizeScriptForSave(scriptText ?? string.Empty));
            return string.IsNullOrWhiteSpace(hash) ? string.Empty : ("hash:" + hash);
        }

        private Dictionary<string, string> CollectCurrentOptionValues()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in _optionBindings)
            {
                if (binding == null || binding.Def == null || string.IsNullOrWhiteSpace(binding.Def.Name)) continue;
                values[binding.Def.Name] = GetOptionEditorValue(binding) ?? string.Empty;
            }
            return values;
        }

        private static string? GetOptionEditorValue(ScriptOptionBinding binding)
        {
            if (binding.Editor is CheckBox chk)
            {
                return (chk.IsChecked ?? false) ? "true" : "false";
            }
            if (binding.Editor is ComboBox cmb)
            {
                return (cmb.Text ?? string.Empty).Trim();
            }
            if (binding.Editor is TextBox tb)
            {
                return (tb.Text ?? string.Empty).Trim();
            }
            return null;
        }

        private void RenderOptionEditors(
            IList<ScriptOptionDef> defs,
            IDictionary<string, string> currentValues,
            IDictionary<string, string> savedValues)
        {
            _optionBindings.Clear();
            OptionsPanel.Children.Clear();

            if (defs == null || defs.Count == 0)
            {
                TxtOptionsInfo.Text = "No options detected. Add `argparse` in script or use `# @arg:` hint comments.";
                return;
            }

            var visibleDefs = defs.Where(d => d != null && !ShouldHideOption(d)).ToList();
            var hiddenCount = defs.Count - visibleDefs.Count;
            if (visibleDefs.Count == 0)
            {
                TxtOptionsInfo.Text = hiddenCount > 0
                    ? $"All detected options are hidden ({hiddenCount}). Use Extra Args when needed."
                    : "No options detected. Add `argparse` in script or use `# @arg:` hint comments.";
                return;
            }

            TxtOptionsInfo.Text = hiddenCount > 0
                ? $"{visibleDefs.Count} options shown ({hiddenCount} hidden). Values are saved per script and restored automatically."
                : $"{visibleDefs.Count} options detected. Values are saved per script and restored automatically.";

            foreach (var def in visibleDefs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var labelText = string.IsNullOrWhiteSpace(def.Label) ? def.Name : def.Label;
                if (def.Required)
                {
                    labelText += " *";
                }
                var label = new TextBlock
                {
                    Text = labelText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = def.Required ? (def.Name + " (required)") : def.Name,
                    Foreground = def.Required ? Brushes.Red : Brushes.Black,
                    FontWeight = def.Required ? FontWeights.Bold : FontWeights.Normal
                };
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                var value = def.DefaultValue ?? string.Empty;
                if (savedValues != null && savedValues.TryGetValue(def.Name, out var saved)) value = saved ?? string.Empty;
                if (currentValues != null && currentValues.TryGetValue(def.Name, out var current)) value = current ?? string.Empty;

                FrameworkElement editor;
                if (string.Equals(def.Type, "bool", StringComparison.OrdinalIgnoreCase))
                {
                    var chk = new CheckBox
                    {
                        IsChecked = ParseBool(value, ParseBool(def.DefaultValue, false)),
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = BuildOptionHint(def)
                    };
                    editor = chk;
                }
                else if (def.Choices != null && def.Choices.Length > 0)
                {
                    var cmb = new ComboBox
                    {
                        MinWidth = 220,
                        IsEditable = true,
                        ToolTip = BuildOptionHint(def)
                    };
                    foreach (var c in def.Choices)
                        cmb.Items.Add(c);
                    cmb.Text = value ?? string.Empty;
                    editor = cmb;
                }
                else
                {
                    var tb = new TextBox
                    {
                        MinWidth = 220,
                        Text = value ?? string.Empty,
                        ToolTip = BuildOptionHint(def)
                    };
                    editor = tb;
                }

                if (def.Required)
                {
                    if (editor is TextBox reqTb)
                    {
                        reqTb.Foreground = Brushes.DarkRed;
                    }
                    else if (editor is ComboBox reqCb)
                    {
                        reqCb.Foreground = Brushes.DarkRed;
                    }
                    else if (editor is CheckBox reqChk)
                    {
                        reqChk.Foreground = Brushes.DarkRed;
                    }
                }

                Grid.SetColumn(editor, 1);
                row.Children.Add(editor);

                if (IsPathLikeOption(def) && editor is TextBox pathBox)
                {
                    var browse = new Button
                    {
                        Content = "...",
                        Width = 32,
                        Height = 24,
                        Margin = new Thickness(6, 0, 8, 0),
                        ToolTip = "Browse"
                    };
                    browse.Click += (_, __) => OpenPathChooserForOption(def, pathBox);
                    Grid.SetColumn(browse, 2);
                    row.Children.Add(browse);
                }

                var hint = new TextBlock
                {
                    Text = BuildOptionHint(def),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(hint, 3);
                row.Children.Add(hint);

                OptionsPanel.Children.Add(row);
                _optionBindings.Add(new ScriptOptionBinding { Def = def, Editor = editor });
            }
        }

        private static bool ParseBool(string? text, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text)) return defaultValue;
            var t = text.Trim().ToLowerInvariant();
            if (t == "1" || t == "true" || t == "yes" || t == "on") return true;
            if (t == "0" || t == "false" || t == "no" || t == "off") return false;
            return defaultValue;
        }

        private static string BuildOptionHint(ScriptOptionDef def)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(def.Type)) parts.Add("type: " + def.Type);
            if (def.Required) parts.Add("required");
            if (!string.IsNullOrWhiteSpace(def.DefaultValue)) parts.Add("default: " + def.DefaultValue);
            if (def.Choices != null && def.Choices.Length > 0) parts.Add("choices: " + string.Join(", ", def.Choices));
            if (!string.IsNullOrWhiteSpace(def.Example)) parts.Add("example: " + def.Example);
            if (!string.IsNullOrWhiteSpace(def.Hint)) parts.Add(def.Hint);
            return string.Join(" | ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static List<ScriptOptionDef> BuildOptionDefinitions(string scriptText)
        {
            var fromArgparse = ParseArgparseOptionDefinitions(scriptText);
            var fromHints = ParseHintOptionDefinitions(scriptText);

            foreach (var kv in fromHints)
            {
                if (fromArgparse.TryGetValue(kv.Key, out var existing))
                {
                    MergeOption(existing, kv.Value);
                }
                else
                {
                    fromArgparse[kv.Key] = kv.Value;
                }
            }

            return fromArgparse.Values.ToList();
        }

        private static void MergeOption(ScriptOptionDef target, ScriptOptionDef src)
        {
            if (target == null || src == null) return;
            if (!string.IsNullOrWhiteSpace(src.Label)) target.Label = src.Label;
            if (!string.IsNullOrWhiteSpace(src.Type)) target.Type = src.Type;
            if (!string.IsNullOrWhiteSpace(src.DefaultValue)) target.DefaultValue = src.DefaultValue;
            if (!string.IsNullOrWhiteSpace(src.Example)) target.Example = src.Example;
            if (!string.IsNullOrWhiteSpace(src.Hint)) target.Hint = src.Hint;
            if (src.Choices != null && src.Choices.Length > 0) target.Choices = src.Choices;
            if (!string.IsNullOrWhiteSpace(src.Action)) target.Action = src.Action;
            if (src.Required) target.Required = true;
            if (src.FromHint) target.FromHint = true;
            if (src.Hidden) target.Hidden = true;
        }

        private static Dictionary<string, ScriptOptionDef> ParseHintOptionDefinitions(string scriptText)
        {
            var map = new Dictionary<string, ScriptOptionDef>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(scriptText)) return map;

            foreach (var line in scriptText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var m = ArgHintLineRx.Match(line ?? string.Empty);
                if (!m.Success) continue;
                var spec = (m.Groups["spec"].Value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(spec)) continue;

                var def = ParseHintSpec(spec);
                if (def == null || string.IsNullOrWhiteSpace(def.Name)) continue;
                map[def.Name] = def;
            }

            return map;
        }

        private static ScriptOptionDef? ParseHintSpec(string spec)
        {
            // example:
            // # @arg: name=--mode; type=choice; default=plan; choices=plan,apply; hint=Execution mode
            // # @arg: --port; type=int; default=5210; hint=Revit MCP port
            var def = new ScriptOptionDef { FromHint = true };
            var tokens = spec.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (tokens.Count == 0) return null;

            foreach (var token in tokens)
            {
                var eq = token.IndexOf('=');
                if (eq > 0)
                {
                    var key = token.Substring(0, eq).Trim().ToLowerInvariant();
                    var val = token.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "name":
                        case "arg":
                        case "option":
                            def.Name = NormalizeOptionName(val);
                            break;
                        case "type":
                            def.Type = NormalizeOptionType(val);
                            break;
                        case "default":
                            def.DefaultValue = TrimQuotes(val);
                            break;
                        case "hint":
                        case "help":
                            def.Hint = TrimQuotes(val);
                            break;
                        case "example":
                        case "eg":
                            def.Example = TrimQuotes(val);
                            break;
                        case "choices":
                            def.Choices = ParseChoiceValues(val);
                            if (def.Choices.Length > 0) def.Type = "choice";
                            break;
                        case "required":
                            def.Required = ParseBool(val, false);
                            break;
                        case "hidden":
                            def.Hidden = ParseBool(val, false);
                            break;
                        case "ui":
                            if (string.Equals(TrimQuotes(val), "advanced", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(TrimQuotes(val), "hidden", StringComparison.OrdinalIgnoreCase))
                            {
                                def.Hidden = true;
                            }
                            break;
                        case "action":
                            def.Action = TrimQuotes(val);
                            if (string.Equals(def.Action, "store_true", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(def.Action, "store_false", StringComparison.OrdinalIgnoreCase))
                            {
                                def.Type = "bool";
                            }
                            break;
                    }
                    continue;
                }

                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    def.Name = NormalizeOptionName(token);
                }
            }

            if (string.IsNullOrWhiteSpace(def.Name)) return null;
            if (IsLikelyPathName(def.Name, def.Hint)
                && !string.Equals(def.Type, "choice", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(def.Type, "bool", StringComparison.OrdinalIgnoreCase))
            {
                def.Type = "path";
            }
            def.Label = def.Name.TrimStart('-');
            if (string.IsNullOrWhiteSpace(def.Example))
            {
                def.Example = BuildOptionExample(def);
            }
            return def;
        }

        private static Dictionary<string, ScriptOptionDef> ParseArgparseOptionDefinitions(string scriptText)
        {
            var map = new Dictionary<string, ScriptOptionDef>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(scriptText)) return map;

            foreach (var call in EnumerateAddArgumentCalls(scriptText))
            {
                var names = Regex.Matches(call, "['\\\"](?<opt>--[A-Za-z0-9][A-Za-z0-9_-]*)['\\\"]")
                    .Cast<Match>()
                    .Select(m => m.Groups["opt"].Value)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (names.Count == 0) continue; // positional args are excluded for now
                var name = NormalizeOptionName(names[0]);
                if (string.IsNullOrWhiteSpace(name)) continue;

                var def = new ScriptOptionDef
                {
                    Name = name,
                    Label = name.TrimStart('-'),
                    Type = "string",
                    DefaultValue = string.Empty
                };

                var action = MatchKeywordQuotedValue(call, "action");
                if (!string.IsNullOrWhiteSpace(action))
                {
                    def.Action = action;
                    if (string.Equals(action, "store_true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(action, "store_false", StringComparison.OrdinalIgnoreCase))
                    {
                        def.Type = "bool";
                        def.DefaultValue = string.Equals(action, "store_true", StringComparison.OrdinalIgnoreCase) ? "false" : "true";
                    }
                }

                var typeWord = MatchKeywordWordValue(call, "type");
                if (!string.IsNullOrWhiteSpace(typeWord))
                {
                    def.Type = NormalizeOptionType(typeWord);
                }

                var defaultValue = MatchKeywordScalarValue(call, "default");
                if (!string.IsNullOrWhiteSpace(defaultValue))
                {
                    def.DefaultValue = defaultValue;
                }

                var required = MatchKeywordScalarValue(call, "required");
                if (!string.IsNullOrWhiteSpace(required))
                {
                    def.Required = ParseBool(required, false);
                }

                var help = MatchKeywordQuotedValue(call, "help");
                if (!string.IsNullOrWhiteSpace(help))
                {
                    def.Hint = help;
                }

                var choices = MatchChoicesValues(call);
                if (choices.Length > 0)
                {
                    def.Choices = choices;
                    def.Type = "choice";
                    if (string.IsNullOrWhiteSpace(def.DefaultValue))
                        def.DefaultValue = choices[0];
                }

                if (IsLikelyPathName(def.Name, def.Hint)
                    && !string.Equals(def.Type, "choice", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(def.Type, "bool", StringComparison.OrdinalIgnoreCase))
                {
                    def.Type = "path";
                }

                if (string.IsNullOrWhiteSpace(def.Example))
                {
                    def.Example = BuildOptionExample(def);
                }

                map[name] = def;
            }

            return map;
        }

        private static IEnumerable<string> EnumerateAddArgumentCalls(string scriptText)
        {
            var start = 0;
            while (start < scriptText.Length)
            {
                var idx = scriptText.IndexOf("add_argument(", start, StringComparison.Ordinal);
                if (idx < 0) yield break;

                var open = scriptText.IndexOf('(', idx);
                if (open < 0) yield break;

                var depth = 0;
                var inSingle = false;
                var inDouble = false;
                var escape = false;
                for (var i = open; i < scriptText.Length; i++)
                {
                    var ch = scriptText[i];
                    if (escape) { escape = false; continue; }
                    if (ch == '\\') { escape = true; continue; }
                    if (inSingle) { if (ch == '\'') inSingle = false; continue; }
                    if (inDouble) { if (ch == '"') inDouble = false; continue; }
                    if (ch == '\'') { inSingle = true; continue; }
                    if (ch == '"') { inDouble = true; continue; }

                    if (ch == '(') depth++;
                    else if (ch == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            var body = scriptText.Substring(open + 1, i - open - 1);
                            yield return body;
                            start = i + 1;
                            break;
                        }
                    }

                    if (i == scriptText.Length - 1)
                    {
                        start = scriptText.Length;
                    }
                }
            }
        }

        private static string MatchKeywordQuotedValue(string text, string key)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key)) return string.Empty;
            var q1 = Regex.Match(text, "\\b" + Regex.Escape(key) + "\\s*=\\s*(?:r)?\"(?<v>[^\"]*)\"");
            if (q1.Success) return q1.Groups["v"].Value;
            var q2 = Regex.Match(text, "\\b" + Regex.Escape(key) + "\\s*=\\s*(?:r)?'(?<v>[^']*)'");
            if (q2.Success) return q2.Groups["v"].Value;
            return string.Empty;
        }

        private static string MatchKeywordWordValue(string text, string key)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key)) return string.Empty;
            var m = Regex.Match(text, "\\b" + Regex.Escape(key) + "\\s*=\\s*(?<v>[A-Za-z_][A-Za-z0-9_]*)");
            return m.Success ? (m.Groups["v"].Value ?? string.Empty) : string.Empty;
        }

        private static string MatchKeywordScalarValue(string text, string key)
        {
            var quoted = MatchKeywordQuotedValue(text, key);
            if (!string.IsNullOrWhiteSpace(quoted)) return quoted;
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(key)) return string.Empty;

            var m = Regex.Match(text, "\\b" + Regex.Escape(key) + "\\s*=\\s*(?<v>True|False|None|-?\\d+(?:\\.\\d+)?|[A-Za-z_][A-Za-z0-9_]*)");
            return m.Success ? (m.Groups["v"].Value ?? string.Empty) : string.Empty;
        }

        private static string[] MatchChoicesValues(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            var m = Regex.Match(text, "\\bchoices\\s*=\\s*(?<v>\\[[^\\]]*\\]|\\([^\\)]*\\))");
            if (!m.Success) return Array.Empty<string>();
            return ParseChoiceValues(m.Groups["v"].Value ?? string.Empty);
        }

        private static string[] ParseChoiceValues(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            var values = new List<string>();
            foreach (Match qm in Regex.Matches(raw, "\"(?<v>[^\"]+)\"|'(?<v>[^']+)'"))
            {
                var v = qm.Groups["v"].Value;
                if (!string.IsNullOrWhiteSpace(v)) values.Add(v.Trim());
            }
            if (values.Count > 0) return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var cleaned = raw.Trim().TrimStart('[', '(').TrimEnd(']', ')');
            foreach (var token in cleaned.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var v = TrimQuotes(token.Trim());
                if (!string.IsNullOrWhiteSpace(v)) values.Add(v);
            }
            return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string NormalizeOptionName(string raw)
        {
            var v = TrimQuotes(raw ?? string.Empty).Trim();
            if (v.StartsWith("--", StringComparison.Ordinal)) return v;
            return string.Empty;
        }

        private static string NormalizeOptionType(string raw)
        {
            var v = TrimQuotes(raw ?? string.Empty).Trim().ToLowerInvariant();
            if (v == "int" || v == "float" || v == "bool" || v == "choice" || v == "string" || v == "str" || v == "file" || v == "path" || v == "dir" || v == "directory")
            {
                if (v == "str") return "string";
                if (v == "file") return "path";
                if (v == "directory") return "dir";
                return v;
            }
            return "string";
        }

        private static string TrimQuotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var v = value.Trim();
            if (v.Length >= 2)
            {
                if ((v.StartsWith("\"", StringComparison.Ordinal) && v.EndsWith("\"", StringComparison.Ordinal)) ||
                    (v.StartsWith("'", StringComparison.Ordinal) && v.EndsWith("'", StringComparison.Ordinal)))
                {
                    v = v.Substring(1, v.Length - 2);
                }
            }
            return v.Trim();
        }

        private string BuildArgsFromOptionEditors()
        {
            var args = new List<string>();
            foreach (var binding in _optionBindings)
            {
                var def = binding.Def;
                if (def == null || string.IsNullOrWhiteSpace(def.Name)) continue;

                if (string.Equals(def.Type, "bool", StringComparison.OrdinalIgnoreCase))
                {
                    var isOn = ParseBool(GetOptionEditorValue(binding), ParseBool(def.DefaultValue, false));
                    if (string.Equals(def.Action, "store_false", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!isOn) args.Add(def.Name);
                    }
                    else
                    {
                        if (isOn) args.Add(def.Name);
                    }
                    continue;
                }

                var value = (GetOptionEditorValue(binding) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (!string.IsNullOrWhiteSpace(def.DefaultValue))
                        value = def.DefaultValue;
                }
                if (string.IsNullOrWhiteSpace(value)) continue;
                args.Add(def.Name);
                args.Add(QuoteArg(value));
            }
            return string.Join(" ", args);
        }

        private static string QuoteArg(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return value;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string RemoveBareBooleanTokens(string argsText, out int removedCount)
        {
            removedCount = 0;
            if (string.IsNullOrWhiteSpace(argsText)) return string.Empty;

            var tokens = Regex.Matches(argsText, "\"(?:\\\\.|[^\"])*\"|'(?:\\\\.|[^'])*'|\\S+")
                .Cast<Match>()
                .Select(m => m.Value)
                .ToList();
            if (tokens.Count == 0) return argsText;

            var kept = new List<string>(tokens.Count);
            foreach (var token in tokens)
            {
                if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
                {
                    removedCount++;
                    continue;
                }
                kept.Add(token);
            }
            return string.Join(" ", kept);
        }

        private static bool ShouldHideOption(ScriptOptionDef def)
        {
            if (def == null) return true;
            if (def.Hidden) return true;
            if (string.IsNullOrWhiteSpace(def.Name)) return false;
            return DefaultHiddenOptionNames.Contains(def.Name.Trim());
        }

        private static bool IsLikelyPathName(string optionName, string hint)
        {
            var key = (optionName ?? string.Empty).ToLowerInvariant();
            var h = (hint ?? string.Empty).ToLowerInvariant();
            return key.Contains("path") || key.Contains("file") || key.Contains("csv") || key.Contains("json") || key.Contains("config") || key.Contains("output")
                || h.Contains("path") || h.Contains("file") || h.Contains("csv") || h.Contains("json") || h.Contains("folder") || h.Contains("directory");
        }

        private static bool IsPathLikeOption(ScriptOptionDef def)
        {
            if (def == null) return false;
            var t = (def.Type ?? string.Empty).ToLowerInvariant();
            if (t == "path" || t == "file" || t == "dir" || t == "directory") return true;
            return IsLikelyPathName(def.Name, def.Hint);
        }

        private static string BuildOptionExample(ScriptOptionDef def)
        {
            if (def == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(def.Example)) return def.Example;
            if (!string.IsNullOrWhiteSpace(def.DefaultValue)) return def.DefaultValue;
            if (def.Choices != null && def.Choices.Length > 0) return def.Choices[0];

            var n = (def.Name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("port")) return "5210";
            if (n.Contains("csv")) return @"C:\work\input.csv";
            if (n.Contains("json")) return @"C:\work\config.json";
            if (n.Contains("output")) return @"C:\work\result.json";
            if (n.Contains("path") || n.Contains("file")) return @"C:\work\file.txt";
            if (string.Equals(def.Type, "int", StringComparison.OrdinalIgnoreCase)) return "1";
            if (string.Equals(def.Type, "float", StringComparison.OrdinalIgnoreCase)) return "1.0";
            if (string.Equals(def.Type, "bool", StringComparison.OrdinalIgnoreCase)) return "true";
            return "sample";
        }

        private void OpenPathChooserForOption(ScriptOptionDef def, TextBox target)
        {
            if (def == null || target == null) return;
            try
            {
                var mode = (def.Type ?? string.Empty).ToLowerInvariant();
                var name = (def.Name ?? string.Empty).ToLowerInvariant();
                var hint = (def.Hint ?? string.Empty).ToLowerInvariant();
                var initial = (target.Text ?? string.Empty).Trim();

                if (mode == "dir" || mode == "directory" || name.Contains("dir") || hint.Contains("folder") || hint.Contains("directory"))
                {
                    using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                    {
                        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
                            dlg.SelectedPath = initial;
                        var res = dlg.ShowDialog();
                        if (res == System.Windows.Forms.DialogResult.OK)
                            target.Text = dlg.SelectedPath ?? string.Empty;
                    }
                    return;
                }

                var isOutput = name.Contains("output") || hint.Contains("保存先") || hint.Contains("save") || hint.Contains("write");
                if (isOutput)
                {
                    var sfd = new SaveFileDialog
                    {
                        Title = "Select output file",
                        Filter = BuildFileDialogFilter(def),
                        AddExtension = true
                    };
                    if (!string.IsNullOrWhiteSpace(initial))
                    {
                        try
                        {
                            var full = Path.GetFullPath(initial);
                            var dir = Path.GetDirectoryName(full);
                            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                                sfd.InitialDirectory = dir;
                            sfd.FileName = Path.GetFileName(full);
                        }
                        catch { }
                    }
                    if (sfd.ShowDialog(this) == true)
                        target.Text = sfd.FileName ?? string.Empty;
                    return;
                }

                var ofd = new OpenFileDialog
                {
                    Title = "Select file",
                    Filter = BuildFileDialogFilter(def),
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (!string.IsNullOrWhiteSpace(initial))
                {
                    try
                    {
                        var full = Path.GetFullPath(initial);
                        var dir = Path.GetDirectoryName(full);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                            ofd.InitialDirectory = dir;
                        if (File.Exists(full))
                            ofd.FileName = Path.GetFileName(full);
                    }
                    catch { }
                }
                if (ofd.ShowDialog(this) == true)
                    target.Text = ofd.FileName ?? string.Empty;
            }
            catch (Exception ex)
            {
                AppendOutput("Browse failed: " + ex.Message);
            }
        }

        private static string BuildFileDialogFilter(ScriptOptionDef def)
        {
            var n = (def?.Name ?? string.Empty).ToLowerInvariant();
            var h = (def?.Hint ?? string.Empty).ToLowerInvariant();
            if (n.Contains("csv") || h.Contains("csv")) return "CSV (*.csv)|*.csv|All files (*.*)|*.*";
            if (n.Contains("json") || h.Contains("json") || n.Contains("config")) return "JSON (*.json)|*.json|All files (*.*)|*.*";
            if (n.Contains("py") || h.Contains("python")) return "Python (*.py)|*.py|All files (*.*)|*.*";
            if (n.Contains("xml") || h.Contains("xml")) return "XML (*.xml)|*.xml|All files (*.*)|*.*";
            return "All files (*.*)|*.*";
        }

        private bool ValidateRequiredOptions(out string message)
        {
            var missing = new List<string>();
            foreach (var binding in _optionBindings)
            {
                var def = binding.Def;
                if (def == null || !def.Required) continue;
                if (string.Equals(def.Type, "bool", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var value = (GetOptionEditorValue(binding) ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(value)) continue;
                var ex = BuildOptionExample(def);
                var row = def.Name;
                if (!string.IsNullOrWhiteSpace(ex))
                    row += " (example: " + ex + ")";
                missing.Add(row);
            }

            if (missing.Count == 0)
            {
                message = string.Empty;
                return true;
            }

            message = "Required options are missing: " + string.Join("; ", missing);
            return false;
        }

        private void CopyOptionSpecToClipboard()
        {
            StartOutputGroup();
            try
            {
                ReloadScriptOptionEditors(preserveCurrentValues: true, addOutput: false);
                var arr = new JArray();
                foreach (var b in _optionBindings)
                {
                    var d = b.Def;
                    if (d == null) continue;
                    var item = new JObject
                    {
                        ["name"] = d.Name,
                        ["type"] = d.Type,
                        ["required"] = d.Required,
                        ["default"] = d.DefaultValue ?? string.Empty,
                        ["example"] = BuildOptionExample(d),
                        ["hint"] = d.Hint ?? string.Empty,
                        ["value"] = GetOptionEditorValue(b) ?? string.Empty
                    };
                    if (d.Choices != null && d.Choices.Length > 0)
                    {
                        item["choices"] = new JArray(d.Choices);
                    }
                    arr.Add(item);
                }
                var root = new JObject
                {
                    ["file"] = _currentPath ?? "(unsaved)",
                    ["optionCount"] = arr.Count,
                    ["options"] = arr
                };
                var txt = root.ToString(Newtonsoft.Json.Formatting.Indented);
                Clipboard.SetText(txt);
                AppendOutput("Option spec copied to clipboard.");
            }
            catch (Exception ex)
            {
                AppendOutput("Copy option spec failed: " + ex.Message);
            }
        }

        private void UpdateStatus()
        {
            var port = PortSettings.GetPort();
            var python = ResolvePythonExe();
            var file = string.IsNullOrWhiteSpace(_currentPath) ? "(unsaved)" : _currentPath;
            var dirty = _isDirty ? "*" : "";
            TxtStatus.Text = $"File: {file}{dirty} | Port: {port} | Python: {python ?? "(not found)"}";
        }

        private string GetDefaultFolder()
        {
            return _defaultScriptsRoot;
        }

        private string ResolveDialogFolder()
        {
            string folder = "";
            try
            {
                folder = GetDefaultFolder();
                if (string.IsNullOrWhiteSpace(folder))
                    folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                // Normalize & validate (avoid OpenFileDialog "value out of range")
                folder = Path.GetFullPath(folder);
                if (folder.Length > 240) // avoid MAX_PATH issues in dialog
                    throw new IOException("path too long");

                var invalid = Path.GetInvalidPathChars();
                if (folder.IndexOfAny(invalid) >= 0)
                    throw new IOException("invalid path chars");

                Directory.CreateDirectory(folder);
                return folder;
            }
            catch (Exception ex)
            {
                AppendOutput("Open: default folder invalid, fallback to Documents. " + ex.Message);
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }

        private string SaveRunCopy(string scriptText)
        {
            var folder = GetDefaultFolder();
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "run_latest.py");
            var normalized = NormalizeScriptForSave(scriptText);
            var hash = ComputeSha256(normalized);

            if (string.IsNullOrWhiteSpace(_lastRunHash) && File.Exists(path))
            {
                try
                {
                    var existing = File.ReadAllText(path, Encoding.UTF8);
                    _lastRunHash = ComputeSha256(existing);
                }
                catch { /* ignore */ }
            }

            if (string.Equals(_lastRunHash, hash, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                return path; // no duplicate save
            }

            File.WriteAllText(path, normalized, new UTF8Encoding(false));
            _lastRunHash = hash;
            return path;
        }

        private static string ComputeSha256(string text)
        {
            try
            {
                using (var sha = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(text ?? "");
                    var hash = sha.ComputeHash(bytes);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return "";
            }
        }

        private static string NormalizePastedScriptText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Force plain text newline handling. RichTextBox otherwise prefers RTF/HTML
            // clipboard formats, which can turn each source line into a separate paragraph.
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }

        private string GetScriptText()
        {
            try
            {
                var range = new TextRange(ScriptBox.Document.ContentStart, ScriptBox.Document.ContentEnd);
                var text = range.Text ?? "";
                // RichTextBox inserts a trailing newline; strip it for stable script I/O.
                if (text.EndsWith("\r\n", StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - 2);
                return text;
            }
            catch
            {
                return "";
            }
        }

        private void SetScriptText(string text)
        {
            try
            {
                ScriptBox.Document.Blocks.Clear();
                var p = new Paragraph(new Run(text ?? "")) { Margin = new Thickness(0) };
                ScriptBox.Document.Blocks.Add(p);
                ScriptBox.Document.PageWidth = 10000;
                ScheduleHighlight();
                UpdateLineNumbers();
            }
            catch
            {
                // ignore
            }
        }

        private void LoadScriptFromPath(string path)
        {
            LoadScriptFromPathInternal(path, addOutput: true);
        }

        private void LoadScriptFromPathInternal(string path, bool addOutput)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            if (!File.Exists(path))
            {
                AppendOutput("Open failed: file not found.");
                return;
            }

            var text = File.ReadAllText(path, Encoding.UTF8);
            SetScriptText(text);
            ApplyMetadataFromText(text);
            ReloadScriptOptionEditors(preserveCurrentValues: false, addOutput: false);
            _currentPath = path;
            _isDirty = false;
            UpdateStatus();
            try { PythonRunnerScriptLibrary.SaveLastScript(path); } catch { /* ignore */ }
            if (addOutput) AppendOutput("Opened: " + path);
        }

        private bool TryLoadFromInbox(bool addOutput)
        {
            try
            {
                var path = PythonRunnerScriptLibrary.ReadInboxScriptPath(out _);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
                if (string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(path, _lastInboxPath, StringComparison.OrdinalIgnoreCase)) return false;
                _lastInboxPath = path;
                LoadScriptFromPathInternal(path, addOutput);
                return true;
            }
            catch { return false; }
        }

        private void OpenLibrary()
        {
            if (_libraryWindow != null)
            {
                _libraryWindow.Activate();
                return;
            }

            _libraryWindow = new ScriptLibraryWindow(GetDefaultFolder(), LoadScriptFromPath);
            _libraryWindow.Owner = this;
            _libraryWindow.Closed += (_, __) => _libraryWindow = null;
            _libraryWindow.Show();
        }

        private void AttachScriptScrollSync()
        {
            try
            {
                _scriptScrollViewer = FindDescendant<ScrollViewer>(ScriptBox);
                if (_scriptScrollViewer != null)
                {
                    _scriptScrollViewer.ScrollChanged += (_, e) =>
                    {
                        if (LineNumbersBox == null) return;
                        LineNumbersBox.ScrollToVerticalOffset(e.VerticalOffset);
                    };
                }
            }
            catch { /* ignore */ }
        }

        private void UpdateLineNumbers()
        {
            if (_suppressLineNumbers) return;
            if (LineNumbersBox == null) return;
            try
            {
                var text = GetScriptText();
                var lines = 1;
                if (!string.IsNullOrEmpty(text))
                {
                    lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
                }

                var sb = new StringBuilder();
                for (int i = 1; i <= lines; i++) sb.Append(i).AppendLine();

                _suppressLineNumbers = true;
                LineNumbersBox.Text = sb.ToString();
                _suppressLineNumbers = false;
            }
            catch
            {
                _suppressLineNumbers = false;
            }
        }

        private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindDescendant<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void ApplyMetadataFromText(string text)
        {
            var (feature, keywords) = ExtractMetadata(text);
            _suppressMetaChange = true;
            TxtFeature.Text = feature;
            TxtKeywords.Text = keywords;
            _suppressMetaChange = false;
        }

        private (string feature, string keywords) ExtractMetadata(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return ("", "");
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string feature = "";
            string keywords = "";
            int limit = Math.Min(5, lines.Length);
            for (int i = 0; i < limit; i++)
            {
                var line = lines[i] ?? "";
                if (FeatureInlineRx.IsMatch(line))
                {
                    var m = FeatureInlineRx.Match(line);
                    feature = (m.Groups["feature"].Value ?? "").Trim();
                    var kw = (m.Groups["keywords"].Value ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(kw)) keywords = kw;
                    continue;
                }
                if (FeatureLineRx.IsMatch(line))
                {
                    var m = FeatureLineRx.Match(line);
                    feature = (m.Groups["feature"].Value ?? "").Trim();
                    continue;
                }
                if (KeywordLineRx.IsMatch(line))
                {
                    var m = KeywordLineRx.Match(line);
                    keywords = (m.Groups["keywords"].Value ?? "").Trim();
                    continue;
                }
            }
            return (feature ?? "", keywords ?? "");
        }

        private string NormalizeScriptForSave(string raw)
        {
            var normalized = DedentCommonLeadingWhitespace(raw ?? string.Empty);

            var feature = (TxtFeature.Text ?? "").Trim();
            var keywords = (TxtKeywords.Text ?? "").Trim();
            var metaLine = BuildMetadataLine(feature, keywords);
            if (string.IsNullOrWhiteSpace(metaLine))
                return normalized;

            var lines = normalized.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

            // Remove existing metadata lines near the top
            int limit = Math.Min(5, lines.Count);
            var kept = new System.Collections.Generic.List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? "";
                if (i < limit)
                {
                    if (FeatureLineRx.IsMatch(line) || KeywordLineRx.IsMatch(line) || FeatureInlineRx.IsMatch(line))
                        continue;
                }
                kept.Add(line);
            }

            kept.Insert(0, metaLine);
            return string.Join(Environment.NewLine, kept);
        }

        private static string BuildMetadataLine(string feature, string keywords)
        {
            feature = (feature ?? "").Trim();
            keywords = (keywords ?? "").Trim();
            if (string.IsNullOrWhiteSpace(feature) && string.IsNullOrWhiteSpace(keywords)) return string.Empty;
            if (string.IsNullOrWhiteSpace(keywords)) return "# @feature: " + feature;
            if (string.IsNullOrWhiteSpace(feature)) return "# @feature: " + "" + " | keywords: " + keywords;
            return "# @feature: " + feature + " | keywords: " + keywords;
        }

        private void ScheduleHighlight()
        {
            try
            {
                if (_highlightTimer == null) return;
                _highlightTimer.Stop();
                _highlightTimer.Start();
            }
            catch
            {
                // ignore
            }
        }

        private void ApplyMcpCommandHighlight()
        {
            if (ScriptBox == null) return;

            var text = GetScriptText();
            if (text == null) text = "";

            try
            {
                _suppressTextChange = true;
                ScriptBox.BeginChange();

                // Reset formatting to normal for whole document.
                var all = new TextRange(ScriptBox.Document.ContentStart, ScriptBox.Document.ContentEnd);
                all.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                all.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Black);

                ApplyRegexHighlight(text, McpMethodInRpcCallRx);
                ApplyRegexHighlight(text, McpMethodInJsonRpcRx);
            }
            catch
            {
                // ignore
            }
            finally
            {
                try { ScriptBox.EndChange(); } catch { }
                _suppressTextChange = false;
            }
        }

        private void ApplyRegexHighlight(string text, Regex rx)
        {
            foreach (Match m in rx.Matches(text))
            {
                var g = m.Groups["method"];
                if (!g.Success) continue;
                ApplyFormatAt(g.Index, g.Length);
            }
        }

        private void ApplyFormatAt(int startIndex, int length)
        {
            if (length <= 0) return;
            var start = GetTextPointerAtOffset(ScriptBox.Document.ContentStart, startIndex);
            var end = GetTextPointerAtOffset(ScriptBox.Document.ContentStart, startIndex + length);
            if (start == null || end == null) return;
            var range = new TextRange(start, end);
            range.ApplyPropertyValue(TextElement.ForegroundProperty, McpCommandBrush);
            range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
        }

        private static TextPointer? GetTextPointerAtOffset(TextPointer start, int charOffset)
        {
            try
            {
                var navigator = start;
                var count = 0;

                while (navigator != null)
                {
                    if (navigator.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                    {
                        var run = navigator.GetTextInRun(LogicalDirection.Forward);
                        if (count + run.Length >= charOffset)
                        {
                            return navigator.GetPositionAtOffset(charOffset - count, LogicalDirection.Forward);
                        }
                        count += run.Length;
                        navigator = navigator.GetPositionAtOffset(run.Length, LogicalDirection.Forward);
                    }
                    else
                    {
                        navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
                    }
                }
            }
            catch
            {
                // ignore
            }
            return start;
        }

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            try { brush.Freeze(); } catch { }
            return brush;
        }

        private static string? ResolvePythonExe()
        {
            var env = Environment.GetEnvironmentVariable("REVIT_MCP_PYTHON_EXE");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            {
                return env;
            }

            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "python", "python.exe"),
                Path.Combine(baseDir, "python310", "python.exe")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return path;
            }

            var pathPython = FindInPath("python.exe");
            if (!string.IsNullOrWhiteSpace(pathPython)) return pathPython;
            var pathPy = FindInPath("py.exe");
            if (!string.IsNullOrWhiteSpace(pathPy)) return pathPy;

            return null;
        }

        private static string? FindInPath(string exeName)
        {
            var env = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in env.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string RewritePorts(string text, int port, out int urlCount, out int argCount)
        {
            var urlHits = 0;
            var argHits = 0;
            if (string.IsNullOrEmpty(text))
            {
                urlCount = 0;
                argCount = 0;
                return text;
            }

            var urlRx = new Regex(@"(?i)\b(https?://(?:127\.0\.0\.1|localhost):)(\d{2,5})\b");
            var argRx = new Regex(@"(?i)\b(--port)(?:=|\s+)(\d{2,5})\b");

            var rewritten = urlRx.Replace(text, m =>
            {
                var oldPort = m.Groups[2].Value;
                if (oldPort == port.ToString(CultureInfo.InvariantCulture)) return m.Value;
                urlHits++;
                return m.Groups[1].Value + port.ToString(CultureInfo.InvariantCulture);
            });

            rewritten = argRx.Replace(rewritten, m =>
            {
                var oldPort = m.Groups[2].Value;
                if (oldPort == port.ToString(CultureInfo.InvariantCulture)) return m.Value;
                argHits++;
                var sep = m.Value.Contains("=") ? "=" : " ";
                return m.Groups[1].Value + sep + port.ToString(CultureInfo.InvariantCulture);
            });

            urlCount = urlHits;
            argCount = argHits;
            return rewritten;
        }

        private static string RewritePortArgs(string argsText, int port, out int argCount)
        {
            var argHits = 0;
            if (string.IsNullOrWhiteSpace(argsText))
            {
                argCount = 0;
                return argsText ?? string.Empty;
            }

            var argRx = new Regex(@"(?i)\b(--port)(?:=|\s+)(\d{2,5})\b");
            var rewritten = argRx.Replace(argsText, m =>
            {
                var oldPort = m.Groups[2].Value;
                if (oldPort == port.ToString(CultureInfo.InvariantCulture)) return m.Value;
                argHits++;
                var sep = m.Value.Contains("=") ? "=" : " ";
                return m.Groups[1].Value + sep + port.ToString(CultureInfo.InvariantCulture);
            });

            argCount = argHits;
            return rewritten;
        }

        private static string DedentCommonLeadingWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var lineEnding = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n');

            int? minIndent = null;
            foreach (var line in lines)
            {
                if (line.Length == 0) continue;
                var i = 0;
                while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
                if (i == line.Length) continue; // blank or whitespace-only
                if (!minIndent.HasValue || i < minIndent.Value) minIndent = i;
                if (minIndent == 0) break;
            }

            if (!minIndent.HasValue || minIndent.Value == 0) return text;
            var trim = minIndent.Value;

            for (var idx = 0; idx < lines.Length; idx++)
            {
                var line = lines[idx];
                if (line.Length >= trim)
                {
                    lines[idx] = line.Substring(trim);
                }
            }

            return string.Join(lineEnding, lines);
        }

        private static string RewriteLegacyEndpoint(string text, out int endpointCount)
        {
            var count = 0;
            if (string.IsNullOrEmpty(text))
            {
                endpointCount = 0;
                return text;
            }

            var rx = new Regex(@"(?i)(/jsonrpc)\b");
            var rewritten = rx.Replace(text, _ =>
            {
                count++;
                return "/rpc";
            });

            endpointCount = count;
            return rewritten;
        }

        private static string ResolveScriptsRoot(string? docTitle, string? docKey)
        {
            var workProject = TryResolveWorkProjectFolder(docTitle, docKey);
            if (!string.IsNullOrWhiteSpace(workProject))
            {
                return Path.Combine(workProject, "python_script");
            }

            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            return Path.Combine(baseDir, "scripts");
        }

        private static string? TryResolveWorkProjectFolder(string? docTitle, string? docKey)
        {
            return Paths.ResolveManagedProjectFolder(docTitle, docKey);
        }

        private async Task TryAutoPollQueuedJobAsync()
        {
            if (_isClosing) return;
            string text;
            if (Dispatcher.CheckAccess())
            {
                text = OutputBox.Text ?? "";
            }
            else
            {
                try
                {
                    text = Dispatcher.Invoke(() => OutputBox.Text ?? "");
                }
                catch
                {
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(text)) return;
            if (!Regex.IsMatch(text, "\"queued\"\\s*:\\s*true", RegexOptions.IgnoreCase)) return;

            var m = Regex.Match(text, "\"jobId\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                m = Regex.Match(text, "\"job_id\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            }
            if (!m.Success) return;

            var jobId = m.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(jobId) || jobId == _lastAutoPolledJobId) return;
            _lastAutoPolledJobId = jobId;

            AppendOutput("AutoPoll start: jobId=" + jobId);

            var port = PortSettings.GetPort();
            var jobUrl = "http://127.0.0.1:" + port + "/job/" + jobId;
            var deadline = DateTime.UtcNow.AddSeconds(60);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    HttpResponseMessage resp;
                    try
                    {
                        resp = await client.GetAsync(jobUrl).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AppendOutput("AutoPoll error: " + ex.Message);
                        return;
                    }

                    var code = (int)resp.StatusCode;
                    if (code == 202 || code == 204)
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                        continue;
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        AppendOutput("AutoPoll http " + code);
                        return;
                    }

                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        AppendOutput("AutoPoll empty response.");
                        return;
                    }

                    JObject job;
                    try
                    {
                        job = JObject.Parse(body);
                    }
                    catch (Exception ex)
                    {
                        AppendOutput("AutoPoll parse error: " + ex.Message);
                        return;
                    }

                    var state = job.Value<string>("state") ?? "";
                    if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                    {
                        var resultJson = job.Value<string>("result_json") ?? "";
                        if (!string.IsNullOrWhiteSpace(resultJson))
                        {
                            AppendOutput("AutoPoll result:");
                            AppendOutput(PrettyUnwrapRpcJson(resultJson));
                        }
                        else
                        {
                            AppendOutput("AutoPoll done.");
                        }
                        return;
                    }

                    if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state, "TIMEOUT", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state, "DEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        var msg = job.Value<string>("error_msg") ?? state;
                        AppendOutput("AutoPoll failed: " + msg);
                        return;
                    }

                    await Task.Delay(500).ConfigureAwait(false);
                }
            }

            AppendOutput("AutoPoll timeout.");
        }

        private void AppendOutput(string text)
        {
            if (_isClosing) return;
            if (!Dispatcher.CheckAccess())
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() => AppendOutput(text)));
                }
                catch
                {
                    // The runner window may already be closing.
                }
                return;
            }

            EnsureOutputHeader();
            OutputBox.AppendText($"{text}{Environment.NewLine}");
            OutputBox.ScrollToEnd();
        }

        private static string PrettyUnwrapRpcJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
            try
            {
                var token = JToken.Parse(json);
                var unwrapped = UnwrapRpcToken(token);
                return unwrapped.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }

        private static JToken UnwrapRpcToken(JToken token)
        {
            if (token == null) return JValue.CreateNull();
            var obj = token as JObject;
            if (obj == null) return token;
            var result = obj["result"] as JObject;
            if (result != null)
            {
                var inner = result["result"] as JObject;
                if (inner != null) return inner;
                return result;
            }
            return token;
        }

        private void StartOutputGroup()
        {
            _needsOutputHeader = true;
        }

        private void EnsureOutputHeader()
        {
            if (!_needsOutputHeader) return;
            _needsOutputHeader = false;
            var ts = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            OutputBox.AppendText($"[{ts}]{Environment.NewLine}");
        }
    }
}
