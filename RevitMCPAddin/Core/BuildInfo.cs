// ================================================================
// File: Core/BuildInfo.cs
// Purpose: Provide a build/version label for ribbon display.
// ================================================================
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;

namespace RevitMCPAddin.Core
{
    internal static class BuildInfo
    {
        private static string? _cached;

        public static string GetDisplayVersion()
        {
            if (!string.IsNullOrWhiteSpace(_cached)) return _cached!;

            string? ver = null;
            try
            {
                var asmPath = Assembly.GetExecutingAssembly().Location;
                DateTime asmUtc = DateTime.MinValue;
                if (!string.IsNullOrWhiteSpace(asmPath) && File.Exists(asmPath))
                    asmUtc = File.GetLastWriteTimeUtc(asmPath);

                DateTime infoUtc;
                ver = TryReadBuildInfoFile(out infoUtc);

                // If build_info.txt is clearly older than the actual assembly, ignore it.
                // This avoids stale ribbon labels when installers forget to overwrite build_info.txt.
                if (!string.IsNullOrWhiteSpace(ver) && asmUtc != DateTime.MinValue && infoUtc != DateTime.MinValue)
                {
                    if (infoUtc < asmUtc.AddMinutes(-1))
                    {
                        ver = null;
                    }
                }
            }
            catch { /* ignore */ }

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
            if (string.IsNullOrWhiteSpace(ver))
            {
                // Last-resort fallback: derive a visible changing label from assembly write time.
                try
                {
                    var asmPath = Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrWhiteSpace(asmPath) && File.Exists(asmPath))
                    {
                        var t = File.GetLastWriteTime(asmPath);
                        ver = t.ToString("yyyy.MM.dd+HHmm", CultureInfo.InvariantCulture);
                    }
                }
                catch { /* ignore */ }
            }
            if (string.IsNullOrWhiteSpace(ver)) ver = "unknown";
            _cached = ver;
            return ver!;
        }

        private static string? TryReadBuildInfoFile(out DateTime infoWriteUtc)
        {
            infoWriteUtc = DateTime.MinValue;
            var asmPath = Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(asmPath);
            if (string.IsNullOrWhiteSpace(dir)) return null;
            var path = Path.Combine(dir, "build_info.txt");
            if (!File.Exists(path)) return null;
            try { infoWriteUtc = File.GetLastWriteTimeUtc(path); } catch { infoWriteUtc = DateTime.MinValue; }
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
