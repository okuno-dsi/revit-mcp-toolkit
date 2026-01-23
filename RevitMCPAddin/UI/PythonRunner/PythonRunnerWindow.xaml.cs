using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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
            BtnSave.Click += (_, __) => SaveScript();
            BtnSaveAs.Click += (_, __) => SaveScriptAs();
            BtnSelectAll.Click += (_, __) => SelectAllScript();
            BtnClearScript.Click += (_, __) => ClearScript();
            BtnResetPaste.Click += (_, __) => ResetAndPaste();
            BtnRun.Click += async (_, __) => await RunAsync();
            BtnStop.Click += (_, __) => StopProcess();
            BtnCopyOut.Click += (_, __) => CopyOutput();
            BtnClearOut.Click += (_, __) => ClearOutput();

            ScriptBox.TextChanged += (_, __) =>
            {
                if (_suppressTextChange) return;
                _isDirty = true;
                UpdateStatus();
                ScheduleHighlight();
            };

            UpdateStatus();
            ApplyMcpCommandHighlight();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopProcess();
        }

        private void OpenScript()
        {
            StartOutputGroup();
            var dlg = new OpenFileDialog
            {
                Filter = "Python (*.py)|*.py|All files (*.*)|*.*",
                InitialDirectory = GetDefaultFolder()
            };

            if (dlg.ShowDialog(this) != true) return;
            var path = dlg.FileName;
            if (!File.Exists(path))
            {
                AppendOutput("Open failed: file not found.");
                return;
            }

            SetScriptText(File.ReadAllText(path, Encoding.UTF8));
            _currentPath = path;
            _isDirty = false;
            UpdateStatus();
            AppendOutput("Opened: " + path);
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
            var folder = GetDefaultFolder();
            Directory.CreateDirectory(folder);

            var dlg = new SaveFileDialog
            {
                Filter = "Python (*.py)|*.py|All files (*.*)|*.*",
                InitialDirectory = folder,
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
                var normalized = DedentCommonLeadingWhitespace(raw);
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

            SetScriptText(Clipboard.GetText() ?? "");
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

            try
            {
                var port = PortSettings.GetPort();
                var scriptText = GetScriptText();
                var rewritten = RewritePorts(scriptText, port, out var urlCount, out var argCount);
                var endpointCount = 0;
                rewritten = RewriteLegacyEndpoint(rewritten, out endpointCount);

                if (urlCount > 0 || argCount > 0 || endpointCount > 0)
                {
                    AppendOutput($"Rewrite: url={urlCount}, args={argCount}, endpoint={endpointCount} -> {port}");
                }

                var runPath = SaveRunCopy(rewritten);
                AppendOutput("Run file: " + runPath);

                var pythonExe = ResolvePythonExe();
                if (string.IsNullOrWhiteSpace(pythonExe))
                {
                    AppendOutput("Python not found. Place python.exe in add-in \\python or set REVIT_MCP_PYTHON_EXE.");
                    return;
                }

                await StartProcessAsync(pythonExe, runPath);
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

        private async Task StartProcessAsync(string pythonExe, string scriptPath)
        {
            var scriptDir = Path.GetDirectoryName(scriptPath) ?? GetDefaultFolder();
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-u \"" + scriptPath + "\"",
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

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _exitTcs = new TaskCompletionSource<int>();

            _proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) AppendOutput(e.Data);
            };
            _proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) AppendOutput("ERR: " + e.Data);
            };
            _proc.Exited += (_, __) =>
            {
                _exitTcs.TrySetResult(_proc?.ExitCode ?? -1);
            };

            AppendOutput("Process start: " + pythonExe);
            if (!_proc.Start())
            {
                AppendOutput("Process failed to start.");
                return;
            }

            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();

            var exitCode = await _exitTcs.Task.ConfigureAwait(false);
            AppendOutput("Process exit: " + exitCode);

            await TryAutoPollQueuedJobAsync().ConfigureAwait(false);

            try { _proc.Dispose(); } catch { }
            _proc = null;
            _exitTcs = null;
        }

        private void StopProcess()
        {
            StartOutputGroup();
            if (_proc == null) return;
            try
            {
                if (!_proc.HasExited)
                {
                    _proc.Kill();
                    AppendOutput("Process killed.");
                }
            }
            catch (Exception ex)
            {
                AppendOutput("Stop failed: " + ex.Message);
            }
            finally
            {
                try { _proc.Dispose(); } catch { }
                _proc = null;
                _exitTcs = null;
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

        private string SaveRunCopy(string scriptText)
        {
            var folder = GetDefaultFolder();
            Directory.CreateDirectory(folder);
            var fileName = $"run_{DateTime.Now:yyyyMMdd_HHmmss}.py";
            var path = Path.Combine(folder, fileName);
            var normalized = DedentCommonLeadingWhitespace(scriptText);
            File.WriteAllText(path, normalized, new UTF8Encoding(false));
            return path;
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
            }
            catch
            {
                // ignore
            }
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
            var workRoot = ResolveWorkRoot();
            if (string.IsNullOrWhiteSpace(workRoot)) return null;

            var workDir = Path.Combine(workRoot, "Work");
            if (!Directory.Exists(workDir)) return null;

            var dirs = Directory.GetDirectories(workDir);
            if (!string.IsNullOrWhiteSpace(docKey))
            {
                var keyToken = "_" + docKey.Trim();
                var match = dirs.FirstOrDefault(d =>
                    Path.GetFileName(d).EndsWith(keyToken, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match)) return match;
            }

            var safeTitle = SanitizePathSegment(docTitle);
            if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Project";
            var safeKey = SanitizePathSegment(docKey);
            if (string.IsNullOrWhiteSpace(safeKey)) safeKey = "unknown";

            var created = Path.Combine(workDir, $"{safeTitle}_{safeKey}");
            Directory.CreateDirectory(created);
            return created;
        }

        private static string? ResolveWorkRoot()
        {
            var env1 = Environment.GetEnvironmentVariable("REVIT_MCP_WORK_ROOT");
            if (!string.IsNullOrWhiteSpace(env1) && Directory.Exists(env1)) return env1;

            var env2 = Environment.GetEnvironmentVariable("CODEX_MCP_ROOT");
            if (!string.IsNullOrWhiteSpace(env2) && Directory.Exists(env2)) return env2;

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
            {
                var p1 = Path.Combine(docs, "Codex_MCP", "Codex");
                if (Directory.Exists(p1)) return p1;

                var p2 = Path.Combine(docs, "Codex");
                if (Directory.Exists(p2)) return p2;
            }

            return null;
        }

        private static string SanitizePathSegment(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return cleaned.Trim();
        }

        private async Task TryAutoPollQueuedJobAsync()
        {
            string text;
            if (Dispatcher.CheckAccess())
            {
                text = OutputBox.Text ?? "";
            }
            else
            {
                text = Dispatcher.Invoke(() => OutputBox.Text ?? "");
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
                            AppendOutput(resultJson);
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
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendOutput(text));
                return;
            }

            EnsureOutputHeader();
            OutputBox.AppendText($"{text}{Environment.NewLine}");
            OutputBox.ScrollToEnd();
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
