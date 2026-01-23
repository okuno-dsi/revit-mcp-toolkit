using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace RevitMcpServer.Core
{
    public sealed class CommandMeta
    {
        public string category { get; set; } = "";
        public string importance { get; set; } = "normal"; // low|normal|high
        public string kind { get; set; } = "read";         // read|write
    }

    public static class CommandRegistry
    {
        private static readonly object _gate = new();
        private static Dictionary<string, CommandMeta>? _cache;

        public static IReadOnlyDictionary<string, CommandMeta> Load(string? path = null)
        {
            lock (_gate)
            {
                if (_cache != null) return _cache;
                // Prefer Config/commands_index.json (SSR removed); fall back to wwwroot for compatibility
                var defaultConfig = Path.Combine(AppContext.BaseDirectory, "Config", "commands_index.json");
                var legacyPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "commands_index.json");
                path = !string.IsNullOrWhiteSpace(path) ? path : (File.Exists(defaultConfig) ? defaultConfig : legacyPath);
                var json = File.ReadAllText(path);
                _cache = JsonConvert.DeserializeObject<Dictionary<string, CommandMeta>>(json)
                         ?? new Dictionary<string, CommandMeta>(StringComparer.OrdinalIgnoreCase);
                return _cache;
            }
        }

        public static bool TryGet(string method, out CommandMeta meta)
        {
            var map = Load();
            return map.TryGetValue(method, out meta!);
        }
    }
}
