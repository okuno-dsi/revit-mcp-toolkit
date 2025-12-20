using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RevitMcpServer.Infra
{
    /// <summary>
    /// Port lock helper that coordinates multiple server instances:
    /// - Tries to acquire a lock file for a candidate port.
    /// - Verifies the port is bindable via TcpListener on 127.0.0.1.
    /// - Holds the lock (FileStream) until Release(port) is called.
    /// Lock file path: %LOCALAPPDATA%/RevitMCP/locks/server_{port}.lock
    /// </summary>
    public static class PortLocker
    {
        private static readonly ConcurrentDictionary<int, FileStream?> Held = new();

        private static string GetLockDir()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(local, "RevitMCP", "locks");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string GetLockPath(int port) => Path.Combine(GetLockDir(), $"server_{port}.lock");

        /// <summary>
        /// Tries the preferred port, then 5210..5219 in order (deduped), returning the first
        /// port for which lock + bind test succeeds. Throws if none available.
        /// </summary>
        public static int AcquireAvailablePort(int preferred)
        {
            var order = new List<int>();
            if (preferred > 0) order.Add(preferred);
            for (int p = 5210; p <= 5219; p++) if (!order.Contains(p)) order.Add(p);

            foreach (var port in order)
            {
                FileStream? fs = null;
                var lockPath = GetLockPath(port);

                // Try to acquire lock file exclusively
                try
                {
                    fs = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    var txt = $"pid={Environment.ProcessId}, port={port}, ts={DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    var bytes = Encoding.UTF8.GetBytes(txt);
                    fs.SetLength(0);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                catch
                {
                    try { fs?.Dispose(); } catch { }
                    fs = null;
                    continue; // lock failed -> next port
                }

                // Bind test: ensure 127.0.0.1:port is actually free
                TcpListener? l = null;
                try
                {
                    l = new TcpListener(IPAddress.Loopback, port);
                    l.Start();
                    l.Stop();
                }
                catch
                {
                    // Bind failed -> release lock and continue
                    try { fs?.Dispose(); } catch { }
                    try { File.Delete(lockPath); } catch { }
                    fs = null;
                    continue;
                }

                // Success: keep lock open until release
                Held[port] = fs;
                return port;
            }

            throw new InvalidOperationException("No available port among 5210..5219 (including preferred).");
        }

        /// <summary>
        /// Releases the lock and deletes the lock file, if any.
        /// </summary>
        public static void Release(int port)
        {
            try
            {
                if (Held.TryRemove(port, out var fs))
                {
                    try { fs?.Dispose(); } catch { }
                }
            }
            catch { }

            try { File.Delete(GetLockPath(port)); } catch { }
        }
    }
}

