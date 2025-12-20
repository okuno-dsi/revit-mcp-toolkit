// ================================================================
// File: Core/Net/BridgeProcessManager.cs
// 目的: STDIOブリッジ (McpRevitBridge.exe) の起動/停止
// 仕様: <addin>\bridge\McpRevitBridge.exe 優先 → 同階層 → 見つからなければエラー
//       Lock: %LOCALAPPDATA%\RevitMCP\bridge.lock に PID 記録
// ================================================================
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace RevitMCPAddin.Core.Net
{
    public static class BridgeProcessManager
    {
        private static string LocalRoot
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP");

        private static string LockPath => Path.Combine(LocalRoot, "bridge.lock");

        private static (bool found, string exePath, string workDir, string describe) ResolveBridgeBinary()
        {
            var addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            foreach (var dir in new[] { Path.Combine(addinDir, "bridge"), addinDir })
            {
                if (!Directory.Exists(dir)) continue;
                var exe = Path.Combine(dir, "McpRevitBridge.exe");
                if (File.Exists(exe))
                    return (true, exe, dir, exe);
            }
            return (false, "", addinDir, "McpRevitBridge.exe not found");
        }

        public static (bool ok, string msg) Start()
        {
            Directory.CreateDirectory(LocalRoot);

            // 既に起動?
            var info = ReadLock();
            if (info.ok && info.pid > 0)
            {
                try
                {
                    var p = Process.GetProcessById(info.pid);
                    if (!p.HasExited) return (true, $"Already running (PID={p.Id})");
                }
                catch { /* dead */ }
            }

            var bin = ResolveBridgeBinary();
            if (!bin.found) return (false, "Bridge exe not found. <addin>\\bridge\\McpRevitBridge.exe に配置してください。");

            // 無引数でOK（server.jsonを読む） / 明示で --url を付けたい場合はここでArgsを作る
            var psi = new ProcessStartInfo
            {
                FileName = bin.exePath,
                Arguments = "",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = bin.workDir
            };

            try
            {
                var p = Process.Start(psi);
                if (p == null) return (false, "Process.Start returned null");
                WriteLock(p.Id);
                return (true, $"Bridge started (PID={p.Id}) :: {bin.describe}");
            }
            catch (Exception ex)
            {
                return (false, "Bridge start failed: " + ex.Message);
            }
        }

        public static (bool ok, string msg) Stop()
        {
            var info = ReadLock();
            if (!info.ok || info.pid <= 0) return (true, "Not running (no lock).");

            try
            {
                var p = Process.GetProcessById(info.pid);
                if (!p.HasExited)
                {
                    try { p.CloseMainWindow(); } catch { }
                    try { if (!p.WaitForExit(500)) p.Kill(); } catch { p.Kill(); }
                }
            }
            catch { /* ignore */ }

            DeleteLock();
            return (true, "Bridge stopped.");
        }

        // ----------------- lock helpers -----------------
        private static void WriteLock(int pid)
        {
            try
            {
                Directory.CreateDirectory(LocalRoot);
                File.WriteAllText(LockPath, pid.ToString());
            }
            catch { }
        }

        private static void DeleteLock()
        {
            try { if (File.Exists(LockPath)) File.Delete(LockPath); } catch { }
        }

        private static (bool ok, int pid) ReadLock()
        {
            try
            {
                if (!File.Exists(LockPath)) return (false, 0);
                var txt = File.ReadAllText(LockPath).Trim();
                if (int.TryParse(txt, out var pid) && pid > 0) return (true, pid);
            }
            catch { }
            return (false, 0);
        }
    }
}
