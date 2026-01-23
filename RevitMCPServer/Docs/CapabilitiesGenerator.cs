// File: RevitMcpServer/Docs/CapabilitiesGenerator.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitMcpServer.Docs
{
    public sealed class CapabilityRecord
    {
        public string Method { get; set; } = "";
        public string Canonical { get; set; } = "";
        public string Summary { get; set; } = "";
        public JsonElement? ParamsExample { get; set; }
        public JsonElement? ResultExample { get; set; }
        public string RevitHandler { get; set; } = "Unknown";
        public string Transaction { get; set; } = "Write"; // Read|Write
        public string[] SupportsFamilyKinds { get; set; } = new[] { "Unknown" };
        public string Since { get; set; } = "";
        public bool Deprecated { get; set; }
    }

    public static class CapabilitiesGenerator
    {
        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false
        };

        // Explicit legacy→canonical map for rename cases where "domain + '.' + legacy" does not hold.
        // This avoids fragile suffix matching and makes `/debug/capabilities` deterministic even if
        // the add-in manifest lacks proper Deprecated/Summary markers.
        private static readonly Dictionary<string, string> LegacyAliasMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["create_clipping_3d_view_from_selection"] = "view.create_focus_3d_view_from_selection",
                ["view.create_clipping_3d_view_from_selection"] = "view.create_focus_3d_view_from_selection",
                ["create_sheet"] = "sheet.create",
                ["delete_sheet"] = "sheet.delete",
                ["get_sheets"] = "sheet.list",
                ["place_view_on_sheet"] = "sheet.place_view",
                ["place_view_on_sheet_auto"] = "sheet.place_view_auto",
                ["remove_view_from_sheet"] = "sheet.remove_view",
                ["replace_view_on_sheet"] = "sheet.replace_view",
                ["revit_batch"] = "revit.batch",
                ["revit_status"] = "revit.status",
                ["status"] = "revit.status",
                ["sheet_inspect"] = "sheet.inspect",
                ["viewport_move_to_sheet_center"] = "viewport.move_to_sheet_center"
            };

        public static string GetDefaultJsonlPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "docs", "capabilities.jsonl");
        }

        public static List<CapabilityRecord> Build(IEnumerable<DocMethod> methods)
        {
            var list = new List<CapabilityRecord>();
            if (methods == null) return list;

            var methodList = methods.Where(x => x != null).ToList();
            var sinceFallback = GetSinceDefault();
            var sinceDefault = MostCommonSince(methodList) ?? sinceFallback;
            var defaultParamsExample = ParseJsonElementOrNull("{\"__example\":true}") ?? ParseJsonElementOrNull("{}");
            var defaultResultExample = ParseJsonElementOrNull("{\"ok\":true}") ?? ParseJsonElementOrNull("{}");

            foreach (var m in methodList)
            {
                if (m == null) continue;
                var name = (m.Name ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                if (name.Equals("test_cap", StringComparison.OrdinalIgnoreCase)) continue;

                var tx = !string.IsNullOrWhiteSpace(m.Transaction) ? m.Transaction!.Trim() : InferTransaction(name);
                if (tx != null)
                {
                    if (tx.Equals("write", StringComparison.OrdinalIgnoreCase)) tx = "Write";
                    else if (tx.Equals("read", StringComparison.OrdinalIgnoreCase)) tx = "Read";
                }

                var summary = (m.Summary ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(summary)) summary = InferSummary(name);

                var handler = !string.IsNullOrWhiteSpace(m.RevitHandler) ? m.RevitHandler!.Trim() : "RevitMcpServer";
                var since = !string.IsNullOrWhiteSpace(m.Since) ? m.Since!.Trim() : sinceDefault;

                var fam = m.SupportsFamilyKinds ?? Array.Empty<string>();
                if (fam.Length == 0) fam = new[] { "Unknown" };

                if (string.IsNullOrWhiteSpace(tx)) tx = "Write";

                // Canonical/alias policy hardening (server-side; best-effort):
                // - Canonical: domain-first names
                // - Legacy aliases remain callable but should be `Deprecated=true` for discovery.
                var deprecated = m.Deprecated ?? false;
                if (LegacyAliasMap.TryGetValue(name, out var mapped) && !string.IsNullOrWhiteSpace(mapped) &&
                    !string.Equals(mapped, name, StringComparison.OrdinalIgnoreCase))
                {
                    deprecated = true;
                    // Keep explicit summary if it already declares the alias target; otherwise normalize.
                    if (!summary.StartsWith("deprecated alias of ", StringComparison.OrdinalIgnoreCase) &&
                        !summary.StartsWith("legacy alias of ", StringComparison.OrdinalIgnoreCase))
                        summary = "deprecated alias of " + mapped;
                }

                // Treat server-local status aliases as deprecated for discovery.
                if (name.Equals("status", StringComparison.OrdinalIgnoreCase) || name.Equals("revit_status", StringComparison.OrdinalIgnoreCase))
                {
                    deprecated = true;
                    summary = "deprecated alias of revit.status";
                    handler = "RevitMcpServer";
                    tx = "Read";
                }

                list.Add(new CapabilityRecord
                {
                    Method = name,
                    Canonical = InferCanonical(name, deprecated, summary),
                    Summary = summary,
                    ParamsExample = m.ParamsExample ?? defaultParamsExample,
                    ResultExample = m.ResultExample ?? defaultResultExample,
                    RevitHandler = handler,
                    Transaction = tx,
                    SupportsFamilyKinds = fam,
                    Since = since,
                    Deprecated = deprecated
                });
            }

            // Ensure stable schema + provide server-side status aliases explicitly.
            EnsureRevitStatusAliases(list, sinceDefault, defaultParamsExample, defaultResultExample);
            EnsureCaptureCommands(list, sinceDefault, defaultParamsExample, defaultResultExample);

            // Normalize the injected status aliases `Since` to the dominant since in this list.
            // This avoids accidental "server build timestamp" drift when the add-in manifest uses its own since.
            try
            {
                var dominantSince = MostCommonSince(list, sinceDefault);
                foreach (var it in list)
                {
                    if (it == null) continue;
                    if (it.Method == null) continue;
                    if (it.Method.Equals("revit.status", StringComparison.OrdinalIgnoreCase) ||
                        it.Method.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                        it.Method.Equals("revit_status", StringComparison.OrdinalIgnoreCase) ||
                        it.Method.StartsWith("capture.", StringComparison.OrdinalIgnoreCase))
                        it.Since = dominantSince;
                }
            }
            catch { /* best-effort */ }

            return list
                .OrderBy(x => x.Method, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string MostCommonSince(List<CapabilityRecord> caps, string fallback)
        {
            try
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var c in caps)
                {
                    if (c == null) continue;
                    var s = (c.Since ?? string.Empty).Trim();
                    if (s.Length == 0) continue;
                    // Ignore status aliases themselves when computing dominance.
                    if (c.Method != null &&
                        (c.Method.Equals("revit.status", StringComparison.OrdinalIgnoreCase) ||
                         c.Method.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                         c.Method.Equals("revit_status", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (counts.TryGetValue(s, out var n)) counts[s] = n + 1;
                    else counts[s] = 1;
                }

                if (counts.Count == 0) return fallback;
                return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
            }
            catch
            {
                return fallback;
            }
        }

        private static string? MostCommonSince(List<DocMethod> methods)
        {
            try
            {
                if (methods == null || methods.Count == 0) return null;

                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var m in methods)
                {
                    var s = (m?.Since ?? string.Empty).Trim();
                    if (s.Length == 0) continue;
                    if (counts.TryGetValue(s, out var c)) counts[s] = c + 1;
                    else counts[s] = 1;
                }

                if (counts.Count == 0) return null;
                return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
            }
            catch
            {
                return null;
            }
        }

        private static string InferCanonical(string method, bool deprecated, string summary)
        {
            try
            {
                var m = (method ?? string.Empty).Trim();
                if (m.Length == 0) return "";
                if (!deprecated) return m;

                var s = (summary ?? string.Empty).Trim();
                if (s.Length == 0) return m;

                const string p1 = "deprecated alias of ";
                if (s.StartsWith(p1, StringComparison.OrdinalIgnoreCase))
                {
                    var c = s.Substring(p1.Length).Trim();
                    return c.Length > 0 ? c : m;
                }

                const string p2 = "legacy alias of ";
                if (s.StartsWith(p2, StringComparison.OrdinalIgnoreCase))
                {
                    var c = s.Substring(p2.Length).Trim();
                    return c.Length > 0 ? c : m;
                }

                return m;
            }
            catch
            {
                return (method ?? string.Empty).Trim();
            }
        }

        public static void WriteJsonl(string path, IEnumerable<CapabilityRecord> items)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (items == null) return;

            var dir = Path.GetDirectoryName(path) ?? "";
            if (dir.Length > 0) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var item in items)
                {
                    if (item == null) continue;
                    var line = JsonSerializer.Serialize(item, _json);
                    sw.WriteLine(line);
                }
            }

            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public static void TryWriteDefault(IEnumerable<DocMethod> methods)
        {
            try
            {
                var caps = Build(methods);
                WriteJsonl(GetDefaultJsonlPath(), caps);
            }
            catch
            {
                // best-effort
            }
        }

        private static string InferTransaction(string method)
        {
            try
            {
                var leaf = method;
                var i = leaf.LastIndexOf('.');
                if (i >= 0) leaf = leaf.Substring(i + 1);
                leaf = leaf.Trim().ToLowerInvariant();

                if (leaf == "status" || leaf == "revit_status" || leaf.EndsWith("_status"))
                    return "Read";
                // Domain-first verbs (e.g., sheet.list)
                if (leaf == "list" || leaf == "get" || leaf == "find" || leaf == "search" || leaf == "describe" || leaf == "ping")
                    return "Read";
                if (leaf.StartsWith("get_") || leaf.StartsWith("list_") || leaf.StartsWith("find_") || leaf.StartsWith("search_") ||
                    leaf.StartsWith("describe_") || leaf.StartsWith("audit_") || leaf.StartsWith("validate_") || leaf.StartsWith("diff_") ||
                    leaf.StartsWith("snapshot_") || leaf.StartsWith("ping_"))
                    return "Read";

                return "Write";
            }
            catch
            {
                return "Write";
            }
        }

        private static string GetSinceDefault()
        {
            try
            {
                var asm = typeof(CapabilitiesGenerator).Assembly;
                var v = asm.GetName().Version;
                var ver = v != null ? v.ToString() : "0.0.0.0";
                string ts = string.Empty;
                try
                {
                    if (!string.IsNullOrWhiteSpace(asm.Location) && File.Exists(asm.Location))
                        ts = File.GetLastWriteTimeUtc(asm.Location).ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
                catch { ts = string.Empty; }
                return !string.IsNullOrWhiteSpace(ts) ? (ver + "@" + ts) : ver;
            }
            catch
            {
                return "0.0.0.0";
            }
        }

        private static string InferSummary(string method)
        {
            try
            {
                var m = (method ?? string.Empty).Trim();
                if (m.Length == 0) return "Command";
                var s = m.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Replace('/', ' ');
                s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
                return s.Length > 0 ? s : m;
            }
            catch
            {
                return "Command";
            }
        }

        private static JsonElement? ParseJsonElementOrNull(string? json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return null;
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureRevitStatusAliases(List<CapabilityRecord> list, string since, JsonElement? defaultParams, JsonElement? defaultResult)
        {
            if (list == null) return;

            bool hasCanon = list.Any(x => string.Equals(x.Method, "revit.status", StringComparison.OrdinalIgnoreCase));
            bool hasAlias1 = list.Any(x => string.Equals(x.Method, "status", StringComparison.OrdinalIgnoreCase));
            bool hasAlias2 = list.Any(x => string.Equals(x.Method, "revit_status", StringComparison.OrdinalIgnoreCase));

            if (!hasCanon)
            {
                list.Add(new CapabilityRecord
                {
                    Method = "revit.status",
                    Canonical = "revit.status",
                    Summary = "revit status (server-side queue/health)",
                    ParamsExample = defaultParams,
                    ResultExample = defaultResult,
                    RevitHandler = "RevitMcpServer",
                    Transaction = "Read",
                    SupportsFamilyKinds = new[] { "Unknown" },
                    Since = since,
                    Deprecated = false
                });
            }

            if (!hasAlias1)
            {
                list.Add(new CapabilityRecord
                {
                    Method = "status",
                    Canonical = "revit.status",
                    Summary = "deprecated alias of revit.status",
                    ParamsExample = defaultParams,
                    ResultExample = defaultResult,
                    RevitHandler = "RevitMcpServer",
                    Transaction = "Read",
                    SupportsFamilyKinds = new[] { "Unknown" },
                    Since = since,
                    Deprecated = true
                });
            }

            if (!hasAlias2)
            {
                list.Add(new CapabilityRecord
                {
                    Method = "revit_status",
                    Canonical = "revit.status",
                    Summary = "deprecated alias of revit.status",
                    ParamsExample = defaultParams,
                    ResultExample = defaultResult,
                    RevitHandler = "RevitMcpServer",
                    Transaction = "Read",
                    SupportsFamilyKinds = new[] { "Unknown" },
                    Since = since,
                    Deprecated = true
                });
            }
        }

        private static void EnsureCaptureCommands(List<CapabilityRecord> list, string since, JsonElement? defaultParams, JsonElement? defaultResult)
        {
            if (list == null) return;

            void Ensure(string method, string summary, string paramsExampleJson)
            {
                if (list.Any(x => string.Equals(x.Method, method, StringComparison.OrdinalIgnoreCase))) return;
                var pe = ParseJsonElementOrNull(paramsExampleJson) ?? defaultParams;
                list.Add(new CapabilityRecord
                {
                    Method = method,
                    Canonical = method,
                    Summary = summary,
                    ParamsExample = pe,
                    ResultExample = defaultResult,
                    RevitHandler = "RevitMcpServer",
                    Transaction = "Read",
                    SupportsFamilyKinds = new[] { "Unknown" },
                    Since = since,
                    Deprecated = false
                });
            }

            Ensure("capture.list_windows", "list visible top-level windows (server-side)", "{\"processName\":\"Revit\",\"titleContains\":\"\",\"visibleOnly\":true}");
            Ensure("capture.screen", "capture screen/monitor screenshot(s) (server-side)", "{\"monitorIndex\":0,\"outDir\":null}");
            Ensure("capture.window", "capture a specific window by hwnd (server-side)", "{\"hwnd\":\"0x00123456\",\"preferPrintWindow\":true,\"outDir\":null}");
            Ensure("capture.revit", "capture Revit windows/dialogs (server-side)", "{\"target\":\"active_dialogs\",\"outDir\":null}");
        }
    }
}
