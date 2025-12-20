#nullable enable
using System.IO;
using System.Text.Json;

namespace RevitMcpServer.Config
{
    public sealed class Settings
    {
        public AiSettings Ai { get; set; } = new();
        public ServerSettings Server { get; set; } = new();

        public static string BaseDir =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "RevitMCP");

        public static string DefaultPath => Path.Combine(BaseDir, "settings.json");

        public static Settings Load(string? path = null)
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new Settings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new Settings();
        }

        public void Save(string? path = null)
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public sealed class AiSettings
    {
        public RevitMcpServer.Ai.AiProvider Provider { get; set; } = RevitMcpServer.Ai.AiProvider.None;
        public string? Model { get; set; }
        public string? ApiKey { get; set; } // ¦Œ´‘¥‚ÍŠÂ‹«•Ï”—Dæ
    }

    public sealed class ServerSettings
    {
        public int Port { get; set; } = 5210;
    }
}
