#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.UI.Chat;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Polls the server-local chat inbox for invite-style messages and shows a non-blocking toast.
    /// </summary>
    internal static class ChatInviteNotifier
    {
        private static readonly object _lock = new object();
        private static Timer? _timer;
        private static int _polling;
        private static DateTimeOffset _lastSeenTsUtc = DateTimeOffset.MinValue;
        private static string? _lastDocPathHint;

        public static void Start()
        {
            lock (_lock)
            {
                if (_timer != null) return;
                _timer = new Timer(_ => Tick(), null, dueTime: 8000, period: 15000);
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                try { _timer?.Dispose(); } catch { }
                _timer = null;
            }
        }

        public static void UpdateContextFromDocument(Document doc)
        {
            try
            {
                if (doc == null) return;
                var hint = GetDocumentPathHint(doc);
                var isCloud = DetectCloudDocument(doc, hint);
                AppServices.CurrentDocIsCloud = isCloud;
                AppServices.CurrentChatDisabledReason = isCloud
                    ? "Chat disabled for cloud models (ACC/BIM 360). Local filesystem path is required for chat storage."
                    : null;
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    AppServices.CurrentDocPathHint = hint;
                    _lastDocPathHint = hint;
                }

                try
                {
                    string source;
                    var docKey = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out source);
                    if (!string.IsNullOrWhiteSpace(docKey))
                        AppServices.CurrentDocKey = docKey.Trim();
                }
                catch { /* ignore */ }

                try
                {
                    var uname = doc.Application?.Username;
                    if (!string.IsNullOrWhiteSpace(uname))
                    {
                        AppServices.CurrentUserId = uname.Trim();
                        AppServices.CurrentUserName = uname.Trim();
                    }
                }
                catch { /* ignore */ }

                // Best-effort: set chat root on the server side.
                if (!isCloud && !string.IsNullOrWhiteSpace(hint))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var p = new JObject
                            {
                                ["docPathHint"] = hint,
                                ["docKey"] = AppServices.CurrentDocKey ?? "",
                                ["channel"] = "ws://Project/General",
                                ["limit"] = 1
                            };
                            await ChatRpcClient.CallAsync("chat.list", p).ConfigureAwait(false);
                        }
                        catch { /* ignore */ }
                    });
                }
            }
            catch { /* keep safe */ }
        }

        private static void Tick()
        {
            if (Interlocked.Exchange(ref _polling, 1) == 1) return;
            try { _ = PollAsync(); }
            finally { Interlocked.Exchange(ref _polling, 0); }
        }

        private static async Task PollAsync()
        {
            try
            {
                if (AppServices.CurrentDocIsCloud) return;
                var hint = AppServices.CurrentDocPathHint ?? _lastDocPathHint;
                if (string.IsNullOrWhiteSpace(hint)) return;
                var docKey = (AppServices.CurrentDocKey ?? string.Empty).Trim();

                var userId = (AppServices.CurrentUserId ?? Environment.UserName).Trim();
                if (string.IsNullOrWhiteSpace(userId)) userId = Environment.UserName;

                var p = new JObject
                {
                    ["docPathHint"] = hint,
                    ["docKey"] = docKey,
                    ["channel"] = "ws://Project/Invites",
                    ["limit"] = 30,
                    ["userId"] = userId
                };

                var resTok = await ChatRpcClient.CallAsync("chat.inbox.list", p).ConfigureAwait(false);
                var res = resTok as JObject;
                if (res == null) return;
                if (!(res.Value<bool?>("ok") ?? false)) return;

                var items = res["items"] as JArray;
                if (items == null || items.Count == 0) return;

                var newItems = new List<JObject>();
                foreach (var it in items)
                {
                    var ev = it as JObject;
                    if (ev == null) continue;
                    var tsStr = (string?)ev["ts"];
                    if (!DateTimeOffset.TryParse(tsStr, out var ts)) ts = DateTimeOffset.MinValue;
                    var tsUtc = ts.ToUniversalTime();
                    if (tsUtc <= _lastSeenTsUtc) continue;
                    newItems.Add(ev);
                }

                if (newItems.Count == 0) return;

                // Update last seen first (avoid repeated toasts if UI throws)
                _lastSeenTsUtc = newItems.Max(e =>
                {
                    var tsStr = (string?)e["ts"];
                    if (!DateTimeOffset.TryParse(tsStr, out var ts)) ts = DateTimeOffset.MinValue;
                    return ts.ToUniversalTime();
                });

                foreach (var ev in newItems.OrderBy(e => (string?)e["ts"], StringComparer.OrdinalIgnoreCase))
                {
                    var actor = (string?)ev["actor"]?["name"] ?? "(unknown)";
                    var text = (string?)ev["payload"]?["text"] ?? "(no text)";
                    InviteToastWindow.ShowToast($"[Invite] {actor}\n{text}");
                }
            }
            catch { /* ignore */ }
        }

        private static string GetDocumentPathHint(Document doc)
        {
            if (doc == null) return string.Empty;

            try
            {
                if (doc.IsWorkshared)
                {
                    var mp = doc.GetWorksharingCentralModelPath();
                    if (mp != null)
                    {
                        var central = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                        if (!string.IsNullOrWhiteSpace(central)) return central;
                    }
                }
            }
            catch { /* ignore */ }

            try { return doc.PathName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool DetectCloudDocument(Document doc, string? hint)
        {
            try
            {
                if (doc != null)
                {
                    try { if (doc.IsModelInCloud) return true; } catch { }
                }
            }
            catch { }

            var h = (hint ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(h))
            {
                // Cloud-style pseudo paths (e.g., "Autodesk Docs://", "BIM 360://") are not filesystem paths.
                if (h.IndexOf("://", StringComparison.OrdinalIgnoreCase) >= 0 && !h.StartsWith(@"\\"))
                    return true;
            }
            return false;
        }
    }
}
