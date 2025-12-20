// File: RevitMcpServer/Docs/ManifestRegistry.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RevitMcpServer.Docs
{
    /// <summary>
    /// Add-in から登録されるコマンド目録（マニフェスト）を保持・永続化するレジストリ。
    /// </summary>
    public static class ManifestRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, DocMethod> _methods =
            new Dictionary<string, DocMethod>(StringComparer.OrdinalIgnoreCase);

        // キャッシュファイルの保存先（Program.cs から起動時に設定）
        private static string _cachePath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "manifest-cache.json");

        public static void ConfigureCachePath(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path)) _cachePath = path!;
        }

        public static void Upsert(DocManifest manifest)
        {
            if (manifest == null) return;
            lock (_lock)
            {
                foreach (var m in manifest.Commands)
                {
                    if (string.IsNullOrWhiteSpace(m.Name)) continue;
                    m.Source = string.IsNullOrWhiteSpace(manifest.Source) ? "RevitAddin" : manifest.Source;
                    if (m.ParamsSchema == null) m.ParamsSchema = new Dictionary<string, object?> { ["type"] = "object" };
                    if (m.ResultSchema == null) m.ResultSchema = new Dictionary<string, object?> { ["type"] = "object" };
                    if (m.Tags == null) m.Tags = new string[0];
                    _methods[m.Name] = m; // 同名は上書き
                }
            }
        }

        public static List<DocMethod> GetAll()
        {
            lock (_lock) { return new List<DocMethod>(_methods.Values); }
        }

        /// <summary>キャッシュファイルからロード（起動時）。</summary>
        public static void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_cachePath)) return;
                var json = File.ReadAllText(_cachePath, Encoding.UTF8);
                var manifest = JsonSerializer.Deserialize<DocManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (manifest?.Commands != null) Upsert(manifest);
            }
            catch
            {
                // 壊れていても起動継続
            }
        }

        /// <summary>現在の内容をキャッシュファイルへ保存（/manifest/register の都度）。</summary>
        public static void SaveToDisk()
        {
            try
            {
                var manifest = new DocManifest { Source = "RevitAddin", Commands = GetAll() };
                var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                var dir = Path.GetDirectoryName(_cachePath) ?? "";
                Directory.CreateDirectory(dir);
                var tmp = _cachePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(_cachePath)) File.Replace(tmp, _cachePath, null);
                else File.Move(tmp, _cachePath);
            }
            catch
            {
                // 保存失敗は致命ではない
            }
        }
    }
}
