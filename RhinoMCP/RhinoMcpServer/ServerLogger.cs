using System;
using System.IO;
using System.Text.Json;

namespace RhinoMcpServer
{
    public static class ServerLogger
    {
        static readonly string LogDir;
        static readonly string LogPath;
        static readonly object _lock = new object();

        static ServerLogger()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            LogDir = Path.Combine(baseDir, "RhinoMCP", "logs");
            Directory.CreateDirectory(LogDir);
            LogPath = Path.Combine(LogDir, "RhinoMcpServer.log");
            try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { }
        }

        public static void Log(ServerLogEntry e)
        {
            try
            {
                var json = JsonSerializer.Serialize(e, new JsonSerializerOptions { WriteIndented = false });
                lock (_lock)
                {
                    File.AppendAllText(LogPath, json + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    public class ServerLogEntry
    {
        public DateTime time { get; set; }
        public object id { get; set; }
        public string method { get; set; }
        public bool ok { get; set; }
        public string msg { get; set; }
    }
}

