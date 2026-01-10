#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RevitMCPAddin.Core
{
    internal sealed class CacheCleanupReport
    {
        public bool ok { get; set; }
        public string? msg { get; set; }
        public int retentionDays { get; set; }
        public bool dryRun { get; set; }
        public int currentPort { get; set; }

        public int deletedFiles { get; set; }
        public int deletedDirs { get; set; }
        public long deletedBytes { get; set; }
        public List<string> deletedPaths { get; set; } = new List<string>();
        public List<string> warnings { get; set; } = new List<string>();
        public List<string> skippedActivePorts { get; set; } = new List<string>();
    }

    internal static class CacheCleanupService
    {
        public const int DefaultRetentionDays = 7;

        public static CacheCleanupReport CleanupLocalCache(int currentPort, int retentionDays, bool dryRun)
        {
            var report = new CacheCleanupReport
            {
                ok = true,
                msg = "OK",
                retentionDays = retentionDays,
                dryRun = dryRun,
                currentPort = currentPort
            };

            if (retentionDays <= 0) retentionDays = DefaultRetentionDays;
            var threshold = DateTime.Now.AddDays(-retentionDays);

            string root;
            try { root = Paths.LocalRoot; }
            catch (Exception ex)
            {
                report.ok = false;
                report.msg = "Paths.LocalRoot failed: " + ex.Message;
                return report;
            }

            string locksDir = Path.Combine(root, "locks");
            string logsDir = Path.Combine(root, "logs");
            string queueDir = Path.Combine(root, "queue");
            string dataDir = Path.Combine(root, "data");

            var activePorts = GetActivePortsFromLocks(locksDir, report);
            activePorts.Add(currentPort);

            // Root-level cache files
            try
            {
                if (Directory.Exists(root))
                {
                    foreach (var path in Directory.GetFiles(root))
                    {
                        var name = Path.GetFileName(path) ?? string.Empty;

                        // Never delete "settings.json" / "config.json" / "failure_whitelist.json" by default.
                        if (StringEquals(name, "settings.json")) continue;
                        if (StringEquals(name, "config.json")) continue;
                        if (StringEquals(name, "failure_whitelist.json")) continue;

                        // Keep user-provided overrides by default (even though add-in folder is preferred).
                        if (StringEquals(name, "RebarMapping.json")) continue;
                        if (StringEquals(name, "RebarBarClearanceTable.json")) continue;
                        if (StringEquals(name, "term_map_ja.json")) continue;

                        // Clean: old per-process port state files
                        if (name.StartsWith("server_state_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            if (GetLastWriteTimeSafe(path) < threshold)
                                TryDeleteFile(path, report, dryRun);
                            continue;
                        }

                        // Clean: old backups
                        if (name.IndexOf(".bak_", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (GetLastWriteTimeSafe(path) < threshold)
                                TryDeleteFile(path, report, dryRun);
                            continue;
                        }

                        // Clean: stale bridge.lock if PID is dead OR too old
                        if (StringEquals(name, "bridge.lock"))
                        {
                            bool shouldDelete = false;
                            try
                            {
                                var txt = File.ReadAllText(path).Trim();
                                if (int.TryParse(txt, out var pid) && pid > 0)
                                {
                                    bool alive = true;
                                    try { Process.GetProcessById(pid); } catch { alive = false; }
                                    if (!alive) shouldDelete = true;
                                }
                            }
                            catch { /* ignore */ }

                            if (!shouldDelete && GetLastWriteTimeSafe(path) < threshold) shouldDelete = true;
                            if (shouldDelete) TryDeleteFile(path, report, dryRun);
                            continue;
                        }

                        // Clean: temp-ish artifacts
                        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp.json", StringComparison.OrdinalIgnoreCase))
                        {
                            if (GetLastWriteTimeSafe(path) < threshold)
                                TryDeleteFile(path, report, dryRun);
                            continue;
                        }

                        // Everything else: keep (best-effort safety)
                    }
                }
            }
            catch (Exception ex)
            {
                report.warnings.Add("Root cleanup failed: " + ex.Message);
            }

            // Data snapshots (%LOCALAPPDATA%\RevitMCP\data\*)
            try
            {
                if (Directory.Exists(dataDir))
                {
                    foreach (var dir in Directory.GetDirectories(dataDir))
                    {
                        // Snapshot folder is usually safe to delete after retention.
                        var newest = GetNewestWriteTimeRecursiveSafe(dir);
                        if (newest < threshold) TryDeleteDirectory(dir, report, dryRun);
                    }
                }
            }
            catch (Exception ex)
            {
                report.warnings.Add("Data cleanup failed: " + ex.Message);
            }

            // Queue cleanup: delete stale per-port queues excluding active ports
            try
            {
                if (Directory.Exists(queueDir))
                {
                    foreach (var dir in Directory.GetDirectories(queueDir, "p*"))
                    {
                        var name = Path.GetFileName(dir) ?? string.Empty;
                        if (name.Length < 2) continue;

                        int port = 0;
                        if (!int.TryParse(name.Substring(1), out port) || port <= 0) continue;

                        if (activePorts.Contains(port))
                        {
                            report.skippedActivePorts.Add(name);
                            continue;
                        }

                        var newest = GetNewestWriteTimeRecursiveSafe(dir);
                        if (newest < threshold) TryDeleteDirectory(dir, report, dryRun);
                    }
                }
            }
            catch (Exception ex)
            {
                report.warnings.Add("Queue cleanup failed: " + ex.Message);
            }

            // logsDir/locksDir: handled elsewhere at startup (best-effort), do not duplicate here.
            // But we do ensure they exist.
            try { if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir); } catch { /* ignore */ }
            try { if (!Directory.Exists(locksDir)) Directory.CreateDirectory(locksDir); } catch { /* ignore */ }

            report.msg = $"Cleanup completed: deletedFiles={report.deletedFiles}, deletedDirs={report.deletedDirs}, deletedBytes={report.deletedBytes}, dryRun={dryRun}, retentionDays={retentionDays}";
            report.ok = true;
            return report;
        }

        private static HashSet<int> GetActivePortsFromLocks(string locksDir, CacheCleanupReport report)
        {
            var set = new HashSet<int>();
            try
            {
                if (!Directory.Exists(locksDir)) return set;
                foreach (var lockPath in Directory.GetFiles(locksDir, "*.lock"))
                {
                    try
                    {
                        var txt = File.ReadAllText(lockPath);
                        var lines = txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        int pid = 0;
                        int port = 0;
                        foreach (var s in lines)
                        {
                            if (s.StartsWith("pid=") && int.TryParse(s.Substring(4), out var p1)) pid = p1;
                            if (s.StartsWith("port=") && int.TryParse(s.Substring(5), out var p2)) port = p2;
                        }

                        if (pid <= 0 || port <= 0) continue;
                        bool alive = true;
                        try { Process.GetProcessById(pid); } catch { alive = false; }
                        if (!alive) continue;
                        set.Add(port);
                    }
                    catch { /* ignore */ }
                }
            }
            catch (Exception ex)
            {
                report.warnings.Add("Lock scan failed: " + ex.Message);
            }
            return set;
        }

        private static bool StringEquals(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime GetLastWriteTimeSafe(string path)
        {
            try { return File.GetLastWriteTime(path); }
            catch { return DateTime.MinValue; }
        }

        private static DateTime GetNewestWriteTimeRecursiveSafe(string dir)
        {
            DateTime newest = DateTime.MinValue;
            try
            {
                if (!Directory.Exists(dir)) return newest;
                try { newest = Directory.GetLastWriteTime(dir); } catch { newest = DateTime.MinValue; }

                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var t = GetLastWriteTimeSafe(f);
                    if (t > newest) newest = t;
                }
            }
            catch { /* ignore */ }
            return newest;
        }

        private static void TryDeleteFile(string path, CacheCleanupReport report, bool dryRun)
        {
            try
            {
                long len = 0;
                try { if (File.Exists(path)) len = new FileInfo(path).Length; } catch { len = 0; }

                if (!dryRun)
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                report.deletedFiles += 1;
                report.deletedBytes += len;
                report.deletedPaths.Add(path);
            }
            catch (Exception ex)
            {
                report.warnings.Add("Delete file failed: " + path + " :: " + ex.Message);
            }
        }

        private static void TryDeleteDirectory(string dir, CacheCleanupReport report, bool dryRun)
        {
            try
            {
                long bytes = 0;
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try { bytes += new FileInfo(f).Length; } catch { /* ignore */ }
                    }
                }
                catch { /* ignore */ }

                if (!dryRun)
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                }
                report.deletedDirs += 1;
                report.deletedBytes += bytes;
                report.deletedPaths.Add(dir + Path.DirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                report.warnings.Add("Delete dir failed: " + dir + " :: " + ex.Message);
            }
        }
    }
}

