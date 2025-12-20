#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core.ViewWorkspace
{
    internal static class ViewWorkspaceStore
    {
        private static string GetDefaultRootDir()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "RevitMCP", "ViewWorkspace");
        }

        public static string GetWorkspaceFilePath(string docKey)
        {
            docKey = (docKey ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(docKey)) docKey = "unknown";
            return Path.Combine(GetDefaultRootDir(), $"workspace_{docKey}.json");
        }

        private static string GetArchiveFilePath(string docKey, DateTime utcNow)
        {
            docKey = (docKey ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(docKey)) docKey = "unknown";
            var stamp = utcNow.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(GetDefaultRootDir(), $"workspace_{docKey}_{stamp}.json");
        }

        public static bool TryLoadFromFile(string docKey, out ViewWorkspaceSnapshot? snapshot, out string? path, out string? error)
        {
            snapshot = null;
            path = null;
            error = null;

            try
            {
                path = GetWorkspaceFilePath(docKey);
                if (!File.Exists(path))
                {
                    error = "Snapshot file not found.";
                    return false;
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                snapshot = JsonConvert.DeserializeObject<ViewWorkspaceSnapshot>(json);
                if (snapshot == null)
                {
                    error = "Snapshot JSON is empty or invalid.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TrySaveToFile(ViewWorkspaceSnapshot snapshot, int retention, out string? savedPath, out List<string> warnings, out string? error)
        {
            savedPath = null;
            warnings = new List<string>();
            error = null;

            if (snapshot == null) { error = "snapshot is null"; return false; }
            if (string.IsNullOrWhiteSpace(snapshot.DocKey)) { error = "snapshot.doc_key is empty"; return false; }

            try
            {
                var root = GetDefaultRootDir();
                Directory.CreateDirectory(root);

                var utcNow = DateTime.UtcNow;
                var path = GetWorkspaceFilePath(snapshot.DocKey);
                var tmp = path + ".tmp";

                var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (File.Exists(path))
                {
                    // Atomic replace when possible
                    try { File.Replace(tmp, path, null); }
                    catch
                    {
                        try { File.Delete(path); } catch { }
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }

                savedPath = path;

                // Retention (optional archive ring buffer)
                if (retention > 1)
                {
                    try
                    {
                        var archivePath = GetArchiveFilePath(snapshot.DocKey, utcNow);
                        File.Copy(path, archivePath, overwrite: true);

                        // Keep: latest.json + (retention-1) archives (best-effort)
                        var prefix = $"workspace_{snapshot.DocKey}_";
                        var archives = Directory.GetFiles(root, $"{prefix}*.json")
                            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        int keep = Math.Max(0, retention - 1);
                        for (int i = keep; i < archives.Count; i++)
                        {
                            try { File.Delete(archives[i]); } catch { /* ignore */ }
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("Archive retention failed: " + ex.Message);
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

