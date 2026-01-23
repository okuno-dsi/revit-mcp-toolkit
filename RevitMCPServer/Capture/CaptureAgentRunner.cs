#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RevitMcpServer.Infra;

namespace RevitMcpServer.Capture
{
    internal static class CaptureAgentRunner
    {
        private static bool IsValidCaptureAgentExe(string exePath, out string? reason)
        {
            reason = null;
            try
            {
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    reason = "exePath is empty.";
                    return false;
                }

                if (!File.Exists(exePath))
                {
                    reason = "exe not found.";
                    return false;
                }

                // In our deployment model (framework-dependent apphost), the .exe requires the .dll next to it.
                // This avoids picking up a stale stub exe that was copied without its managed payload.
                var dll = Path.ChangeExtension(exePath, ".dll");
                if (!string.IsNullOrWhiteSpace(dll) && File.Exists(dll)) return true;

                reason = "missing companion .dll: " + dll;
                return false;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        public static string? TryFindExePath(out string? error)
        {
            error = null;
            try
            {
                var env = (Environment.GetEnvironmentVariable("REVIT_MCP_CAPTURE_AGENT_EXE") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(env) && IsValidCaptureAgentExe(env, out _)) return env;

                var baseDir = AppContext.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "capture-agent", "RevitMcp.CaptureAgent.exe"),
                    Path.Combine(baseDir, "CaptureAgent", "RevitMcp.CaptureAgent.exe"),
                    Path.Combine(baseDir, "RevitMcp.CaptureAgent.exe"),
                };
                string? lastReason = null;
                foreach (var c in candidates)
                {
                    if (string.IsNullOrWhiteSpace(c)) continue;
                    if (!IsValidCaptureAgentExe(c, out var reason))
                    {
                        if (!string.IsNullOrWhiteSpace(reason)) lastReason = reason;
                        continue;
                    }
                    return c;
                }

                error =
                    "CaptureAgent executable not found (or invalid). " +
                    "Build RevitMcp.CaptureAgent and ensure it is deployed next to the server " +
                    "(capture-agent\\RevitMcp.CaptureAgent.exe + RevitMcp.CaptureAgent.dll), " +
                    "or set REVIT_MCP_CAPTURE_AGENT_EXE. " +
                    (string.IsNullOrWhiteSpace(lastReason) ? "" : ("Last reason: " + lastReason));
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static async Task<JsonNode> RunAsync(string exePath, string command, Dictionary<string, string?> args, int timeoutMs = 15000)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, code = "CAPTURE_AGENT_NOT_FOUND", msg = "CaptureAgent executable not found.", exePath }))!;

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                psi.ArgumentList.Add(command);
                if (args != null)
                {
                    foreach (var kv in args)
                    {
                        var k = (kv.Key ?? "").Trim();
                        if (k.Length == 0) continue;
                        psi.ArgumentList.Add("--" + k);
                        if (kv.Value != null) psi.ArgumentList.Add(kv.Value);
                    }
                }

                using var p = Process.Start(psi);
                if (p == null)
                    return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, code = "CAPTURE_AGENT_START_FAIL", msg = "Failed to start CaptureAgent process." }))!;

                using var cts = new CancellationTokenSource(timeoutMs);
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();

                try
                {
                    await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    var so = await SafeAwait(stdoutTask).ConfigureAwait(false);
                    var se = await SafeAwait(stderrTask).ConfigureAwait(false);
                    return JsonNode.Parse(JsonSerializer.Serialize(new
                    {
                        ok = false,
                        code = "CAPTURE_AGENT_TIMEOUT",
                        msg = "CaptureAgent timed out.",
                        timeoutMs,
                        stdout = so,
                        stderr = se
                    }))!;
                }

                var stdout = await SafeAwait(stdoutTask).ConfigureAwait(false);
                var stderr = await SafeAwait(stderrTask).ConfigureAwait(false);

                if (p.ExitCode != 0)
                {
                    return JsonNode.Parse(JsonSerializer.Serialize(new
                    {
                        ok = false,
                        code = "CAPTURE_AGENT_EXIT_NONZERO",
                        msg = "CaptureAgent failed.",
                        exitCode = p.ExitCode,
                        stderr,
                        stdout
                    }))!;
                }

                if (string.IsNullOrWhiteSpace(stdout))
                    return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, code = "CAPTURE_AGENT_EMPTY", msg = "CaptureAgent returned empty stdout.", stderr }))!;

                try
                {
                    return JsonNode.Parse(stdout) ?? JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, code = "CAPTURE_AGENT_PARSE_NULL", msg = "CaptureAgent JSON parsed to null.", stdout, stderr }))!;
                }
                catch (Exception ex)
                {
                    return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, code = "CAPTURE_AGENT_INVALID_JSON", msg = ex.Message, stdout, stderr }))!;
                }
            }
            catch (Exception ex)
            {
                Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WARN CaptureAgentRunner.RunAsync failed: {ex.Message}");
                return JsonNode.Parse(JsonSerializer.Serialize(new { ok = false, code = "CAPTURE_AGENT_RUN_FAIL", msg = ex.Message }))!;
            }
        }

        private static async Task<string> SafeAwait(Task<string> t)
        {
            try { return await t.ConfigureAwait(false); }
            catch { return ""; }
        }
    }
}
