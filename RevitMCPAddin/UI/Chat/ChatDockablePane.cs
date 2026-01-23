#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Rvt = Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.RevitUI;

namespace RevitMCPAddin.UI.Chat
{
    internal static class ChatDockablePaneIds
    {
        public static readonly Guid PaneGuid = new Guid("b5cfe62c-4c71-4a25-86d0-7d1d6af6f0c8");
        public static Rvt.DockablePaneId PaneId => new Rvt.DockablePaneId(PaneGuid);
        public const string PaneTitle = "MCP Chat";
    }

    internal sealed class ChatDockablePaneProvider : Rvt.IDockablePaneProvider
    {
        public void SetupDockablePane(Rvt.DockablePaneProviderData data)
        {
            if (data == null) return;

            data.FrameworkElement = new ChatPaneControl();

            var state = new Rvt.DockablePaneState
            {
                DockPosition = Rvt.DockPosition.Right
            };
            data.InitialState = state;

            // Keep closed by default to avoid disturbing non-chat users.
            data.VisibleByDefault = false;
        }
    }

    internal sealed class ChatPaneControl : UserControl
    {
        private readonly TextBox _channel;
        private readonly ListBox _list;
        private readonly TextBox _input;
        private readonly TextBlock _status;
        private readonly CheckBox _autoRefresh;
        private readonly DispatcherTimer _autoRefreshTimer;
        private bool _busy;
        private bool _isUnloaded;
        private string _lastResolvedDocPathHint = string.Empty;
        private string _lastResolvedDocKey = string.Empty;

        public ChatPaneControl()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Top bar
            var top = new DockPanel { LastChildFill = true, Margin = new Thickness(6) };
            Grid.SetRow(top, 0);
            root.Children.Add(top);

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(left, Dock.Left);
            top.Children.Add(left);

            left.Children.Add(new TextBlock { Text = "Channel:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _channel = new TextBox { Width = 220, Text = "ws://Project/General" };
            left.Children.Add(_channel);

            var btnRefresh = new Button { Content = "Refresh", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            btnRefresh.Click += async (_, __) => await RefreshAsync();
            left.Children.Add(btnRefresh);

            _autoRefresh = new CheckBox { Content = "Auto", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            left.Children.Add(_autoRefresh);

            var btnInvite = new Button { Content = "Invite", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            btnInvite.Click += async (_, __) => await InviteAsync();
            left.Children.Add(btnInvite);

            var btnCopy = new Button { Content = "Copy", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            btnCopy.Click += (_, __) => CopySelected();
            left.Children.Add(btnCopy);

            var btnCopyAll = new Button { Content = "Copy All", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            btnCopyAll.Click += (_, __) => CopyAll();
            left.Children.Add(btnCopyAll);

            _status = new TextBlock { Text = "", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            top.Children.Add(_status);

            // Messages list
            _list = new ListBox { Margin = new Thickness(6) };
            Grid.SetRow(_list, 1);
            root.Children.Add(_list);

            // Bottom input
            var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(6) };
            Grid.SetRow(bottom, 2);
            root.Children.Add(bottom);

            var btnSend = new Button { Content = "Send", Width = 70, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(btnSend, Dock.Right);
            btnSend.Click += async (_, __) => await SendAsync();
            bottom.Children.Add(btnSend);

            _input = new TextBox
            {
                AcceptsReturn = true,
                Height = 54,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap
            };
            bottom.Children.Add(_input);

            Content = root;

            Loaded += async (_, __) =>
            {
                _isUnloaded = false;
                try { await RefreshAsync(); } catch { }
                try { _autoRefreshTimer.Start(); } catch { }
            };

            Unloaded += (_, __) =>
            {
                _isUnloaded = true;
                try { _autoRefreshTimer.Stop(); } catch { }
            };

            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _autoRefreshTimer.Tick += async (_, __) =>
            {
                try
                {
                    if (_isUnloaded) return;
                    if (GetIsCheckedSafe(_autoRefresh) != true) return;
                    if (!IsVisible) return;
                    await RefreshAsync();
                }
                catch { }
            };
        }

        private string GetTextSafe(TextBox tb)
        {
            try
            {
                if (tb.Dispatcher.CheckAccess()) return tb.Text ?? string.Empty;
                return tb.Dispatcher.Invoke(() => tb.Text ?? string.Empty);
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool? GetIsCheckedSafe(CheckBox cb)
        {
            try
            {
                if (cb.Dispatcher.CheckAccess()) return cb.IsChecked;
                return cb.Dispatcher.Invoke(() => cb.IsChecked);
            }
            catch
            {
                return null;
            }
        }

        private void CopySelected()
        {
            try
            {
                string? text = null;
                if (_list.Dispatcher.CheckAccess())
                    text = _list.SelectedItem as string;
                else
                    text = _list.Dispatcher.Invoke(() => _list.SelectedItem as string);

                text = (text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text)) return;
                Clipboard.SetText(text);
                SetStatus("Copied.");
            }
            catch (Exception ex)
            {
                SetStatus("Copy ERR: " + ex.Message);
            }
        }

        private void CopyAll()
        {
            try
            {
                List<string> lines = new List<string>();
                if (_list.Dispatcher.CheckAccess())
                {
                    foreach (var it in _list.Items)
                    {
                        var s = it as string;
                        if (!string.IsNullOrWhiteSpace(s)) lines.Add(s);
                    }
                }
                else
                {
                    lines = _list.Dispatcher.Invoke(() =>
                    {
                        var xs = new List<string>();
                        foreach (var it in _list.Items)
                        {
                            var s = it as string;
                            if (!string.IsNullOrWhiteSpace(s)) xs.Add(s);
                        }
                        return xs;
                    });
                }

                var text = string.Join(Environment.NewLine, lines.Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)));
                if (string.IsNullOrWhiteSpace(text)) return;
                Clipboard.SetText(text);
                SetStatus("Copied all.");
            }
            catch (Exception ex)
            {
                SetStatus("CopyAll ERR: " + ex.Message);
            }
        }

        private string ResolveDocPathHint()
        {
            try
            {
                var hint = (AppServices.CurrentDocPathHint ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(hint)) { _lastResolvedDocPathHint = hint; return hint; }

                // Fallback: selection monitor keeps doc path even when ViewActivated didn't fire yet.
                try
                {
                    var snap = SelectionStash.GetSnapshot();
                    hint = (snap != null ? snap.DocPath : string.Empty) ?? string.Empty;
                    hint = hint.Trim();
                }
                catch { hint = string.Empty; }

                if (!string.IsNullOrWhiteSpace(hint)) { _lastResolvedDocPathHint = hint; return hint; }

                // Last-resort: keep last resolved hint (helps when the doc loses focus temporarily).
                if (!string.IsNullOrWhiteSpace(_lastResolvedDocPathHint)) return _lastResolvedDocPathHint;

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ResolveDocKey()
        {
            try
            {
                var key = (AppServices.CurrentDocKey ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(key)) { _lastResolvedDocKey = key; return key; }

                // Fallback: selection snapshot may carry DocKey.
                try
                {
                    var snap = SelectionStash.GetSnapshot();
                    key = (snap != null ? snap.DocKey : string.Empty) ?? string.Empty;
                    key = key.Trim();
                }
                catch { key = string.Empty; }

                if (!string.IsNullOrWhiteSpace(key)) { _lastResolvedDocKey = key; return key; }
                if (!string.IsNullOrWhiteSpace(_lastResolvedDocKey)) return _lastResolvedDocKey;
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task RefreshAsync()
        {
            if (_isUnloaded) return;
            if (_busy) return;
            _busy = true;
            try
            {
                SetStatus("Loading...");
                if (IsChatDisabled(out var disabledReason))
                {
                    SetStatus(disabledReason);
                    return;
                }
                var docPathHint = ResolveDocPathHint();
                if (string.IsNullOrWhiteSpace(docPathHint))
                {
                    SetStatus("No project context. Open a saved project and activate a view (then retry).");
                    return;
                }

                var channelText = GetTextSafe(_channel);
                var p = new JObject
                {
                    ["docPathHint"] = docPathHint,
                    ["docKey"] = ResolveDocKey(),
                    ["channel"] = string.IsNullOrWhiteSpace(channelText) ? "ws://Project/General" : channelText.Trim(),
                    ["limit"] = 60
                };
                var resTok = await ChatRpcClient.CallAsync("chat.list", p);
                var res = resTok as JObject;
                if (res == null || !(res.Value<bool?>("ok") ?? false))
                {
                    string code = (string?)res?["code"] ?? "UNKNOWN";
                    string msg = (string?)res?["msg"] ?? "";
                    SetStatus("chat.list failed: " + code + (string.IsNullOrWhiteSpace(msg) ? "" : (" " + msg)));
                    try { RevitLogger.Warn($"ChatPane: chat.list failed code={code} msg={msg} docPathHint='{docPathHint}'"); } catch { }
                    return;
                }

                var items = res["items"] as JArray;
                var lines = new List<string>();
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        var ev = it as JObject;
                        if (ev == null) continue;
                        lines.Add(FormatEventLine(ev));
                    }
                }

                await _list.Dispatcher.InvokeAsync(() =>
                {
                    if (_isUnloaded) return;
                    _list.ItemsSource = lines;
                    if (_list.Items.Count > 0) _list.ScrollIntoView(_list.Items[_list.Items.Count - 1]);
                });
                SetStatus($"OK ({lines.Count})");
            }
            catch (Exception ex)
            {
                SetStatus("ERR: " + ex.Message);
                try { RevitLogger.Warn($"ChatPane: RefreshAsync exception: {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task SendAsync()
        {
            if (_isUnloaded) return;
            var text = GetTextSafe(_input).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            if (_busy) return;
            _busy = true;
            try
            {
                SetStatus("Sending...");
                if (IsChatDisabled(out var disabledReason))
                {
                    SetStatus(disabledReason);
                    return;
                }
                var docPathHint = ResolveDocPathHint();
                if (string.IsNullOrWhiteSpace(docPathHint))
                {
                    SetStatus("No project context. Open a saved project and activate a view (then retry).");
                    return;
                }

                var actorId = (AppServices.CurrentUserId ?? Environment.UserName).Trim();
                if (string.IsNullOrWhiteSpace(actorId)) actorId = Environment.UserName;

                var channelText = GetTextSafe(_channel);
                var p = new JObject
                {
                    ["docPathHint"] = docPathHint,
                    ["docKey"] = ResolveDocKey(),
                    ["channel"] = string.IsNullOrWhiteSpace(channelText) ? "ws://Project/General" : channelText.Trim(),
                    ["text"] = text,
                    ["type"] = "note",
                    ["actor"] = new JObject
                    {
                        ["type"] = "human",
                        ["id"] = actorId,
                        ["name"] = AppServices.CurrentUserName ?? actorId
                    }
                };

                var resTok = await ChatRpcClient.CallAsync("chat.post", p);
                var res = resTok as JObject;
                if (res == null || !(res.Value<bool?>("ok") ?? false))
                {
                    string code = (string?)res?["code"] ?? "UNKNOWN";
                    string msg = (string?)res?["msg"] ?? "";
                    SetStatus("chat.post failed: " + code + (string.IsNullOrWhiteSpace(msg) ? "" : (" " + msg)));
                    try { RevitLogger.Warn($"ChatPane: chat.post failed code={code} msg={msg} docPathHint='{docPathHint}'"); } catch { }
                    return;
                }

                await _input.Dispatcher.InvokeAsync(() => { _input.Text = ""; });
                SetStatus("Sent.");
                try { RevitLogger.Info($"ChatPane: chat.post ok channel='{(string.IsNullOrWhiteSpace(channelText) ? "ws://Project/General" : channelText.Trim())}' docPathHint='{docPathHint}'"); } catch { }
            }
            catch (Exception ex)
            {
                SetStatus("ERR: " + ex.Message);
                try { RevitLogger.Warn($"ChatPane: SendAsync exception: {ex.GetType().Name}: {ex.Message}"); } catch { }
            }
            finally
            {
                _busy = false;
            }

            // Refresh after releasing _busy.
            try { await RefreshAsync(); } catch { }
        }

        private async Task InviteAsync()
        {
            try
            {
                if (IsChatDisabled(out var disabledReason))
                {
                    SetStatus(disabledReason);
                    return;
                }
                var dlg = new InviteInputWindow();
                dlg.Owner = Window.GetWindow(this);
                var ok = dlg.ShowDialog() ?? false;
                if (!ok) return;

                var targets = (dlg.TargetUserIds ?? string.Empty)
                    .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (targets.Length == 0) return;

                var actorId = (AppServices.CurrentUserId ?? Environment.UserName).Trim();
                if (string.IsNullOrWhiteSpace(actorId)) actorId = Environment.UserName;

                var mentions = new JArray(targets);
                var text = "Invite: " + string.Join(" ", targets.Select(t => "@" + t));

                var p = new JObject
                {
                    ["docPathHint"] = ResolveDocPathHint(),
                    ["docKey"] = ResolveDocKey(),
                    ["channel"] = "ws://Project/Invites",
                    ["text"] = text,
                    ["type"] = "system",
                    ["mentions"] = mentions,
                    ["actor"] = new JObject
                    {
                        ["type"] = "human",
                        ["id"] = actorId,
                        ["name"] = AppServices.CurrentUserName ?? actorId
                    }
                };

                await ChatRpcClient.CallAsync("chat.post", p);
                try
                {
                    await _channel.Dispatcher.InvokeAsync(() => { _channel.Text = "ws://Project/General"; });
                }
                catch { /* ignore */ }
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Invite ERR: " + ex.Message);
            }
        }

        private void SetStatus(string text)
        {
            try
            {
                if (_status.Dispatcher.CheckAccess())
                    _status.Text = text ?? "";
                else
                    _status.Dispatcher.Invoke(() => { _status.Text = text ?? ""; });
            }
            catch { /* ignore */ }
        }

        private static string FormatEventLine(JObject ev)
        {
            string tsStr = (string?)ev["ts"] ?? "";
            string actor = (string?)ev["actor"]?["name"] ?? "(unknown)";
            string text = (string?)ev["payload"]?["text"] ?? "";

            string tsShort = tsStr;
            try
            {
                if (DateTimeOffset.TryParse(tsStr, out var dto))
                    tsShort = dto.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            catch { }

            return $"[{tsShort}] {actor}: {text}";
        }

        private static bool IsChatDisabled(out string reason)
        {
            reason = AppServices.CurrentChatDisabledReason ?? string.Empty;
            if (AppServices.CurrentDocIsCloud)
            {
                if (string.IsNullOrWhiteSpace(reason))
                    reason = "Chat disabled for cloud models (ACC/BIM 360). Local filesystem path is required for chat storage.";
                return true;
            }
            return false;
        }
    }

    internal sealed class InviteInputWindow : Window
    {
        private readonly TextBox _input;
        public string? TargetUserIds { get; private set; }

        public InviteInputWindow()
        {
            Title = "Invite to MCP Chat";
            Width = 420;
            Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new DockPanel { Margin = new Thickness(12) };
            Content = root;

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(btns, Dock.Bottom);
            root.Children.Add(btns);

            var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 8, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 8, 0, 0), IsCancel = true };
            ok.Click += (_, __) => { TargetUserIds = _input.Text; DialogResult = true; Close(); };
            cancel.Click += (_, __) => { DialogResult = false; Close(); };
            btns.Children.Add(ok);
            btns.Children.Add(cancel);

            var label = new TextBlock { Text = "User IDs (comma or space separated):", Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(label, Dock.Top);
            root.Children.Add(label);

            _input = new TextBox { MinHeight = 30 };
            root.Children.Add(_input);
        }
    }

    internal sealed class InviteToastWindow : Window
    {
        private InviteToastWindow(string text)
        {
            Width = 360;
            Height = 110;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10)
            };

            var tb = new TextBlock
            {
                Text = text ?? "",
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            border.Child = tb;
            Content = border;

            Loaded += (_, __) =>
            {
                try
                {
                    var area = SystemParameters.WorkArea;
                    Left = area.Right - Width - 12;
                    Top = area.Bottom - Height - 12;
                }
                catch { }

                // Auto-close
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(12)
                };
                timer.Tick += (_, __) =>
                {
                    try { timer.Stop(); } catch { }
                    try { Close(); } catch { }
                };
                timer.Start();
            };

            MouseDown += (_, __) =>
            {
                try { Close(); } catch { }
            };
        }

        public static void ShowToast(string text)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;
                app.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var w = new InviteToastWindow(text);
                        w.Show();
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
