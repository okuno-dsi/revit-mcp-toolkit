using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CodexGui;

public partial class MainWindow : Window
{
    private const string SessionsFileName = "CodexGuiSessions.json";
    internal const int MaxSessionNameLength = 28;
    private const string RevitIntroInstruction =
        "このディレクトリにあるREAD_FIRST_RevitMCP_EN.md を読んでRevitへの接続を準備してください。" +
        "一時保存データはすべてWorkフォルダ内にプロジェクト専用のフォルダを作成してそこに保存すること。" +
        "セキュリティ以上危険を及ぼす可能性のある操作やスクリプトの作成やコードの作成は行わないこと。" +
        "システムディレクトリやファイルには一切触れないこと。" +
        "Pythonスクリプトの作成を求められた場合は、必ず```python```のコードブロックで全文を出力すること。" +
        "ユーザーには可能な限り親切に対応すること。";

    private readonly List<SessionInfo> _sessions = new();
    private bool _isSending;
    private double _halPulseValue;
    private bool _halPulseIncreasing;
    private bool _isBusy;
    private readonly string _baseTitle = "Codex GUI";
    private ImageSource? _taskbarBusyOverlayIcon;

    // These static fields are used to pass per-call options to the
    // PowerShell process builder without complicating the signature.
    private static string? _currentModelForProcess;
    private static string? _currentReasoningEffortForProcess;
    private static bool _currentIsStatusRequestForProcess;
    private static string[]? _currentImagePathsForProcess;
    private static Process? _currentPwshProcess;
    private static bool _wasCancelledByUser;

    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex BracketAnsiRegex = new(@"\[[0-9;]*m", RegexOptions.Compiled);
    private static readonly SolidColorBrush BusyRedBrush = new(Color.FromRgb(0xCC, 0x33, 0x33));

    private readonly System.Windows.Threading.DispatcherTimer _busyTimer;
    private DateTime? _busyStartUtc;
    private int _busySpinnerIndex;
    private readonly Dictionary<string, string> _sessionLogPaths = new();
    private System.Windows.IDataObject? _startupClipboardSnapshot;
    private bool _startupClipboardRestored;

    // RevitMCP progress (reads %LOCALAPPDATA%\RevitMCP\progress\progress_<port>.jsonl)
    private readonly System.Windows.Threading.DispatcherTimer _revitProgressTimer;
    private bool _revitProgressRefreshing;
    private int _revitProgressPort;
    private string? _revitProgressLastLine;
    private RevitProgressSnapshot? _revitProgressLastSnapshot;
    private DateTime _lastProjectSyncUtc = DateTime.MinValue;
    private string? _lastProjectDocGuid;
    private string? _lastSavedPythonHash;
    private readonly object _pythonSaveLock = new();

    // Pending images to attach to the next Codex run (requires explicit consent via CaptureConsentWindow).
    private readonly List<string> _pendingImagePaths = new();

    // Model selector: MRU list + built-in presets (editable ComboBox).
    private const int MaxRecentModels = 12;
    private static readonly string[] BuiltInModelPresets =
    {
        // Keep this list small and focused. It is only for quick picks;
        // users can still type arbitrary model names in the editable ComboBox.
        "gpt-5.2-codex",
        "gpt-5.2",
        "gpt-5.1-codex",
        "gpt-5.1",
        "gpt-4.1",
        "gpt-4.1-mini",
        "o3",
        "o4-mini"
    };
    private readonly List<string> _recentModels = new();
    private readonly List<string> _codexConfigModelPresets = new();
    private readonly Dictionary<string, string> _codexConfigModelMigrations = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] BuiltInReasoningEffortPresets =
    {
        "low",
        "medium",
        "high",
        "xhigh"
    };

    private string? _codexConfigDefaultModel;
    private string? _codexConfigDefaultReasoningEffort;

    private enum HalPulsePhase
    {
        Idle,
        Awakening,
        Breathing,
        Sleeping
    }

    private HalPulsePhase _halPulsePhase = HalPulsePhase.Idle;
    private const double HalMinScale = 0.25;
    private const double HalMaxScale = 1.35;
    private const double HalBreathStep = 0.0396;       // 通常の呼吸スピード（従来比約20%高速）
    private const double HalAwakeStep = HalBreathStep * 2; // 目覚め（0→50%）
    private const double HalSleepStep = HalBreathStep * 4; // 眠り（50/100→0）

    // 会話欄を「落下」させてから赤→元の色で復活させるギミック用
    private enum FallAnimationPhase
    {
        Idle,
        Falling,
        WaitingAfterFall,
        FadeInRed,
        FadeToNormal
    }

    private sealed class FallingLine
    {
        public TextBlock Block { get; set; } = null!;
        public double StartY { get; set; }
        public double EndY { get; set; }
        public double DelaySeconds { get; set; }
        public double DurationSeconds { get; set; }
    }

    private readonly System.Windows.Threading.DispatcherTimer _fallTimer;
    private readonly List<FallingLine> _fallLines = new();
    private FallAnimationPhase _fallPhase = FallAnimationPhase.Idle;
    private DateTime _fallLastTick;
    private double _fallElapsedSeconds;
    private string? _fallOriginalText;
    private Brush? _fallOriginalForegroundBrush;
    private Color _fallOriginalForegroundColor;
    private readonly Random _fallRandom = new();

    // ウィンドウ全体を小刻みに揺らすためのタイマー
    private readonly System.Windows.Threading.DispatcherTimer _shakeTimer;
    private bool _isShaking;
    private DateTime _shakeStartUtc;
    private double _shakeOriginLeft;
    private double _shakeOriginTop;
    private const double ShakeDurationSeconds = 2.0;

    private const double FallDurationSeconds = 3.0;
    private const double FallWaitAfterSeconds = 3.0;
    private const double FallFadeInSeconds = 3.0;
    private const double FallFadeToNormalSeconds = 3.0;

    private double _traceExpandedHeight = 160;

    public sealed class CodexRunCompletedEventArgs : EventArgs
    {
        public string SessionId { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string PromptForCodex { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public bool WasCancelled { get; set; }
        public bool HadStreamingOutput { get; set; }
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset EndedUtc { get; set; }
    }

    public event EventHandler<CodexRunCompletedEventArgs>? CodexRunCompleted;

    public MainWindow()
    {
        InitializeComponent();
        _baseTitle = Title;
        try { _taskbarBusyOverlayIcon = TryFindResource("BusyOverlayIcon") as ImageSource; } catch { }
        Loaded += MainWindow_OnLoaded;
        PromptTextBox.KeyDown += PromptTextBox_OnKeyDown;
        KeyDown += MainWindow_OnKeyDown;
        MouseLeftButtonDown += Window_OnMouseLeftButtonDown;
        SourceInitialized += (_, _) => ApplyBackdropFromCurrentState();
        SourceInitialized += (_, _) => HookWndProcForResize();

        _busyTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _busyTimer.Tick += BusyTimer_OnTick;

        _fallTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _fallTimer.Tick += FallTimer_OnTick;

        _shakeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _shakeTimer.Tick += ShakeTimer_OnTick;

        _revitProgressTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _revitProgressTimer.Tick += async (_, _) => { await RefreshRevitProgressAsync(); };

        CodexRunCompleted += MainWindow_OnCodexRunCompleted;
    }

    private void UpdateTaskbarBusyState(bool isBusy)
    {
        try
        {
            if (MainTaskbarItemInfo == null) return;

            if (isBusy)
            {
                MainTaskbarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
                MainTaskbarItemInfo.Overlay = _taskbarBusyOverlayIcon;
            }
            else
            {
                MainTaskbarItemInfo.ProgressState = TaskbarItemProgressState.None;
                MainTaskbarItemInfo.Overlay = null;
            }
        }
        catch { /* ignore */ }
    }

    private void FlashTaskbarIfNotActive()
    {
        try
        {
            if (IsActive) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)),
                hwnd = hwnd,
                dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
                uCount = 3,
                dwTimeout = 0
            };

            FlashWindowEx(ref info);
        }
        catch { /* ignore */ }
    }

    private void TraceExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Restore a reasonable trace height and make the splitter available again.
            if (TraceSplitterRow != null) TraceSplitterRow.Height = new GridLength(6);
            if (TraceSplitter != null) TraceSplitter.Visibility = Visibility.Visible;

            if (TraceRow != null)
            {
                var h = _traceExpandedHeight;
                if (double.IsNaN(h) || double.IsInfinity(h) || h < 80) h = 160;
                TraceRow.Height = new GridLength(h);
            }
        }
        catch { }
    }

    private void TraceExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        try
        {
            // Remember last expanded height (if meaningful), then collapse to header-only.
            if (TraceRow != null && TraceRow.Height.IsAbsolute)
            {
                var h = TraceRow.Height.Value;
                if (!double.IsNaN(h) && !double.IsInfinity(h) && h >= 80)
                    _traceExpandedHeight = h;
            }

            if (TraceSplitter != null) TraceSplitter.Visibility = Visibility.Collapsed;
            if (TraceSplitterRow != null) TraceSplitterRow.Height = new GridLength(0);
            if (TraceRow != null) TraceRow.Height = GridLength.Auto;
        }
        catch { }
    }

    private void TraceSplitter_OnDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        try
        {
            if (TraceRow == null) return;
            var h = TraceRow.ActualHeight;
            if (!double.IsNaN(h) && !double.IsInfinity(h) && h >= 80)
                _traceExpandedHeight = h;
        }
        catch { }
    }

    public void SendPromptFromExternal(string prompt)
    {
        TrySendPromptFromExternal(prompt);
    }

    public bool TrySendPromptFromExternal(string prompt)
    {
        try
        {
            var text = (prompt ?? string.Empty).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(text)) return false;

            bool accepted = false;
            Dispatcher.Invoke(() =>
            {
                if (_isSending) return;

                // Ensure we have a session selected (create one if needed) to avoid modal dialogs.
                if (SessionComboBox.SelectedItem is not SessionInfo)
                {
                    if (_sessions.Count == 0)
                    {
                        var session = new SessionInfo
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = "Session 1",
                            CodexSessionId = null,
                            LastUsedUtc = DateTime.UtcNow
                        };
                        EnsurePromptHistoryInitialized(session);
                        _sessions.Add(session);
                        SaveSessions();
                        RefreshSessionComboBox();
                    }
                    else
                    {
                        SessionComboBox.SelectedItem = GetLastUsedSession() ?? _sessions[0];
                    }
                }

                if (SessionComboBox.SelectedItem is not SessionInfo) return;

                PromptTextBox.Text = text;
                PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
                PromptTextBox.Focus();
                accepted = true;
            });

            return accepted;
        }
        catch
        {
            return false;
        }
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSessions();
        RefreshSessionComboBox();
        _ = EnsureProjectSessionAsync(forceSelect: true);

        // フォント設定の初期値
        if (FontFamilyComboBox.Items.Count > 0)
        {
            FontFamilyComboBox.SelectedIndex = 0;
        }
        FontSizeTextBox.Text = FontSize.ToString("0");

        // デフォルト値（設定ファイルがない場合に使用）
        if (BgColorTextBox != null && string.IsNullOrWhiteSpace(BgColorTextBox.Text)) BgColorTextBox.Text = "#122445";
        if (FgColorTextBox != null && string.IsNullOrWhiteSpace(FgColorTextBox.Text)) FgColorTextBox.Text = "#C0C0C0";

        // UI 設定を読み込み（ウィンドウサイズ / 位置 / 色 / 不透明度）
        LoadUiSettings();

        // 現在の BG/FG とスライダー値を反映
        ApplyColorsButton_OnClick(this, new RoutedEventArgs());

        // Populate model / reasoning presets after settings are loaded.
        RefreshCodexConfigPresets();
        RefreshModelComboBoxItems();
        RefreshReasoningEffortComboBoxItems();

        // Busy インジケータ初期表示（停止中は 00:00 を赤で表示）
        BusyIndicatorTextBlock.Text = "00:00";
        BusyIndicatorTextBlock.Foreground = BusyRedBrush;

        // 起動時のクリップボード内容をスナップショットし、
        // GUI 起動の影響で消えてしまった場合にのみ一度だけ復元する。
        TrySnapshotStartupClipboard();
        ScheduleStartupClipboardRestore();

        // HAL 君風のタスクバー用アイコンを生成
        CreateHalLikeTaskbarIcon();

        // RevitMCP progress poller (file-based, no Revit API calls)
        try
        {
            _revitProgressTimer.Start();
            _ = RefreshRevitProgressAsync();
        }
        catch { }

        UpdateAttachedImagesIndicator();
    }

    private void UpdateAttachedImagesIndicator()
    {
        try
        {
            if (AttachedImagesCountTextBlock == null) return;
            if (_pendingImagePaths.Count <= 0)
            {
                AttachedImagesCountTextBlock.Text = "";
                AttachedImagesCountTextBlock.ToolTip = null;
                return;
            }

            AttachedImagesCountTextBlock.Text = $"img:{_pendingImagePaths.Count}";
            AttachedImagesCountTextBlock.ToolTip = string.Join(Environment.NewLine, _pendingImagePaths);
        }
        catch { }
    }

    private static string GetSessionsFilePath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, SessionsFileName);
    }

    internal static string TruncateForUi(string text, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        if (maxLen <= 0) return string.Empty;
        if (t.Length <= maxLen) return t;
        if (maxLen <= 1) return t.Substring(0, 1);
        return t.Substring(0, maxLen - 1) + "…";
    }

    private static string GetUiSettingsFilePath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "CodexGuiUiSettings.json");
    }

    private void LoadSessions()
    {
        _sessions.Clear();
        var path = GetSessionsFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var list = JsonSerializer.Deserialize<List<SessionInfo>>(json);
            if (list != null)
            {
                _sessions.AddRange(list);
                foreach (var s in _sessions)
                {
                    EnsurePromptHistoryInitialized(s);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"セッション情報の読み込みに失敗しました。\n{ex.Message}",
                "Codex GUI",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void LoadUiSettings()
    {
        try
        {
            var path = GetUiSettingsFilePath();
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<UiSettings>(json);
            if (settings == null)
            {
                return;
            }

            // ウィンドウ位置とサイズ
            if (settings.Width.HasValue && settings.Height.HasValue)
            {
                Width = settings.Width.Value;
                Height = settings.Height.Value;
            }

            if (settings.Left.HasValue && settings.Top.HasValue)
            {
                Left = settings.Left.Value;
                Top = settings.Top.Value;
            }

            if (settings.WindowState.HasValue)
            {
                WindowState = settings.WindowState.Value;
            }

            // 色と不透明度
            if (!string.IsNullOrWhiteSpace(settings.BgColorHex) && BgColorTextBox != null)
            {
                BgColorTextBox.Text = settings.BgColorHex;
            }

            if (!string.IsNullOrWhiteSpace(settings.FgColorHex) && FgColorTextBox != null)
            {
                FgColorTextBox.Text = settings.FgColorHex;
            }

            if (settings.Opacity.HasValue && OpacitySlider != null)
            {
                var v = settings.Opacity.Value;
                if (v < OpacitySlider.Minimum) v = OpacitySlider.Minimum;
                if (v > OpacitySlider.Maximum) v = OpacitySlider.Maximum;
                OpacitySlider.Value = v;
            }

            // Model MRU list (optional)
            _recentModels.Clear();
            if (settings.RecentModels != null)
            {
                foreach (var m in settings.RecentModels)
                {
                    var t = (m ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    if (_recentModels.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) continue;
                    _recentModels.Add(t);
                    if (_recentModels.Count >= MaxRecentModels) break;
                }
            }
        }
        catch
        {
            // 設定読み込み失敗は無視
        }
    }

    private void SaveSessions()
    {
        var path = GetSessionsFilePath();
        try
        {
            var json = JsonSerializer.Serialize(_sessions,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"セッション情報の保存に失敗しました。\n{ex.Message}",
                "Codex GUI",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RefreshSessionComboBox()
    {
        SessionComboBox.ItemsSource = null;
        SessionComboBox.ItemsSource = _sessions;
        if (_sessions.Count > 0)
        {
            var lastUsed = GetLastUsedSession();
            SessionComboBox.SelectedItem = lastUsed ?? _sessions[0];
        }
        else
        {
            if (ModelComboBox != null) ModelComboBox.Text = string.Empty;
            PromptHistoryListBox.ItemsSource = null;
            AllPromptHistoryListBox.ItemsSource = null;
        }
    }

    private async Task EnsureProjectSessionAsync(bool forceSelect)
    {
        try
        {
            // Throttle to avoid frequent RPC calls
            if ((DateTime.UtcNow - _lastProjectSyncUtc) < TimeSpan.FromSeconds(10))
            {
                return;
            }

            var ctx = await TryGetActiveProjectContextAsync();
            if (ctx == null || string.IsNullOrWhiteSpace(ctx.DocGuid))
            {
                return;
            }

            _lastProjectSyncUtc = DateTime.UtcNow;
            if (!string.Equals(_lastProjectDocGuid, ctx.DocGuid, StringComparison.OrdinalIgnoreCase))
            {
                _lastProjectDocGuid = ctx.DocGuid;
            }

            var session = _sessions.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.ProjectId)
                                                        && string.Equals(s.ProjectId, ctx.DocGuid, StringComparison.OrdinalIgnoreCase));
            if (session == null)
            {
                var name = BuildAutoSessionName(ctx.DocTitle, ctx.DocGuid);
                session = new SessionInfo
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    ProjectId = ctx.DocGuid,
                    ProjectName = ctx.DocTitle,
                    CodexSessionId = null,
                    LastUsedUtc = DateTime.UtcNow
                };
                EnsurePromptHistoryInitialized(session);
                _sessions.Add(session);
                SaveSessions();
                RefreshSessionComboBox();
                AppendSystemMessage($"プロジェクト用セッションを作成しました: {session.Name}");
            }
            else
            {
                // If this session looks auto-generated and the project title is known, refresh name.
                if (string.IsNullOrWhiteSpace(session.ProjectName) && !string.IsNullOrWhiteSpace(ctx.DocTitle))
                {
                    session.ProjectName = ctx.DocTitle;
                }
                if (!string.IsNullOrWhiteSpace(ctx.DocTitle) &&
                    (string.IsNullOrWhiteSpace(session.Name) ||
                     session.Name.StartsWith("Session ", StringComparison.OrdinalIgnoreCase)))
                {
                    session.Name = BuildAutoSessionName(ctx.DocTitle, ctx.DocGuid);
                    SaveSessions();
                    RefreshSessionComboBox();
                }
            }

            bool shouldSelect = forceSelect;
            if (!shouldSelect)
            {
                if (SessionComboBox.SelectedItem is not SessionInfo current)
                {
                    shouldSelect = true;
                }
                else if (!string.Equals(current.ProjectId ?? "", ctx.DocGuid ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    shouldSelect = true;
                }
            }

            if (shouldSelect && SessionComboBox.SelectedItem != session)
            {
                SessionComboBox.SelectedItem = session;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string BuildAutoSessionName(string? docTitle, string? docGuid)
    {
        var name = (docTitle ?? string.Empty).Trim();
        var guidShort = string.Empty;
        if (!string.IsNullOrWhiteSpace(docGuid))
        {
            guidShort = docGuid.Trim();
            if (guidShort.Length > 8) guidShort = guidShort.Substring(0, 8);
        }

        if (!string.IsNullOrWhiteSpace(guidShort))
        {
            if (string.IsNullOrWhiteSpace(name)) name = $"Project_{guidShort}";
            else name = $"{name}_{guidShort}";
        }

        if (string.IsNullOrWhiteSpace(name)) name = "Session";
        return TruncateForUi(name, MaxSessionNameLength);
    }

    private sealed class ProjectContext
    {
        public string? DocGuid { get; set; }
        public string? DocTitle { get; set; }
        public string? DocPath { get; set; }
    }

    private async Task<ProjectContext?> TryGetActiveProjectContextAsync()
    {
        try
        {
            if (!TryGetRevitMcpPort(out var port) || port <= 0) return null;

            var baseUrl = $"http://127.0.0.1:{port}";
            var endpoints = new[] { "/rpc", "/jsonrpc" };
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };

            foreach (var ep in endpoints)
            {
                try
                {
                    var url = baseUrl + ep;
                    var payload = new Dictionary<string, object?>
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = "codex-gui-context",
                        ["method"] = "help.get_context",
                        ["params"] = new Dictionary<string, object?>
                        {
                            ["includeSelectionIds"] = false,
                            ["maxSelectionIds"] = 0
                        }
                    };
                    var json = JsonSerializer.Serialize(payload);
                    using var resp = await client.PostAsync(url,
                        new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json"));
                    if (!resp.IsSuccessStatusCode) continue;

                    var text = await resp.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    var unwrapped = UnwrapJsonRpcResult(root);
                    if (TryReadBool(unwrapped, new[] { "queued" }) == true)
                    {
                        var jobId = TryReadString(unwrapped, new[] { "jobId" }) ?? TryReadString(unwrapped, new[] { "job_id" });
                        if (!string.IsNullOrWhiteSpace(jobId))
                        {
                            var jobResult = await PollJobResultAsync(client, baseUrl, jobId);
                            if (jobResult.HasValue)
                            {
                                unwrapped = jobResult.Value;
                            }
                        }
                    }
                    if (unwrapped.ValueKind != JsonValueKind.Object) continue;

                    var ctx = new ProjectContext
                    {
                        DocGuid = TryReadString(unwrapped, new[] { "data", "docGuid" })
                                  ?? TryReadString(unwrapped, new[] { "document", "docGuid" })
                                  ?? TryReadString(unwrapped, new[] { "project", "documentGuid" })
                                  ?? TryReadString(unwrapped, new[] { "project", "docGuid" })
                                  ?? TryReadString(unwrapped, new[] { "docGuid" }),
                        DocTitle = TryReadString(unwrapped, new[] { "data", "docTitle" })
                                   ?? TryReadString(unwrapped, new[] { "document", "docTitle" })
                                   ?? TryReadString(unwrapped, new[] { "project", "title" })
                                   ?? TryReadString(unwrapped, new[] { "project", "name" })
                                   ?? TryReadString(unwrapped, new[] { "docTitle" })
                                   ?? TryReadString(unwrapped, new[] { "title" }),
                        DocPath = TryReadString(unwrapped, new[] { "data", "docPath" })
                                  ?? TryReadString(unwrapped, new[] { "document", "docPath" })
                                  ?? TryReadString(unwrapped, new[] { "project", "docPath" })
                                  ?? TryReadString(unwrapped, new[] { "docPath" })
                    };

                    if (string.IsNullOrWhiteSpace(ctx.DocTitle) && !string.IsNullOrWhiteSpace(ctx.DocPath))
                    {
                        try
                        {
                            ctx.DocTitle = Path.GetFileNameWithoutExtension(ctx.DocPath);
                        }
                        catch { /* ignore */ }
                    }

                    if (string.IsNullOrWhiteSpace(ctx.DocGuid) && !string.IsNullOrWhiteSpace(ctx.DocPath))
                    {
                        try
                        {
                            ctx.DocGuid = "path-" + ShortHash(ctx.DocPath);
                        }
                        catch { /* ignore */ }
                    }

                    if (!string.IsNullOrWhiteSpace(ctx.DocGuid) || !string.IsNullOrWhiteSpace(ctx.DocTitle))
                    {
                        return ctx;
                    }
                }
                catch
                {
                    // try next endpoint
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private void MainWindow_OnCodexRunCompleted(object? sender, CodexRunCompletedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Output)) return;

        var session = _sessions.FirstOrDefault(s => s.Id == e.SessionId);
        var sessionName = session?.Name ?? e.SessionName;
        var projectName = session?.ProjectName;
        var projectId = session?.ProjectId;
        var output = e.Output;

        _ = Task.Run(async () =>
        {
            await TrySavePythonScriptFromOutputAsync(output, sessionName, projectName, projectId, e.Prompt);
        });
    }

    private async Task TrySavePythonScriptFromOutputAsync(string output, string? sessionName, string? projectName, string? projectId, string? prompt)
    {
        try
        {
            var code = ExtractPythonCodeBlock(output);
            if (string.IsNullOrWhiteSpace(code)) return;

            code = NormalizePythonCode(code);
            code = EnsureScriptMetadata(code, prompt, sessionName);
            var hash = ShortHash(code);
            lock (_pythonSaveLock)
            {
                if (string.Equals(_lastSavedPythonHash, hash, StringComparison.OrdinalIgnoreCase)) return;
                _lastSavedPythonHash = hash;
            }

            var docTitle = projectName;
            var docGuid = projectId;
            if (string.IsNullOrWhiteSpace(docTitle) || string.IsNullOrWhiteSpace(docGuid))
            {
                var ctx = await TryGetActiveProjectContextAsync();
                if (ctx != null)
                {
                    if (string.IsNullOrWhiteSpace(docTitle)) docTitle = ctx.DocTitle;
                    if (string.IsNullOrWhiteSpace(docGuid)) docGuid = ctx.DocGuid;
                }
            }

            if (string.IsNullOrWhiteSpace(docGuid))
            {
                var seed = string.IsNullOrWhiteSpace(docTitle) ? (sessionName ?? "unknown") : docTitle;
                docGuid = "session-" + ShortHash(seed);
            }

            var workProject = ResolveWorkProjectFolder(docTitle, docGuid);
            if (string.IsNullOrWhiteSpace(workProject)) return;

            var scriptDir = Path.Combine(workProject, "python_script");
            Directory.CreateDirectory(scriptDir);

            var fileName = $"codex_{DateTime.Now:yyyyMMdd_HHmmss}.py";
            var scriptPath = Path.Combine(scriptDir, fileName);
            File.WriteAllText(scriptPath, code, new UTF8Encoding(false));

            UpdatePythonRunnerInbox(scriptPath, sessionName, docTitle, docGuid);
            UpdatePythonRunnerLastScript(scriptPath);
        }
        catch
        {
            // ignore
        }
    }

    private static string NormalizePythonCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        var trimmed = code.Trim('\r', '\n');
        var dedented = DedentCommonLeadingWhitespace(trimmed);
        var normalized = dedented.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        return normalized;
    }

    private static string EnsureScriptMetadata(string code, string? prompt, string? sessionName)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;

        var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var scanLines = lines.Take(Math.Min(12, lines.Count)).ToList();
        if (scanLines.Any(l => l.IndexOf("@feature", StringComparison.OrdinalIgnoreCase) >= 0) ||
            scanLines.Any(l => l.IndexOf("@keywords", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return code;
        }

        var feature = ExtractFeatureFromPrompt(prompt, sessionName);
        var keywords = ExtractKeywordsFromPrompt(prompt);

        var insertAt = 0;
        if (lines.Count > 0 && lines[0].StartsWith("#!"))
        {
            insertAt = 1;
        }

        if (insertAt < lines.Count && IsEncodingLine(lines[insertAt]))
        {
            insertAt++;
        }

        var header = new List<string>
        {
            $"# @feature: {feature}",
            $"# @keywords: {keywords}"
        };

        lines.InsertRange(insertAt, header);
        if (insertAt + header.Count < lines.Count && !string.IsNullOrWhiteSpace(lines[insertAt + header.Count]))
        {
            lines.Insert(insertAt + header.Count, string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ExtractFeatureFromPrompt(string? prompt, string? sessionName)
    {
        var fallback = string.IsNullOrWhiteSpace(sessionName) ? "Codex Script" : sessionName!;
        if (string.IsNullOrWhiteSpace(prompt)) return fallback;

        var first = prompt
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

        if (string.IsNullOrWhiteSpace(first)) return fallback;

        first = Regex.Replace(first, "^[-*>\\s]+", string.Empty);
        if (first.Length > 120) first = first.Substring(0, 120);
        return string.IsNullOrWhiteSpace(first) ? fallback : first;
    }

    private static string ExtractKeywordsFromPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
        var rx = new Regex("(?:keywords?|キーワード)\\s*[:：]\\s*(?<kw>.+)", RegexOptions.IgnoreCase);
        foreach (var line in prompt.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var m = rx.Match(line);
            if (m.Success)
            {
                return m.Groups["kw"].Value.Trim();
            }
        }
        return string.Empty;
    }

    private static bool IsEncodingLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        return Regex.IsMatch(line, "^\\s*#.*coding[:=]\\s*[-\\w.]+", RegexOptions.IgnoreCase);
    }

    private static string? ExtractPythonCodeBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var pyFence = Regex.Match(text, "```(?:python|py)\\s*\\r?\\n(?<code>[\\s\\S]*?)```", RegexOptions.IgnoreCase);
        if (pyFence.Success)
        {
            return pyFence.Groups["code"].Value;
        }

        // Only accept explicitly labeled python fences to avoid mixing prose and code.
        return null;
    }

    // Intentionally no heuristic fallback: python code is only extracted from ```python fences.

    private static string? ResolveWorkProjectFolder(string? docTitle, string? docKey)
    {
        var root = ResolveWorkRoot();
        if (string.IsNullOrWhiteSpace(root)) return null;

        var workDir = Path.Combine(root, "Work");
        if (!Directory.Exists(workDir)) return null;

        var dirs = Directory.GetDirectories(workDir);
        if (!string.IsNullOrWhiteSpace(docKey))
        {
            var keyToken = "_" + docKey.Trim();
            var match = dirs.FirstOrDefault(d =>
                Path.GetFileName(d).EndsWith(keyToken, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        var safeTitle = SanitizePathSegment(docTitle);
        if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "Project";
        var safeKey = SanitizePathSegment(docKey);
        if (string.IsNullOrWhiteSpace(safeKey)) safeKey = "unknown";

        var created = Path.Combine(workDir, $"{safeTitle}_{safeKey}");
        Directory.CreateDirectory(created);
        return created;
    }

    private static string? ResolveWorkRoot()
    {
        var env1 = Environment.GetEnvironmentVariable("REVIT_MCP_WORK_ROOT");
        if (!string.IsNullOrWhiteSpace(env1) && Directory.Exists(env1)) return env1;

        var env2 = Environment.GetEnvironmentVariable("CODEX_MCP_ROOT");
        if (!string.IsNullOrWhiteSpace(env2) && Directory.Exists(env2)) return env2;

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs))
        {
            var p1 = Path.Combine(docs, "Codex_MCP", "Codex");
            if (Directory.Exists(p1)) return p1;

            var p2 = Path.Combine(docs, "Codex");
            if (Directory.Exists(p2)) return p2;
        }

        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Work")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string SanitizePathSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return cleaned.Trim();
    }

    private sealed class PythonRunnerPaths
    {
        public List<string>? roots { get; set; }
        public List<string>? files { get; set; }
        public List<string>? excluded { get; set; }
        public string? lastScript { get; set; }
    }

    private static void UpdatePythonRunnerLastScript(string scriptPath)
    {
        try
        {
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP");
            Directory.CreateDirectory(baseDir);
            var path = Path.Combine(baseDir, "python_runner_paths.json");

            PythonRunnerPaths cfg;
            if (File.Exists(path))
            {
                try
                {
                    cfg = JsonSerializer.Deserialize<PythonRunnerPaths>(File.ReadAllText(path, Encoding.UTF8)) ?? new PythonRunnerPaths();
                }
                catch
                {
                    cfg = new PythonRunnerPaths();
                }
            }
            else
            {
                cfg = new PythonRunnerPaths();
            }

            cfg.lastScript = scriptPath;
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch
        {
            // ignore
        }
    }

    private static void UpdatePythonRunnerInbox(string scriptPath, string? sessionName, string? docTitle, string? docGuid)
    {
        try
        {
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP");
            Directory.CreateDirectory(baseDir);
            var path = Path.Combine(baseDir, "python_runner_inbox.json");
            var obj = new Dictionary<string, object?>
            {
                ["path"] = scriptPath,
                ["source"] = "CodexGUI",
                ["sessionName"] = sessionName,
                ["docTitle"] = docTitle,
                ["docGuid"] = docGuid,
                ["savedAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch
        {
            // ignore
        }
    }

    private static string DedentCommonLeadingWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int min = int.MaxValue;
        foreach (var raw in lines)
        {
            var line = raw ?? "";
            if (line.Trim().Length == 0) continue;
            int count = 0;
            while (count < line.Length && (line[count] == ' ' || line[count] == '\t')) count++;
            if (count < min) min = count;
            if (min == 0) break;
        }
        if (min == int.MaxValue || min == 0) return string.Join("\n", lines).Replace("\n", Environment.NewLine);
        var adjusted = lines.Select(l => (l ?? "").Length >= min ? (l ?? "").Substring(min) : (l ?? ""));
        return string.Join(Environment.NewLine, adjusted);
    }

    private static async Task<JsonElement?> PollJobResultAsync(System.Net.Http.HttpClient client, string baseUrl, string jobId)
    {
        try
        {
            var url = baseUrl.TrimEnd('/') + "/job/" + jobId;
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                using var resp = await client.GetAsync(url);
                var code = (int)resp.StatusCode;
                if (code == 202 || code == 204)
                {
                    await Task.Delay(300);
                    continue;
                }
                if (!resp.IsSuccessStatusCode) return null;
                var text = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(text)) return null;
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var state = TryReadString(root, new[] { "state" }) ?? "";
                if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    var resultJson = TryReadString(root, new[] { "result_json" });
                    if (string.IsNullOrWhiteSpace(resultJson)) return root;
                    using var inner = JsonDocument.Parse(resultJson);
                    return UnwrapJsonRpcResult(inner.RootElement);
                }
                if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state, "TIMEOUT", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state, "DEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                await Task.Delay(300);
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static bool? TryReadBool(JsonElement root, string[] path)
    {
        try
        {
            var cur = root;
            foreach (var p in path)
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out var next))
                    return null;
                cur = next;
            }
            if (cur.ValueKind == JsonValueKind.True) return true;
            if (cur.ValueKind == JsonValueKind.False) return false;
            if (cur.ValueKind == JsonValueKind.String)
            {
                var s = cur.GetString();
                if (bool.TryParse(s, out var b)) return b;
            }
        }
        catch { }
        return null;
    }

    private static JsonElement UnwrapJsonRpcResult(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("result", out var r1) &&
            r1.ValueKind == JsonValueKind.Object)
        {
            if (r1.TryGetProperty("result", out var r2) && r2.ValueKind == JsonValueKind.Object)
                return r2;
            return r1;
        }
        return root;
    }

    private static string? TryReadString(JsonElement root, string[] path)
    {
        try
        {
            var cur = root;
            foreach (var p in path)
            {
                if (cur.ValueKind != JsonValueKind.Object) return null;
                if (!cur.TryGetProperty(p, out var next)) return null;
                cur = next;
            }
            return (cur.ToString() ?? "").Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string ShortHash(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "00000000";
        try
        {
            using var sha1 = SHA1.Create();
            var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(text));
            var hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            return hex.Length >= 8 ? hex.Substring(0, 8) : hex;
        }
        catch
        {
            return "00000000";
        }
    }

    private SessionInfo? GetLastUsedSession()
    {
        SessionInfo? best = null;
        foreach (var s in _sessions)
        {
            if (best == null || s.LastUsedUtc > best.LastUsedUtc)
            {
                best = s;
            }
        }

        return best;
    }

    private async void NewSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        ProjectContext? ctx = null;
        try
        {
            ctx = await TryGetActiveProjectContextAsync();
        }
        catch { /* ignore */ }

        var name = $"Session {_sessions.Count + 1}";
        var projectId = ctx?.DocGuid;
        var projectName = ctx?.DocTitle;
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            name = BuildAutoSessionName(projectName, projectId);
        }

        var session = new SessionInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ProjectId = projectId,
            ProjectName = projectName,
            CodexSessionId = null,
            LastUsedUtc = DateTime.UtcNow
        };
        EnsurePromptHistoryInitialized(session);
        _sessions.Add(session);
        SaveSessions();
        RefreshSessionComboBox();
        AppendSystemMessage($"新しいセッションを作成しました: {session.Name}");
    }

    private void SessionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Commit edits for the previous session (model field) before switching.
        try
        {
            if (e.RemovedItems != null && e.RemovedItems.Count > 0 && e.RemovedItems[0] is SessionInfo prev)
            {
                ApplyModelFromUiToSession(prev);
                ApplyReasoningEffortFromUiToSession(prev);
                SaveSessions();
                RememberRecentModel(prev.Model);
            }
        }
        catch { }

        if (SessionComboBox.SelectedItem is SessionInfo session)
        {
            if (ModelComboBox != null) ModelComboBox.Text = session.Model ?? string.Empty;
            if (ReasoningEffortComboBox != null) ReasoningEffortComboBox.Text = session.ReasoningEffort ?? string.Empty;
            if (SessionIdTextBlock != null)
            {
                SessionIdTextBlock.Text = session.Id;
            }
            RefreshPromptHistory(session);
        }
        else
        {
            if (ModelComboBox != null) ModelComboBox.Text = string.Empty;
            if (ReasoningEffortComboBox != null) ReasoningEffortComboBox.Text = string.Empty;
            if (SessionIdTextBlock != null)
            {
                SessionIdTextBlock.Text = string.Empty;
            }
            RefreshPromptHistory(null);
        }
    }

    private void RenameSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SessionComboBox.SelectedItem is not SessionInfo session)
        {
            MessageBox.Show(this, "変更するセッションを選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SimpleTextInputWindow("セッション名の変更", "新しいセッション名:", session.Name);
        if (dialog.ShowDialog() == true)
        {
            session.Name = dialog.ResultText;
            session.LastUsedUtc = DateTime.UtcNow;
            SaveSessions();
            RefreshSessionComboBox();
        }
    }

    private void DeleteSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SessionComboBox.SelectedItem is not SessionInfo session)
        {
            MessageBox.Show(this, "削除するセッションを選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(this,
            $"セッション '{session.Name}' を削除しますか？\n（この操作は元に戻せません）",
            "Codex GUI",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _sessions.Remove(session);
        SaveSessions();
        RefreshSessionComboBox();
        AppendSystemMessage($"セッション '{session.Name}' を削除しました。");
    }

    private void PromptTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            MainWindow_OnKeyDown(this, e);
            return;
        }

        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            SendButton_OnClick(this, new RoutedEventArgs());
        }
    }

    private void ApplyModelFromUiToSession(SessionInfo session)
    {
        var modelText = (ModelComboBox?.Text ?? string.Empty).Trim();
        session.Model = string.IsNullOrWhiteSpace(modelText) ? null : modelText;
    }

    private void ApplyReasoningEffortFromUiToSession(SessionInfo session)
    {
        var effortText = (ReasoningEffortComboBox?.Text ?? string.Empty).Trim();
        session.ReasoningEffort = string.IsNullOrWhiteSpace(effortText) ? null : effortText;
    }

    private void ModelComboBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        try
        {
            if (SessionComboBox.SelectedItem is not SessionInfo session) return;
            ApplyModelFromUiToSession(session);
            SaveSessions();
            RememberRecentModel(session.Model);
        }
        catch { }
    }

    private void ReasoningEffortComboBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        try
        {
            if (SessionComboBox.SelectedItem is not SessionInfo session) return;
            ApplyReasoningEffortFromUiToSession(session);
            SaveSessions();
        }
        catch { }
    }

    private void ClearModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ModelComboBox != null) ModelComboBox.Text = string.Empty;
            if (SessionComboBox.SelectedItem is SessionInfo session)
            {
                session.Model = null;
                SaveSessions();
            }
        }
        catch { }
    }

    private void ClearReasoningEffortButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ReasoningEffortComboBox != null) ReasoningEffortComboBox.Text = string.Empty;
            if (SessionComboBox.SelectedItem is SessionInfo session)
            {
                session.ReasoningEffort = null;
                SaveSessions();
            }
        }
        catch { }
    }

    private void RefreshModelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshCodexConfigPresets();
            RefreshModelComboBoxItems();
            RefreshReasoningEffortComboBoxItems();
        }
        catch { }
    }

    private void RefreshModelComboBoxItems()
    {
        try
        {
            if (ModelComboBox == null) return;

            var currentText = ModelComboBox.Text ?? string.Empty;
            var items = new List<string>();

            void Add(string? m)
            {
                var t = (m ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(t)) return;
                if (items.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) return;
                items.Add(t);
            }

            foreach (var m in _recentModels) Add(m);
            foreach (var m in _codexConfigModelPresets) Add(m);
            foreach (var m in BuiltInModelPresets) Add(m);

            ModelComboBox.ItemsSource = items;
            ModelComboBox.Text = currentText;
        }
        catch { }
    }

    private void RefreshReasoningEffortComboBoxItems()
    {
        try
        {
            if (ReasoningEffortComboBox == null) return;

            var currentText = ReasoningEffortComboBox.Text ?? string.Empty;
            var items = new List<string>();

            void Add(string? e)
            {
                var t = (e ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(t)) return;
                if (items.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) return;
                items.Add(t);
            }

            // Put the current Codex config default first (if present), so it is discoverable.
            Add(_codexConfigDefaultReasoningEffort);
            foreach (var e in BuiltInReasoningEffortPresets) Add(e);

            ReasoningEffortComboBox.ItemsSource = items;
            ReasoningEffortComboBox.Text = currentText;
        }
        catch { }
    }

    private void RefreshCodexConfigPresets()
    {
        try
        {
            _codexConfigModelPresets.Clear();
            _codexConfigModelMigrations.Clear();
            _codexConfigDefaultModel = null;
            _codexConfigDefaultReasoningEffort = null;

            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "config.toml");

            if (!File.Exists(configPath))
            {
                UpdateModelAndReasoningTooltips();
                return;
            }

            string? currentSection = null;

            foreach (var rawLine in File.ReadAllLines(configPath, Encoding.UTF8))
            {
                var line = StripTomlComment(rawLine).Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = UnquoteTomlString(line.Substring(0, eq).Trim());
                var value = UnquoteTomlString(line.Substring(eq + 1).Trim());

                if (string.IsNullOrWhiteSpace(key)) continue;

                if (string.IsNullOrWhiteSpace(currentSection))
                {
                    if (string.Equals(key, "model", StringComparison.OrdinalIgnoreCase))
                    {
                        _codexConfigDefaultModel = value;
                        AddModelPreset(value);
                    }
                    else if (string.Equals(key, "model_reasoning_effort", StringComparison.OrdinalIgnoreCase))
                    {
                        _codexConfigDefaultReasoningEffort = value;
                    }
                }
                else if (string.Equals(currentSection, "notice.model_migrations", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        _codexConfigModelMigrations[key] = value;
                        AddModelPreset(key);
                        AddModelPreset(value);
                    }
                }
            }

            // If a default model has a migration target, include it explicitly (discoverability).
            if (!string.IsNullOrWhiteSpace(_codexConfigDefaultModel)
                && _codexConfigModelMigrations.TryGetValue(_codexConfigDefaultModel, out var migrated))
            {
                AddModelPreset(migrated);
            }

            // Best-effort: add models that Codex has actually used recently (from ~/.codex/sessions/**.jsonl).
            // This is more reliable than hardcoding, and avoids depending on remote "list models" APIs.
            AddModelsFromCodexSessionLogs(maxFiles: 24, maxLinesPerFile: 600);

            UpdateModelAndReasoningTooltips();
        }
        catch
        {
            // ignore
        }

        string StripTomlComment(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var inQuote = false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '"' && (i == 0 || s[i - 1] != '\\'))
                {
                    inQuote = !inQuote;
                }
                if (!inQuote && c == '#')
                {
                    return s.Substring(0, i);
                }
            }
            return s;
        }

        string UnquoteTomlString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var t = s.Trim();
            if (t.StartsWith("\"", StringComparison.Ordinal) && t.EndsWith("\"", StringComparison.Ordinal) && t.Length >= 2)
            {
                t = t.Substring(1, t.Length - 2);
                t = t.Replace("\\\"", "\"");
            }
            return t.Trim();
        }

        void AddModelPreset(string? model)
        {
            var t = (model ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(t)) return;
            if (_codexConfigModelPresets.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) return;
            _codexConfigModelPresets.Add(t);
        }

        void AddModelsFromCodexSessionLogs(int maxFiles, int maxLinesPerFile)
        {
            try
            {
                var sessionsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex",
                    "sessions");

                if (!Directory.Exists(sessionsRoot)) return;

                var modelRegex = new Regex("\"model\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

                var files = new DirectoryInfo(sessionsRoot)
                    .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(Math.Max(1, maxFiles))
                    .ToList();

                foreach (var f in files)
                {
                    try
                    {
                        using var sr = new StreamReader(f.FullName, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                        for (var i = 0; i < Math.Max(1, maxLinesPerFile); i++)
                        {
                            var line = sr.ReadLine();
                            if (line == null) break;
                            var m = modelRegex.Match(line);
                            if (m.Success)
                            {
                                var value = m.Groups[1].Value?.Trim();
                                AddModelPreset(value);
                            }
                        }
                    }
                    catch
                    {
                        // ignore and continue
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private void UpdateModelAndReasoningTooltips()
    {
        try
        {
            if (ModelComboBox != null)
            {
                var sb = new StringBuilder();
                sb.Append("Codex モデル名（空欄=デフォルト）。");
                if (!string.IsNullOrWhiteSpace(_codexConfigDefaultModel))
                {
                    sb.AppendLine();
                    sb.Append("Codex config default: ").Append(_codexConfigDefaultModel);
                    if (_codexConfigModelMigrations.TryGetValue(_codexConfigDefaultModel, out var migrated) &&
                        !string.IsNullOrWhiteSpace(migrated) &&
                        !string.Equals(migrated, _codexConfigDefaultModel, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append(" → ").Append(migrated);
                    }
                }
                ModelComboBox.ToolTip = sb.ToString();
            }

            if (ReasoningEffortComboBox != null)
            {
                var sb = new StringBuilder();
                sb.Append("model_reasoning_effort（空欄=デフォルト）。");
                if (!string.IsNullOrWhiteSpace(_codexConfigDefaultReasoningEffort))
                {
                    sb.AppendLine();
                    sb.Append("Codex config default: ").Append(_codexConfigDefaultReasoningEffort);
                }
                ReasoningEffortComboBox.ToolTip = sb.ToString();
            }
        }
        catch
        {
            // ignore
        }
    }

    private void RememberRecentModel(string? model)
    {
        try
        {
            var t = (model ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(t)) return;

            _recentModels.RemoveAll(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
            _recentModels.Insert(0, t);
            if (_recentModels.Count > MaxRecentModels)
            {
                _recentModels.RemoveRange(MaxRecentModels, _recentModels.Count - MaxRecentModels);
            }

            RefreshModelComboBoxItems();
            SaveUiSettings();
        }
        catch { }
    }

    private void ApplyFontButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (FontFamilyComboBox.SelectedItem is ComboBoxItem item &&
            item.Content is string familyName &&
            !string.IsNullOrWhiteSpace(familyName))
        {
            try
            {
                FontFamily = new FontFamily(familyName);
            }
            catch
            {
                MessageBox.Show(this, "フォント名が正しくありません。", "Codex GUI",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        if (double.TryParse(FontSizeTextBox.Text, out var size))
        {
            if (size >= 6 && size <= 72)
            {
                FontSize = size;
            }
            else
            {
                MessageBox.Show(this, "フォントサイズは 6～72 の範囲で指定してください。", "Codex GUI",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void MainWindow_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;

        if (_isSending && _currentPwshProcess is { HasExited: false })
        {
            try
            {
                _wasCancelledByUser = true;
                _currentPwshProcess.Kill(true);
            }
            catch
            {
                // ignore
            }
        }
        else
        {
            PromptTextBox.Clear();
        }
    }

    private static void EnsurePromptHistoryInitialized(SessionInfo session)
    {
        session.PromptHistory ??= new List<string>();
    }

    private void RefreshPromptHistory(SessionInfo? session)
    {
        if (session == null)
        {
            PromptHistoryListBox.ItemsSource = null;
            AllPromptHistoryListBox.ItemsSource = null;
            return;
        }

        EnsurePromptHistoryInitialized(session);
        PromptHistoryListBox.ItemsSource = null;
        PromptHistoryListBox.ItemsSource = session.PromptHistory;
        RefreshAllPromptHistory();
    }

    private void RefreshAllPromptHistory()
    {
        var items = new List<PromptHistoryItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in _sessions.OrderByDescending(x => x.LastUsedUtc))
        {
            if (s.PromptHistory == null) continue;
            foreach (var p in s.PromptHistory)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var key = p.Trim();
                if (!seen.Add(key)) continue;
                var display = $"{TruncateForUi(s.Name, 16)}: {TruncateForUi(key, 80)}";
                items.Add(new PromptHistoryItem
                {
                    Prompt = key,
                    SessionId = s.Id,
                    SessionName = s.Name,
                    Display = display
                });
            }
        }

        AllPromptHistoryListBox.ItemsSource = null;
        AllPromptHistoryListBox.ItemsSource = items;
    }

    private bool IsAllPromptTabSelected()
    {
        try
        {
            if (PromptHistoryTab?.SelectedIndex == 1) return true;
        }
        catch { /* ignore */ }
        return false;
    }

    private IEnumerable<string> EnumerateSelectedPrompts()
    {
        if (IsAllPromptTabSelected())
        {
            foreach (var item in AllPromptHistoryListBox.SelectedItems)
            {
                if (item is PromptHistoryItem phi && !string.IsNullOrWhiteSpace(phi.Prompt))
                    yield return phi.Prompt.TrimEnd('\r', '\n');
            }
            yield break;
        }

        foreach (var item in PromptHistoryListBox.SelectedItems)
        {
            if (item is string prompt && !string.IsNullOrWhiteSpace(prompt))
                yield return prompt.TrimEnd('\r', '\n');
        }
    }

    private static void TrimPromptHistory(SessionInfo session, int maxCount)
    {
        EnsurePromptHistoryInitialized(session);
        if (maxCount <= 0)
        {
            session.PromptHistory.Clear();
            return;
        }

        var list = session.PromptHistory;
        if (list.Count <= maxCount)
        {
            return;
        }

        var removeCount = list.Count - maxCount;
        list.RemoveRange(0, removeCount);
    }

    private void StartBusyIndicator()
    {
        _isBusy = true;
        _busyStartUtc = DateTime.UtcNow;
        _busySpinnerIndex = 0;
        UpdateTaskbarBusyState(true);

        // 経過時間を赤文字で表示（スピナーは使用しない）
        BusyIndicatorTextBlock.Foreground = BusyRedBrush;
        BusyIndicatorTextBlock.Text = "00:00";

        // HAL 目を「目覚め」状態からスタート（0%→50% を少し速く）
        _halPulsePhase = HalPulsePhase.Awakening;
        _halPulseIncreasing = true;
        _halPulseValue = 0.0;

        if (HalRedEllipse != null)
        {
            HalRedEllipse.Visibility = Visibility.Visible;
            HalRedEllipse.Opacity = 1.0;
            ApplyHalScaleFromIntensity(_halPulseValue);
        }

        if (!_busyTimer.IsEnabled)
        {
            _busyTimer.Start();
        }
    }

    private void StopBusyIndicator()
    {
        _isBusy = false;
        _busyStartUtc = null;
        UpdateTaskbarBusyState(false);
        FlashTaskbarIfNotActive();

        // 停止中は 00:00 を赤文字で表示
        BusyIndicatorTextBlock.Text = "00:00";
        BusyIndicatorTextBlock.Foreground = BusyRedBrush;

        // 赤いレンズはその時点から 2 倍速で 0% に向かって「眠る」
        if (HalRedEllipse != null)
        {
            if (HalRedEllipse.Visibility == Visibility.Visible)
            {
                _halPulsePhase = HalPulsePhase.Sleeping;
            }
            else
            {
                _halPulsePhase = HalPulsePhase.Idle;
            }
        }
        else
        {
            _halPulsePhase = HalPulsePhase.Idle;
        }

        if (!_busyTimer.IsEnabled)
        {
            _busyTimer.Start();
        }
    }

    private void BusyTimer_OnTick(object? sender, EventArgs e)
    {
        if (_isBusy && _busyStartUtc != null)
        {
            var elapsed = DateTime.UtcNow - _busyStartUtc.Value;
            if (elapsed < TimeSpan.Zero) { elapsed = TimeSpan.Zero; }
            var text = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            BusyIndicatorTextBlock.Text = text;
            BusyIndicatorTextBlock.Foreground = BusyRedBrush;
        }
        else
        {
            BusyIndicatorTextBlock.Text = "00:00";
            BusyIndicatorTextBlock.Foreground = BusyRedBrush;
        }

        UpdateHalPulse();

        // Codex 思考中かどうかをタイトルバーにも反映
        if (_isBusy)
        {
            var spinnerFrames = new[] { "○", "●" };
            var frame = spinnerFrames[_busySpinnerIndex % spinnerFrames.Length];
            _busySpinnerIndex++;
            Title = $"{_baseTitle} {frame}";
        }
        else
        {
            Title = _baseTitle;
        }

        if (!_isBusy && _halPulsePhase == HalPulsePhase.Idle && _busyTimer.IsEnabled)
        {
            _busyTimer.Stop();
        }
    }

    private void SaveUiSettings()
    {
        try
        {
            var path = GetUiSettingsFilePath();

            double width, height, left, top;
            if (WindowState == WindowState.Normal)
            {
                width = Width;
                height = Height;
                left = Left;
                top = Top;
            }
            else
            {
                var bounds = RestoreBounds;
                width = bounds.Width;
                height = bounds.Height;
                left = bounds.Left;
                top = bounds.Top;
            }

            var settings = new UiSettings
            {
                Width = width,
                Height = height,
                Left = left,
                Top = top,
                WindowState = WindowState,
                BgColorHex = BgColorTextBox?.Text,
                FgColorHex = FgColorTextBox?.Text,
                Opacity = OpacitySlider?.Value,
                RecentModels = _recentModels.Count > 0 ? _recentModels.ToList() : null
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch
        {
            // 保存失敗は無視
        }
    }

    private void ApplyHalScaleFromIntensity(double intensity)
    {
        if (HalRedEllipse?.RenderTransform is ScaleTransform scale)
        {
            var t = Math.Max(0.0, Math.Min(1.0, intensity));
            var s = HalMinScale + (HalMaxScale - HalMinScale) * t;
            scale.ScaleX = s;
            scale.ScaleY = s;
        }
    }

    private void HalEyeBorder_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        StartWindowShake();
        StartConversationFallEffect();
    }

    private void StartWindowShake()
    {
        if (_isShaking)
        {
            return;
        }

        if (WindowState != WindowState.Normal)
        {
            // 最大化などの場合は位置を揺らさない
            return;
        }

        _isShaking = true;
        _shakeOriginLeft = Left;
        _shakeOriginTop = Top;
        _shakeStartUtc = DateTime.UtcNow;

        if (!_shakeTimer.IsEnabled)
        {
            _shakeTimer.Start();
        }
    }

    private void StartConversationFallEffect()
    {
        if (_fallPhase != FallAnimationPhase.Idle)
        {
            return;
        }

        if (ConversationTextBox == null || ConversationOverlayCanvas == null)
        {
            return;
        }

        var text = ConversationTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _fallOriginalText = text;
        _fallOriginalForegroundBrush = ConversationTextBox.Foreground;
        if (_fallOriginalForegroundBrush is SolidColorBrush scb)
        {
            _fallOriginalForegroundColor = scb.Color;
        }
        else
        {
            _fallOriginalForegroundColor = Colors.White;
        }

        _fallLines.Clear();
        ConversationOverlayCanvas.Children.Clear();

        var canvasHeight = ConversationOverlayCanvas.ActualHeight;
        if (canvasHeight <= 0)
        {
            canvasHeight = ConversationTextBox.ActualHeight;
        }
        if (canvasHeight <= 0)
        {
            canvasHeight = 400;
        }

        var lineHeight = ConversationTextBox.FontSize * 1.2;
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        double y = 0;
        foreach (var line in lines)
        {
            var tb = new TextBlock
            {
                Text = line,
                FontFamily = ConversationTextBox.FontFamily,
                FontSize = ConversationTextBox.FontSize,
                FontWeight = ConversationTextBox.FontWeight,
                Foreground = ConversationTextBox.Foreground
            };

            ConversationOverlayCanvas.Children.Add(tb);
            Canvas.SetLeft(tb, 4);
            Canvas.SetTop(tb, y);

            var startY = y;
            var endY = canvasHeight + lineHeight * 2;

            var delay = _fallRandom.NextDouble() * (FallDurationSeconds * 0.5); // 0～1.5秒程度のランダム遅延
            var duration = Math.Max(0.5, FallDurationSeconds - delay);          // 落下自体は 0.5～3秒程度

            _fallLines.Add(new FallingLine
            {
                Block = tb,
                StartY = startY,
                EndY = endY,
                DelaySeconds = delay,
                DurationSeconds = duration
            });

            y += lineHeight;
        }

        ConversationOverlayCanvas.Visibility = Visibility.Visible;
        ConversationTextBox.Opacity = 0.0;

        _fallPhase = FallAnimationPhase.Falling;
        _fallElapsedSeconds = 0;
        _fallLastTick = DateTime.UtcNow;
        if (!_fallTimer.IsEnabled)
        {
            _fallTimer.Start();
        }
    }

    private void FallTimer_OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _fallLastTick).TotalSeconds;
        if (dt < 0) dt = 0;
        if (dt > 0.1) dt = 0.1;
        _fallLastTick = now;

        switch (_fallPhase)
        {
            case FallAnimationPhase.Falling:
                _fallElapsedSeconds += dt;
                foreach (var line in _fallLines)
                {
                    var local = _fallElapsedSeconds - line.DelaySeconds;
                    double y;
                    if (local <= 0)
                    {
                        y = line.StartY;
                    }
                    else if (local >= line.DurationSeconds)
                    {
                        y = line.EndY;
                    }
                    else
                    {
                        var t = local / line.DurationSeconds;
                        t = t * t; // 少し加速感を出す
                        y = line.StartY + (line.EndY - line.StartY) * t;
                    }
                    Canvas.SetTop(line.Block, y);
                }

                if (_fallElapsedSeconds >= FallDurationSeconds)
                {
                    _fallPhase = FallAnimationPhase.WaitingAfterFall;
                    _fallElapsedSeconds = 0;
                }
                break;

            case FallAnimationPhase.WaitingAfterFall:
                _fallElapsedSeconds += dt;
                if (_fallElapsedSeconds >= FallWaitAfterSeconds)
                {
                    // 落下完了から一定時間待ったら、赤い文字で復活フェーズへ
                    ConversationOverlayCanvas.Children.Clear();
                    ConversationOverlayCanvas.Visibility = Visibility.Collapsed;

                    if (_fallOriginalText != null)
                    {
                        ConversationTextBox.Text = _fallOriginalText;
                    }

                    ConversationTextBox.Opacity = 0.0;
                    _fallPhase = FallAnimationPhase.FadeInRed;
                    _fallElapsedSeconds = 0;
                }
                break;

            case FallAnimationPhase.FadeInRed:
                _fallElapsedSeconds += dt;
                {
                    var t = Math.Max(0.0, Math.Min(1.0, _fallElapsedSeconds / FallFadeInSeconds));
                    // 暗い赤から明るい赤へ、かつ透明→不透明
                    var dark = Color.FromRgb(0x80, 0x00, 0x00);
                    var bright = Color.FromRgb(0xFF, 0x33, 0x33);
                    var c = Color.FromRgb(
                        (byte)(dark.R + (bright.R - dark.R) * t),
                        (byte)(dark.G + (bright.G - dark.G) * t),
                        (byte)(dark.B + (bright.B - dark.B) * t));
                    ConversationTextBox.Foreground = new SolidColorBrush(c);
                    ConversationTextBox.Opacity = t;
                }

                if (_fallElapsedSeconds >= FallFadeInSeconds)
                {
                    _fallPhase = FallAnimationPhase.FadeToNormal;
                    _fallElapsedSeconds = 0;
                    ConversationTextBox.Opacity = 1.0;
                }
                break;

            case FallAnimationPhase.FadeToNormal:
                _fallElapsedSeconds += dt;
                {
                    var t = Math.Max(0.0, Math.Min(1.0, _fallElapsedSeconds / FallFadeToNormalSeconds));
                    var bright = Color.FromRgb(0xFF, 0x33, 0x33);
                    var target = _fallOriginalForegroundColor;
                    var c = Color.FromRgb(
                        (byte)(bright.R + (target.R - bright.R) * t),
                        (byte)(bright.G + (target.G - bright.G) * t),
                        (byte)(bright.B + (target.B - bright.B) * t));
                    ConversationTextBox.Foreground = new SolidColorBrush(c);
                }

                if (_fallElapsedSeconds >= FallFadeToNormalSeconds)
                {
                    // 最終的に元の設定色へ戻す
                    if (_fallOriginalForegroundBrush != null)
                    {
                        ConversationTextBox.Foreground = _fallOriginalForegroundBrush;
                    }
                    _fallPhase = FallAnimationPhase.Idle;
                    _fallTimer.Stop();
                    _fallLines.Clear();
                    _fallOriginalText = null;
                }
                break;

            case FallAnimationPhase.Idle:
            default:
                _fallTimer.Stop();
                break;
        }
    }

    private void ShakeTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_isShaking)
        {
            _shakeTimer.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = (now - _shakeStartUtc).TotalSeconds;
        if (elapsed >= ShakeDurationSeconds)
        {
            // 揺れ終了。元の位置に戻す
            _isShaking = false;
            _shakeTimer.Stop();
            try
            {
                Left = _shakeOriginLeft;
                Top = _shakeOriginTop;
            }
            catch
            {
                // 位置復元に失敗しても無視
            }
            return;
        }

        // 時間経過に応じて振幅を少しずつ減衰させる
        var progress = elapsed / ShakeDurationSeconds; // 0～1
        var baseAmplitude = 8.0; // ピクセル
        var amplitude = baseAmplitude * (1.0 - progress);

        var offsetX = (float)(_fallRandom.NextDouble() * 2.0 - 1.0) * amplitude;
        var offsetY = (float)(_fallRandom.NextDouble() * 2.0 - 1.0) * amplitude;

        try
        {
            Left = _shakeOriginLeft + offsetX;
            Top = _shakeOriginTop + offsetY;
        }
        catch
        {
            // 位置更新失敗は無視
        }
    }

    private void CreateHalLikeTaskbarIcon()
    {
        const int size = 64;
        const double dpi = 96.0;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var center = new Point(size / 2.0, size / 2.0);
            var outerRadius = size * 0.48;
            var ringRadius = size * 0.36;
            // 元の赤レンズ半径(0.26)を 0.5 倍に縮小
            var redRadius = size * 0.26 * 0.5;

            // 黒いディスク
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                null, center, outerRadius, outerRadius);

            // 明るめのシルバーリング
            var ringBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
            var ringPen = new Pen(ringBrush, size * 0.06);
            ringPen.Freeze();
            dc.DrawEllipse(null, ringPen, center, ringRadius, ringRadius);

            // 赤いレンズ（中心が少し上寄りのグラデーション）
            var redBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.45, 0.35),
                RadiusX = 0.6,
                RadiusY = 0.6
            };
            redBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x44, 0x44), 0.0));
            redBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xCC, 0x00, 0x00), 0.4));
            redBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0x00, 0x00), 1.0));
            redBrush.Freeze();
            dc.DrawEllipse(redBrush, null, center, redRadius, redRadius);

            // 右上に小さなハイライト
            var highlight = new RadialGradientBrush
            {
                Center = new Point(0.35, 0.3),
                GradientOrigin = new Point(0.32, 0.25),
                RadiusX = 0.4,
                RadiusY = 0.4,
                Opacity = 0.7
            };
            highlight.GradientStops.Add(new GradientStop(Color.FromArgb(180, 255, 255, 255), 0.0));
            highlight.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
            highlight.Freeze();
            dc.DrawEllipse(
                highlight,
                null,
                new Point(center.X - size * 0.06, center.Y - size * 0.10),
                redRadius * 0.55,
                redRadius * 0.55);
        }

        var bmp = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();

        Icon = bmp;
    }

    private void UpdateHalPulse()
    {
        if (HalRedEllipse == null)
        {
            _halPulsePhase = HalPulsePhase.Idle;
            return;
        }

        switch (_halPulsePhase)
        {
            case HalPulsePhase.Idle:
                HalRedEllipse.Visibility = Visibility.Collapsed;
                break;

            case HalPulsePhase.Awakening:
            {
                const double target = 0.5; // 50%
                _halPulseValue += HalAwakeStep;
                if (_halPulseValue >= target)
                {
                    _halPulseValue = target;
                    _halPulsePhase = HalPulsePhase.Breathing;
                    _halPulseIncreasing = true;
                }
                HalRedEllipse.Visibility = Visibility.Visible;
                HalRedEllipse.Opacity = 1.0;
                ApplyHalScaleFromIntensity(_halPulseValue);
                break;
            }

            case HalPulsePhase.Breathing:
            {
                const double min = 0.5; // 50%
                const double max = 1.0; // 100%

                if (_halPulseIncreasing)
                {
                    _halPulseValue += HalBreathStep;
                    if (_halPulseValue >= max)
                    {
                        _halPulseValue = max;
                        _halPulseIncreasing = false;
                    }
                }
                else
                {
                    _halPulseValue -= HalBreathStep;
                    if (_halPulseValue <= min)
                    {
                        _halPulseValue = min;
                        _halPulseIncreasing = true;
                    }
                }

                HalRedEllipse.Visibility = Visibility.Visible;
                HalRedEllipse.Opacity = 1.0;
                ApplyHalScaleFromIntensity(_halPulseValue);
                break;
            }

            case HalPulsePhase.Sleeping:
            {
                _halPulseValue -= HalSleepStep;
                if (_halPulseValue <= 0.0)
                {
                    _halPulseValue = 0.0;
                    ApplyHalScaleFromIntensity(_halPulseValue);
                    HalRedEllipse.Visibility = Visibility.Collapsed;
                    HalRedEllipse.Opacity = 1.0;
                    _halPulsePhase = HalPulsePhase.Idle;
                }
                else
                {
                    HalRedEllipse.Visibility = Visibility.Visible;
                    HalRedEllipse.Opacity = Math.Max(0.0, Math.Min(1.0, _halPulseValue));
                    ApplyHalScaleFromIntensity(_halPulseValue);
                }
                break;
            }
        }
    }

    private async void StatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isSending)
        {
            return;
        }

        if (SessionComboBox.SelectedItem is not SessionInfo session)
        {
            MessageBox.Show(this, "まずセッションを作成・選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApplyModelFromUiToSession(session);
        RememberRecentModel(session.Model);

        _isSending = true;
        StatusButton.IsEnabled = false;
        StartBusyIndicator();

        try
        {
            session.LastUsedUtc = DateTime.UtcNow;
            SaveSessions();

            AppendSystemMessage("Status リクエストを送信します。");

            // Status リクエストは空プロンプトで、PowerShell 側の -ShowStatus フラグに任せます。
            var (output, error, exitCode, hadStreamingOutput) = await RunPromptThroughPowerShellAsync(
                string.Empty,
                session,
                isStatusRequest: true);

            if (_wasCancelledByUser)
            {
                AppendSystemMessage("ユーザーにより Codex の実行を中断しました。");
                _wasCancelledByUser = false;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(error) || exitCode != 0)
                {
                    AppendSystemMessage("PowerShell スクリプト実行エラー:");
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        AppendSystemMessage(error.Trim());
                    }
                    else
                    {
                        AppendSystemMessage($"ExitCode={exitCode}");
                    }
                }

                if (!hadStreamingOutput && !string.IsNullOrWhiteSpace(output))
                {
                    AppendAssistantMessage(output.TrimEnd());
                }
            }
        }
        finally
        {
            _isSending = false;
            StatusButton.IsEnabled = true;
            StopBusyIndicator();
        }
    }

    private void ChatMonitorButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var w = new ChatMonitorWindow(this)
            {
                Owner = this
            };
            w.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Chat Monitor を開けませんでした。\n" + ex.Message, "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CaptureButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!TryGetRevitMcpPort(out var port) || port <= 0)
            {
                MessageBox.Show(this,
                    "RevitMCP サーバーのポートが特定できません。\nRevitMCPServer を起動してから再度お試しください。",
                    "Codex GUI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var w = new CaptureConsentWindow(port)
            {
                Owner = this
            };
            var ok = w.ShowDialog() ?? false;
            if (!ok) return;

            _pendingImagePaths.Clear();
            _pendingImagePaths.AddRange(w.ApprovedImagePaths);
            UpdateAttachedImagesIndicator();

            if (_pendingImagePaths.Count > 0)
            {
                AppendSystemMessage($"画像を添付対象に設定しました: {_pendingImagePaths.Count} 件");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Capture の実行に失敗しました。\n" + ex.Message,
                "Codex GUI",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UsePromptButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = EnumerateSelectedPrompts().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "使用するプロンプトを選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existing = PromptTextBox.Text ?? string.Empty;
        var builder = new StringBuilder(existing);
        var inserted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prompt in selected)
        {
            // すでに同じ内容が入力欄に含まれている場合や、この操作中に追加済みの場合はスキップ
            if (existing.Contains(prompt, StringComparison.Ordinal) || inserted.Contains(prompt))
            {
                continue;
            }

            // 直前が改行で終わっていなければ、行末に 1 つだけ改行を足す
            if (builder.Length > 0)
            {
                var lastChar = builder[builder.Length - 1];
                if (lastChar != '\n' && lastChar != '\r')
                {
                    builder.AppendLine();
                }
            }

            builder.AppendLine(prompt);
            inserted.Add(prompt);
            existing = builder.ToString();
        }

        PromptTextBox.Text = builder.ToString();
        PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
        PromptTextBox.Focus();
    }

    private void DeletePromptButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsAllPromptTabSelected())
        {
            MessageBox.Show(this, "全プロンプトは削除できません。セッション履歴から削除してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SessionComboBox.SelectedItem is not SessionInfo session)
        {
            MessageBox.Show(this, "セッションを選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (PromptHistoryListBox.SelectedItem is not string prompt)
        {
            MessageBox.Show(this, "削除するプロンプトを選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        EnsurePromptHistoryInitialized(session);
        if (session.PromptHistory.Remove(prompt))
        {
            SaveSessions();
            RefreshPromptHistory(session);
        }
    }

    private void EditPromptButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsAllPromptTabSelected())
        {
            MessageBox.Show(this, "全プロンプトは編集できません。セッション履歴から編集してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SessionComboBox.SelectedItem is not SessionInfo session)
        {
            MessageBox.Show(this, "セッションを選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (PromptHistoryListBox.SelectedItem is not string prompt)
        {
            MessageBox.Show(this, "編集するプロンプトを選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        EnsurePromptHistoryInitialized(session);
        var index = session.PromptHistory.IndexOf(prompt);
        if (index < 0)
        {
            return;
        }

        var dialog = new SimpleTextInputWindow("プロンプトの編集", "プロンプトを編集してください:", prompt);
        if (dialog.ShowDialog() == true)
        {
            var newText = dialog.ResultText ?? string.Empty;
            newText = newText.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(newText) || newText == prompt)
            {
                return;
            }

            // すでに同じ内容が別エントリとして存在する場合は、今回編集したものを削除するだけにする
            if (session.PromptHistory.Contains(newText))
            {
                session.PromptHistory.RemoveAt(index);
            }
            else
            {
                session.PromptHistory[index] = newText;
            }

            SaveSessions();
            RefreshPromptHistory(session);
        }
    }

    private void PromptHistoryListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        UsePromptButton_OnClick(sender, e);
    }

    private void AllPromptHistoryListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        UsePromptButton_OnClick(sender, e);
    }

    private void PromptHistoryTab_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            var isAll = IsAllPromptTabSelected();
            if (EditPromptButton != null) EditPromptButton.IsEnabled = !isAll;
            if (DeletePromptButton != null) DeletePromptButton.IsEnabled = !isAll;
        }
        catch { /* ignore */ }
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isSending)
        {
            return;
        }

        await EnsureProjectSessionAsync(forceSelect: true);

        if (SessionComboBox.SelectedItem is not SessionInfo session)
        {
            MessageBox.Show(this, "まずセッションを作成・選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApplyModelFromUiToSession(session);
        RememberRecentModel(session.Model);
        EnsurePromptHistoryInitialized(session);

        var prompt = PromptTextBox.Text.TrimEnd();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        var isFirstPromptInSession = session.PromptHistory.Count == 0;
        var shouldIncludeRevitIntro = isFirstPromptInSession && !session.HasSentRevitIntro;
        var promptForCodex = prompt;
        if (shouldIncludeRevitIntro)
        {
            var builder = new StringBuilder();
            builder.AppendLine(RevitIntroInstruction);
            builder.AppendLine();
            builder.AppendLine("上記の制約と準備を前提として、以下のユーザーリクエストに対応してください。");
            builder.AppendLine(prompt);
            promptForCodex = builder.ToString();
        }

        _isSending = true;
        SendButton.IsEnabled = false;
        StartBusyIndicator();

        var startedUtc = DateTimeOffset.MinValue;
        var endedUtc = DateTimeOffset.MinValue;
        var output = string.Empty;
        var error = string.Empty;
        var exitCode = -1;
        var hadStreamingOutput = false;
        var wasCancelled = false;

        try
        {
            session.LastUsedUtc = DateTime.UtcNow;
            EnsurePromptHistoryInitialized(session);
            // 同じ内容のプロンプトは重複させない（最新の位置に移動）
            session.PromptHistory.Remove(prompt);
            session.PromptHistory.Add(prompt);
            TrimPromptHistory(session, 200);
            SaveSessions();
            RefreshPromptHistory(session);

            if (!string.IsNullOrWhiteSpace(ConversationTextBox.Text))
            {
                AppendSeparatorLine();
            }

            AppendUserMessage(prompt);
            PromptTextBox.Clear();

            var imagesForThisRun = _pendingImagePaths.ToArray();
            if (imagesForThisRun.Length > 0)
            {
                _pendingImagePaths.Clear();
                UpdateAttachedImagesIndicator();
                AppendSystemMessage($"画像を添付して実行します: {imagesForThisRun.Length} 件");
            }

            var modelLabel = string.IsNullOrWhiteSpace(session.Model) ? "(default)" : session.Model;
            AppendSystemMessage($"model: {modelLabel}");
            var effortLabel = string.IsNullOrWhiteSpace(session.ReasoningEffort) ? "(default)" : session.ReasoningEffort;
            AppendSystemMessage($"reasoning_effort: {effortLabel}");

            startedUtc = DateTimeOffset.UtcNow;
            try
            {
                (output, error, exitCode, hadStreamingOutput) = await RunPromptThroughPowerShellAsync(
                    promptForCodex,
                    session,
                    isStatusRequest: false,
                    imagePaths: imagesForThisRun);
            }
            catch (Exception ex)
            {
                // Restore pending images if the process failed to start.
                if (imagesForThisRun.Length > 0)
                {
                    _pendingImagePaths.Clear();
                    _pendingImagePaths.AddRange(imagesForThisRun);
                    UpdateAttachedImagesIndicator();
                }

                output = string.Empty;
                error = ex.ToString();
                exitCode = -1;
                hadStreamingOutput = false;
            }
            endedUtc = DateTimeOffset.UtcNow;

            wasCancelled = _wasCancelledByUser;
            if (wasCancelled)
            {
                AppendSystemMessage("ユーザーにより Codex の実行を中断しました。");
                _wasCancelledByUser = false;
            }
            else
            {
                if (shouldIncludeRevitIntro && exitCode == 0)
                {
                    session.HasSentRevitIntro = true;
                    SaveSessions();
                }

                if (!string.IsNullOrWhiteSpace(error) || exitCode != 0)
                {
                    AppendSystemMessage("PowerShell スクリプト実行エラー:");
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        AppendSystemMessage(error.Trim());
                    }
                    else
                    {
                        AppendSystemMessage($"ExitCode={exitCode}");
                    }
                }

                if (!hadStreamingOutput && !string.IsNullOrWhiteSpace(output))
                {
                    AppendAssistantMessage(output.TrimEnd());
                }
            }
        }
        finally
        {
            _isSending = false;
            SendButton.IsEnabled = true;
            PromptTextBox.Focus();
            StopBusyIndicator();

            try
            {
                var args = new CodexRunCompletedEventArgs
                {
                    SessionId = session.Id,
                    SessionName = session.Name,
                    Prompt = prompt,
                    PromptForCodex = promptForCodex,
                    Output = output ?? string.Empty,
                    Error = error ?? string.Empty,
                    ExitCode = exitCode,
                    WasCancelled = wasCancelled,
                    HadStreamingOutput = hadStreamingOutput,
                    StartedUtc = startedUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : startedUtc,
                    EndedUtc = endedUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : endedUtc
                };
                CodexRunCompleted?.Invoke(this, args);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string scriptPath, string promptFilePath, string sessionId)
    {
        var fileNameCandidates = new[] { "pwsh", "pwsh.exe", "powershell", "powershell.exe" };
        string? fileName = null;

        foreach (var candidate in fileNameCandidates)
        {
            try
            {
                var psiTest = new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = new Process { StartInfo = psiTest };
                if (p.Start())
                {
                    // We immediately exit the test process.
                    p.Kill(true);
                    fileName = candidate;
                    break;
                }
            }
            catch
            {
                // ignore and try next candidate
            }
        }

        fileName ??= "pwsh";

        static string EscapePwshDoubleQuotedArg(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("`", "``").Replace("\"", "`\"");
        }

        var argsBuilder = new StringBuilder();
        argsBuilder.Append("-NoLogo -NoProfile -ExecutionPolicy Bypass ");
        argsBuilder.Append("-File ");
        argsBuilder.Append('"').Append(scriptPath).Append('"');
        argsBuilder.Append(" -PromptFile ");
        argsBuilder.Append('"').Append(promptFilePath).Append('"');
        argsBuilder.Append(" -SessionId ");
        argsBuilder.Append('"').Append(sessionId).Append('"');

        // Pass model (optional), reasoning effort (optional) and status flag
        argsBuilder.Append(" -Model ");
        argsBuilder.Append('"').Append(EscapePwshDoubleQuotedArg(_currentModelForProcess ?? string.Empty)).Append('"');
        argsBuilder.Append(" -ReasoningEffort ");
        argsBuilder.Append('"').Append(EscapePwshDoubleQuotedArg(_currentReasoningEffortForProcess ?? string.Empty)).Append('"');

        if (_currentImagePathsForProcess != null && _currentImagePathsForProcess.Length > 0)
        {
            argsBuilder.Append(" -ImagePaths");
            foreach (var p in _currentImagePathsForProcess)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var val = p.Trim();
                if (string.IsNullOrWhiteSpace(val)) continue;
                // PowerShell escaping for double-quoted args.
                val = val.Replace("`", "``").Replace("\"", "`\"");
                argsBuilder.Append(' ').Append('"').Append(val).Append('"');
            }
        }
        if (_currentIsStatusRequestForProcess)
        {
            argsBuilder.Append(" -ShowStatus");
        }

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = argsBuilder.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

private static async Task<(string output, string error, int exitCode, bool hadStreamingOutput)> RunPromptThroughPowerShellAsync(
        string prompt,
        SessionInfo session,
        bool isStatusRequest,
        IReadOnlyList<string>? imagePaths = null)
    {
        var baseDir = AppContext.BaseDirectory;
        var scriptPath = Path.Combine(baseDir, "run_codex_prompt.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Codex 用の PowerShell スクリプトが見つかりません。",
                scriptPath);
        }

        var tempFile = Path.Combine(Path.GetTempPath(),
            $"codex_prompt_{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(tempFile, prompt, Encoding.UTF8);

            _currentModelForProcess = session.Model ?? string.Empty;
            _currentReasoningEffortForProcess = session.ReasoningEffort ?? string.Empty;
            _currentIsStatusRequestForProcess = isStatusRequest;
            _currentImagePathsForProcess = (!isStatusRequest && imagePaths != null && imagePaths.Count > 0)
                ? imagePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray()
                : null;

            var psi = CreatePowerShellStartInfo(scriptPath, tempFile, session.Id);
            using var process = new Process { StartInfo = psi };
            _currentPwshProcess = process;

            process.Start();

            var outputBuilder = new StringBuilder();
            var errorTask = process.StandardError.ReadToEndAsync();
            var hadStreamingOutput = false;
            var isFirstAnswerLine = true;
            string? lastUiLine = null;
            var seenLines = new HashSet<string>(StringComparer.Ordinal);

            // Codex CLI の出力を行単位で読み取りつつ、
            // 「codex」以降のアシスタント回答がまるごと 2 回連続で出力されるケースでは
            // 2 回目のブロックを丸ごとスキップするための簡易フラグを用意する。
            bool inCodexSection = false;
            string? firstAnswerLine = null;
            int answerLineCount = 0;
            bool skippingDuplicateAnswer = false;

            var outputTask = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var line = await process.StandardOutput.ReadLineAsync();
                        if (line == null)
                        {
                            break;
                        }

                        // 以降の処理は、画面とログに出す「クリーンな文字列」を基準に判定する。
                        var rawLine = line ?? string.Empty;
                        var cleanLine = AnsiRegex.Replace(rawLine, string.Empty);
                        cleanLine = BracketAnsiRegex.Replace(cleanLine, string.Empty);
                        var isCodexMarkerLine = string.Equals(cleanLine.Trim(), "codex", StringComparison.OrdinalIgnoreCase);

                        // "tokens used" 以降に現れるブロックは、内容が重複することが多いので、
                        // トークン情報だけ取り込んで以降の行は無視する。
                        if (cleanLine.StartsWith("tokens used", StringComparison.OrdinalIgnoreCase))
                        {
                            lock (outputBuilder)
                            {
                                outputBuilder.AppendLine(cleanLine);
                            }

                            hadStreamingOutput = true;

                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                if (Application.Current?.MainWindow is MainWindow mw2)
                                {
                                    mw2.AppendTraceLine(cleanLine);
                                }
                            });

                            break;
                        }

                        // Codex 本体の出力形式:
                        //   thinking
                        //   ...
                        //   codex
                        //   (アシスタントの回答 ...)
                        //
                        // 稀に「codex」以降の回答ブロックが 2 回連続で出力されるため、
                        // 2 回目のブロックを検出してスキップする。
                        if (string.Equals(cleanLine, "codex", StringComparison.OrdinalIgnoreCase))
                        {
                            inCodexSection = true;
                            firstAnswerLine = null;
                            answerLineCount = 0;
                            skippingDuplicateAnswer = false;
                            isFirstAnswerLine = true;
                        }
                        else if (inCodexSection)
                        {
                            var trimmed = cleanLine.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                answerLineCount++;
                                if (firstAnswerLine == null)
                                {
                                    // 最初の非空行を「回答ブロックの先頭行」とみなす
                                    firstAnswerLine = cleanLine;
                                }
                                else if (!skippingDuplicateAnswer &&
                                         answerLineCount >= 4 && // ある程度行数がたまってからだけ重複判定
                                         string.Equals(cleanLine, firstAnswerLine, StringComparison.Ordinal))
                                {
                                    // 先頭行とまったく同じ内容が再度現れたら、
                                    // それ以降は 2 回目の回答ブロックとみなしスキップする。
                                    skippingDuplicateAnswer = true;
                                }
                            }

                            if (skippingDuplicateAnswer)
                            {
                                continue;
                            }
                        }

                        if (IsIgnorableCodexNoiseLine(cleanLine))
                        {
                            continue;
                        }

                        lock (outputBuilder)
                        {
                            // 同じ行が同一呼び出し中に複数回現れる場合は、一度だけ記録・表示する
                            if (!seenLines.Add(cleanLine))
                            {
                                continue;
                            }
                            outputBuilder.AppendLine(cleanLine);
                        }

                        hadStreamingOutput = true;

                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            if (Application.Current?.MainWindow is MainWindow mw)
                            {
                                // 同一行が連続して届く場合は 2 重表示しない
                                if (string.Equals(cleanLine, lastUiLine, StringComparison.Ordinal))
                                {
                                    return;
                                }
                                lastUiLine = cleanLine;

                                if (isCodexMarkerLine)
                                {
                                    // Marker line is useful for parsing, but not useful for UI.
                                    return;
                                }

                                if (inCodexSection)
                                {
                                    // Only the answer section goes to the main conversation.
                                    if (isFirstAnswerLine && !string.IsNullOrWhiteSpace(cleanLine))
                                    {
                                        isFirstAnswerLine = false;
                                        mw.AppendAssistantMessage(cleanLine);
                                    }
                                    else
                                    {
                                        mw.AppendLine(cleanLine);
                                    }
                                }
                                else
                                {
                                    // Everything else (thinking/exec/logs) goes to the trace panel.
                                    mw.AppendTraceLine(cleanLine);
                                }
                            }
                        });
                    }
                }
                catch
                {
                    // ignore (process killed or stream closed)
                }
            });

            await Task.WhenAll(outputTask, errorTask);
            try { process.WaitForExit(); } catch { }

            string finalOutput;
            lock (outputBuilder)
            {
                finalOutput = outputBuilder.ToString();
            }

            return (finalOutput, errorTask.Result, process.ExitCode, hadStreamingOutput);
        }
        finally
        {
            _currentImagePathsForProcess = null;
            _currentPwshProcess = null;
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }

    private void AppendLine(string text)
    {
        text ??= string.Empty;
        // Codex の出力に含まれる ANSI 風の装飾コード（\x1B[...m や [31;1m など）は
        // TextBox では再現できないため、画面とログから取り除く。
        var clean = AnsiRegex.Replace(text, string.Empty);
        clean = BracketAnsiRegex.Replace(clean, string.Empty);
        ConversationTextBox.AppendText(clean + Environment.NewLine);
        ConversationTextBox.ScrollToEnd();

        try
        {
            if (SessionComboBox.SelectedItem is SessionInfo session)
            {
                if (!_sessionLogPaths.TryGetValue(session.Id, out var logPath))
                {
                    logPath = CreateSessionLogFile(session);
                    _sessionLogPaths[session.Id] = logPath;
                }

                File.AppendAllText(logPath, clean + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // ログ書き込み失敗は GUI 動作に影響させない
        }
    }

    private void AppendSystemMessage(string text)
    {
        AppendLine($"[System] {text}");
    }

    private void AppendUserMessage(string text)
    {
        AppendLine($">> {text}");
    }

    private void AppendAssistantMessage(string text)
    {
        AppendLine($"[Codex] {text}");
    }

    private void AppendTraceLine(string text)
    {
        text ??= string.Empty;

        // Keep trace output readable but less prominent than the main conversation.
        var clean = AnsiRegex.Replace(text, string.Empty);
        clean = BracketAnsiRegex.Replace(clean, string.Empty);

        if (IsIgnorableCodexNoiseLine(clean))
        {
            return;
        }

        TraceTextBox.AppendText(clean + Environment.NewLine);
        TraceTextBox.ScrollToEnd();

        try
        {
            if (SessionComboBox.SelectedItem is SessionInfo session)
            {
                if (!_sessionLogPaths.TryGetValue(session.Id, out var logPath))
                {
                    logPath = CreateSessionLogFile(session);
                    _sessionLogPaths[session.Id] = logPath;
                }

                File.AppendAllText(logPath, "[Trace] " + clean + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static bool IsIgnorableCodexNoiseLine(string cleanLine)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cleanLine)) return false;
            // Codex CLI internal flag; not an actionable error for users.
            if (cleanLine.IndexOf("codex_core::codex: needs_follow_up", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private sealed class RevitProgressSnapshot
    {
        public DateTime TsUtc { get; set; }
        public string JobId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Done { get; set; }
        public double? Percent { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private static string GetRevitMcpStateFilePath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "RevitMCP", "server_state.json");
    }

    private static string GetRevitMcpProgressFilePath(int port)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "RevitMCP", "progress", $"progress_{port}.jsonl");
    }

    private static bool TryGetRevitMcpPort(out int port)
    {
        port = 0;
        try
        {
            var path = GetRevitMcpStateFilePath();
            if (!File.Exists(path)) return false;

            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("port", out var p)) return false;

            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out port)) return port > 0;
            if (p.ValueKind == JsonValueKind.String && int.TryParse((p.ToString() ?? "").Trim(), out port)) return port > 0;
            return false;
        }
        catch
        {
            port = 0;
            return false;
        }
    }

    private static string? TryReadLastJsonlLine(string path, int maxBytes = 65536)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!File.Exists(path)) return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= 0) return null;

            var readSize = (int)Math.Min(maxBytes, fs.Length);
            fs.Seek(-readSize, SeekOrigin.End);
            var buffer = new byte[readSize];
            var n = fs.Read(buffer, 0, readSize);
            if (n <= 0) return null;

            var txt = Encoding.UTF8.GetString(buffer, 0, n);
            txt = txt.Replace("\r\n", "\n").Replace("\r", "\n");

            // Trim trailing newlines
            var end = txt.Length;
            while (end > 0 && txt[end - 1] == '\n') end--;
            if (end <= 0) return null;

            var lastNl = txt.LastIndexOf('\n', end - 1);
            var line = lastNl >= 0 ? txt.Substring(lastNl + 1, end - (lastNl + 1)) : txt.Substring(0, end);

            line = line.Trim();
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseRevitProgressSnapshot(string jsonLine, out RevitProgressSnapshot snapshot)
    {
        snapshot = new RevitProgressSnapshot();
        try
        {
            if (string.IsNullOrWhiteSpace(jsonLine)) return false;
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (root.TryGetProperty("tsUtc", out var tsEl))
            {
                var ts = (tsEl.ToString() ?? "").Trim();
                if (DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                {
                    snapshot.TsUtc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                }
            }

            if (root.TryGetProperty("jobId", out var jobEl)) snapshot.JobId = (jobEl.ToString() ?? "").Trim();
            if (root.TryGetProperty("title", out var titleEl)) snapshot.Title = (titleEl.ToString() ?? "").Trim();
            if (root.TryGetProperty("message", out var msgEl)) snapshot.Message = (msgEl.ToString() ?? "").Trim();

            if (root.TryGetProperty("total", out var totalEl)) snapshot.Total = TryReadInt32(totalEl);
            if (root.TryGetProperty("done", out var doneEl)) snapshot.Done = TryReadInt32(doneEl);

            if (root.TryGetProperty("percent", out var pctEl))
            {
                if (pctEl.ValueKind == JsonValueKind.Number && pctEl.TryGetDouble(out var d))
                    snapshot.Percent = d;
                else if (pctEl.ValueKind == JsonValueKind.String &&
                         double.TryParse((pctEl.ToString() ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dd))
                    snapshot.Percent = dd;
                else
                    snapshot.Percent = null;
            }

            return true;
        }
        catch
        {
            snapshot = new RevitProgressSnapshot();
            return false;
        }
    }

    private static int TryReadInt32(JsonElement el)
    {
        try
        {
            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetInt32(out var i)) return i;
                if (el.TryGetInt64(out var l))
                {
                    if (l > int.MaxValue) return int.MaxValue;
                    if (l < int.MinValue) return int.MinValue;
                    return (int)l;
                }
                return 0;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                if (int.TryParse((el.ToString() ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return i;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsTerminalProgressMessage(string? message)
    {
        try
        {
            var m = (message ?? "").Trim();
            if (string.IsNullOrWhiteSpace(m)) return false;
            if (string.Equals(m, "done", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.StartsWith("done", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.StartsWith("failed", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.StartsWith("error", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.StartsWith("cancel", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task RefreshRevitProgressAsync()
    {
        if (_revitProgressRefreshing) return;
        _revitProgressRefreshing = true;

        try
        {
            if (!TryGetRevitMcpPort(out var port) || port <= 0)
            {
                _revitProgressPort = 0;
                _revitProgressLastLine = null;
                _revitProgressLastSnapshot = null;
                HideRevitProgressUi();
                return;
            }

            if (port != _revitProgressPort)
            {
                _revitProgressPort = port;
                _revitProgressLastLine = null;
                _revitProgressLastSnapshot = null;
            }

            var path = GetRevitMcpProgressFilePath(port);
            var line = await Task.Run(() => TryReadLastJsonlLine(path)).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(line))
            {
                HideRevitProgressUi();
                return;
            }

            if (string.Equals(line, _revitProgressLastLine, StringComparison.Ordinal))
            {
                if (_revitProgressLastSnapshot != null)
                {
                    UpdateRevitProgressUi(_revitProgressLastSnapshot);
                    return;
                }
            }

            if (!TryParseRevitProgressSnapshot(line, out var snap))
            {
                HideRevitProgressUi();
                return;
            }

            _revitProgressLastLine = line;
            _revitProgressLastSnapshot = snap;
            UpdateRevitProgressUi(snap);
            await EnsureProjectSessionAsync(forceSelect: false);
        }
        catch
        {
            HideRevitProgressUi();
        }
        finally
        {
            _revitProgressRefreshing = false;
        }
    }

    private void HideRevitProgressUi()
    {
        try
        {
            if (RevitProgressBar != null) RevitProgressBar.Visibility = Visibility.Collapsed;
            if (RevitProgressTextBlock != null) RevitProgressTextBlock.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void UpdateRevitProgressUi(RevitProgressSnapshot snap)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var tsUtc = snap.TsUtc;
            if (tsUtc == default)
            {
                HideRevitProgressUi();
                return;
            }

            var age = nowUtc - tsUtc;
            if (age > TimeSpan.FromSeconds(20))
            {
                HideRevitProgressUi();
                return;
            }

            var msg = (snap.Message ?? "").Trim();
            if (IsTerminalProgressMessage(msg) && age > TimeSpan.FromSeconds(6))
            {
                HideRevitProgressUi();
                return;
            }

            var title = (snap.Title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title)) title = "RevitMCP";

            var isIndeterminate = snap.Total <= 0 || snap.Percent == null;

            if (RevitProgressBar != null)
            {
                RevitProgressBar.Visibility = Visibility.Visible;
                RevitProgressBar.IsIndeterminate = isIndeterminate;
                if (!isIndeterminate)
                {
                    var pct = snap.Percent.GetValueOrDefault();
                    if (double.IsNaN(pct) || double.IsInfinity(pct)) pct = 0;
                    RevitProgressBar.Value = Math.Max(0, Math.Min(100, pct));
                }
            }

            if (RevitProgressTextBlock != null)
            {
                RevitProgressTextBlock.Visibility = Visibility.Visible;

                string text;
                if (isIndeterminate)
                {
                    text = string.IsNullOrWhiteSpace(msg) ? title : $"{title} — {msg}";
                }
                else
                {
                    var pct = snap.Percent.GetValueOrDefault();
                    var detail = $"{snap.Done}/{snap.Total} ({pct:0.#}%)";
                    if (!string.IsNullOrWhiteSpace(msg)) detail += " " + msg;
                    text = $"{title} — {detail}";
                }

                RevitProgressTextBlock.Text = text;
            }
        }
        catch
        {
            HideRevitProgressUi();
        }
    }

    private void OpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // スライダーの値に応じて、現在指定されている BG/FG 色を保ったまま
        // 不透明度だけを更新する
        ApplyColorsButton_OnClick(this, e);
        SaveUiSettings();
    }

    private void ApplyBackdropFromCurrentState()
    {
        try
        {
            // Deep navy default tint (will apply alpha from slider)
            byte a = (byte)(Math.Round((OpacitySlider?.Value ?? 0.9) * 255));
            var navy = Color.FromArgb(a, 0x12, 0x24, 0x45); // default deep blue

            if (AllowsTransparency)
            {
                // Layered window path: emulate acrylic via tinted backdrop + local blur on backdrop only
                if (BackdropBorder != null)
                {
                    BackdropBorder.Background = new SolidColorBrush(navy);
                }
                return;
            }

            // If non-layered: keep OS acrylic; default enable=false (no blur)
            EnableAcrylicBlur(false, navy);
        }
        catch
        {
            // ignore: OS not supported etc.
        }
    }

    private void ApplySurfaceOpacityToContent()
    {
        try
        {
            byte a = (byte)(Math.Round((OpacitySlider?.Value ?? 0.9) * 255));
            // Use deep navy palette
            var baseColor = Color.FromArgb(a, 0x12, 0x24, 0x45); // backdrop & conversation
            if (BackdropBorder != null)
            {
                BackdropBorder.Background = new SolidColorBrush(baseColor);
            }
            // Conversation area surface
            var convColor = Color.FromArgb(a, 0x12, 0x24, 0x45);
            if (ConversationTextBox != null)
            {
                ConversationTextBox.Background = new SolidColorBrush(convColor);
            }
            // Prompt area surface (slightly lighter)
            var promptColor = Color.FromArgb(a, 0x16, 0x2C, 0x56);
            if (PromptTextBox != null)
            {
                PromptTextBox.Background = new SolidColorBrush(promptColor);
            }
            // Prompt history area
            if (FindName("PromptHistoryGroupBox") is System.Windows.Controls.GroupBox phg)
            {
                phg.Background = new SolidColorBrush(Color.FromArgb(a, 0x10, 0x22, 0x40));
            }
            if (PromptHistoryListBox != null)
            {
                PromptHistoryListBox.Background = new SolidColorBrush(Color.FromArgb(a, 0x10, 0x22, 0x40));
            }
            if (AllPromptHistoryListBox != null)
            {
                AllPromptHistoryListBox.Background = new SolidColorBrush(Color.FromArgb(a, 0x10, 0x22, 0x40));
            }
        }
        catch { }
    }

    private static bool TryParseHexColor(string text, out Color rgb)
    {
        rgb = Color.FromRgb(0,0,0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("#")) s = s.Substring(1);
        if (s.Length == 6)
        {
            try
            {
                byte r = Convert.ToByte(s.Substring(0,2), 16);
                byte g = Convert.ToByte(s.Substring(2,2), 16);
                byte b = Convert.ToByte(s.Substring(4,2), 16);
                rgb = Color.FromRgb(r,g,b);
                return true;
            }
            catch { return false; }
        }
        if (s.Length == 8)
        {
            // Ignore provided alpha; we use slider alpha
            try
            {
                byte r = Convert.ToByte(s.Substring(2,2), 16);
                byte g = Convert.ToByte(s.Substring(4,2), 16);
                byte b = Convert.ToByte(s.Substring(6,2), 16);
                rgb = Color.FromRgb(r,g,b);
                return true;
            }
            catch { return false; }
        }
        return false;
    }

    private void ApplyColorsButton_OnClick(object? sender, RoutedEventArgs? e)
    {
        // Read BG/FG hex, apply to surfaces using current opacity
        if (!TryParseHexColor(BgColorTextBox?.Text ?? "#122445", out var bg))
        {
            bg = Color.FromRgb(0x12, 0x24, 0x45);
        }
        if (!TryParseHexColor(FgColorTextBox?.Text ?? "#C0C0C0", out var fg))
        {
            fg = Color.FromRgb(0xC0, 0xC0, 0xC0);
        }

        try
        {
            byte a = (byte)(Math.Round((OpacitySlider?.Value ?? 0.9) * 255));
            var bgA = Color.FromArgb(a, bg.R, bg.G, bg.B);
            var fgBrush = new SolidColorBrush(Color.FromArgb(0xFF, fg.R, fg.G, fg.B));

            // Secondary text color for trace: move 30% toward BG (less prominent, but readable).
            var traceRgb = BlendRgb(fg, bg, 0.30);
            var traceBrush = new SolidColorBrush(Color.FromArgb(0xFF, traceRgb.R, traceRgb.G, traceRgb.B));

            if (BackdropBorder != null) BackdropBorder.Background = new SolidColorBrush(bgA);
            if (ConversationTextBox != null)
            {
                ConversationTextBox.Background = new SolidColorBrush(bgA);
                ConversationTextBox.Foreground = fgBrush;
            }
            if (PromptTextBox != null)
            {
                // Slightly lighter for prompt area
                var prompt = Color.FromArgb(a, (byte)Math.Min(255, bg.R + 6), (byte)Math.Min(255, bg.G + 8), (byte)Math.Min(255, bg.B + 16));
                PromptTextBox.Background = new SolidColorBrush(prompt);
                PromptTextBox.Foreground = fgBrush;
            }
            if (FindName("PromptHistoryGroupBox") is System.Windows.Controls.GroupBox phg)
            {
                phg.Background = new SolidColorBrush(bgA);
                phg.Foreground = fgBrush;
            }
            if (PromptHistoryListBox != null)
            {
                PromptHistoryListBox.Background = new SolidColorBrush(bgA);
                PromptHistoryListBox.Foreground = fgBrush;
            }
            if (AllPromptHistoryListBox != null)
            {
                AllPromptHistoryListBox.Background = new SolidColorBrush(bgA);
                AllPromptHistoryListBox.Foreground = fgBrush;
            }
            if (TraceExpander != null)
            {
                TraceExpander.Background = new SolidColorBrush(bgA);
                TraceExpander.Foreground = traceBrush;
            }
            if (TraceTextBox != null)
            {
                TraceTextBox.Foreground = traceBrush;
            }
            if (BusyIndicatorTextBlock != null)
            {
                BusyIndicatorTextBlock.Foreground = traceBrush;
            }
            if (TraceSplitter != null)
            {
                var splitter = BlendRgb(bg, fg, 0.08); // subtle but visible
                TraceSplitter.Background = new SolidColorBrush(Color.FromArgb(0xFF, splitter.R, splitter.G, splitter.B));
            }
            if (RevitProgressTextBlock != null)
            {
                RevitProgressTextBlock.Foreground = traceBrush;
            }
            if (RevitProgressBar != null)
            {
                var track = BlendRgb(bg, fg, 0.12);
                RevitProgressBar.Foreground = fgBrush;
                RevitProgressBar.Background = new SolidColorBrush(Color.FromArgb(a, track.R, track.G, track.B));
            }
            // Window-level foreground
            Foreground = fgBrush;
        }
        catch { }

        // 色と不透明度の変更を保存
        SaveUiSettings();
    }

    private static Color BlendRgb(Color a, Color b, double t)
    {
        try
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
            byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
            byte bb = (byte)Math.Round(a.B + (b.B - a.B) * t);
            return Color.FromRgb(r, g, bb);
        }
        catch
        {
            return a;
        }
    }

    // Windows 10+ acrylic/blur behind via SetWindowCompositionAttribute
    private void EnableAcrylicBlur(bool enable, Color tintColor)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var accent = new ACCENT_POLICY
        {
            AccentState = enable ? ACCENT_STATE.ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_STATE.ACCENT_DISABLED,
            AccentFlags = 2,
            GradientColor = (tintColor.A << 24) | (tintColor.B << 16) | (tintColor.G << 8) | tintColor.R,
            AnimationId = 0
        };

        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private enum WINDOWCOMPOSITIONATTRIB
    {
        WCA_ACCENT_POLICY = 19
    }

    private enum ACCENT_STATE
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_ENABLE_HOSTBACKDROP = 5,
        ACCENT_INVALID_STATE = 6
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY
    {
        public ACCENT_STATE AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public WINDOWCOMPOSITIONATTRIB Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_TRAY = 0x00000002;
    private const uint FLASHW_TIMERNOFG = 0x0000000C;

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        var pos = e.GetPosition(this);
        bool nearEdge = pos.X < ResizeBorder || pos.X >= ActualWidth - ResizeBorder || pos.Y < ResizeBorder || pos.Y >= ActualHeight - ResizeBorder;
        if (!nearEdge && !IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            try { DragMove(); } catch { }
        }
    }

    private static bool IsInteractiveElement(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase
                || d is TextBox
                || d is PasswordBox
                || d is ComboBox
                || d is Slider
                || d is ListBox
                || d is ListView
                || d is TreeView)
            {
                return true;
            }
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaxButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ----- Window resize support when AllowsTransparency=True -----
    private void HookWndProcForResize()
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private const int WM_NCHITTEST = 0x84;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    // Match RootGrid's margin to avoid stealing clicks from the top toolbar controls.
    private const int ResizeBorder = 8; // px

    private static int GetXLParam(IntPtr lp)
    {
        int val = lp.ToInt32() & 0xFFFF;
        if (val > 32767) val -= 65536;
        return val;
    }

    private static int GetYLParam(IntPtr lp)
    {
        int val = (lp.ToInt32() >> 16) & 0xFFFF;
        if (val > 32767) val -= 65536;
        return val;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            // Mouse position in screen coords
            int x = GetXLParam(lParam);
            int y = GetYLParam(lParam);
            var mouse = PointFromScreen(new System.Windows.Point(x, y));
            double width = ActualWidth;
            double height = ActualHeight;

            // If the cursor is over an interactive control, don't steal the hit-test.
            try
            {
                var hit = InputHitTest(mouse) as DependencyObject;
                if (IsInteractiveElement(hit))
                {
                    handled = true;
                    return new IntPtr(HTCLIENT);
                }
            }
            catch { }

            bool left = mouse.X < ResizeBorder;
            bool right = mouse.X >= width - ResizeBorder;
            bool top = mouse.Y < ResizeBorder;
            bool bottom = mouse.Y >= height - ResizeBorder;

            if (top && left) { handled = true; return new IntPtr(HTTOPLEFT); }
            if (top && right) { handled = true; return new IntPtr(HTTOPRIGHT); }
            if (bottom && left) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
            if (bottom && right) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }

            if (left) { handled = true; return new IntPtr(HTLEFT); }
            if (right) { handled = true; return new IntPtr(HTRIGHT); }
            if (top) { handled = true; return new IntPtr(HTTOP); }
            if (bottom) { handled = true; return new IntPtr(HTBOTTOM); }

            handled = true;
            return new IntPtr(HTCLIENT);
        }
        return IntPtr.Zero;
    }

    private void ResizeLeftThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromLeft(e.HorizontalChange);
    }

    private void ResizeRightThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromRight(e.HorizontalChange);
    }

    private void ResizeTopThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromTop(e.VerticalChange);
    }

    private void ResizeBottomThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromBottom(e.VerticalChange);
    }

    private void ResizeTopLeftThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromLeft(e.HorizontalChange);
        ResizeFromTop(e.VerticalChange);
    }

    private void ResizeTopRightThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromRight(e.HorizontalChange);
        ResizeFromTop(e.VerticalChange);
    }

    private void ResizeBottomLeftThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromLeft(e.HorizontalChange);
        ResizeFromBottom(e.VerticalChange);
    }

    private void ResizeBottomRightThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ResizeFromRight(e.HorizontalChange);
        ResizeFromBottom(e.VerticalChange);
    }

    private bool TryGetResizeBounds(out double left, out double top, out double width, out double height)
    {
        left = Left;
        top = Top;

        width = double.IsNaN(Width) ? ActualWidth : Width;
        height = double.IsNaN(Height) ? ActualHeight : Height;

        if (double.IsNaN(left) || double.IsInfinity(left)) left = RestoreBounds.Left;
        if (double.IsNaN(top) || double.IsInfinity(top)) top = RestoreBounds.Top;
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) width = ActualWidth;
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0) height = ActualHeight;

        return !double.IsNaN(width) && !double.IsInfinity(width) && width > 0
               && !double.IsNaN(height) && !double.IsInfinity(height) && height > 0;
    }

    private void ResizeFromLeft(double horizontalChange)
    {
        if (WindowState != WindowState.Normal) return;
        if (!TryGetResizeBounds(out var left, out _, out var width, out _)) return;

        var rightEdge = left + width;
        var newWidth = Math.Max(MinWidth, width - horizontalChange);
        var newLeft = rightEdge - newWidth;

        Width = newWidth;
        Left = newLeft;
    }

    private void ResizeFromRight(double horizontalChange)
    {
        if (WindowState != WindowState.Normal) return;
        if (!TryGetResizeBounds(out _, out _, out var width, out _)) return;

        Width = Math.Max(MinWidth, width + horizontalChange);
    }

    private void ResizeFromTop(double verticalChange)
    {
        if (WindowState != WindowState.Normal) return;
        if (!TryGetResizeBounds(out _, out var top, out _, out var height)) return;

        var bottomEdge = top + height;
        var newHeight = Math.Max(MinHeight, height - verticalChange);
        var newTop = bottomEdge - newHeight;

        Height = newHeight;
        Top = newTop;
    }

    private void ResizeFromBottom(double verticalChange)
    {
        if (WindowState != WindowState.Normal) return;
        if (!TryGetResizeBounds(out _, out _, out _, out var height)) return;

        Height = Math.Max(MinHeight, height + verticalChange);
    }

    private void AppendSeparatorLine()
    {
        // 枠幅いっぱいをカバーする横線（会話欄の実幅に合わせる）
        var width = ConversationTextBox.ActualWidth;
        if (width <= 0)
        {
            AppendLine(new string('─', 80));
            return;
        }

        // 会話欄と同じフォントで「─」1文字の幅を正確に測定
        var typeface = new Typeface(
            ConversationTextBox.FontFamily,
            ConversationTextBox.FontStyle,
            ConversationTextBox.FontWeight,
            ConversationTextBox.FontStretch);

        var formatted = new FormattedText(
            "─",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            ConversationTextBox.FontSize,
            Brushes.Transparent,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var charWidth = formatted.WidthIncludingTrailingWhitespace;
        if (charWidth <= 0)
        {
            charWidth = ConversationTextBox.FontSize * 0.6;
        }

        // 左右の余白を少し差し引いて行数を算出
        var usableWidth = Math.Max(0, width - 16);
        var count = (int)(usableWidth / charWidth);
        if (count < 20) { count = 20; }
        AppendLine(new string('─', count));
    }

    private static string GetGuiLogDirectory()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(docs))
        {
            docs = AppContext.BaseDirectory;
        }

        var dir = Path.Combine(docs, "Codex_MCP", "Codex", "GUI_Log");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateSessionLogFile(SessionInfo session)
    {
        var dir = GetGuiLogDirectory();
        var baseName = string.IsNullOrWhiteSpace(session.Name) ? "Session" : session.Name;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(c, '_');
        }

        var fileName = $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var fullPath = Path.Combine(dir, fileName);

        var header = new StringBuilder();
        header.AppendLine($"# Codex GUI Log");
        header.AppendLine($"# Session Name : {session.Name}");
        header.AppendLine($"# Session Id   : {session.Id}");
        header.AppendLine($"# Started At   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        header.AppendLine();
        File.WriteAllText(fullPath, header.ToString(), Encoding.UTF8);

        return fullPath;
    }

    private void TrySnapshotStartupClipboard()
    {
        try
        {
            _startupClipboardSnapshot = Clipboard.GetDataObject();
        }
        catch
        {
            _startupClipboardSnapshot = null;
        }
    }

    private void ScheduleStartupClipboardRestore()
    {
        if (_startupClipboardSnapshot == null)
        {
            return;
        }

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TryRestoreStartupClipboardOnce();
        };
        timer.Start();
    }

    private void TryRestoreStartupClipboardOnce()
    {
        if (_startupClipboardSnapshot == null || _startupClipboardRestored)
        {
            return;
        }

        try
        {
            bool hasContent = false;
            try
            {
                var current = Clipboard.GetDataObject();
                if (current != null)
                {
                    var formats = current.GetFormats();
                    if (formats != null && formats.Length > 0)
                    {
                        hasContent = true;
                    }
                }
            }
            catch
            {
                hasContent = true;
            }

            // すでに何らかの内容が入っている場合はユーザー操作を優先して復元しない
            if (hasContent)
            {
                _startupClipboardSnapshot = null;
                return;
            }

            Clipboard.SetDataObject(_startupClipboardSnapshot, true);
            _startupClipboardRestored = true;
            _startupClipboardSnapshot = null;
        }
        catch
        {
            // 復元失敗は無視
        }
    }
}

public class UiSettings
{
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public WindowState? WindowState { get; set; }
    public string? BgColorHex { get; set; }
    public string? FgColorHex { get; set; }
    public double? Opacity { get; set; }
    public List<string>? RecentModels { get; set; }
}

public class SessionInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string DisplayName
        => MainWindow.TruncateForUi(Name, MainWindow.MaxSessionNameLength);

    public string? ProjectId { get; set; } // docGuid

    public string? ProjectName { get; set; }

    /// <summary>
    /// 実際の Codex セッションID を保存したい場合に使用します。
    /// このサンプル実装では未使用ですが、PowerShell スクリプト側でマッピングに利用できます。
    /// </summary>
    public string? CodexSessionId { get; set; }

    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    public string? Model { get; set; }

    public string? ReasoningEffort { get; set; }

    public List<string>? PromptHistory { get; set; }

    /// <summary>
    /// このセッションで RevitMCP 用の初期インストラクションを Codex に送ったかどうか。
    /// </summary>
    public bool HasSentRevitIntro { get; set; }
}

internal sealed class PromptHistoryItem
{
    public string Display { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
}
