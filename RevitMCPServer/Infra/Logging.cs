using System.Text;

namespace RevitMcpServer.Infra
{
    public static class Logging
    {
        private static readonly object _lock = new object();
        private static string _logPath = string.Empty;
        private const long MaxLogBytes = 1_000_000; // 1MB rotate

        public static void Init(int port)
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logs = Path.Combine(local, "RevitMCP", "logs");
                Directory.CreateDirectory(logs);
                _logPath = Path.Combine(logs, $"server_{port}.log");
                // Always start with a fresh log file per process start
                try { if (File.Exists(_logPath)) File.Delete(_logPath); } catch { }
                try { File.WriteAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Server starting on port {port}{Environment.NewLine}", new UTF8Encoding(false)); }
                catch { }
            }
            catch { }
        }

        public static void Append(string line)
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            lock (_lock)
            {
                try
                {
                    var fi = new FileInfo(_logPath);
                    if (fi.Exists && fi.Length > MaxLogBytes)
                    {
                        var bak = _logPath + ".1";
                        try { File.Move(_logPath, bak, true); } catch { }
                    }
                }
                catch { }

                try { File.AppendAllText(_logPath, line + Environment.NewLine, new UTF8Encoding(false)); } catch { }
            }
        }
    }
}
