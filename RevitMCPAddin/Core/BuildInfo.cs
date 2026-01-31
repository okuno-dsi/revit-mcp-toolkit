// ================================================================
// File: Core/BuildInfo.cs
// Purpose: Provide a build/version label for ribbon display.
// ================================================================
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RevitMCPAddin.Core
{
    internal static class BuildInfo
    {
        private static string? _cached;

        public static string GetDisplayVersion()
        {
            if (!string.IsNullOrWhiteSpace(_cached)) return _cached!;

            string? ver = null;
            try { ver = TryReadBuildInfoFile(); } catch { /* ignore */ }
            if (string.IsNullOrWhiteSpace(ver))
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                    if (!string.IsNullOrWhiteSpace(info)) ver = info;
                }
                catch { /* ignore */ }
            }
            if (string.IsNullOrWhiteSpace(ver))
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    ver = asm.GetName().Version?.ToString();
                }
                catch { /* ignore */ }
            }
            if (string.IsNullOrWhiteSpace(ver)) ver = "unknown";
            _cached = ver;
            return ver!;
        }

        private static string? TryReadBuildInfoFile()
        {
            var asmPath = Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(asmPath);
            if (string.IsNullOrWhiteSpace(dir)) return null;
            var path = Path.Combine(dir, "build_info.txt");
            if (!File.Exists(path)) return null;
            var line = File.ReadLines(path).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (string.IsNullOrWhiteSpace(line)) return null;
            line = line.Trim();
            if (line.IndexOf('=') >= 0)
            {
                // Support "version=..." format if present
                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2) line = parts[1].Trim();
            }
            return line.Length == 0 ? null : line;
        }
    }
}
