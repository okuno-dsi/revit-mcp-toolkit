#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RevitMcpServer.Infra;

namespace RevitMcpServer.Chat
{
    /// <summary>
    /// Append-only per-writer JSONL chat store in &lt;CentralModelFolder&gt;\_RevitMCP\projects\&lt;docKey&gt;\chat\writers\.
    /// </summary>
    public sealed class ChatStore
    {
        private static readonly Regex MentionRegex = new Regex(@"@([A-Za-z0-9_\-]+)", RegexOptions.Compiled);
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly ChatRootState _root;

        public ChatStore(ChatRootState root)
        {
            _root = root;
        }

        public bool IsChatMethod(string method)
        {
            if (string.IsNullOrWhiteSpace(method)) return false;
            return method.StartsWith("chat.", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<object> ExecuteAsync(string method, JsonElement? param)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(method))
                    return new { ok = false, code = "INVALID_METHOD", msg = "method is required" };

                if (method.Equals("chat.post", StringComparison.OrdinalIgnoreCase))
                    return await PostAsync(param).ConfigureAwait(false);
                if (method.Equals("chat.list", StringComparison.OrdinalIgnoreCase))
                    return await ListAsync(param).ConfigureAwait(false);
                if (method.Equals("chat.inbox.list", StringComparison.OrdinalIgnoreCase))
                    return await InboxListAsync(param).ConfigureAwait(false);

                return new { ok = false, code = "UNKNOWN_CHAT_METHOD", msg = $"Unknown chat method: {method}" };
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "CHAT_EXEC_ERROR", msg = ex.Message };
            }
        }

        // ---------------- chat.post ----------------
        private async Task<object> PostAsync(JsonElement? param)
        {
            var p = param;
            string? docPathHint = GetString(p, "docPathHint") ?? GetString(p, "doc_path_hint");
            string? docKey = GetString(p, "docKey") ?? GetString(p, "doc_key");
            string? resolvedRoot = null;
            if (!string.IsNullOrWhiteSpace(docPathHint))
            {
                _root.TrySetRootFromDocPathHint(docPathHint, docKey, out resolvedRoot, out _);
            }

            var projectRoot = resolvedRoot ?? _root.GetRootOrNull((docKey ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return new
                {
                    ok = false,
                    code = "CHAT_ROOT_UNKNOWN",
                    msg = "Chat storage root is not configured yet. Provide params.docPathHint and params.docKey (preferred)."
                };
            }

            var channel = (GetString(p, "channel") ?? "ws://Project/General").Trim();
            var text = (GetString(p, "text") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return new { ok = false, code = "EMPTY_TEXT", msg = "params.text is required" };

            var msgType = (GetString(p, "type") ?? "note").Trim();
            var labels = GetStringArray(p, "labels");
            var mentionsInput = GetStringArray(p, "mentions");

            // Actor (fallback to local user)
            var actorObj = GetObject(p, "actor");
            var actorType = (GetString(actorObj, "type") ?? "human").Trim();
            var actorId = (GetString(actorObj, "id") ?? Environment.UserName).Trim();
            var actorName = (GetString(actorObj, "name") ?? actorId).Trim();
            var actorMachine = (GetString(actorObj, "machineName") ?? Environment.MachineName).Trim();

            var mentions = MergeMentions(mentionsInput, ExtractMentions(text));

            var threadId = (GetString(p, "threadId") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(threadId))
            {
                threadId = "thr-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }

            var messageId = "msg-" + Guid.NewGuid().ToString("N");
            var eventId = "evt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var payload = new JsonObject
            {
                ["messageId"] = messageId,
                ["type"] = msgType,
                ["text"] = text,
                ["mentions"] = mentions != null ? new JsonArray(mentions.Select(x => (JsonNode?)x).ToArray()) : null,
                ["labels"] = labels != null ? new JsonArray(labels.Select(x => (JsonNode?)x).ToArray()) : null,
                ["state"] = new JsonObject { ["resolved"] = false }
            };

            var ev = new JsonObject
            {
                ["eventId"] = eventId,
                ["ts"] = DateTimeOffset.Now.ToString("o"),
                ["channel"] = channel,
                ["kind"] = "chat.message",
                ["actor"] = new JsonObject
                {
                    ["type"] = actorType,
                    ["id"] = actorId,
                    ["name"] = actorName,
                    ["machineName"] = actorMachine
                },
                ["threadId"] = threadId,
                ["replyTo"] = GetString(p, "replyTo"),
                ["correlationId"] = GetString(p, "correlationId"),
                ["payload"] = payload
            };

            var writerFile = GetWriterFilePath(projectRoot!, actorType, actorId, actorMachine);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(writerFile)!);
                await AppendJsonlAsync(writerFile, ev).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "CHAT_WRITE_FAIL", msg = ex.Message, writerFile };
            }

            return new
            {
                ok = true,
                projectRoot = projectRoot,
                writerFile = writerFile,
                @event = ev
            };
        }

        // ---------------- chat.list ----------------
        private Task<object> ListAsync(JsonElement? param)
        {
            var p = param;
            string? docPathHint = GetString(p, "docPathHint") ?? GetString(p, "doc_path_hint");
            string? docKey = GetString(p, "docKey") ?? GetString(p, "doc_key");
            string? resolvedRoot = null;
            if (!string.IsNullOrWhiteSpace(docPathHint))
            {
                _root.TrySetRootFromDocPathHint(docPathHint, docKey, out resolvedRoot, out _);
            }

            var projectRoot = resolvedRoot ?? _root.GetRootOrNull((docKey ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return Task.FromResult<object>(new { ok = false, code = "CHAT_ROOT_UNKNOWN", msg = "Chat storage root is not configured yet. Provide params.docPathHint and params.docKey (preferred)." });
            }

            var channel = GetString(p, "channel");
            int limit = GetInt(p, "limit") ?? 50;
            if (limit <= 0) limit = 50;
            limit = Math.Min(limit, 500);

            // Read a bit more per file to allow sorting/merging.
            int tailLines = Math.Min(2000, Math.Max(200, limit * 6));

            var dir = Path.Combine(projectRoot!, "chat", "writers");
            if (!Directory.Exists(dir))
            {
                return Task.FromResult<object>(new { ok = true, projectRoot = projectRoot, items = Array.Empty<object>() });
            }

            var files = Directory.GetFiles(dir, "*.jsonl").ToList();
            var events = new List<JsonNode>();

            foreach (var f in files)
            {
                List<string> lines;
                try { lines = TailLines(f, tailLines); }
                catch { continue; }

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    JsonNode? node = null;
                    try { node = JsonNode.Parse(line); }
                    catch { continue; }
                    if (node == null) continue;
                    if (channel != null)
                    {
                        var ch = node?["channel"]?.GetValue<string>();
                        if (!string.Equals(ch, channel, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    if (!string.Equals(node?["kind"]?.GetValue<string>(), "chat.message", StringComparison.OrdinalIgnoreCase))
                        continue;
                    events.Add(node!);
                }
            }

            var ordered = events
                .OrderBy(n => SafeTs(n?["ts"]?.GetValue<string>()))
                .ThenBy(n => n?["eventId"]?.GetValue<string>(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count > limit)
            {
                ordered = ordered.Skip(Math.Max(0, ordered.Count - limit)).ToList();
            }

            return Task.FromResult<object>(new { ok = true, projectRoot = projectRoot, channel = channel, count = ordered.Count, items = ordered });
        }

        // ---------------- chat.inbox.list ----------------
        private async Task<object> InboxListAsync(JsonElement? param)
        {
            var p = param;
            var userId = (GetString(p, "userId") ?? GetString(p, "user_id") ?? Environment.UserName).Trim();
            if (string.IsNullOrWhiteSpace(userId))
                userId = Environment.UserName;

            // For now, inbox = messages that mention @userId.
            var listRes = await ListAsync(param).ConfigureAwait(false);

            // Extract items list from the dynamic result.
            var json = JsonSerializer.Serialize(listRes, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return listRes;
            if (!root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True) return listRes;
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return new { ok = true, userId, items = Array.Empty<object>() };

            var inbox = new List<JsonNode>();
            foreach (var it in items.EnumerateArray())
            {
                try
                {
                    var node = JsonNode.Parse(it.GetRawText());
                    var mentions = node?["payload"]?["mentions"] as JsonArray;
                    if (mentions == null || mentions.Count == 0) continue;
                    bool hit = mentions.Any(m =>
                    {
                        try { return string.Equals(m?.GetValue<string>(), userId, StringComparison.OrdinalIgnoreCase); } catch { return false; }
                    });
                    if (hit) inbox.Add(node!);
                }
                catch { }
            }

            return new { ok = true, userId, count = inbox.Count, items = inbox };
        }

        // ---------------- utils ----------------

        private static DateTimeOffset SafeTs(string? ts)
        {
            if (string.IsNullOrWhiteSpace(ts)) return DateTimeOffset.MinValue;
            if (DateTimeOffset.TryParse(ts, out var dto)) return dto;
            return DateTimeOffset.MinValue;
        }

        private static async Task AppendJsonlAsync(string path, JsonNode obj)
        {
            var line = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs, Utf8NoBom);
            await sw.WriteLineAsync(line).ConfigureAwait(false);
        }

        private static string GetWriterFilePath(string projectRoot, string actorType, string actorId, string actorMachine)
        {
            var writersDir = Path.Combine(projectRoot, "chat", "writers");
            var safeType = SanitizeFileToken(actorType);
            var safeId = SanitizeFileToken(actorId);
            var safeMachine = SanitizeFileToken(actorMachine);
            var file = $"{safeType}-{safeId}-{safeMachine}.jsonl";
            return Path.Combine(writersDir, file);
        }

        private static string SanitizeFileToken(string s)
        {
            s = (s ?? string.Empty).Trim();
            if (s.Length == 0) return "unknown";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (invalid.Contains(c) || c == '\\' || c == '/' || c == ':' || c == '*'
                    || c == '?' || c == '\"' || c == '<' || c == '>' || c == '|')
                {
                    sb.Append('_');
                }
                else if (char.IsWhiteSpace(c))
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }
            var outS = sb.ToString();
            if (outS.Length > 64) outS = outS.Substring(0, 64);
            return outS;
        }

        private static string[]? ExtractMentions(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = MentionRegex.Matches(text);
            if (m.Count == 0) return null;
            var list = new List<string>();
            foreach (Match x in m)
            {
                if (!x.Success) continue;
                var v = (x.Groups.Count >= 2) ? x.Groups[1].Value : null;
                if (string.IsNullOrWhiteSpace(v)) continue;
                v = v.Trim();
                if (list.Any(e => string.Equals(e, v, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(v);
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        private static string[]? MergeMentions(string[]? a, string[]? b)
        {
            var list = new List<string>();
            void AddMany(string[]? arr)
            {
                if (arr == null) return;
                foreach (var x in arr)
                {
                    var v = (x ?? string.Empty).Trim();
                    if (v.Length == 0) continue;
                    if (list.Any(e => string.Equals(e, v, StringComparison.OrdinalIgnoreCase))) continue;
                    list.Add(v);
                }
            }
            AddMany(a);
            AddMany(b);
            return list.Count == 0 ? null : list.ToArray();
        }

        private static JsonElement? GetObject(JsonElement? p, string key)
        {
            if (!TryGetProperty(p, key, out var v)) return null;
            if (v.ValueKind != JsonValueKind.Object) return null;
            return v;
        }

        private static string? GetString(JsonElement? p, string key)
        {
            if (!TryGetProperty(p, key, out var v)) return null;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            if (v.ValueKind == JsonValueKind.True) return "true";
            if (v.ValueKind == JsonValueKind.False) return "false";
            return null;
        }

        private static int? GetInt(JsonElement? p, string key)
        {
            if (!TryGetProperty(p, key, out var v)) return null;
            try
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j)) return j;
            }
            catch { }
            return null;
        }

        private static string[]? GetStringArray(JsonElement? p, string key)
        {
            if (!TryGetProperty(p, key, out var v)) return null;
            if (v.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var it in v.EnumerateArray())
            {
                try
                {
                    if (it.ValueKind == JsonValueKind.String)
                    {
                        var s = (it.GetString() ?? string.Empty).Trim();
                        if (s.Length > 0) list.Add(s);
                    }
                }
                catch { }
            }
            return list.Count == 0 ? null : list.ToArray();
        }

        private static bool TryGetProperty(JsonElement? p, string key, out JsonElement value)
        {
            value = default;
            if (!p.HasValue) return false;
            if (p.Value.ValueKind != JsonValueKind.Object) return false;
            if (p.Value.TryGetProperty(key, out value)) return true;
            foreach (var prop in p.Value.EnumerateObject())
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
            return false;
        }

        private static List<string> TailLines(string path, int maxLines)
        {
            // Read from end in chunks until we collect enough lines.
            const int chunkSize = 16 * 1024;
            var lines = new List<string>();
            var buffer = new byte[chunkSize];
            long pos;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                pos = fs.Length;
                var sb = new StringBuilder();
                while (pos > 0 && lines.Count < maxLines)
                {
                    var toRead = (int)Math.Min(chunkSize, pos);
                    pos -= toRead;
                    fs.Seek(pos, SeekOrigin.Begin);
                    fs.Read(buffer, 0, toRead);

                    var chunk = Utf8NoBom.GetString(buffer, 0, toRead);
                    sb.Insert(0, chunk);

                    // Quick split when we have enough newlines.
                    var nlCount = chunk.Count(c => c == '\n');
                    if (nlCount == 0 && pos > 0) continue;
                    if (sb.Length > 1024 * 1024) break; // safety cap
                }

                var all = sb.ToString();
                var rawLines = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var l in rawLines.Reverse())
                {
                    if (lines.Count >= maxLines) break;
                    if (l == null) continue;
                    lines.Add(l);
                }
            }

            lines.Reverse();
            return lines;
        }
    }
}
