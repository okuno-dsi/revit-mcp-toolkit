using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CodexGui;

public partial class MainWindow : Window
{
    private const string SessionsFileName = "CodexGuiSessions.json";
    private const string RevitIntroInstruction =
        "このディレクトリにあるREAD_FIRST_RevitMCP_EN.md を読んでRevitへの接続を準備してください。" +
        "一時保存データはすべてWorkフォルダ内にプロジェクト専用のフォルダを作成してそこに保存すること。" +
        "セキュリティ以上危険を及ぼす可能性のある操作やスクリプトの作成やコードの作成は行わないこと。" +
        "システムディレクトリやファイルには一切触れないこと。" +
        "ユーザーには可能な限り親切に対応すること。";

    private readonly List<SessionInfo> _sessions = new();
    private bool _isSending;
    private double _halPulseValue;
    private bool _halPulseIncreasing;
    private bool _isBusy;
    private readonly string _baseTitle = "Codex GUI";

    // These static fields are used to pass per-call options to the
    // PowerShell process builder without complicating the signature.
    private static string? _currentModelForProcess;
    private static bool _currentIsStatusRequestForProcess;
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

    public MainWindow()
    {
        InitializeComponent();
        _baseTitle = Title;
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
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSessions();
        RefreshSessionComboBox();

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

        // Busy インジケータ初期表示（停止中は 00:00 を赤で表示）
        BusyIndicatorTextBlock.Text = "00:00";
        BusyIndicatorTextBlock.Foreground = BusyRedBrush;

        // 起動時のクリップボード内容をスナップショットし、
        // GUI 起動の影響で消えてしまった場合にのみ一度だけ復元する。
        TrySnapshotStartupClipboard();
        ScheduleStartupClipboardRestore();

        // HAL 君風のタスクバー用アイコンを生成
        CreateHalLikeTaskbarIcon();
    }

    private static string GetSessionsFilePath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, SessionsFileName);
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
            ModelTextBox.Text = string.Empty;
            PromptHistoryListBox.ItemsSource = null;
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

    private void NewSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = $"Session {_sessions.Count + 1}";
        var session = new SessionInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
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
        if (SessionComboBox.SelectedItem is SessionInfo session)
        {
            ModelTextBox.Text = session.Model ?? string.Empty;
            if (SessionIdTextBlock != null)
            {
                SessionIdTextBlock.Text = session.Id;
            }
            RefreshPromptHistory(session);
        }
        else
        {
            ModelTextBox.Text = string.Empty;
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
        var modelText = ModelTextBox.Text?.Trim();
        session.Model = string.IsNullOrWhiteSpace(modelText) ? null : modelText;
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
            return;
        }

        EnsurePromptHistoryInitialized(session);
        PromptHistoryListBox.ItemsSource = null;
        PromptHistoryListBox.ItemsSource = session.PromptHistory;
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
                Opacity = OpacitySlider?.Value
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

    private void UsePromptButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PromptHistoryListBox.SelectedItems == null || PromptHistoryListBox.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "使用するプロンプトを選択してください。", "Codex GUI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existing = PromptTextBox.Text ?? string.Empty;
        var builder = new StringBuilder(existing);
        var inserted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in PromptHistoryListBox.SelectedItems)
        {
            if (item is not string prompt || string.IsNullOrWhiteSpace(prompt))
            {
                continue;
            }

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

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
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

            var (output, error, exitCode, hadStreamingOutput) = await RunPromptThroughPowerShellAsync(promptForCodex, session, isStatusRequest: false);

            if (_wasCancelledByUser)
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

        var argsBuilder = new StringBuilder();
        argsBuilder.Append("-NoLogo -NoProfile -ExecutionPolicy Bypass ");
        argsBuilder.Append("-File ");
        argsBuilder.Append('"').Append(scriptPath).Append('"');
        argsBuilder.Append(" -PromptFile ");
        argsBuilder.Append('"').Append(promptFilePath).Append('"');
        argsBuilder.Append(" -SessionId ");
        argsBuilder.Append('"').Append(sessionId).Append('"');

        // Pass model (optional) and status flag
        argsBuilder.Append(" -Model ");
        argsBuilder.Append('"').Append(_currentModelForProcess ?? string.Empty).Append('"');
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
        bool isStatusRequest)
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
            _currentIsStatusRequestForProcess = isStatusRequest;

            var psi = CreatePowerShellStartInfo(scriptPath, tempFile, session.Id);
            using var process = new Process { StartInfo = psi };
            _currentPwshProcess = process;

            process.Start();

            var outputBuilder = new StringBuilder();
            var errorTask = process.StandardError.ReadToEndAsync();
            var hadStreamingOutput = false;
            var isFirstAssistantChunk = true;
            string? lastUiLine = null;
            var seenLines = new HashSet<string>(StringComparer.Ordinal);
            var stopAfterTokensUsed = false;

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
                                    mw2.AppendSystemMessage(cleanLine);
                                }
                            });

                            stopAfterTokensUsed = true;
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

                                if (isFirstAssistantChunk)
                                {
                                    isFirstAssistantChunk = false;
                                    mw.AppendAssistantMessage(cleanLine);
                                }
                                else
                                {
                                    mw.AppendLine(cleanLine);
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
            // Window-level foreground
            Foreground = fgBrush;
        }
        catch { }

        // 色と不透明度の変更を保存
        SaveUiSettings();
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

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        var pos = e.GetPosition(this);
        bool nearEdge = pos.X <= ResizeBorder || pos.X >= ActualWidth - ResizeBorder || pos.Y <= ResizeBorder || pos.Y >= ActualHeight - ResizeBorder;
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

    private const int ResizeBorder = 16; // px, broader for easier hit

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

            bool left = mouse.X <= ResizeBorder;
            bool right = mouse.X >= width - ResizeBorder;
            bool top = mouse.Y <= ResizeBorder;
            bool bottom = mouse.Y >= height - ResizeBorder;

            if (left && top) { handled = true; return new IntPtr(HTTOPLEFT); }
            if (right && top) { handled = true; return new IntPtr(HTTOPRIGHT); }
            if (left && bottom) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
            if (right && bottom) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
            if (left) { handled = true; return new IntPtr(HTLEFT); }
            if (right) { handled = true; return new IntPtr(HTRIGHT); }
            if (top) { handled = true; return new IntPtr(HTTOP); }
            if (bottom) { handled = true; return new IntPtr(HTBOTTOM); }

            handled = true;
            return new IntPtr(HTCLIENT);
        }
        return IntPtr.Zero;
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
}

public class SessionInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 実際の Codex セッションID を保存したい場合に使用します。
    /// このサンプル実装では未使用ですが、PowerShell スクリプト側でマッピングに利用できます。
    /// </summary>
    public string? CodexSessionId { get; set; }

    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    public string? Model { get; set; }

    public List<string>? PromptHistory { get; set; }

    /// <summary>
    /// このセッションで RevitMCP 用の初期インストラクションを Codex に送ったかどうか。
    /// </summary>
    public bool HasSentRevitIntro { get; set; }
}
