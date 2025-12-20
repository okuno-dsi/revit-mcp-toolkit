// ================================================================
// File: Core/Net/ServerProcessManager.cs
// Purpose : サーバープロセスの起動/停止管理（5210最優先・多重起動安全・ownerPid対応）
// Target  : .NET Framework 4.8 / C# 8
// ================================================================
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;

namespace RevitMCPAddin.Core.Net
{
    public static class ServerProcessManager
    {
        public const int BasePort = 5210;

        private static readonly string _appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP");
        private static readonly string _locksDir = Path.Combine(_appDataRoot, "locks");

        // -------------------- Public APIs --------------------

        /// <summary>
        /// サーバーを起動。まず 5210 を最優先で使用。埋まっている場合は 5211→… の空きを使用。
        /// 他インスタンスは停止・乗っ取りしない。ownerPid で「自分のサーバー」を識別。
        /// </summary>
        public static (bool ok, int port, string msg) StartOrAttach(int ownerPid)
        {
            EnsureAppData();

            // Inter-process mutex to prevent race conditions starting multiple servers at once per user session
            bool gateTaken = false;
            using (var gate = new Mutex(false, @$"Global\RevitMCP_ServerStarter_{Environment.UserName}"))
            {
                try
                {
                    try { gateTaken = gate.WaitOne(millisecondsTimeout: 15000); } catch { gateTaken = false; }

                    CleanupStaleLocks(stopAll: false);

                // 1) まず 5210 を試す（空いていれば必ず 5210 を使う）
                if (IsPortFree(BasePort))
                {
                    // lock はあるがプロセス死んでいたら掃除
                    var infoDead = ReadLock(BasePort);
                    if (infoDead != null && !IsProcessAlive(infoDead.Value.serverPid))
                        TryDeleteLock(BasePort);

                    if (ReadLock(BasePort) == null) // 生存なし
                    {
                        var res = StartServerOnPort(BasePort);
                        if (res.ok) WriteLock(BasePort, res.pid, ownerPid);
                        return (res.ok, BasePort, res.msg);
                    }
                }

            // 2) 5210 が埋まっている
            var info = ReadLock(BasePort);
            if (info != null && IsProcessAlive(info.Value.serverPid))
            {
                // 自分（同じ Revit プロセス）が起動したサーバーならアタッチ許可
                if (info.Value.ownerPid == ownerPid)
                {
                    return (true, BasePort, "already running on 5210 (same owner)");
                }
                // 他人の 5210 → 別ポートへ
            }
            else
            {
                // ロックはあるが死骸 → 掃除して 5210 を再起動
                if (info != null) TryDeleteLock(BasePort);
                if (IsPortFree(BasePort))
                {
                    var resAgain = StartServerOnPort(BasePort);
                    if (resAgain.ok) WriteLock(BasePort, resAgain.pid, ownerPid);
                    return (resAgain.ok, BasePort, resAgain.msg);
                }
            }

                    // 3) Scan incrementally (5211, 5212, ...)
                    for (int i = 1; i < 200; i++)
                    {
                        int p = BasePort + i;
                        var info2 = ReadLock(p);
                        if (info2 != null)
                        {
                            if (!IsProcessAlive(info2.Value.serverPid)) TryDeleteLock(p); else continue;
                        }
                        if (IsPortFree(p))
                        {
                            var res2 = StartServerOnPort(p);
                            if (res2.ok) { WriteLock(p, res2.pid, ownerPid); return (true, p, $"started on {p}"); }
                        }
                    }

                    // attach to existing 5210 if alive
                    var exist = ReadLock(BasePort);
                    if (exist != null && IsProcessAlive(exist.Value.serverPid))
                        return (true, BasePort, "attached to existing server on 5210");

                    return (false, BasePort, "No free port found.");
                }
                finally
                {
                    if (gateTaken)
                    {
                        try { gate.ReleaseMutex(); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// 自分の ownerPid と一致する場合のみ、指定ポートのサーバーを停止（他人は触らない）。
        /// </summary>
        public static (bool ok, string msg) StopByLock(int ownerPid, int port)
        {
            try
            {
                var info = ReadLock(port);
                if (info == null) return (true, $"lock({port}) not found");

                if (info.Value.ownerPid != ownerPid)
                    return (true, $"skip stop: not owner (lock.ownerPid={info.Value.ownerPid}, me={ownerPid})");

                try
                {
                    var p = Process.GetProcessById(info.Value.serverPid);
                    TryKillTree(p);
                }
                catch
                {
                    // 既に終了している等 → ロックだけ削除
                }
                TryDeleteLock(port);
                return (true, $"stopped by lock: {port}");
            }
            catch (Exception ex)
            {
                return (false, $"StopByLock({port}) failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// ロック全確認。
        /// - サーバープロセスが不在のロックは削除。
        /// - サーバーは生存しているが ownerPid（起動したRevit）が死んでいる場合は、
        ///   孤児とみなして安全に Kill しロックも削除。
        /// </summary>
        public static void StopAllServers()
        {
            EnsureAppData();
            foreach (var lockPath in Directory.GetFiles(_locksDir, "server_*.lock"))
            {
                var port = ParsePortFromLockFileName(lockPath);
                if (port <= 0) continue;

                var info = ReadLock(port);
                if (info == null) { TryDelete(lockPath); continue; }

                // サーバーがいない → ロック掃除
                if (!IsProcessAlive(info.Value.serverPid))
                {
                    TryDeleteLock(port);
                    continue;
                }

                // サーバーは生存しているが、起動元の Revit(ownerPid) が死んでいる → 孤児として終了
                if (info.Value.ownerPid > 0 && !IsProcessAlive(info.Value.ownerPid))
                {
                    try
                    {
                        var p = Process.GetProcessById(info.Value.serverPid);
                        TryKillTree(p);
                    }
                    catch { /* ignore */ }
                    TryDeleteLock(port);
                }
            }
        }

        /// <summary>
        /// Force stop by port regardless of ownerPid. Safe guardrails:
        /// - Only acts when a lock file exists for the port.
        /// - Kills the recorded serverPid if still alive, then deletes the lock.
        /// - Returns (false, msg) when no lock or process already gone.
        /// Intended for explicit user confirmation from UI.
        /// </summary>
        public static (bool ok, string msg) ForceStopByPort(int port)
        {
            try
            {
                var info = ReadLock(port);
                if (info == null) return (false, $"no lock for port {port}");

                try
                {
                    var p = Process.GetProcessById(info.Value.serverPid);
                    TryKillTree(p);
                }
                catch
                {
                    // already exited; continue to lock cleanup
                }
                TryDeleteLock(port);
                return (true, $"force-stopped server on {port}");
            }
            catch (Exception ex)
            {
                return (false, $"ForceStopByPort({port}) failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // -------------------- Internals --------------------

        private static (bool ok, int pid, string msg) StartServerOnPort(int port)
        {
            if (!IsPortFree(port))
                return (false, 0, $"port {port} is busy");

            // ロックはあるが死んでいれば掃除
            var info = ReadLock(port);
            if (info != null)
            {
                if (!IsProcessAlive(info.Value.serverPid))
                    TryDeleteLock(port);
                else
                    return (false, 0, $"alive server exists on {port}");
            }

            var exe = ResolveServerExePath(out string describe);
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                Log($"Server exe not found. Looked at: {describe}");
                return (false, 0, "Server executable not found. Place server/RevitMCPServer.exe next to the Add-in or set REVIT_MCP_SERVER_EXE.");
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"--port {port}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
                };

                Log($"Starting server: {psi.FileName} {psi.Arguments} (WD={psi.WorkingDirectory})");
                var proc = Process.Start(psi);
                if (proc == null) return (false, 0, "Process.Start returned null");

                // 自分が起動したサーバーだけ Job 登録（失敗しても続行）
                TryAssignJob(proc);

                // wait for the port to bind (slightly extended to reduce startup races)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool bound = false;
                int bindTimeoutMs = 30000; // default 30s
                try
                {
                    var envTimeout = Environment.GetEnvironmentVariable("REVITMCP_BIND_TIMEOUT_MS");
                    if (!string.IsNullOrWhiteSpace(envTimeout) && int.TryParse(envTimeout, out var ms) && ms > 0)
                        bindTimeoutMs = ms;
                }
                catch { }
                while (sw.ElapsedMilliseconds < bindTimeoutMs)
                {
                    try
                    {
                        using (var client = new System.Net.Sockets.TcpClient())
                        {
                            var ar = client.BeginConnect(System.Net.IPAddress.Loopback, port, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(500))
                            {
                                try { client.EndConnect(ar); } catch { }
                                bound = true; break;
                            }
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(100);
                }

                // 18s→30sに延長。それでも bind しない場合でも、プロセスは起動済みのため OK 扱いでロックを書き、
                // 後続の StartOrAttach が二重起動しないようにする。
                var msg = bound ? $"started: {describe} (port {port})" : $"started (pending bind): {describe} (port {port})";
                Log($"Start result: bound={bound}, hasExited={proc.HasExited}, pid={proc.Id}, msg={msg}");
                return (bound || proc.HasExited == false, proc.Id,
                    msg);
            }
            catch (Exception ex)
            {
                Log($"Start failed: {ex.GetType().Name}: {ex.Message}");
                return (false, 0, $"start failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void EnsureAppData()
        {
            try { Directory.CreateDirectory(_locksDir); } catch { }
        }

        private static int FindNextFreeFrom(int startPort, int maxScan = 200)
        {
            int p = startPort;
            for (int i = 0; i < maxScan; i++, p++)
            {
                if (p < 1 || p > 65535) continue;
                if (IsPortFree(p)) return p;
            }
            return -1;
        }

        private static bool IsPortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        private static bool IsProcessAlive(int pid)
        {
            try { var _ = Process.GetProcessById(pid); return true; }
            catch { return false; }
        }

        // ---------- Locks ----------
        private struct LockInfo { public int port; public int serverPid; public int ownerPid; public string path; }

        private static string GetLockPath(int port) => Path.Combine(_locksDir, $"server_{port}.lock");

        private static void WriteLock(int port, int serverPid, int ownerPid)
        {
            try
            {
                var exe = ResolveServerExePath(out _);
                var text = $"pid={serverPid}\nownerPid={ownerPid}\npath={exe}\nport={port}\nstartedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                File.WriteAllText(GetLockPath(port), text);
            }
            catch { /* ignore */ }
        }

        private static LockInfo? ReadLock(int port)
        {
            try
            {
                var path = GetLockPath(port);
                if (!File.Exists(path)) return null;
                var txt = File.ReadAllText(path);
                int pid = 0;
                int owner = 0;
                string exe = "";
                foreach (var line in txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("pid=") && int.TryParse(line.Substring(4), out var p)) pid = p;
                    else if (line.StartsWith("ownerPid=") && int.TryParse(line.Substring(9), out var o)) owner = o;
                    else if (line.StartsWith("path=")) exe = line.Substring(5);
                }
                if (pid <= 0) return null;
                return new LockInfo { port = port, serverPid = pid, ownerPid = owner, path = exe };
            }
            catch { return null; }
        }

        private static void TryDeleteLock(int port)
        {
            TryDelete(GetLockPath(port));
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static int ParsePortFromLockFileName(string lockPath)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(lockPath); // server_5210
                var idx = name.LastIndexOf('_');
                if (idx < 0) return -1;
                if (int.TryParse(name.Substring(idx + 1), out var p)) return p;
                return -1;
            }
            catch { return -1; }
        }

        // ---------- JobObject（任意/ダミー） ----------
        // ---------- JobObject（子プロセスをRevit終了に追随させる） ----------
        // Revitプロセス終了時に Job ハンドルが閉じられ、
        // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE によりサーバープロセスも確実に終了する。
        // 失敗時はそのまま（従来どおり StopByLock にフォールバック）。

        private static IntPtr _jobHandle = IntPtr.Zero;

        private static bool TryEnsureJob()
        {
            try
            {
                if (_jobHandle != IntPtr.Zero) return true;
                var name = $"RevitMCP_ServerJob_{Process.GetCurrentProcess().Id}";
                _jobHandle = CreateJobObject(IntPtr.Zero, name);
                if (_jobHandle == IntPtr.Zero) return false;

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JobObjectLimitFlags.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, ptr, (uint)length))
                        return false;
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
                return true;
            }
            catch { return false; }
        }

        private static bool TryAssignJob(Process proc)
        {
            try
            {
                if (!TryEnsureJob()) return false;
                return AssignProcessToJobObject(_jobHandle, proc.Handle);
            }
            catch { return false; }
        }

        // Win32 Job API
        private enum JobObjectInfoType
        {
            BasicLimitInformation = 2,
            ExtendedLimitInformation = 9,
        }

        [Flags]
        private enum JobObjectLimitFlags : uint
        {
            JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public JobObjectLimitFlags LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        // ---------- EXE解決 ----------
        private static string ResolveServerExePath(out string describe)
        {
            // 1) Environment variable (two spellings)
            var env = Environment.GetEnvironmentVariable("REVITMCP_SERVER_EXE");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            {
                describe = env;
                return env;
            }
            env = Environment.GetEnvironmentVariable("REVIT_MCP_SERVER_EXE");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            {
                describe = env;
                return env;
            }

            // 2) Near the Add-in assembly
            var addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var candidates = new[]
            {
                Path.Combine(addinDir, "server", "RevitMCPServer.exe"),
                Path.Combine(addinDir, "server", "RevitMcpServer.exe"),
                Path.Combine(addinDir, "RevitMCPServer.exe"),
                Path.Combine(addinDir, "RevitMcpServer.exe")
            };
            foreach (var c in candidates)
            {
                var p = Path.GetFullPath(c);
                if (File.Exists(p)) { describe = p; return p; }
            }

            // 3) Dev-layout fallbacks (when running from solution/bin tree)
            try
            {
                var dir = addinDir;
                for (int up = 0; up < 6 && !string.IsNullOrEmpty(dir); up++)
                {
                    var parent = Directory.GetParent(dir)?.FullName;
                    if (string.IsNullOrEmpty(parent)) break;
                    // RevitMCPServer\publish
                    var p1 = Path.Combine(parent, "RevitMCPServer", "publish", "RevitMCPServer.exe");
                    if (File.Exists(p1)) { describe = p1; return p1; }
                    // RevitMCPServer\bin\x64\Release\net8.0
                    var p2 = Path.Combine(parent, "RevitMCPServer", "bin", "x64", "Release", "net8.0", "RevitMCPServer.exe");
                    if (File.Exists(p2)) { describe = p2; return p2; }
                    dir = parent;
                }
            }
            catch { }

            describe = "(server exe not found)";
            return "";
        }

        // ---------- ログ ----------
        private static void Log(string msg)
        {
            try
            {
                var dir = Path.Combine(_appDataRoot, "logs");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "ServerProcessManager.log");
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch { }
        }

        private static void TryKillTree(Process p)
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        // duplicate StopByLock/StopAllServers removed (implemented earlier in file)

        /// <summary>
        /// stopAll=false のときは「死骸」だけを掃除。true は未使用（干渉を避けるため）。
        /// </summary>
        public static void CleanupStaleLocks(bool stopAll)
        {
            EnsureAppData();
            foreach (var lockPath in Directory.GetFiles(_locksDir, "server_*.lock"))
            {
                var port = ParsePortFromLockFileName(lockPath);
                if (port <= 0) continue;

                var info = ReadLock(port);
                if (info == null) { TryDelete(lockPath); continue; }

                if (!IsProcessAlive(info.Value.serverPid))
                    TryDeleteLock(port);
                // 生存中は尊重（stopAll==true でも触らない）
            }
        }
    }
}
