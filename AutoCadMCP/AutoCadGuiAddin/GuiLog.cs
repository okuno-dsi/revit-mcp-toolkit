// ================================================================
// File: GuiLog.cs  – AutoCAD GUI ログ（Editor + ファイル）
// Target: net8.0-windows, AutoCAD GUI (acmgd)
// ================================================================
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application; // GUI Application

namespace MergeDwgsPlugin
{
    internal static class GuiLog
    {
        private static readonly object _gate = new();
        private static string? _logPathCached;

        public static string LogPath
        {
            get
            {
                if (_logPathCached != null) return _logPathCached;
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(baseDir, "AutoCadMCP", "gui");
                Directory.CreateDirectory(dir);
                _logPathCached = Path.Combine(dir, "addin.log");
                return _logPathCached!;
            }
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);
        public static void Step(string msg) => Write("STEP", msg); // 進捗表示用

        private static void Write(string level, string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {msg}";

            // 1) AutoCAD コマンドラインへ
            try
            {
                var ed = AcAp.DocumentManager?.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n" + line);
            }
            catch { /* Editor が未準備でも例外で落とさない */ }

            // 2) ファイルへ
            try
            {
                lock (_gate)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch { /* 書けなくても主処理は継続 */ }

            // 3) デバッグ出力
            Debug.WriteLine(line);
        }
    }
}
