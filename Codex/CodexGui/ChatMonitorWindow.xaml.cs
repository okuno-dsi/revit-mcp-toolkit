using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CodexGui;

public partial class ChatMonitorWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly ObservableCollection<ChatEventItem> _items = new();
    private readonly DispatcherTimer _pollTimer;
    private HttpClient? _client;
    private int _clientPort;
    private bool _refreshing;

    private sealed class ChatEventItem
    {
        public string EventId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string ThreadId { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Ts { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public ChatMonitorWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        InitializeComponent();

        MessagesListBox.ItemsSource = _items;

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += async (_, _) =>
        {
            if (AutoPollCheckBox.IsChecked == true)
            {
                await RefreshAsync().ConfigureAwait(false);
            }
        };

        Loaded += async (_, _) =>
        {
            AutoPortFromStateFile();
            _pollTimer.Start();
            await RefreshAsync().ConfigureAwait(false);
        };

        Closed += (_, _) =>
        {
            try { _pollTimer.Stop(); } catch { }
            try { _client?.Dispose(); } catch { }
        };
    }

    private static string GetRevitMcpStateFilePath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "RevitMCP", "server_state.json");
    }

    private static string GetChatRootStateFilePath(int port)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "RevitMCP", "chat", $"chat_root_{port}.json");
    }

    private sealed class ChatContext
    {
        public string DocPathHint { get; set; } = string.Empty;
        public string DocKey { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
        public string ProjectRoot { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    private static ChatContext? TryLoadChatContext(int port)
    {
        try
        {
            if (port <= 0) return null;
            var path = GetChatRootStateFilePath(port);
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var ctx = new ChatContext { RawJson = json };
            if (root.TryGetProperty("docPathHint", out var dph)) ctx.DocPathHint = (dph.ToString() ?? "").Trim();
            if (root.TryGetProperty("docKey", out var dk)) ctx.DocKey = (dk.ToString() ?? "").Trim();
            if (root.TryGetProperty("projectKey", out var pk)) ctx.ProjectKey = (pk.ToString() ?? "").Trim();
            if (root.TryGetProperty("projectRoot", out var pr)) ctx.ProjectRoot = (pr.ToString() ?? "").Trim();
            return ctx;
        }
        catch
        {
            return null;
        }
    }

    private void AutoPortFromStateFile()
    {
        try
        {
            var path = GetRevitMcpStateFilePath();
            if (!File.Exists(path))
            {
                SetStatus($"No port state file: {path}");
                return;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("port", out var p)) return;
            if (!p.TryGetInt32(out var port) || port <= 0) return;
            PortTextBox.Text = port.ToString();
            SetStatus($"Auto port={port}");
        }
        catch (Exception ex)
        {
            SetStatus("Auto port failed: " + ex.Message);
        }
    }

    private HttpClient GetClient(int port)
    {
        if (_client != null && _clientPort == port) return _client;
        _client?.Dispose();
        _clientPort = port;
        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        return _client;
    }

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return int.TryParse(text.Trim(), out value);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (!TryParseInt(PortTextBox.Text, out var port) || port <= 0)
            {
                SetStatus("Invalid port.");
                return;
            }

            var channel = (ChannelTextBox.Text ?? string.Empty).Trim();
            if (!TryParseInt(LimitTextBox.Text, out var limit) || limit <= 0) limit = 120;
            limit = Math.Min(limit, 500);

            var ctx = TryLoadChatContext(port);
            // When multiple .rvt exist in the same folder, chat is keyed by DocKey (project identifier).
            // The add-in writes chat_root_<port>.json when it knows the current project context.
            if (ctx == null || string.IsNullOrWhiteSpace(ctx.DocPathHint))
            {
                SetStatus("Chat context not ready. Open Revit, activate a view, and post once in MCP Chat.");
                return;
            }

            var body = new
            {
                jsonrpc = "2.0",
                id = "chatmon:" + Guid.NewGuid().ToString("N"),
                @params = new
                {
                    docPathHint = ctx.DocPathHint,
                    docKey = ctx.DocKey,
                    channel = string.IsNullOrWhiteSpace(channel) ? null : channel,
                    limit = limit
                }
            };

            var json = JsonSerializer.Serialize(body);
            var client = GetClient(port);
            using var resp = await client.PostAsync("rpc/chat.list", new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                SetStatus($"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");
                return;
            }

            using var doc = JsonDocument.Parse(txt);
            if (!doc.RootElement.TryGetProperty("result", out var result))
            {
                SetStatus("No JSON-RPC result.");
                return;
            }

            if (!result.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
            {
                var msg = result.TryGetProperty("msg", out var m) ? m.ToString() : "chat.list failed";
                SetStatus(msg);
                return;
            }

            if (!result.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                SetStatus("No items.");
                return;
            }

            var next = new List<ChatEventItem>();
            foreach (var it in items.EnumerateArray())
            {
                var evId = it.TryGetProperty("eventId", out var eid) ? eid.ToString() : string.Empty;
                if (string.IsNullOrWhiteSpace(evId)) continue;

                var ch = it.TryGetProperty("channel", out var chEl) ? chEl.ToString() : string.Empty;
                var thr = it.TryGetProperty("threadId", out var thEl) ? thEl.ToString() : string.Empty;
                var ts = it.TryGetProperty("ts", out var tsEl) ? tsEl.ToString() : string.Empty;

                var actorName = "";
                if (it.TryGetProperty("actor", out var actor) && actor.ValueKind == JsonValueKind.Object)
                {
                    actorName = actor.TryGetProperty("name", out var an) ? an.ToString() : "";
                }

                var text2 = "";
                if (it.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
                {
                    text2 = payload.TryGetProperty("text", out var tx) ? tx.ToString() : "";
                }

                var display = BuildDisplay(ts, actorName, text2);

                next.Add(new ChatEventItem
                {
                    EventId = evId,
                    Channel = ch,
                    ThreadId = thr,
                    Ts = ts,
                    ActorName = actorName,
                    Text = text2,
                    DisplayText = display,
                    RawJson = it.GetRawText()
                });
            }

            await Dispatcher.InvokeAsync(() =>
            {
                _items.Clear();
                foreach (var x in next)
                {
                    _items.Add(x);
                }
                if (_items.Count > 0) MessagesListBox.SelectedIndex = _items.Count - 1;
            });

            SetStatus($"OK ({next.Count}) {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            SetStatus("ERR: " + ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static string BuildDisplay(string ts, string actor, string text)
    {
        var tsShort = ts;
        if (DateTimeOffset.TryParse(ts, out var dto))
        {
            tsShort = dto.ToLocalTime().ToString("HH:mm");
        }
        return $"[{tsShort}] {actor}: {text}";
    }

    private void SetStatus(string text)
    {
        Dispatcher.Invoke(() => { StatusTextBlock.Text = text ?? ""; });
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    private void AutoPortButton_OnClick(object sender, RoutedEventArgs e)
    {
        AutoPortFromStateFile();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CopyToCodexButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessagesListBox.SelectedItem is not ChatEventItem item)
        {
            MessageBox.Show(this, "コピーするメッセージを選択してください。", "Chat Monitor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var text = (item.Text ?? string.Empty).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("Empty message.");
                return;
            }

            // Copy + paste into Codex GUI (but never auto-execute).
            Clipboard.SetText(text);
            _mainWindow.TrySendPromptFromExternal(text);
            try
            {
                _mainWindow.WindowState = _mainWindow.WindowState == WindowState.Minimized ? WindowState.Normal : _mainWindow.WindowState;
                _mainWindow.Activate();
            }
            catch { }
            SetStatus("Copied → Codex.");
        }
        catch (Exception ex)
        {
            SetStatus("Copy→Codex failed: " + ex.Message);
        }
    }

    private void CopySelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessagesListBox.SelectedItem is not ChatEventItem item) return;
        try
        {
            Clipboard.SetText(item.DisplayText ?? string.Empty);
            SetStatus("Copied.");
        }
        catch (Exception ex)
        {
            SetStatus("Copy failed: " + ex.Message);
        }
    }

    private void CopyAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = string.Join(Environment.NewLine, _items.Select(i => i.DisplayText ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(text)) return;
            Clipboard.SetText(text);
            SetStatus("Copied all.");
        }
        catch (Exception ex)
        {
            SetStatus("Copy all failed: " + ex.Message);
        }
    }
}
