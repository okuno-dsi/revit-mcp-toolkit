// ================================================================
// File: App.cs – RevitMCP Add-in（重複起動ガード + 非同期クリーンアップ + 多重起動安全サーバ）
// Target: Revit 2023+ / .NET Framework 4.8 / C# 8
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;     // RevitLogger
using RevitMCPAddin.Core.Net; // ServerProcessManager
using RevitMCPAddin.Core.ViewWorkspace;
using RevitMCPAddin.Manifest;
using RevitMCPAddin.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RevitMCPAddin
{
    internal static class AppServices
    {
        public static UIControlledApplication? UIControlledApp { get; internal set; }
        public static int CurrentPort { get; internal set; }
    }

    public class App : IExternalApplication
    {
        private static int _startupEntered = 0;
        private static int _workerStarted = 0;
        private static int _ribbonBuilt = 0;
        private static int _selectionMonitorStarted = 0;

        private static DateTime _lastSelPollUtc = DateTime.MinValue;
        private static string _lastSelDocPath = string.Empty;
        private static int _lastSelViewId = 0;
        private static int[] _lastSelIdsSorted = Array.Empty<int>();

        private RevitMcpWorker? _worker;
        private string? _lockFilePath;
        private int _pid;

        public Result OnStartup(UIControlledApplication application)
        {
            if (Interlocked.Exchange(ref _startupEntered, 1) == 1)
                return Result.Succeeded;

            // 1) ロガー初期化（起動直後は pending ログ → 後でポート別に切替）
            RevitLogger.Init(deleteOldOnStartup: false);
            RevitLogger.MinLevel = RevitLogger.Level.Info;
            RevitLogger.Info("Logger initialized.");

            AppServices.UIControlledApp = application;

            // 2) %LOCALAPPDATA%\RevitMCP\{logs|locks}
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string root = Path.Combine(local, "RevitMCP");
            string logs = Path.Combine(root, "logs");
            string locks = Path.Combine(root, "locks");
            string queue = Path.Combine(root, "queue");
            try { Directory.CreateDirectory(logs); } catch (Exception ex) { RevitLogger.Warn($"CreateDirectory(logs) failed: {ex.Message}"); }
            try { Directory.CreateDirectory(locks); } catch (Exception ex) { RevitLogger.Warn($"CreateDirectory(locks) failed: {ex.Message}"); }
            // Initialize queue root (per-port subdir is resolved by the server). Do not clean shared root.
            try { Directory.CreateDirectory(queue); RevitLogger.Info("Queue directory initialized."); }
            catch (Exception ex) { RevitLogger.Warn($"CreateDirectory(queue) failed: {ex.Message}"); }

            // 3) 起動時クリーンアップ（古ロック・古ログ）
            TryCleanupStaleArtifacts(logs, locks);

            string ver = application?.ControlledApplication?.VersionNumber ?? "unknown";
            _pid = Process.GetCurrentProcess().Id;
            RevitLogger.Info($"AddIn OnStartup (v{ver}) PID={_pid}");

            // Prefer High CPU priority to mitigate background throttling
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.PriorityClass = ProcessPriorityClass.High;
                try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch { /* best-effort */ }
                RevitLogger.Info($"Process priority set to {proc.PriorityClass}.");
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"Setting process priority failed: {ex.Message}");
            }

            // 4) サーバー起動（多重起動安全版）
            var me = Process.GetCurrentProcess().Id;
            var (ok, port, msg) = ServerProcessManager.StartOrAttach(me);
            AppServices.CurrentPort = port;

            // ポート確定後に addin_<port>.log へ切替
            RevitLogger.SwitchToPortLog(port, overwriteAtStart: true);
            RevitLogger.Info($"PORT: {port} // {msg}");

            // Ensure per-port queue directory exists for diagnostics (server uses this path for jobs)
            try
            {
                var portQueue = Path.Combine(queue, $"p{port}");
                Directory.CreateDirectory(portQueue);
            }
            catch { /* best-effort */ }

            // 5) インスタンスロック（Add-in側情報）
            _lockFilePath = Path.Combine(locks, $"revit{ver}_{_pid}.lock");
            TryWriteAllText(_lockFilePath, $"pid={_pid}\nversion={ver}\nport={port}\nstartedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            // 6) Ribbon / Worker
            TryBuildRibbon(application, port);
            TryStartWorker(application, port);
            TryStartSelectionMonitor(application);
            try { ViewWorkspaceService.Initialize(application); } catch (Exception ex) { RevitLogger.Warn($"ViewWorkspaceService.Initialize failed: {ex.Message}"); }

            // ENV（参照用）
            try
            {
                Environment.SetEnvironmentVariable("REVIT_MCP_PORT", port.ToString());
                PortLocator.SaveCurrentPort(port);
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"Set REVIT_MCP_PORT or SaveCurrentPort failed: {ex.Message}");
            }

            // 設定ファイルの初期化（任意）
            try { RevitMCPAddin.Core.SettingsHelper.EnsureSettingsFile(); }
            catch (Exception ex) { RevitLogger.Warn($"EnsureSettingsFile failed: {ex.Message}"); }

            // マニフェスト送信（best-effort）
            var baseUrl = "http://127.0.0.1:" + port.ToString();
            Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        var okPub = await ManifestExporter.PublishAsync(baseUrl);
                        if (okPub) break;
                    }
                    catch (Exception ex)
                    {
                        RevitLogger.Warn($"Manifest publish attempt failed: {ex.Message}");
                    }
                    await Task.Delay(1000);
                }
            });

            // 7) 終了ハンドラ
            try { AppDomain.CurrentDomain.ProcessExit += (_, __) => SafeStopAll("ProcessExit"); }
            catch (Exception ex) { RevitLogger.Warn($"Registering ProcessExit handler failed: {ex.Message}"); }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            RevitLogger.Info("AddIn OnShutdown");

            try { _worker?.Stop(); } catch (Exception ex) { RevitLogger.Warn($"Worker.Stop failed: {ex.Message}"); }
            try { ViewWorkspaceService.Shutdown(application); } catch { }
            TryStopSelectionMonitor(application);

            SafeStopAll("OnShutdown");

            TryDeleteFile(_lockFilePath);

            RevitLogger.FlushAndClose();
            AppServices.UIControlledApp = null;
            return Result.Succeeded;
        }

        // --------- helpers ---------

        private void TryStartWorker(UIControlledApplication application, int port)
        {
            if (Interlocked.Exchange(ref _workerStarted, 1) == 1)
            {
                RevitLogger.Info("Worker already started. Skip duplicate.");
                return;
            }
            try
            {
                _worker = new RevitMcpWorker(application, port);
                _worker.Start();
                RevitLogger.Info($"Worker started for port {port}");
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"Worker start failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void TryBuildRibbon(UIControlledApplication application, int port)
        {
            if (Interlocked.Exchange(ref _ribbonBuilt, 1) == 1)
            {
                RevitLogger.Info("Ribbon already built. Skip duplicate.");
                return;
            }
            try
            {
                var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string asm = Assembly.GetExecutingAssembly().Location;

                RibbonPortUi.Setup(application, port, iconDir: dllDir);
                RibbonUnits.AddUnitsPanel(application, dllDir);
                RibbonViewWorkspace.AddPanel(application, asm);
                try { RevitMCPAddin.UI.RibbonPortUi.AddPerformancePanel(application); } catch { }
                try { RevitMCPAddin.UI.RibbonPortUi.AddGuiPanel(application); } catch { }

                RevitLogger.Info($"Ribbon built (port={port}).");
            }
            catch (Exception rex)
            {
                RevitLogger.Warn($"Ribbon setup failed: {rex.GetType().Name}: {rex.Message}");
            }
        }

        private void TryStartSelectionMonitor(UIControlledApplication application)
        {
            if (Interlocked.Exchange(ref _selectionMonitorStarted, 1) == 1)
                return;

            try
            {
                // SelectionChanged event is not always available from UIControlledApplication.
                // Idling-based polling is low-risk and works across versions.
                application.Idling += OnIdlingUpdateSelectionStash;
                try { application.ViewActivated += OnViewActivatedBumpContextToken; } catch { /* best-effort */ }
                try { application.ControlledApplication.DocumentChanged += OnDocumentChangedBumpContextToken; } catch { /* best-effort */ }
                RevitLogger.Info("Selection monitor started (Idling polling).");
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"Selection monitor start failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void TryStopSelectionMonitor(UIControlledApplication application)
        {
            try
            {
                application.Idling -= OnIdlingUpdateSelectionStash;
                try { application.ViewActivated -= OnViewActivatedBumpContextToken; } catch { /* ignore */ }
                try { application.ControlledApplication.DocumentChanged -= OnDocumentChangedBumpContextToken; } catch { /* ignore */ }
                RevitLogger.Info("Selection monitor stopped.");
            }
            catch { /* ignore */ }
        }

        private static bool SameSortedIds(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static void OnIdlingUpdateSelectionStash(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            // View workspace autosave/restore ticks (best-effort; keep idling safe)
            // Note: must run even when selection/doc/view didn't change (restore state machine relies on Idling).
            try
            {
                var uiapp = sender as UIApplication;
                if (uiapp != null) ViewWorkspaceService.OnIdling(uiapp);
            }
            catch { }

            try
            {
                var now = DateTime.UtcNow;
                if (_lastSelPollUtc != DateTime.MinValue && (now - _lastSelPollUtc).TotalMilliseconds < 250)
                    return;
                _lastSelPollUtc = now;

                var uiapp = sender as UIApplication;
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return;

                // 1) Live selection
                ICollection<ElementId> sel;
                try { sel = uidoc.Selection.GetElementIds(); }
                catch { return; }

                var ids = new List<int>(sel != null ? sel.Count : 0);
                if (sel != null)
                {
                    foreach (var id in sel)
                    {
                        if (id == null) continue;
                        int v = id.IntValue();
                        if (v != 0) ids.Add(v);
                    }
                }
                ids.Sort();
                var idsArr = ids.ToArray();

                // 2) Context
                string docPath = string.Empty;
                string docTitle = string.Empty;
                int viewId = 0;
                try { docPath = doc.PathName ?? string.Empty; } catch { }
                try { docTitle = doc.Title ?? string.Empty; } catch { }
                try { viewId = uidoc.ActiveView?.Id?.IntValue() ?? 0; } catch { }

                bool sameIds = SameSortedIds(idsArr, _lastSelIdsSorted);
                bool sameDoc = string.Equals(docPath, _lastSelDocPath, StringComparison.OrdinalIgnoreCase);
                bool sameView = viewId == _lastSelViewId;
                if (sameIds && sameDoc && sameView) return;

                _lastSelIdsSorted = idsArr;
                _lastSelDocPath = docPath ?? string.Empty;
                _lastSelViewId = viewId;

                SelectionStash.Set(idsArr, docPath, docTitle, viewId);

                // Step 7: bump revision on selection changes (doc/view changes are captured via events).
                if (!sameIds)
                {
                    try { RevitMCPAddin.Core.ContextTokenService.BumpRevision(doc, "SelectionChanged"); } catch { /* ignore */ }
                }
            }
            catch
            {
                // keep Idling safe
            }
        }

        private static void OnViewActivatedBumpContextToken(object sender, Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            try
            {
                var doc = e != null ? e.Document : null;
                if (doc == null) return;
                RevitMCPAddin.Core.ContextTokenService.BumpRevision(doc, "ViewActivated");
            }
            catch
            {
                // keep handler safe
            }
        }

        private static void OnDocumentChangedBumpContextToken(object sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
        {
            try
            {
                var doc = e != null ? e.GetDocument() : null;
                if (doc == null) return;
                RevitMCPAddin.Core.ContextTokenService.BumpRevision(doc, "DocumentChanged");
            }
            catch
            {
                // keep handler safe
            }
        }

        private void SafeStopAll(string reason)
        {
            try
            {
                RevitLogger.Info($"StopAllServers queued ({reason})");

                // ★ 自分が使っていたポートだけ停止（他インスタンスに干渉しない）
                try
                {
                    int port = AppServices.CurrentPort;
                    var me = Process.GetCurrentProcess().Id;
                    var (ok, msg) = ServerProcessManager.StopByLock(me, port);
                    RevitLogger.Info($"StopByLock({port}): {msg}");
                }
                catch (Exception ex)
                {
                    RevitLogger.Warn($"StopByLock failed: {ex.Message}");
                }

                // 死骸ロックの掃除は非同期で（他人は殺さない）
                Task.Run(() =>
                {
                    try { ServerProcessManager.StopAllServers(); }
                    catch (Exception ex)
                    {
                        RevitLogger.Warn($"StopAllServers(bg) failed: {ex.GetType().Name}: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"queue StopAllServers failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void TryCleanupStaleArtifacts(string logsDir, string locksDir)
        {
            try
            {
                // 孤児ロックの掃除（pidが生存していないものを削除）
                foreach (var lockPath in Directory.GetFiles(locksDir, "*.lock"))
                {
                    try
                    {
                        var txt = File.ReadAllText(lockPath);
                        var lines = txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        string? pidStr = null;
                        foreach (var s in lines)
                            if (s.StartsWith("pid=")) { pidStr = s.Substring(4); break; }

                        if (int.TryParse(pidStr, out var pid))
                        {
                            bool alive = true;
                            try { Process.GetProcessById(pid); } catch { alive = false; }
                            if (!alive)
                            {
                                File.Delete(lockPath);
                                // 古いPID名入りログは削除（best-effort）
                                foreach (var log in Directory.GetFiles(logsDir, $"*_{pid}.log"))
                                    TryDeleteFile(log);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RevitLogger.Warn($"Lock cleanup failed: {ex.Message}");
                    }
                }

                // 7日以上前のログを掃除（best-effort）
                var threshold = DateTime.Now.AddDays(-7);
                foreach (var logPath in Directory.GetFiles(logsDir, "*.log"))
                {
                    try { if (File.GetLastWriteTime(logPath) < threshold) File.Delete(logPath); }
                    catch (Exception ex)
                    {
                        RevitLogger.Warn($"Old log cleanup failed for '{logPath}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"Cleanup stale artifacts failed: {ex.Message}");
            }
        }

        private static void TryWriteAllText(string? path, string text)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { File.WriteAllText(path, text); }
            catch (Exception ex) { RevitLogger.Warn($"WriteAllText failed for '{path}': {ex.Message}"); }
        }

        private static void TryDeleteFile(string? path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                RevitLogger.Warn($"Delete file failed for '{path}': {ex.Message}");
            }
        }
    }
}

