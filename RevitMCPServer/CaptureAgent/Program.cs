using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Tesseract;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            TryEnablePerMonitorDpiAwareness();

            var cmd = (args.Length > 0 ? args[0] : string.Empty).Trim();
            var opts = ParseArgs(args, startIndex: 1);

            if (string.IsNullOrWhiteSpace(cmd) || cmd.Equals("help", StringComparison.OrdinalIgnoreCase) || cmd.Equals("--help"))
            {
                WriteJson(new { ok = false, code = "USAGE", msg = "Usage: RevitMcp.CaptureAgent.exe list_windows|capture_window|capture_screen|capture_revit [--key value]" });
                return 2;
            }

            object result;
            switch (NormalizeCmd(cmd))
            {
                case "list_windows":
                    result = ListWindows(opts);
                    break;
                case "capture_window":
                    result = CaptureWindow(opts);
                    break;
                case "capture_screen":
                    result = CaptureScreen(opts);
                    break;
                case "capture_revit":
                    result = CaptureRevit(opts);
                    break;
                default:
                    WriteJson(new { ok = false, code = "UNKNOWN_CMD", msg = "Unknown command: " + cmd });
                    return 2;
            }

            TryAppendLog(cmd, opts, result);
            WriteJson(result);
            return 0;
        }
        catch (Exception ex)
        {
            var err = new { ok = false, code = "CAPTURE_AGENT_FAIL", msg = ex.Message };
            try { TryAppendLog("exception", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), err); } catch { }
            WriteJson(err);
            return 1;
        }
    }

    private static string NormalizeCmd(string cmd)
    {
        var c = (cmd ?? string.Empty).Trim().ToLowerInvariant();
        c = c.Replace('-', '_');
        return c;
    }

    private static Dictionary<string, string> ParseArgs(string[] args, int startIndex)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (args == null) return dict;
        for (int i = startIndex; i < args.Length; i++)
        {
            var a = args[i] ?? string.Empty;
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;
            var key = a.Substring(2).Trim();
            if (key.Length == 0) continue;

            var val = "true";
            if (i + 1 < args.Length && !(args[i + 1] ?? "").StartsWith("--", StringComparison.Ordinal))
            {
                val = args[i + 1] ?? "";
                i++;
            }
            dict[key] = val;
        }
        return dict;
    }

    private static object ListWindows(Dictionary<string, string> opts)
    {
        var processName = GetOpt(opts, "processName") ?? GetOpt(opts, "process") ?? "";
        var titleContains = GetOpt(opts, "titleContains") ?? GetOpt(opts, "title") ?? "";
        var visibleOnly = GetOptBool(opts, "visibleOnly", defaultValue: true);

        var windows = EnumerateTopLevelWindows(visibleOnly);
        if (!string.IsNullOrWhiteSpace(processName))
            windows = windows.Where(w => string.Equals(w.Process, processName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(titleContains))
            windows = windows.Where(w => (w.Title ?? "").IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        return new { ok = true, windows, warnings = Array.Empty<string>() };
    }

    private static object CaptureWindow(Dictionary<string, string> opts)
    {
        var hwndStr = GetOpt(opts, "hwnd") ?? "";
        if (!TryParseHwnd(hwndStr, out var hwnd))
            return new { ok = false, code = "INVALID_HWND", msg = "params.hwnd is required (hex like 0x00123456 or integer)." };

        var outDir = ResolveOutDir(opts);
        var preferPrintWindow = GetOptBool(opts, "preferPrintWindow", defaultValue: true);
        var includeSha256 = GetOptBool(opts, "includeSha256", defaultValue: false);
        var ocrEnabled = GetOptBool(opts, "ocr", defaultValue: false);
        var ocrLang = GetOpt(opts, "ocrLang");

        var win = GetWindowInfo(hwnd);
        if (win == null)
            return new { ok = false, code = "HWND_NOT_FOUND", msg = "Window not found or not accessible.", hwnd = ToHwndString(hwnd) };

        var captures = new List<object>();
        try
        {
            var path = CaptureWindowToFile(hwnd, win, outDir, preferPrintWindow, includeSha256, out var sha256);
            var ocr = TryOcrIfEnabled(path, ocrEnabled, ocrLang, win.Bounds);
            captures.Add(new
            {
                path,
                hwnd = win.Hwnd,
                process = win.Process,
                title = win.Title,
                className = win.ClassName,
                bounds = win.Bounds,
                risk = ClassifyRiskForWindowCapture(win),
                sha256 = sha256,
                ocr
            });
        }
        catch (Exception ex)
        {
            return new { ok = false, code = "CAPTURE_WINDOW_FAIL", msg = ex.Message, hwnd = win.Hwnd };
        }

        return new { ok = true, captures, warnings = Array.Empty<string>() };
    }

    private static object CaptureScreen(Dictionary<string, string> opts)
    {
        var outDir = ResolveOutDir(opts);
        var includeSha256 = GetOptBool(opts, "includeSha256", defaultValue: false);
        var ocrEnabled = GetOptBool(opts, "ocr", defaultValue: false);
        var ocrLang = GetOpt(opts, "ocrLang");
        int? monitorIndex = GetOptInt(opts, "monitorIndex");

        var screens = Screen.AllScreens ?? Array.Empty<Screen>();
        if (screens.Length == 0)
            return new { ok = false, code = "NO_SCREENS", msg = "No screens detected." };

        var captures = new List<object>();

        IEnumerable<(int idx, Screen s)> targets;
        if (monitorIndex.HasValue)
        {
            if (monitorIndex.Value < 0 || monitorIndex.Value >= screens.Length)
                return new { ok = false, code = "INVALID_MONITOR", msg = "monitorIndex out of range.", screenCount = screens.Length };
            targets = new[] { (monitorIndex.Value, screens[monitorIndex.Value]) };
        }
        else
        {
            targets = screens.Select((s, i) => (i, s));
        }

        foreach (var (idx, s) in targets)
        {
            try
            {
                var b = s.Bounds;
                var bounds = new BoundsDto { X = b.Left, Y = b.Top, W = b.Width, H = b.Height };
                var label = $"screen{idx}";
                var path = CaptureRectToFile(b.Left, b.Top, b.Width, b.Height, outDir, $"screen_{idx}", label, includeSha256, out var sha256);
                var ocr = TryOcrIfEnabled(path, ocrEnabled, ocrLang, bounds);
                captures.Add(new
                {
                    path,
                    monitorIndex = idx,
                    device = s.DeviceName ?? "",
                    bounds,
                    risk = "high",
                    sha256,
                    ocr
                });
            }
            catch
            {
                // Keep going (best-effort batch capture).
            }
        }

        return new { ok = true, captures, warnings = Array.Empty<string>() };
    }

    private static object CaptureRevit(Dictionary<string, string> opts)
    {
        var target = (GetOpt(opts, "target") ?? "active_dialogs").Trim();
        if (string.IsNullOrWhiteSpace(target)) target = "active_dialogs";
        target = target.ToLowerInvariant();

        var outDir = ResolveOutDir(opts);
        var preferPrintWindow = GetOptBool(opts, "preferPrintWindow", defaultValue: true);
        var includeSha256 = GetOptBool(opts, "includeSha256", defaultValue: false);
        var ocrEnabled = GetOptBool(opts, "ocr", defaultValue: false);
        var ocrLang = GetOpt(opts, "ocrLang");

        var revit = EnumerateTopLevelWindows(visibleOnly: true)
            .Where(w => string.Equals(w.Process, "Revit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (revit.Count == 0)
            return new { ok = false, code = "NO_REVIT_WINDOWS", msg = "No visible Revit windows found." };

        List<WindowInfo> targets;
        switch (target)
        {
            case "active_dialogs":
            case "dialogs":
                targets = revit.Where(w => string.Equals(w.ClassName, "#32770", StringComparison.Ordinal)).ToList();
                break;
            case "main":
                targets = new List<WindowInfo>();
                var main = PickLargestNonDialog(revit);
                if (main != null) targets.Add(main);
                break;
            case "floating_windows":
            case "floating":
                {
                    var main2 = PickLargestNonDialog(revit);
                    targets = revit
                        .Where(w => !string.Equals(w.ClassName, "#32770", StringComparison.Ordinal))
                        .Where(w => main2 == null || !string.Equals(w.Hwnd, main2.Hwnd, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    break;
                }
            case "all_visible":
            case "all":
                targets = revit;
                break;
            default:
                return new { ok = false, code = "INVALID_TARGET", msg = "target must be main|active_dialogs|floating_windows|all_visible" };
        }

        if (targets.Count == 0)
            return new { ok = true, captures = Array.Empty<object>(), warnings = new[] { "No matching Revit windows for target=" + target } };

        var captures = new List<object>();
        foreach (var w in targets)
        {
            try
            {
                if (!TryParseHwnd(w.Hwnd, out var hwnd)) continue;
                var path = CaptureWindowToFile(hwnd, w, outDir, preferPrintWindow, includeSha256, out var sha256);
                var ocr = TryOcrIfEnabled(path, ocrEnabled, ocrLang, w.Bounds);
                captures.Add(new
                {
                    path,
                    hwnd = w.Hwnd,
                    process = w.Process,
                    title = w.Title,
                    className = w.ClassName,
                    bounds = w.Bounds,
                    risk = ClassifyRiskForWindowCapture(w),
                    sha256,
                    ocr
                });
            }
            catch
            {
                // keep going
            }
        }

        return new { ok = true, captures, warnings = Array.Empty<string>() };
    }

    private static WindowInfo? PickLargestNonDialog(List<WindowInfo> windows)
    {
        try
        {
            return windows
                .Where(w => !string.Equals(w.ClassName, "#32770", StringComparison.Ordinal))
                .OrderByDescending(w => (long)w.Bounds.W * (long)w.Bounds.H)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveOutDir(Dictionary<string, string> opts)
    {
        var requested = GetOpt(opts, "outDir") ?? GetOpt(opts, "out") ?? "";
        if (!string.IsNullOrWhiteSpace(requested))
        {
            try
            {
                var full = Path.GetFullPath(requested);
                Directory.CreateDirectory(full);
                return full;
            }
            catch
            {
                // fall through
            }
        }

        var baseRoot = ResolveDefaultBaseRoot();
        var dir = Path.Combine(baseRoot, "captures");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ResolveDefaultBaseRoot()
    {
        // Prefer %LOCALAPPDATA%\RevitMCP to avoid permission issues.
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                var dir = Path.Combine(local, "RevitMCP");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        catch { }

        // Fallback: C:\RevitMcp (best-effort)
        try
        {
            var dir = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "RevitMcp");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private static string ResolveLogPath()
    {
        try
        {
            var baseRoot = ResolveDefaultBaseRoot();
            var dir = Path.Combine(baseRoot, "logs");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "capture.jsonl");
        }
        catch
        {
            return Path.Combine(AppContext.BaseDirectory, "capture.jsonl");
        }
    }

    // ---------------------- OCR ----------------------

    private sealed class OcrResult
    {
        public bool ok { get; set; }
        public string status { get; set; } = "";
        public string engine { get; set; } = "";
        public string text { get; set; } = "";
        public string error { get; set; } = "";
    }

    private static object TryOcrIfEnabled(string path, bool enabled, string? langTag, BoundsDto bounds)
    {
        if (!enabled) return new { ok = false, status = "disabled" };
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new { ok = false, status = "file_not_found" };

        // Skip OCR for very large images (likely model/sheet captures).
        try
        {
            long area = (long)Math.Max(1, bounds.W) * (long)Math.Max(1, bounds.H);
            if (area > 6_000_000) return new { ok = false, status = "skipped_large" };
        }
        catch { }

        var res = TryOcr(path, langTag);
        return new { ok = res.ok, status = res.status, engine = res.engine, text = res.text, error = res.error };
    }

    private static OcrResult TryOcr(string path, string? langTag)
    {
        try
        {
            var tessdataPath = ResolveTessdataPath();
            if (!Directory.Exists(tessdataPath))
            {
                return new OcrResult
                {
                    ok = false,
                    status = "tessdata_missing",
                    error = "tessdata directory not found: " + tessdataPath
                };
            }

            var lang = NormalizeTessLang(langTag);
            if (!HasTraineddata(tessdataPath, lang))
            {
                return new OcrResult
                {
                    ok = false,
                    status = "langdata_missing",
                    error = "traineddata missing for: " + lang
                };
            }

            using var engine = new TesseractEngine(tessdataPath, lang, EngineMode.Default);
            using var image = Pix.LoadFromFile(path);
            using var page = engine.Process(image);
            var text = page.GetText() ?? "";
            return new OcrResult
            {
                ok = true,
                status = "ok",
                engine = "tesseract:" + lang,
                text = text
            };
        }
        catch (Exception ex)
        {
            return new OcrResult { ok = false, status = "error", error = ex.Message };
        }
    }

    private static string ResolveTessdataPath()
    {
        try
        {
            return Path.Combine(AppContext.BaseDirectory, "tessdata");
        }
        catch
        {
            return "tessdata";
        }
    }

    private static string NormalizeTessLang(string? langTag)
    {
        var raw = (langTag ?? "").Trim();
        if (raw.Length == 0) return "jpn+eng";
        raw = raw.ToLowerInvariant();

        var parts = raw.Split(new[] { '+', ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "jpn+eng";

        var mapped = new List<string>();
        foreach (var partRaw in parts)
        {
            var part = partRaw.Trim();
            if (part.Length == 0) continue;
            if (part == "ja" || part.StartsWith("ja-", StringComparison.Ordinal) || part == "jpn" || part == "jp")
            {
                if (!mapped.Contains("jpn")) mapped.Add("jpn");
                continue;
            }
            if (part == "en" || part.StartsWith("en-", StringComparison.Ordinal) || part == "eng")
            {
                if (!mapped.Contains("eng")) mapped.Add("eng");
                continue;
            }
            if (!mapped.Contains(part)) mapped.Add(part);
        }

        if (mapped.Count == 0) return "jpn+eng";
        return string.Join("+", mapped);
    }

    private static bool HasTraineddata(string tessdataPath, string lang)
    {
        try
        {
            var parts = lang.Split(new[] { '+', ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            foreach (var part in parts)
            {
                var file = Path.Combine(tessdataPath, part + ".traineddata");
                if (!File.Exists(file)) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetOpt(Dictionary<string, string> opts, string key)
    {
        if (opts == null) return null;
        return opts.TryGetValue(key, out var v) ? v : null;
    }

    private static bool GetOptBool(Dictionary<string, string> opts, string key, bool defaultValue)
    {
        try
        {
            var raw = GetOpt(opts, key);
            if (raw == null) return defaultValue;
            var s = raw.Trim();
            if (s.Length == 0) return defaultValue;
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static int? GetOptInt(Dictionary<string, string> opts, string key)
    {
        try
        {
            var raw = GetOpt(opts, key);
            if (raw == null) return null;
            if (int.TryParse(raw.Trim(), out var i)) return i;
        }
        catch { }
        return null;
    }

    private static void WriteJson(object obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        Console.Out.Write(json);
    }

    private static void TryAppendLog(string cmd, Dictionary<string, string> opts, object result)
    {
        try
        {
            var path = ResolveLogPath();
            var rec = new
            {
                tsUtc = DateTimeOffset.UtcNow.ToString("o"),
                cmd = cmd,
                opts = opts,
                ok = GetOkFlag(result),
                resultSummary = SummarizeResult(result)
            };
            var line = JsonSerializer.Serialize(rec, JsonOpts) + "\n";
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch
        {
            // ignore
        }
    }

    private static bool? GetOkFlag(object result)
    {
        try
        {
            var pi = result.GetType().GetProperty("ok");
            if (pi == null) pi = result.GetType().GetProperty("Ok");
            if (pi == null) return null;
            var v = pi.GetValue(result);
            if (v is bool b) return b;
        }
        catch { }
        return null;
    }

    private static object SummarizeResult(object result)
    {
        try
        {
            var t = result.GetType();
            var ok = GetOkFlag(result);
            var count = 0;
            var piCaps = t.GetProperty("captures") ?? t.GetProperty("Captures");
            if (piCaps != null)
            {
                var v = piCaps.GetValue(result) as System.Collections.IEnumerable;
                if (v != null)
                {
                    foreach (var _ in v) count++;
                }
            }
            var piWins = t.GetProperty("windows") ?? t.GetProperty("Windows");
            if (piWins != null)
            {
                var v = piWins.GetValue(result) as System.Collections.IEnumerable;
                if (v != null)
                {
                    foreach (var _ in v) count++;
                }
            }
            return new { ok, count };
        }
        catch
        {
            return new { ok = (bool?)null, count = 0 };
        }
    }

    // ---------------------- Window Enumeration ----------------------

    private sealed class BoundsDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }

    private sealed class WindowInfo
    {
        public string Hwnd { get; set; } = "";
        public int Pid { get; set; }
        public string Process { get; set; } = "";
        public string Title { get; set; } = "";
        public string ClassName { get; set; } = "";
        public BoundsDto Bounds { get; set; } = new BoundsDto();
    }

    private static List<WindowInfo> EnumerateTopLevelWindows(bool visibleOnly)
    {
        var list = new List<WindowInfo>();
        EnumWindows((hWnd, _) =>
        {
            try
            {
                if (visibleOnly && !IsWindowVisible(hWnd)) return true;

                var title = GetWindowTextSafe(hWnd);
                var className = GetClassNameSafe(hWnd);
                var pid = GetPid(hWnd);
                var proc = GetProcessNameSafe(pid);
                var bounds = GetWindowBounds(hWnd);

                // Skip completely empty windows (title and class are empty and bounds are tiny).
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(className))
                {
                    if (bounds.W < 8 || bounds.H < 8) return true;
                }

                list.Add(new WindowInfo
                {
                    Hwnd = ToHwndString(hWnd),
                    Pid = pid,
                    Process = proc,
                    Title = title,
                    ClassName = className,
                    Bounds = bounds
                });
            }
            catch
            {
                // ignore and continue
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static WindowInfo? GetWindowInfo(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return null;
            var title = GetWindowTextSafe(hwnd);
            var className = GetClassNameSafe(hwnd);
            var pid = GetPid(hwnd);
            var proc = GetProcessNameSafe(pid);
            var bounds = GetWindowBounds(hwnd);
            return new WindowInfo
            {
                Hwnd = ToHwndString(hwnd),
                Pid = pid,
                Process = proc,
                Title = title,
                ClassName = className,
                Bounds = bounds
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetProcessNameSafe(int pid)
    {
        try
        {
            if (pid <= 0) return "";
            using var p = Process.GetProcessById(pid);
            return p.ProcessName ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static int GetPid(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            return pid;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetWindowTextSafe(IntPtr hwnd)
    {
        try
        {
            int len = GetWindowTextLength(hwnd);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 2);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string GetClassNameSafe(IntPtr hwnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static BoundsDto GetWindowBounds(IntPtr hwnd)
    {
        try
        {
            if (TryGetExtendedFrameBounds(hwnd, out var r))
            {
                return new BoundsDto { X = r.Left, Y = r.Top, W = Math.Max(1, r.Right - r.Left), H = Math.Max(1, r.Bottom - r.Top) };
            }
        }
        catch { }

        try
        {
            if (GetWindowRect(hwnd, out var r2))
            {
                return new BoundsDto { X = r2.Left, Y = r2.Top, W = Math.Max(1, r2.Right - r2.Left), H = Math.Max(1, r2.Bottom - r2.Top) };
            }
        }
        catch { }

        return new BoundsDto { X = 0, Y = 0, W = 1, H = 1 };
    }

    // ---------------------- Capture ----------------------

    private static string CaptureWindowToFile(IntPtr hwnd, WindowInfo win, string outDir, bool preferPrintWindow, bool includeSha256, out string? sha256)
    {
        sha256 = null;
        var safeTitle = MakeSafeFileName(win.Title);
        var ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var baseName = $"{ts}_{(string.IsNullOrWhiteSpace(win.Process) ? "window" : win.Process)}_{safeTitle}";
        if (baseName.Length > 120) baseName = baseName.Substring(0, 120);
        var path = Path.Combine(outDir, baseName + ".png");

        using var bmp = CaptureWindowBitmap(hwnd, win.Bounds, preferPrintWindow);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        if (includeSha256)
        {
            try { sha256 = ComputeSha256(path); } catch { sha256 = null; }
        }

        return path;
    }

    private static string CaptureRectToFile(int x, int y, int w, int h, string outDir, string kind, string title, bool includeSha256, out string? sha256)
    {
        sha256 = null;
        var safeTitle = MakeSafeFileName(title);
        var ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var baseName = $"{ts}_{kind}_{safeTitle}";
        if (baseName.Length > 120) baseName = baseName.Substring(0, 120);
        var path = Path.Combine(outDir, baseName + ".png");

        using var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(Math.Max(1, w), Math.Max(1, h)), CopyPixelOperation.SourceCopy);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        if (includeSha256)
        {
            try { sha256 = ComputeSha256(path); } catch { sha256 = null; }
        }

        return path;
    }

    private static Bitmap CaptureWindowBitmap(IntPtr hwnd, BoundsDto bounds, bool preferPrintWindow)
    {
        var w = Math.Max(1, bounds.W);
        var h = Math.Max(1, bounds.H);

        if (preferPrintWindow)
        {
            try
            {
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    try
                    {
                        // PW_RENDERFULLCONTENT = 0x00000002 (Win8+)
                        if (PrintWindow(hwnd, hdc, 0x00000002))
                            return bmp;
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
                bmp.Dispose();
            }
            catch
            {
                // fallback
            }
        }

        var bmp2 = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g2 = Graphics.FromImage(bmp2))
        {
            g2.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }
        return bmp2;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static string ClassifyRiskForWindowCapture(WindowInfo w)
    {
        try
        {
            if (w == null) return "high";
            if (string.Equals(w.ClassName, "#32770", StringComparison.Ordinal)) return "low";

            // Large Revit windows are likely model/sheet canvas -> high OCR risk.
            if (string.Equals(w.Process, "Revit", StringComparison.OrdinalIgnoreCase))
            {
                if (w.Bounds.W >= 800 && w.Bounds.H >= 600) return "high";
            }

            return "low";
        }
        catch
        {
            return "high";
        }
    }

    private static string MakeSafeFileName(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0) t = "untitled";
        foreach (var c in Path.GetInvalidFileNameChars())
            t = t.Replace(c, '_');
        t = t.Replace(' ', '_');
        if (t.Length > 80) t = t.Substring(0, 80);
        return t;
    }

    private static bool TryParseHwnd(string hwnd, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        try
        {
            var s = (hwnd ?? "").Trim();
            if (s.Length == 0) return false;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var u))
            {
                handle = new IntPtr(unchecked((long)u));
                return handle != IntPtr.Zero;
            }
            if (long.TryParse((hwnd ?? "").Trim(), out var l))
            {
                handle = new IntPtr(l);
                return handle != IntPtr.Zero;
            }
        }
        catch { }
        return false;
    }

    private static string ToHwndString(IntPtr hwnd)
    {
        try
        {
            var v = hwnd.ToInt64();
            return "0x" + v.ToString("X8");
        }
        catch
        {
            return "0x00000000";
        }
    }

    // ---------------------- DPI Awareness ----------------------

    private static void TryEnablePerMonitorDpiAwareness()
    {
        try
        {
            // PER_MONITOR_AWARE_V2
            SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch { }
    }

    // ---------------------- Win32 ----------------------

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static bool TryGetExtendedFrameBounds(IntPtr hwnd, out RECT rect)
    {
        rect = default;
        try
        {
            var hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
            return hr == 0;
        }
        catch
        {
            rect = default;
            return false;
        }
    }
}
