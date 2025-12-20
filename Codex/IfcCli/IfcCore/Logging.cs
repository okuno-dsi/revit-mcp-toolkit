using System;
using System.IO;

namespace IfcCore;

public interface ILog
{
    void Info(string msg);
    void Warn(string msg);
    void Error(string msg, Exception? ex = null);
}

public sealed class FileLog : ILog, IDisposable
{
    private readonly StreamWriter _w;

    public FileLog(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _w = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
            NewLine = Environment.NewLine
        };
    }

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg, Exception? ex = null)
    {
        var m = ex == null ? msg : $"{msg} :: {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", m);
    }

    private void Write(string level, string msg)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level,-5} {msg}";
        _w.WriteLine(line);
    }

    public void Dispose() => _w.Dispose();
}

