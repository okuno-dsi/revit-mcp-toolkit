#nullable enable
using System;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
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

                // Process-scoped add-in lock: revit<ver>_<pid>.lock
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var lockDir = Path.Combine(BaseDir, "locks");
                if (Directory.Exists(lockDir))
                {
                    foreach (var lockPath in Directory.GetFiles(lockDir, "revit*.lock"))
                    {
                        try
                        {
                            string txt = File.ReadAllText(lockPath);
                            int lockPid = 0;
                            int lockPort = 0;
                            foreach (var line in txt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (line.StartsWith("pid=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.Substring(4), out var o)) lockPid = o;
                                else if (line.StartsWith("port=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.Substring(5), out var lp)) lockPort = lp;
                            }

                            if (lockPid == pid && lockPort > 0)
                                return lockPort;
                        }
                        catch
                        {
                            // ignore malformed lock entries and continue scanning
                        }
                    }
                }

                // Process-scoped state file: server_state_{pid}.json (server side writes this too)
                var procPath = Path.Combine(BaseDir, $"server_state_{pid}.json");
                if (File.Exists(procPath))
                {
                    var jo = JObject.Parse(File.ReadAllText(procPath));
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

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                var _ = System.Diagnostics.Process.GetProcessById(pid);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void SaveCurrentPort(int port)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;

                // Avoid JObject.ToString()/JsonConvert.SerializeObject(JToken) to prevent Json.NET host
                // binding issues (MissingMethodException) observed in some Revit environments.
                var json = "{\"port\":" + port.ToString(CultureInfo.InvariantCulture)
                         + ",\"pid\":" + pid.ToString(CultureInfo.InvariantCulture) + "}";

                // Write process-scoped
                var procPath = Path.Combine(BaseDir, $"server_state_{pid}.json");
                File.WriteAllText(procPath, json);

                // Also write legacy
                var legacy = Path.Combine(BaseDir, "server_state.json");
                File.WriteAllText(legacy, json);
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
