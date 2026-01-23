// File: RevitMcpServer/Docs/ManifestRegistry.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RevitMcpServer.Docs
{
    /// <summary>
    /// Add-in ����o�^�����R�}���h�ژ^�i�}�j�t�F�X�g�j��ێ��E�i�������郌�W�X�g���B
    /// </summary>
    public static class ManifestRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, DocMethod> _methods =
            new Dictionary<string, DocMethod>(StringComparer.OrdinalIgnoreCase);

        // �L���b�V���t�@�C���̕ۑ���iProgram.cs ����N�����ɐݒ�j
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
                // Treat each /manifest/register as authoritative for that source.
                // This prevents stale commands (e.g. test artifacts) from lingering forever.
                var src = string.IsNullOrWhiteSpace(manifest.Source) ? "RevitAddin" : manifest.Source.Trim();
                var keysToRemove = _methods
                    .Where(kv => string.Equals(kv.Value?.Source ?? "", src, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in keysToRemove) _methods.Remove(k);

                foreach (var m in manifest.Commands)
                {
                    if (string.IsNullOrWhiteSpace(m.Name)) continue;
                    var nm = (m.Name ?? string.Empty).Trim();
                    if (nm.Equals("test_cap", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nm.Equals("status", StringComparison.OrdinalIgnoreCase)) continue;
                    if (nm.Equals("revit_status", StringComparison.OrdinalIgnoreCase)) continue;

                    m.Source = src;
                    if (m.ParamsSchema == null) m.ParamsSchema = new Dictionary<string, object?> { ["type"] = "object" };
                    if (m.ResultSchema == null) m.ResultSchema = new Dictionary<string, object?> { ["type"] = "object" };
                    if (m.Tags == null) m.Tags = new string[0];
                    m.Name = nm;
                    _methods[m.Name] = m; // �����͏㏑��
                }
            }
        }

        public static List<DocMethod> GetAll()
        {
            lock (_lock) { return new List<DocMethod>(_methods.Values); }
        }

        /// <summary>�L���b�V���t�@�C�����烍�[�h�i�N�����j�B</summary>
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
                // ���Ă��Ă��N���p��
            }
        }

        /// <summary>���݂̓��e���L���b�V���t�@�C���֕ۑ��i/manifest/register �̓s�x�j�B</summary>
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
                // �ۑ����s�͒v���ł͂Ȃ�
            }
        }
    }
}
