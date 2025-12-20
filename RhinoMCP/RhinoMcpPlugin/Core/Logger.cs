using System;
using System.IO;

namespace RhinoMcpPlugin.Core
{
    public static class Logger
    {
        private static string _path;
        public static void Init()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(baseDir, "RhinoMCP", "logs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "RhinoMcpPlugin.log");
            try { if (File.Exists(_path)) File.Delete(_path); } catch {}
        }

        public static void Info(string msg)
        {
            try { File.AppendAllText(_path, DateTime.Now.ToString("s") + " INFO " + msg + Environment.NewLine); } catch {}
        }
        public static void Error(string msg)
        {
            try { File.AppendAllText(_path, DateTime.Now.ToString("s") + " ERROR " + msg + Environment.NewLine); } catch {}
        }
    }
}
