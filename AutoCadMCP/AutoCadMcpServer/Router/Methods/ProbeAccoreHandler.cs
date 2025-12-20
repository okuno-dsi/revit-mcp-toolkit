using System.Diagnostics;
using System.Text.Json.Nodes;
using AutoCadMcpServer.Router;

namespace AutoCadMcpServer.Router.Methods
{
    public static class ProbeAccoreHandler
    {
        public static Task<object> Handle(JsonObject p, ILogger logger, IConfiguration config)
        {
            var path = p["path"]?.GetValue<string>() ?? config["Accore:Path"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult<object>(new { ok = false, exists = false, msg = "No path provided." });

            var exists = File.Exists(path);
            if (!exists) return Task.FromResult<object>(new { ok = false, exists = false, msg = "File not found." });

            try
            {
                var psi = new ProcessStartInfo(path, "/? ")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi)!;
                proc.WaitForExit(5000);
                return Task.FromResult<object>(new { ok = true, exists = true, exitCode = proc.ExitCode });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object>(new { ok = false, exists = true, error = ex.Message });
            }
        }
    }
}

