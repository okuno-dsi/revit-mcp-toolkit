using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CodexGui;

public partial class CaptureConsentWindow : Window
{
    public sealed class CaptureItemVm
    {
        public string Path { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Risk { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
        public string RiskLabel => string.IsNullOrWhiteSpace(Risk) ? "risk: unknown" : ("risk: " + Risk);
    }

    private readonly ObservableCollection<CaptureItemVm> _items = new();
    private readonly int _port;
    private bool _busy;
    private string? _lastOutDir;

    public IReadOnlyList<string> ApprovedImagePaths { get; private set; } = Array.Empty<string>();

    public CaptureConsentWindow(int port)
    {
        _port = port;
        InitializeComponent();
        CapturesListBox.ItemsSource = _items;
        PortTextBox.Text = port > 0 ? port.ToString() : "";
    }

    private static string GetSelectedTarget(ComboBox cb)
    {
        try
        {
            if (cb.SelectedItem is ComboBoxItem item)
                return (item.Tag as string) ?? "active_dialogs";
        }
        catch { }
        return "active_dialogs";
    }

    private async void CaptureButton_OnClick(object sender, RoutedEventArgs e)
    {
        await CaptureAsync();
    }

    private async Task CaptureAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            SetStatus("Capturing...");
            CodexGuiLog.Info("Capture: start (port=" + _port + ")");
            await Dispatcher.InvokeAsync(() =>
            {
                CaptureButton.IsEnabled = false;
                ApproveButton.IsEnabled = false;
                OpenImageButton.IsEnabled = false;
                OpenFolderButton.IsEnabled = false;
            });

            var target = "";
            await Dispatcher.InvokeAsync(() => { target = GetSelectedTarget(TargetComboBox); });
            CodexGuiLog.Info("Capture: target=" + target);

            var result = await CallCaptureAsync(_port, target).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                _items.Clear();
                foreach (var c in result.Captures)
                {
                    // Default deny high-risk captures (unchecked).
                    var isHigh = string.Equals(c.Risk, "high", StringComparison.OrdinalIgnoreCase);
                    _items.Add(new CaptureItemVm
                    {
                        Path = c.Path,
                        Title = c.Title,
                        Risk = c.Risk,
                        IsSelected = !isHigh
                    });
                }
                if (_items.Count > 0)
                {
                    CapturesListBox.SelectedIndex = 0;
                }
            });

            _lastOutDir = result.OutDir;
            CodexGuiLog.Info("Capture: ok (count=" + result.Captures.Count + ", outDir=" + (_lastOutDir ?? "") + ")");
            SetStatus(result.Captures.Count == 0 ? "No captures." : $"OK ({result.Captures.Count})");
        }
        catch (Exception ex)
        {
            CodexGuiLog.Exception("Capture: exception", ex);
            SetStatus("ERR: " + ex.Message);
        }
        finally
        {
            _busy = false;
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    CaptureButton.IsEnabled = true;
                    ApproveButton.IsEnabled = _items.Count > 0;
                    OpenImageButton.IsEnabled = CapturesListBox.SelectedItem is CaptureItemVm;
                    OpenFolderButton.IsEnabled = true;
                });
            }
            catch { }
        }
    }

    private sealed class CaptureResult
    {
        public List<CaptureDto> Captures { get; set; } = new();
        public string OutDir { get; set; } = string.Empty;
    }

    private sealed class CaptureDto
    {
        public string Path { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Risk { get; set; } = string.Empty;
    }

    private static async Task<CaptureResult> CallCaptureAsync(int port, string target)
    {
        if (port <= 0) throw new InvalidOperationException("RevitMCP server port is not available.");

        var isScreen = string.Equals(target, "screen", StringComparison.OrdinalIgnoreCase);
        var method = isScreen ? "capture.screen" : "capture.revit";

        object paramsObj = isScreen
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?> { ["target"] = target };

        var body = new
        {
            jsonrpc = "2.0",
            id = "capui:" + Guid.NewGuid().ToString("N"),
            @params = paramsObj
        };
        var json = JsonSerializer.Serialize(body);

        using var client = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:" + port.ToString() + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };

        using var resp = await client.PostAsync("rpc/" + method, new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");

        using var doc = JsonDocument.Parse(txt);
        if (!doc.RootElement.TryGetProperty("result", out var result))
            throw new InvalidOperationException("No JSON-RPC result.");

        if (!result.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
        {
            var code = result.TryGetProperty("code", out var c) ? (c.ToString() ?? "") : "";
            var msg = result.TryGetProperty("msg", out var m) ? (m.ToString() ?? "") : "capture failed";
            var exitCode = result.TryGetProperty("exitCode", out var ec) ? (ec.ToString() ?? "") : "";
            var stderr = result.TryGetProperty("stderr", out var se) ? (se.ToString() ?? "") : "";

            var head = string.IsNullOrWhiteSpace(code) ? msg : (code + ": " + msg);
            if (!string.IsNullOrWhiteSpace(exitCode)) head += " (exitCode=" + exitCode + ")";
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                // Avoid flooding the UI with very large stderr.
                var s = stderr.Trim();
                if (s.Length > 400) s = s.Substring(0, 400) + "...";
                head += "\n" + s;
            }

            CodexGuiLog.Warn("Capture RPC failed: method=" + method + " code=" + code + " msg=" + msg + (string.IsNullOrWhiteSpace(exitCode) ? "" : (" exitCode=" + exitCode)));
            throw new InvalidOperationException(head);
        }

        var caps = new List<CaptureDto>();
        if (result.TryGetProperty("captures", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in arr.EnumerateArray())
            {
                var path = it.TryGetProperty("path", out var p) ? (p.ToString() ?? "") : "";
                var title = it.TryGetProperty("title", out var t) ? (t.ToString() ?? "") : "";
                var risk = it.TryGetProperty("risk", out var r) ? (r.ToString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(path)) continue;
                caps.Add(new CaptureDto { Path = path, Title = title, Risk = risk });
            }
        }

        var outDir = "";
        if (caps.Count > 0)
        {
            try
            {
                outDir = Path.GetDirectoryName(caps[0].Path) ?? "";
            }
            catch { outDir = ""; }
        }

        return new CaptureResult { Captures = caps, OutDir = outDir };
    }

    private void CapturesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (CapturesListBox.SelectedItem is not CaptureItemVm item) return;
            LoadPreview(item.Path);
            OpenImageButton.IsEnabled = true;
        }
        catch { }
    }

    private void LoadPreview(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                PreviewImage.Source = null;
                return;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            // Avoid decoding huge images into memory for preview; users can "Open" for full resolution.
            bmp.DecodePixelWidth = 1600;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            PreviewImage.Source = bmp;
        }
        catch
        {
            PreviewImage.Source = null;
        }
    }

    private void OpenImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CapturesListBox.SelectedItem is not CaptureItemVm item) return;
            if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path)) return;
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("Open failed: " + ex.Message);
        }
    }

    private void OpenFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = _lastOutDir;
            if (string.IsNullOrWhiteSpace(dir))
            {
                if (CapturesListBox.SelectedItem is CaptureItemVm item && !string.IsNullOrWhiteSpace(item.Path))
                {
                    try { dir = Path.GetDirectoryName(item.Path); } catch { dir = null; }
                }
            }
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("OpenFolder failed: " + ex.Message);
        }
    }

    private async void ApproveButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = await Dispatcher.InvokeAsync(() =>
            {
                return _items.Where(x => x.IsSelected).Select(x => (x.Path ?? "").Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            });

            if (selected.Count == 0)
            {
                SetStatus("No images selected.");
                return;
            }

            var hasHighRisk = await Dispatcher.InvokeAsync(() =>
            {
                return _items.Any(x => x.IsSelected && string.Equals(x.Risk, "high", StringComparison.OrdinalIgnoreCase));
            });

            if (hasHighRisk)
            {
                // Default deny high-risk sends unless explicitly confirmed.
                var r = MessageBox.Show(this,
                    "High-risk capture(s) selected (likely drawing/model canvas).\nSend anyway?",
                    "Confirm (High Risk)",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (r != MessageBoxResult.Yes)
                {
                    SetStatus("Denied (high-risk not approved).");
                    return;
                }
            }

            ApprovedImagePaths = selected;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus("Approve failed: " + ex.Message);
        }
    }

    private void SetStatus(string text)
    {
        try
        {
            Dispatcher.Invoke(() => { StatusTextBlock.Text = text ?? ""; });
        }
        catch { }
    }
}
