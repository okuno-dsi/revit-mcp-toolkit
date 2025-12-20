// ================================================================
// File: Core/Net/ServerLock.cs
// Purpose : サーバーロックを %LOCALAPPDATA%\RevitMCP\locks に統一管理
// Notes   : 旧版の Program Files 側ロック "RevitMcpServer_{port}.lock" も読取/削除のみ互換
// ================================================================
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace RevitMCPAddin.Core.Net
{
    public static class ServerLock
    {
        public struct LockInfo
        {
            public int pid;
            public int port;
            public string? version;
            public DateTime startedAt;
        }

        private static string AppDataRoot
        {
            get
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "RevitMCP");
            }
        }
        private static string LocksDir => Path.Combine(AppDataRoot, "locks");

        private static void EnsureDir()
        {
            try { Directory.CreateDirectory(LocksDir); } catch { /* best-effort */ }
        }

        private static string CurrentLockPath(int port)
            => Path.Combine(LocksDir, $"server_{port}.lock");

        // 旧版（アセンブリ配置直下）互換：読取/削除のみ
        private static string LegacyLockPath(int port)
        {
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            return Path.Combine(asmDir, $"RevitMcpServer_{port}.lock");
        }

        public static void WriteLock(int port, int pid)
        {
            EnsureDir();
            try
            {
                File.WriteAllText(CurrentLockPath(port),
                    $"pid={pid}\nport={port}\nversion={TryGetRevitVersion()}\nstartedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch { /* best-effort */ }

            // 旧版残骸の掃除
            TryDeleteFile(LegacyLockPath(port));
        }

        public static LockInfo? ReadLock(int port)
        {
            var cur = TryRead(CurrentLockPath(port));
            if (cur.HasValue) return cur;
            return TryRead(LegacyLockPath(port)); // 旧版互換
        }

        public static void DeleteLock(int port)
        {
            TryDeleteFile(CurrentLockPath(port));
            TryDeleteFile(LegacyLockPath(port)); // 旧版も掃除
        }

        public static bool IsOurServerAlive(int port)
        {
            var info = ReadLock(port);
            if (!info.HasValue) return false;
            try
            {
                var p = Process.GetProcessById(info.Value.pid);
                return !p.HasExited;
            }
            catch { return false; }
        }

        // ===== helpers =====
        private static ServerLock.LockInfo? TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path);
                int pid = TryExtractInt(text, "pid=");
                int prt = TryExtractInt(text, "port=");
                string? ver = TryExtractStr(text, "version=");
                DateTime dt = TryExtractDate(text, "startedAt=");

                if (pid <= 0 || prt <= 0) return null;
                return new LockInfo { pid = pid, port = prt, version = ver, startedAt = dt };
            }
            catch { return null; }
        }

        private static int TryExtractInt(string text, string key)
        {
            try
            {
                foreach (var line in text.Split('\n', '\r'))
                {
                    var s = line.Trim();
                    if (s.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    {
                        var v = s.Substring(key.Length).Trim();
                        if (int.TryParse(v, out var i)) return i;
                    }
                }
            }
            catch { }
            return 0;
        }

        private static string? TryExtractStr(string text, string key)
        {
            try
            {
                foreach (var line in text.Split('\n', '\r'))
                {
                    var s = line.Trim();
                    if (s.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    {
                        return s.Substring(key.Length).Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        private static DateTime TryExtractDate(string text, string key)
        {
            try
            {
                var s = TryExtractStr(text, key);
                if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, out var d)) return d;
            }
            catch { }
            return DateTime.MinValue;
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string? TryGetRevitVersion()
        {
            try { return null; } catch { return null; }
        }
    }
}
