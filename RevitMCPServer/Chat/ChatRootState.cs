#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RevitMcpServer.Infra;

namespace RevitMcpServer.Chat
{
    /// <summary>
    /// Resolves and persists the current project root for chat/workflow logs.
    ///
    /// Important:
    /// - Multiple .rvt files can exist in the same folder, so storage must be keyed by a stable project identifier.
    /// - We use a "docKey" (the same stable ID used by the ViewWorkspace/Ledger feature in the add-in) when available.
    ///
    /// Root layout:
    ///   &lt;ProjectFolder&gt;\_RevitMCP\projects\&lt;docKey&gt;\
    /// (Fallback when docKey is missing: path-hash-based key)
    /// </summary>
    public sealed class ChatRootState
    {
        private static readonly object _lock = new object();

        private readonly Dictionary<string, string> _cachedRootsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string? _cachedLastRoot;
        private string? _cachedLastProjectKey;

        private static string GetStateFilePath()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(local, "RevitMCP", "chat");
            Directory.CreateDirectory(dir);
            var port = ServerContext.Port;
            if (port <= 0) port = 0;
            return Path.Combine(dir, $"chat_root_{port}.json");
        }

        public string? GetRootOrNull(string? projectKey = null)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(projectKey) && _cachedRootsByKey.TryGetValue(projectKey, out var byKey))
                    return byKey;

                if (!string.IsNullOrWhiteSpace(_cachedLastRoot))
                    return _cachedLastRoot;

                try
                {
                    var path = GetStateFilePath();
                    if (!File.Exists(path)) return null;
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;
                    if (!root.TryGetProperty("projectRoot", out var pr) || pr.ValueKind != JsonValueKind.String) return null;
                    var val = (pr.GetString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(val)) return null;

                    var key = "";
                    if (root.TryGetProperty("projectKey", out var pk) && pk.ValueKind == JsonValueKind.String)
                        key = (pk.GetString() ?? "").Trim();

                    _cachedLastRoot = val;
                    _cachedLastProjectKey = string.IsNullOrWhiteSpace(key) ? null : key;
                    if (!string.IsNullOrWhiteSpace(_cachedLastProjectKey))
                        _cachedRootsByKey[_cachedLastProjectKey] = val;

                    if (!string.IsNullOrWhiteSpace(projectKey) && _cachedRootsByKey.TryGetValue(projectKey, out var r2))
                        return r2;

                    return _cachedLastRoot;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string ComputePathKey(string docPathHint)
        {
            try
            {
                var hint = (docPathHint ?? "").Trim();
                if (string.IsNullOrWhiteSpace(hint)) return "";
                // Normalize a little to reduce accidental key changes.
                hint = hint.Replace('/', '\\').ToLowerInvariant();
                using var sha1 = SHA1.Create();
                var bytes = Encoding.UTF8.GetBytes(hint);
                var hash = sha1.ComputeHash(bytes);
                // Shorten for filesystem friendliness.
                var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return "path-" + hex.Substring(0, 12);
            }
            catch
            {
                return "path-unknown";
            }
        }

        public bool TrySetRootFromDocPathHint(string? docPathHint, string? docKey, out string? projectRoot, out string? error)
        {
            projectRoot = null;
            error = null;

            try
            {
                var hint = (docPathHint ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(hint))
                {
                    error = "docPathHint is empty.";
                    return false;
                }

                // Cloud-style pseudo paths are not usable for filesystem persistence.
                if (hint.Contains("://", StringComparison.OrdinalIgnoreCase) && !hint.StartsWith(@"\\"))
                {
                    error = "docPathHint looks like a non-filesystem path (cloud URL).";
                    return false;
                }

                string? folder = null;
                try { folder = Path.GetDirectoryName(hint); } catch { folder = null; }
                if (string.IsNullOrWhiteSpace(folder))
                {
                    error = "Could not resolve folder from docPathHint.";
                    return false;
                }

                var key = (docKey ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    // Fallback: stable key derived from path.
                    key = ComputePathKey(hint);
                }

                // Root is per project key to avoid collisions when multiple .rvt exist in one folder.
                var baseRoot = Path.Combine(folder!, "_RevitMCP");
                projectRoot = Path.Combine(baseRoot, "projects", key);

                lock (_lock)
                {
                    _cachedLastRoot = projectRoot;
                    _cachedLastProjectKey = key;
                    _cachedRootsByKey[key] = projectRoot;

                    try
                    {
                        var statePath = GetStateFilePath();
                        var obj = new
                        {
                            projectRoot = projectRoot,
                            projectKey = key,
                            docKey = (docKey ?? "").Trim(),
                            docPathHint = hint,
                            updatedUtc = DateTimeOffset.UtcNow.ToString("o"),
                            serverPort = ServerContext.Port
                        };
                        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });
                        File.WriteAllText(statePath, json, new UTF8Encoding(false));
                    }
                    catch (Exception ex)
                    {
                        Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WARN ChatRootState.Save failed: {ex.Message}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
