using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    public sealed class CommandMeta
    {
        public string category { get; set; } = "";
        public string importance { get; set; } = "normal";
        public string kind { get; set; } = "read";
    }

    public static class CommandRegistry
    {
        private static Dictionary<string, CommandMeta>? _cache;

        public static bool TryGet(string method, out CommandMeta meta)
        {
            if (_cache == null)
            {
                var dllDir = Path.GetDirectoryName(typeof(CommandRegistry).Assembly.Location)!;
                var path = Path.Combine(dllDir, "commands_index.json"); // アドインと同フォルダに配置
                var json = File.ReadAllText(path);
                _cache = JsonConvert.DeserializeObject<Dictionary<string, CommandMeta>>(json)
                         ?? new Dictionary<string, CommandMeta>(StringComparer.OrdinalIgnoreCase);
            }
            return _cache.TryGetValue(method, out meta!);
        }
    }
}