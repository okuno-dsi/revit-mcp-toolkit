#nullable enable
using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class PortLocator
    {
        private static string BaseDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP");

        public static int GetCurrentPortOrDefault(int fallback = 5210)
        {
            try
            {
                // Prefer per-process environment variable when provided
                var env = Environment.GetEnvironmentVariable("REVIT_MCP_PORT");
                if (int.TryParse(env, out var pEnv) && pEnv > 0 && pEnv < 65536)
                    return pEnv;

                // Process-scoped state file: server_state_{pid}.json
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var procPath = Path.Combine(BaseDir, $"server_state_{pid}.json");
                if (File.Exists(procPath))
                {
                    var jo = JObject.Parse(File.ReadAllText(procPath));
                    var p = jo.Value<int?>("port");
                    if (p.HasValue && p.Value > 0) return p.Value;
                }

                // Legacy shared state
                var legacy = Path.Combine(BaseDir, "server_state.json");
                if (File.Exists(legacy))
                {
                    var jo = JObject.Parse(File.ReadAllText(legacy));
                    var p = jo.Value<int?>("port");
                    if (p.HasValue && p.Value > 0) return p.Value;
                }
                return fallback;
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"GetCurrentPortOrDefault failed: {ex.Message}");
                return fallback;
            }
        }

        public static void SaveCurrentPort(int port)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var jo = new JObject { ["port"] = port, ["pid"] = pid };

                // Write process-scoped
                var procPath = Path.Combine(BaseDir, $"server_state_{pid}.json");
                File.WriteAllText(procPath, jo.ToString());

                // Also write legacy
                var legacy = Path.Combine(BaseDir, "server_state.json");
                File.WriteAllText(legacy, jo.ToString());
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"SaveCurrentPort failed: {ex.Message}");
            }
        }

        public static void DeleteStateFile()
        {
            try
            {
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var procPath = Path.Combine(BaseDir, $"server_state_{pid}.json");
                if (File.Exists(procPath)) File.Delete(procPath);

                var legacy = Path.Combine(BaseDir, "server_state.json");
                if (File.Exists(legacy)) File.Delete(legacy);
            }
            catch (Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"DeleteStateFile failed: {ex.Message}");
            }
        }
    }
}
