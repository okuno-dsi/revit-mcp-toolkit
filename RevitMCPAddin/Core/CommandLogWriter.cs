// ================================================================
// File: Core/CommandLogWriter.cs
// Purpose: JSONL と Markdown の二重書き込み。開始/停止をコマンドで制御
// ================================================================
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    public static class CommandLogWriter
    {
        private static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private static string _jsonlPath = "";
        private static string _journalPath = "";
        private static bool _enabled = false;

        public static bool Enabled => _enabled;
        public static string CurrentJsonlPath => _jsonlPath;
        public static string CurrentJournalPath => _journalPath;

        public static void Start(string dir, string? prefix = null)
        {
            _lock.EnterWriteLock();
            try
            {
                var date = DateTime.Now.ToString("yyyyMMdd");
                Directory.CreateDirectory(dir);

                var pre = string.IsNullOrWhiteSpace(prefix) ? "" : (prefix.Trim() + "_");
                _jsonlPath = Path.Combine(dir, $"{pre}OperationLog_{date}.jsonl");
                _journalPath = Path.Combine(dir, $"{pre}OperationJournal_{date}.md");

                if (!File.Exists(_journalPath))
                {
                    File.WriteAllText(_journalPath, "# Operation Journal\r\n\r\n", Encoding.UTF8);
                }

                _enabled = true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public static void Stop()
        {
            _lock.EnterWriteLock();
            try
            {
                _enabled = false;
                _jsonlPath = "";
                _journalPath = "";
            }
            finally { _lock.ExitWriteLock(); }
        }

        public static void Append(CommandLogEntry entry)
        {
            _lock.EnterWriteLock();
            try
            {
                if (!_enabled) return; // 記録OFF時は何もしない

                // JSONL
                var json = JsonConvert.SerializeObject(entry, Formatting.None);
                File.AppendAllText(_jsonlPath, json + "\r\n", Encoding.UTF8);

                // Journal（人間可読）
                var sb = new StringBuilder();
                sb.AppendLine($"## {entry.Ts:yyyy-MM-dd HH:mm:ss.fff zzz} — {entry.Command}");
                sb.AppendLine($"- {entry.Summary}");
                if (entry.AffectedElementIds.Any())
                    sb.AppendLine($"- Affected: {string.Join(", ", entry.AffectedElementIds)}");

                // 再実行スニペット
                sb.AppendLine();
                sb.AppendLine("```jsonc");
                sb.AppendLine(JsonConvert.SerializeObject(entry.Replay, Formatting.Indented));
                sb.AppendLine("```");

                // 参考：前値（人間の判断材料）
                if (entry.Before != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("_Before (hints for manual undo via Revit Undo):_");
                    sb.AppendLine("```jsonc");
                    sb.AppendLine(JsonConvert.SerializeObject(entry.Before, Formatting.Indented));
                    sb.AppendLine("```");
                }

                sb.AppendLine();
                File.AppendAllText(_journalPath, sb.ToString(), Encoding.UTF8);
            }
            finally { _lock.ExitWriteLock(); }
        }
    }
}
