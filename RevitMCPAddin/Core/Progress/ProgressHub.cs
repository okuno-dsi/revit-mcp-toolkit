// RevitMCPAddin/Core/Progress/ProgressHub.cs
// Notes:
// - Avoid Newtonsoft.Json here to be resilient against host binding quirks.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RevitMCPAddin.Core.Progress
{
    public static class ProgressHub
    {
        private static readonly object Gate = new object();
        private static ProgressState? _current;
        private static string _logFolder = "";
        private static string _logPath = "";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static event Action<ProgressState>? Changed;

        public static bool IsInitialized
        {
            get { lock (Gate) { return !string.IsNullOrWhiteSpace(_logFolder); } }
        }

        public static void Initialize(string logFolder)
        {
            if (string.IsNullOrWhiteSpace(logFolder)) return;
            lock (Gate)
            {
                _logFolder = logFolder.Trim();
                try { Directory.CreateDirectory(_logFolder); } catch { /* ignore */ }
                _logPath = "";
                _current = null;
            }
        }

        /// <summary>Bind log output to a per-port JSONL file.</summary>
        public static void SwitchToPort(int port, bool overwriteAtStart = true)
        {
            if (port <= 0) return;
            lock (Gate)
            {
                if (string.IsNullOrWhiteSpace(_logFolder))
                {
                    // Default location (best-effort)
                    try
                    {
                        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        _logFolder = Path.Combine(local, "RevitMCP", "progress");
                        Directory.CreateDirectory(_logFolder);
                    }
                    catch { _logFolder = ""; }
                }

                if (string.IsNullOrWhiteSpace(_logFolder)) return;

                _logPath = Path.Combine(_logFolder, $"progress_{port}.jsonl");
                if (overwriteAtStart)
                {
                    try { if (File.Exists(_logPath)) File.Delete(_logPath); } catch { /* ignore */ }
                }
            }
        }

        public static ProgressState? GetCurrent()
        {
            lock (Gate) { return _current?.Clone(); }
        }

        public static ProgressReporter Start(string jobId, string title, int total, TimeSpan tick)
        {
            var reporter = new ProgressReporter(jobId, title, total, tick, Publish);
            reporter.ReportNow("start");
            return reporter;
        }

        public static void Finish(string jobId, string message = "done")
        {
            if (string.IsNullOrWhiteSpace(jobId)) return;

            ProgressState? snapshot = null;
            lock (Gate)
            {
                if (_current == null) return;
                if (!string.Equals(_current.JobId, jobId, StringComparison.OrdinalIgnoreCase))
                    return;

                _current.Message = message ?? "done";
                if (_current.Total > 0) _current.Done = Math.Max(_current.Done, _current.Total);
                _current.UpdatedAtUtc = DateTime.UtcNow;
                snapshot = _current.Clone();
                _current = null;
            }

            if (snapshot != null) Publish(snapshot);
        }

        private static void Publish(ProgressState state)
        {
            if (state == null) return;

            ProgressState snapshot;
            lock (Gate)
            {
                _current = state.Clone();
                snapshot = _current.Clone();
            }

            TryAppendJsonl(snapshot);

            var h = Changed;
            if (h != null)
            {
                try { h(snapshot); } catch { /* ignore */ }
            }
        }

        private static void TryAppendJsonl(ProgressState s)
        {
            var path = "";
            lock (Gate) { path = _logPath; }
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var json = BuildJsonLine(s);
                File.AppendAllText(path, json, Utf8NoBom);
            }
            catch
            {
                // ignore
            }
        }

        private static string BuildJsonLine(ProgressState s)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');

            sb.Append("\"tsUtc\":\"").Append(EscapeJson(s.UpdatedAtUtc.ToString("o"))).Append("\",");
            sb.Append("\"jobId\":\"").Append(EscapeJson(s.JobId ?? "")).Append("\",");
            sb.Append("\"title\":\"").Append(EscapeJson(s.Title ?? "")).Append("\",");

            sb.Append("\"total\":").Append(s.Total.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"done\":").Append(s.Done.ToString(CultureInfo.InvariantCulture)).Append(',');

            if (s.IsIndeterminate) sb.Append("\"percent\":null,");
            else sb.Append("\"percent\":").Append(s.Percent.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');

            sb.Append("\"message\":\"").Append(EscapeJson(s.Message ?? "")).Append('"');

            sb.Append('}').Append('\n');
            return sb.ToString();
        }

        private static string EscapeJson(string? x)
        {
            if (string.IsNullOrEmpty(x)) return "";

            // Minimal escaping for JSON string values.
            var sb = new StringBuilder(x.Length + 8);
            foreach (var ch in x)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (char.IsControl(ch))
                        {
                            sb.Append("\\u");
                            sb.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

