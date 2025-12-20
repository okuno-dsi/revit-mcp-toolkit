// ================================================================
// File: Core/RevitLogger.cs  （ポート確定までメモリバッファ → 切替時に一括フラッシュ）
// Target : .NET Framework 4.8 / Revit 2023+
// Policy : Add-inフォルダへは書かない。%LOCALAPPDATA%\RevitMCP\logs へ統一。
//          ログ名は addin_<port>.log に統一（例: addin_5210.log）
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace RevitMCPAddin.Core
{
    public static class RevitLogger
    {
        private static readonly object _gate = new object();

        private static volatile bool _initialized = false;
        private static volatile bool _portBound = false;

        private static string _logDir = "";
        private static string _logPath = "";

        private static StreamWriter? _writer;

        // 早期ログバッファ（port 未確定の間はここに溜める）
        private static readonly List<string> _earlyBuffer = new List<string>(64);

        private static bool _enabled = true;

        public enum Level { Trace = 0, Info = 1, Warn = 2, Error = 3 }
        public static Level MinLevel { get; set; } = Level.Info;

        /// <summary>
        /// 初期化。ここではファイルは開かない／作らない（早期ログはメモリへ）
        /// </summary>
        public static void Init(string? preferredDir = null, bool deleteOldOnStartup = false, bool enabled = true)
        {
            if (_initialized) return;
            lock (_gate)
            {
                if (_initialized) return;

                _enabled = enabled;

                string dirEnv = Environment.GetEnvironmentVariable("REVITMCP_LOG_DIR") ?? "";
                string dir =
                    !string.IsNullOrWhiteSpace(dirEnv)
                        ? dirEnv
                        : (preferredDir ?? Path.Combine(
                              Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                              "RevitMCP", "logs"));

                _logDir = dir;
                try { Directory.CreateDirectory(_logDir); } catch { /* ignore */ }

                if (deleteOldOnStartup)
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(_logDir, "*.log"))
                            File.Delete(f);
                    }
                    catch { /* ignore */ }
                }

                // ここでは _writer を開かず、_portBound=false のまま
                _logPath = "";    // pendingファイルは作らない
                _initialized = true;
            }
        }

        public static string LogDirectory => _logDir;
        public static string LogPath => _logPath;

        /// <summary>
        /// ポート確定後に実ファイルを開く（addin_<port>.log）。早期バッファを一括書き出し。
        /// </summary>
        public static void SwitchToPortLog(int port, bool overwriteAtStart = true, long maxBytes = 5_000_000)
        {
            lock (_gate)
            {
                if (!_initialized) Init();

                try { Directory.CreateDirectory(_logDir); } catch { /* ignore */ }

                var newPath = Path.Combine(_logDir, $"addin_{port}.log");

                if (!overwriteAtStart)
                {
                    // Append運用にしたいケースだけ従来の簡易ローテーションを使う
                    try
                    {
                        var fi = new FileInfo(newPath);
                        if (fi.Exists && fi.Length > maxBytes)
                        {
                            var bak = newPath + ".1";
                            try { if (File.Exists(bak)) File.Delete(bak); } catch { }
                            File.Move(newPath, bak);
                        }
                    }
                    catch { /* ignore */ }
                }

                // 既存Writerを閉じる
                try { _writer?.Flush(); _writer?.Dispose(); } catch { }
                _writer = null;

                // 起動時は FileMode.Create で「上書き開始」
                var fileMode = overwriteAtStart ? FileMode.Create : FileMode.Append;

                try
                {
                    _writer = new StreamWriter(new FileStream(newPath, fileMode, FileAccess.Write, FileShare.ReadWrite))
                    {
                        AutoFlush = true,
                        NewLine = "\n"
                    };
                    _logPath = newPath;
                    _portBound = true;

                    // 早期ログを一括フラッシュ
                    if (_earlyBuffer.Count > 0)
                    {
                        foreach (var line in _earlyBuffer)
                            _writer.WriteLine(line);
                        _earlyBuffer.Clear();
                    }

                    var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var firstLine = $"[{ts}] {"INFO",5} SwitchToPortLog: port={port} path={_logPath}";
                    _writer.WriteLine(firstLine);
                    Debug.WriteLine(firstLine);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LOGGER] failed to switch log: {ex}");
                }
            }
        }

        private static void EnsureWriter() { /* 何もしない：SwitchToPortLogまで開かない */ }

        private static void Write(Level lv, string message)
        {
            if (!_enabled) return;
            if (lv < MinLevel) return;

            string line;
            try
            {
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                line = $"[{ts}] {lv.ToString().ToUpper(),5} {message}";
            }
            catch
            {
                // フォーマットに失敗しても最小情報で吐く
                line = $"[{DateTime.Now:O}] {lv.ToString().ToUpper()} {message}";
            }

            lock (_gate)
            {
                try
                {
                    if (_portBound && _writer != null)
                    {
                        _writer.WriteLine(line);
                        Debug.WriteLine(line);
                        return;
                    }

                    // まだポート未確定 → メモリへ積む
                    _earlyBuffer.Add(line);
                    Debug.WriteLine(line);
                }
                catch
                {
                    // 書けなくてもアプリ動作を阻害しない
                }
            }
        }

        private static string SafeFormat(string format, params object[]? args)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;
            if (args == null || args.Length == 0) return format;
            try { return string.Format(CultureInfo.InvariantCulture, format, args); }
            catch { return format; }
        }

        // ---- 基本API ----
        public static void Trace(string msg) => Write(Level.Trace, msg);
        public static void Trace(string format, params object[] args) => Write(Level.Trace, SafeFormat(format, args));

        public static void Info(string msg) => Write(Level.Info, msg);
        public static void Info(string format, params object[] args) => Write(Level.Info, SafeFormat(format, args));

        public static void Warn(string msg) => Write(Level.Warn, msg);
        public static void Warn(string format, params object[] args) => Write(Level.Warn, SafeFormat(format, args));

        public static void Error(string msg) => Write(Level.Error, msg);
        public static void Error(string format, params object[] args) => Write(Level.Error, SafeFormat(format, args));
        public static void Error(Exception? ex) => Write(Level.Error, ex?.ToString() ?? "Exception: <null>");
        public static void Error(string message, Exception? ex)
            => Write(Level.Error, $"{message}{(ex == null ? "" : $" | {ex.GetType().Name}: {ex.Message}\n{ex}")}");

        // 後方互換の別名
        public static void AppendTrace(string msg) => Trace(msg);
        public static void AppendInfo(string msg) => Info(msg);
        public static void AppendWarn(string msg) => Warn(msg);
        public static void AppendError(string msg) => Error(msg);
        public static void LogTrace(string msg) => Trace(msg);
        public static void LogInfo(string msg) => Info(msg);
        public static void LogWarn(string msg) => Warn(msg);
        public static void LogError(string msg) => Error(msg);

        public static void FlushAndClose()
        {
            lock (_gate)
            {
                try { _writer?.Flush(); _writer?.Dispose(); } catch { }
                _writer = null;
                _portBound = false;
            }
        }
    }
}
