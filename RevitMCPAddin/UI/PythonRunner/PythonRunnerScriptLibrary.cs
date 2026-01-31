using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace RevitMCPAddin.UI.PythonRunner
{
    internal sealed class ScriptLibraryItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Feature { get; set; } = string.Empty;
        public string Keywords { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public string LastWriteTime { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // "root" or "file"
    }

    internal static class PythonRunnerScriptLibrary
    {
        public static string GetInboxPath()
        {
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP");
            Directory.CreateDirectory(baseDir);
            return Path.Combine(baseDir, "python_runner_inbox.json");
        }

        public static string? ReadInboxScriptPath(out string? source)
        {
            source = null;
            try
            {
                var path = GetInboxPath();
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var obj = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json);
                if (obj == null) return null;
                if (obj.TryGetValue("source", out var s)) source = s?.ToString();
                if (obj.TryGetValue("path", out var p)) return (p?.ToString() ?? "").Trim();
            }
            catch
            {
                return null;
            }
            return null;
        }
        private static readonly Regex FeatureLineRx = new Regex("^\\s*#\\s*@feature\\s*:\\s*(?<feature>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex KeywordLineRx = new Regex("^\\s*#\\s*@keywords\\s*:\\s*(?<keywords>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FeatureInlineRx = new Regex("^\\s*#\\s*@feature\\s*:\\s*(?<feature>[^#|]*)(?:\\|\\s*keywords\\s*:\\s*(?<keywords>.*))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class RootsConfig
        {
            public List<string> roots { get; set; } = new List<string>();
            public List<string> files { get; set; } = new List<string>();
            public List<string> excluded { get; set; } = new List<string>();
            public string? lastScript { get; set; }
        }

        public static string GetConfigPath()
        {
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP");
            Directory.CreateDirectory(baseDir);
            return Path.Combine(baseDir, "python_runner_paths.json");
        }

        public static List<string> LoadUserRoots()
        {
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path)) return new List<string>();
                var json = File.ReadAllText(path, Encoding.UTF8);
                var cfg = JsonConvert.DeserializeObject<RootsConfig>(json);
                return cfg?.roots?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList()
                       ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public static string? LoadLastScript()
        {
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path, Encoding.UTF8);
                var cfg = JsonConvert.DeserializeObject<RootsConfig>(json);
                return string.IsNullOrWhiteSpace(cfg?.lastScript) ? null : cfg?.lastScript?.Trim();
            }
            catch
            {
                return null;
            }
        }

        public static void SaveLastScript(string path)
        {
            try
            {
                var cfgPath = GetConfigPath();
                RootsConfig cfg;
                if (File.Exists(cfgPath))
                {
                    try
                    {
                        cfg = JsonConvert.DeserializeObject<RootsConfig>(File.ReadAllText(cfgPath, Encoding.UTF8)) ?? new RootsConfig();
                    }
                    catch
                    {
                        cfg = new RootsConfig();
                    }
                }
                else
                {
                    cfg = new RootsConfig();
                }

                cfg.lastScript = path;
                File.WriteAllText(cfgPath, JsonConvert.SerializeObject(cfg, Formatting.Indented), Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        public static List<string> LoadUserFiles()
        {
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path)) return new List<string>();
                var json = File.ReadAllText(path, Encoding.UTF8);
                var cfg = JsonConvert.DeserializeObject<RootsConfig>(json);
                return cfg?.files?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList()
                       ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public static List<string> LoadExcludedFiles()
        {
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path)) return new List<string>();
                var json = File.ReadAllText(path, Encoding.UTF8);
                var cfg = JsonConvert.DeserializeObject<RootsConfig>(json);
                return cfg?.excluded?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList()
                       ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public static void SaveUserRoots(IEnumerable<string> roots)
        {
            try
            {
                var path = GetConfigPath();
                RootsConfig cfg;
                if (File.Exists(path))
                {
                    try
                    {
                        cfg = JsonConvert.DeserializeObject<RootsConfig>(File.ReadAllText(path, Encoding.UTF8)) ?? new RootsConfig();
                    }
                    catch
                    {
                        cfg = new RootsConfig();
                    }
                }
                else
                {
                    cfg = new RootsConfig();
                }

                cfg.roots = roots?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                           ?? new List<string>();
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented), Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        public static void SaveUserFiles(IEnumerable<string> files)
        {
            try
            {
                var path = GetConfigPath();
                RootsConfig cfg;
                if (File.Exists(path))
                {
                    try
                    {
                        cfg = JsonConvert.DeserializeObject<RootsConfig>(File.ReadAllText(path, Encoding.UTF8)) ?? new RootsConfig();
                    }
                    catch
                    {
                        cfg = new RootsConfig();
                    }
                }
                else
                {
                    cfg = new RootsConfig();
                }

                cfg.files = files?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                           ?? new List<string>();

                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented), Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        public static void SaveExcludedFiles(IEnumerable<string> excluded)
        {
            try
            {
                var path = GetConfigPath();
                RootsConfig cfg;
                if (File.Exists(path))
                {
                    try
                    {
                        cfg = JsonConvert.DeserializeObject<RootsConfig>(File.ReadAllText(path, Encoding.UTF8)) ?? new RootsConfig();
                    }
                    catch
                    {
                        cfg = new RootsConfig();
                    }
                }
                else
                {
                    cfg = new RootsConfig();
                }

                cfg.excluded = excluded?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                              ?? new List<string>();

                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented), Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        public static List<string> BuildSearchRoots(string defaultRoot)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(defaultRoot)) list.Add(defaultRoot);
            foreach (var r in LoadUserRoots())
                list.Add(r);

            return list
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IEnumerable<ScriptLibraryItem> ScanScripts(IEnumerable<string> roots, IEnumerable<string> explicitFiles, IEnumerable<string> excludedFiles)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(excludedFiles ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var file in explicitFiles ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                if (!File.Exists(file)) continue;
                if (excluded.Contains(file)) continue;
                if (!seen.Add(file)) continue;
                var meta = ParseMetadata(file);
                yield return new ScriptLibraryItem
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    Feature = meta.feature,
                    Keywords = meta.keywords,
                    Folder = Path.GetDirectoryName(file) ?? string.Empty,
                    LastWriteTime = SafeFormatTime(file),
                    Source = "file"
                };
            }

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                if (!Directory.Exists(root)) continue;

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, "*.py", SearchOption.TopDirectoryOnly); }
                catch { continue; }

                foreach (var file in files)
                {
                    if (string.IsNullOrWhiteSpace(file)) continue;
                    if (excluded.Contains(file)) continue;
                    if (!seen.Add(file)) continue;
                    var meta = ParseMetadata(file);
                    yield return new ScriptLibraryItem
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        Feature = meta.feature,
                        Keywords = meta.keywords,
                        Folder = Path.GetDirectoryName(file) ?? string.Empty,
                        LastWriteTime = SafeFormatTime(file),
                        Source = "root"
                    };
                }
            }
        }

        public static (string feature, string keywords) ParseMetadata(string path)
        {
            string feature = "";
            string keywords = "";
            try
            {
                using var sr = new StreamReader(path, Encoding.UTF8, true);
                for (int i = 0; i < 5; i++)
                {
                    var line = sr.ReadLine();
                    if (line == null) break;
                    if (FeatureInlineRx.IsMatch(line))
                    {
                        var m = FeatureInlineRx.Match(line);
                        feature = (m.Groups["feature"].Value ?? "").Trim();
                        var kw = (m.Groups["keywords"].Value ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(kw)) keywords = kw;
                        continue;
                    }
                    if (FeatureLineRx.IsMatch(line))
                    {
                        var m = FeatureLineRx.Match(line);
                        feature = (m.Groups["feature"].Value ?? "").Trim();
                        continue;
                    }
                    if (KeywordLineRx.IsMatch(line))
                    {
                        var m = KeywordLineRx.Match(line);
                        keywords = (m.Groups["keywords"].Value ?? "").Trim();
                        continue;
                    }
                }
            }
            catch
            {
                // ignore
            }
            return (feature, keywords);
        }

        public static void UpdateMetadataInScript(string path, string feature, string keywords)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var raw = File.ReadAllText(path, Encoding.UTF8);
            var normalized = DedentCommonLeadingWhitespace(raw);

            var metaLine = BuildMetadataLine(feature, keywords);
            if (string.IsNullOrWhiteSpace(metaLine))
            {
                File.WriteAllText(path, normalized, new UTF8Encoding(false));
                return;
            }

            var lines = normalized.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            int limit = Math.Min(5, lines.Count);
            var kept = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? "";
                if (i < limit)
                {
                    if (FeatureLineRx.IsMatch(line) || KeywordLineRx.IsMatch(line) || FeatureInlineRx.IsMatch(line))
                        continue;
                }
                kept.Add(line);
            }
            kept.Insert(0, metaLine);
            File.WriteAllText(path, string.Join(Environment.NewLine, kept), new UTF8Encoding(false));
        }

        private static string BuildMetadataLine(string feature, string keywords)
        {
            feature = (feature ?? "").Trim();
            keywords = (keywords ?? "").Trim();
            if (string.IsNullOrWhiteSpace(feature) && string.IsNullOrWhiteSpace(keywords)) return string.Empty;
            if (string.IsNullOrWhiteSpace(keywords)) return "# @feature: " + feature;
            if (string.IsNullOrWhiteSpace(feature)) return "# @feature: " + "" + " | keywords: " + keywords;
            return "# @feature: " + feature + " | keywords: " + keywords;
        }

        private static string DedentCommonLeadingWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int min = int.MaxValue;
            foreach (var raw in lines)
            {
                var line = raw ?? "";
                if (line.Trim().Length == 0) continue;
                int count = 0;
                while (count < line.Length && (line[count] == ' ' || line[count] == '\t')) count++;
                if (count < min) min = count;
                if (min == 0) break;
            }
            if (min == int.MaxValue || min == 0) return string.Join("\n", lines).Replace("\n", Environment.NewLine);
            var adjusted = lines.Select(l => (l ?? "").Length >= min ? (l ?? "").Substring(min) : (l ?? ""));
            return string.Join(Environment.NewLine, adjusted);
        }

        private static string SafeFormatTime(string path)
        {
            try
            {
                var dt = File.GetLastWriteTime(path);
                return dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "";
            }
        }
    }
}
