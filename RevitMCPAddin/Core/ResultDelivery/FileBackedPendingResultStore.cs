#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core.ResultDelivery
{
    internal sealed class FileBackedPendingResultStore
    {
        private readonly string _rootDir;

        public FileBackedPendingResultStore(int port)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _rootDir = Path.Combine(local, "RevitMCP", "queue", "pending_results", $"p{port}");
            Directory.CreateDirectory(_rootDir);
        }

        public IReadOnlyList<PendingResultItem> LoadAll()
        {
            var list = new List<PendingResultItem>();
            string[] files;
            try
            {
                files = Directory.GetFiles(_rootDir, "*.json", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return list;
            }

            foreach (var path in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var text = File.ReadAllText(path);
                    var item = JsonConvert.DeserializeObject<PendingResultItem>(text);
                    if (item == null || string.IsNullOrWhiteSpace(item.JsonBody))
                    {
                        continue;
                    }
                    item.StorePath = path;
                    list.Add(item);
                }
                catch (Exception ex)
                {
                    try
                    {
                        var bad = path + ".bad";
                        if (File.Exists(bad)) File.Delete(bad);
                        File.Move(path, bad);
                        RevitLogger.Warn($"[RESULT] pending result parse failed, moved to .bad: {Path.GetFileName(path)} ({ex.Message})");
                    }
                    catch { /* ignore */ }
                }
            }
            return list;
        }

        public string Save(PendingResultItem item)
        {
            Directory.CreateDirectory(_rootDir);
            var safeRpc = MakeSafeName(string.IsNullOrWhiteSpace(item.RpcId) ? "no_rpc" : item.RpcId);
            var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{safeRpc}_{Guid.NewGuid():N}.json";
            var path = Path.Combine(_rootDir, fileName);
            var text = JsonConvert.SerializeObject(item, Formatting.None);
            File.WriteAllText(path, text);
            return path;
        }

        public void Delete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignore */ }
        }

        public bool OwnsPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var root = Path.GetFullPath(_rootDir);
                if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    root += Path.DirectorySeparatorChar;
                }

                var full = Path.GetFullPath(path);
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string MakeSafeName(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var arr = input.ToCharArray();
            for (int i = 0; i < arr.Length; i++)
            {
                if (invalid.Contains(arr[i])) arr[i] = '_';
            }
            return new string(arr);
        }
    }
}
