using System;
using System.Diagnostics;
using System.Text;

namespace AutoCadMcpServer.Core
{
    /// <summary>
    /// Thin wrapper to execute accoreconsole.exe with a .scr script.
    /// - No arbitrary timeout cap (respects caller). If timeoutMs &lt;= 0, defaults to 30min.
    /// - On timeout: kill entire process tree, wait up to 5s for exit, return ExitCode = -1.
    /// - Collects stdout/stderr asynchronously to avoid deadlocks; returns only the tail for logs.
    /// </summary>
    public static class AccoreRunner
    {
        public static AccoreResult Run(string accorePath, string seed, string script, string locale, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(accorePath)) throw new ArgumentNullException(nameof(accorePath));
            if (string.IsNullOrWhiteSpace(seed)) throw new ArgumentNullException(nameof(seed));
            if (string.IsNullOrWhiteSpace(script)) throw new ArgumentNullException(nameof(script));
            if (string.IsNullOrWhiteSpace(locale)) locale = "JPN";

            var effectiveTimeout = timeoutMs > 0 ? timeoutMs : 30 * 60 * 1000; // 30min default

            var psi = new ProcessStartInfo(accorePath, $"/i \"{seed}\" /s \"{script}\" /l {locale}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(932),   // Shift-JIS
                StandardErrorEncoding  = Encoding.GetEncoding(932),
                WorkingDirectory = System.IO.Path.GetDirectoryName(script) ?? Environment.CurrentDirectory,
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using var p = new Process { StartInfo = psi, EnableRaisingEvents = false };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var exited = p.WaitForExit(effectiveTimeout);
            if (!exited)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                try { p.WaitForExit(5000); } catch { /* ignore */ }
                return new AccoreResult(
                    Ok: false,
                    Error: "E_ACCORE_TIMEOUT",
                    StdoutTail: Tail(stdout.ToString()),
                    StderrTail: Tail(stderr.ToString()),
                    ExitCode: -1
                );
            }

            // ensure all output drained
            try { p.WaitForExit(); } catch { /* ignore */ }

            var ok = p.ExitCode == 0;
            return new AccoreResult(
                Ok: ok,
                Error: ok ? null : "E_SCRIPT_FAIL",
                StdoutTail: Tail(stdout.ToString()),
                StderrTail: Tail(stderr.ToString()),
                ExitCode: p.ExitCode
            );
        }

        private static string Tail(string s, int n = 4000)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= n ? s : s.Substring(s.Length - n);
        }
    }

    public record AccoreResult(bool Ok, string? Error, string StdoutTail, string StderrTail, int ExitCode);
}
