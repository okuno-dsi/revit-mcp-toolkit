using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CodexGui;

internal static class CodexGuiLog
{
    private static readonly object Sync = new object();
    private static string? _logPath;
    private const long MaxBytes = 10L * 1024 * 1024; // 10 MB
    private const int MaxArchives = 5;
    private const int MaxEntryChars = 50_000;

    public static string LogPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_logPath)) return _logPath!;
            _logPath = GetDefaultLogPath();
            return _logPath!;
        }
    }

    public static void Init()
    {
        try
        {
            var path = LogPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            RotateIfTooLarge_NoThrow();
            Info("CodexGuiLog initialized. pid=" + Environment.ProcessId);
        }
        catch
        {
            // ignore
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    public static void Exception(string context, Exception? ex)
    {
        try
        {
            var m = (context ?? "Exception") + ": " + (ex?.GetType().FullName ?? "Unknown") + ": " + (ex?.Message ?? "");
            var detail = ex?.ToString() ?? "";
            if (detail.Length > MaxEntryChars) detail = detail.Substring(0, MaxEntryChars) + "...(truncated)";
            Write("EXC", m + Environment.NewLine + detail);
        }
        catch
        {
            // ignore
        }
    }

    private static void Write(string level, string msg)
    {
        try
        {
            if (msg != null && msg.Length > MaxEntryChars) msg = msg.Substring(0, MaxEntryChars) + "...(truncated)";
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level} {msg}";
            lock (Sync)
            {
                RotateIfTooLarge_NoThrow();
                File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void RotateIfTooLarge_NoThrow()
    {
        try { RotateIfTooLarge(); } catch { }
    }

    private static void RotateIfTooLarge()
    {
        var path = LogPath;
        if (!File.Exists(path)) return;
        var fi = new FileInfo(path);
        if (fi.Length < MaxBytes) return;

        var dir = fi.DirectoryName;
        if (string.IsNullOrWhiteSpace(dir)) return;

        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var rotated = Path.Combine(dir, $"codexgui_{ts}.log");
        try
        {
            File.Move(path, rotated);
        }
        catch
        {
            // If rotation fails, keep going (best-effort).
            return;
        }

        // Prune old archives.
        try
        {
            var archives = Directory.GetFiles(dir, "codexgui_*.log")
                .Select(p => new FileInfo(p))
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .ToList();

            for (var i = MaxArchives; i < archives.Count; i++)
            {
                try { archives[i].Delete(); } catch { }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string GetDefaultLogPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) local = ".";
        return Path.Combine(local, "RevitMCP", "logs", "codexgui.log");
    }
}
