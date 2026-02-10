// Core/Paths.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    public static class Paths
    {
        private sealed class PathsConfig
        {
            public string? root { get; set; }
            public string? codexRoot { get; set; }
            public string? workRoot { get; set; }
            public string? appsRoot { get; set; }
            public string? docsRoot { get; set; }
            public string? scriptsRoot { get; set; }
        }

        public static string AddinFolder
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly().Location;
                var dir = Path.GetDirectoryName(asm);
                return string.IsNullOrEmpty(dir) ? AppDomain.CurrentDomain.BaseDirectory : dir!;
            }
        }

        /// <summary>%LOCALAPPDATA%\RevitMCP</summary>
        public static string LocalRoot
        {
            get
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "RevitMCP");
            }
        }

        /// <summary>%LOCALAPPDATA%\RevitMCP\logs（存在しなければ作成）</summary>
        public static string EnsureLocalLogs()
        {
            var p = Path.Combine(LocalRoot, "logs");
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }

        /// <summary>
        /// Ensure paths.json exists under %LOCALAPPDATA%\RevitMCP (and optionally Documents\Revit_MCP).
        /// This helps keep CodexGUI/Apps paths stable across machines.
        /// </summary>
        public static void EnsurePathsConfig(bool overwriteInvalid = false)
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrWhiteSpace(docs)) return;

                var root = Path.Combine(docs, "Revit_MCP");
                var legacyRoot = Path.Combine(docs, "Codex_MCP");
                if (!Directory.Exists(root) && Directory.Exists(legacyRoot)) root = legacyRoot;

                // Ensure root directories exist (best-effort).
                TryCreateDir(root);
                TryCreateDir(Path.Combine(root, "Apps"));
                TryCreateDir(Path.Combine(root, "Codex"));
                TryCreateDir(Path.Combine(root, "Docs"));
                TryCreateDir(Path.Combine(root, "Scripts"));
                TryCreateDir(Path.Combine(root, "Projects"));
                TryCreateDir(Path.Combine(root, "Logs"));
                TryCreateDir(Path.Combine(root, "Settings"));

                var cfg = new PathsConfig
                {
                    root = root,
                    codexRoot = Path.Combine(root, "Codex"),
                    workRoot = Path.Combine(root, "Projects"),
                    appsRoot = Path.Combine(root, "Apps"),
                    docsRoot = Path.Combine(root, "Docs"),
                    scriptsRoot = Path.Combine(root, "Scripts")
                };

                var localPath = Path.Combine(LocalRoot, "paths.json");
                EnsurePathsFile(localPath, cfg, overwriteInvalid);

                // Also place a copy under Documents\Revit_MCP for user visibility.
                var docsPath = Path.Combine(root, "paths.json");
                EnsurePathsFile(docsPath, cfg, overwriteInvalid);
            }
            catch
            {
                // ignore (best-effort)
            }
        }

        private static void EnsurePathsFile(string path, PathsConfig cfg, bool overwriteInvalid)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    if (!overwriteInvalid) return;
                    try
                    {
                        var json0 = File.ReadAllText(path, Encoding.UTF8);
                        var c0 = JsonConvert.DeserializeObject<PathsConfig>(json0);
                        if (c0 != null && !string.IsNullOrWhiteSpace(c0.root)) return;
                    }
                    catch
                    {
                        // fallthrough to overwrite
                    }
                }

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>%LOCALAPPDATA%\RevitMCP\locks（存在しなければ作成）</summary>
        public static string EnsureLocalLocks()
        {
            var p = Path.Combine(LocalRoot, "locks");
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }

        // （参考）アドイン配下の logs が必要な場合だけ使う
        public static string EnsureAddinLogs()
        {
            var p = Path.Combine(AddinFolder, "logs");
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }

        public static string? ResolveRoot()
        {
            var env = Environment.GetEnvironmentVariable("REVIT_MCP_ROOT");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

            var cfg = TryLoadPathsConfig();
            if (!string.IsNullOrWhiteSpace(cfg?.root) && Directory.Exists(cfg.root)) return cfg.root;

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
            {
                var p0 = Path.Combine(docs, "Revit_MCP");
                if (Directory.Exists(p0)) return p0;
                var p1 = Path.Combine(docs, "Codex_MCP");
                if (Directory.Exists(p1)) return p1;
            }

            return null;
        }

        public static string? ResolveAppsRoot()
        {
            var cfg = TryLoadPathsConfig();
            if (!string.IsNullOrWhiteSpace(cfg?.appsRoot) && Directory.Exists(cfg.appsRoot)) return cfg.appsRoot;

            var root = ResolveRoot();
            if (!string.IsNullOrWhiteSpace(root))
            {
                var p = Path.Combine(root, "Apps");
                if (Directory.Exists(p)) return p;
            }

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
            {
                var p0 = Path.Combine(docs, "Revit_MCP", "Apps");
                if (Directory.Exists(p0)) return p0;
            }

            return null;
        }

        public static string? ResolveCodexRoot()
        {
            var env = Environment.GetEnvironmentVariable("REVIT_MCP_ROOT");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            {
                var codex2 = Path.Combine(env, "Codex");
                if (Directory.Exists(codex2)) return codex2;
            }

            var env2 = Environment.GetEnvironmentVariable("CODEX_MCP_ROOT");
            if (!string.IsNullOrWhiteSpace(env2) && Directory.Exists(env2)) return env2;

            var cfg = TryLoadPathsConfig();
            if (cfg != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.codexRoot) && Directory.Exists(cfg.codexRoot))
                    return cfg.codexRoot;
                if (!string.IsNullOrWhiteSpace(cfg.root))
                {
                    var c2 = Path.Combine(cfg.root, "Codex");
                    if (Directory.Exists(c2)) return c2;
                }
            }

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
            {
                var p0b = Path.Combine(docs, "Revit_MCP", "Codex");
                if (Directory.Exists(p0b)) return p0b;
                var p1 = Path.Combine(docs, "Codex_MCP", "Codex");
                if (Directory.Exists(p1)) return p1;
                var p2 = Path.Combine(docs, "Codex");
                if (Directory.Exists(p2)) return p2;
            }

            return null;
        }

        public static string? ResolveWorkRoot()
        {
            var env = Environment.GetEnvironmentVariable("REVIT_MCP_WORK_ROOT");
            if (!string.IsNullOrWhiteSpace(env))
            {
                var workDir = NormalizeWorkDir(env);
                TryCreateDir(workDir);
                if (Directory.Exists(workDir)) return workDir;
            }

            var cfg = TryLoadPathsConfig();
            if (cfg != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.workRoot))
                {
                    var workDir = NormalizeWorkDir(cfg.workRoot);
                    TryCreateDir(workDir);
                    if (Directory.Exists(workDir)) return workDir;
                }

                if (!string.IsNullOrWhiteSpace(cfg.root))
                {
                    var workDir = NormalizeWorkDir(cfg.root);
                    TryCreateDir(workDir);
                    if (Directory.Exists(workDir)) return workDir;
                }

                if (!string.IsNullOrWhiteSpace(cfg.codexRoot))
                {
                    var rootFromCodex = TryGetRootFromCodex(cfg.codexRoot);
                    if (!string.IsNullOrWhiteSpace(rootFromCodex))
                    {
                        var workDir = NormalizeWorkDir(rootFromCodex);
                        TryCreateDir(workDir);
                        if (Directory.Exists(workDir)) return workDir;
                    }
                }
            }

            var env2 = Environment.GetEnvironmentVariable("CODEX_MCP_ROOT");
            if (!string.IsNullOrWhiteSpace(env2))
            {
                var rootFromCodex = TryGetRootFromCodex(env2) ?? env2;
                var workDir = NormalizeWorkDir(rootFromCodex);
                TryCreateDir(workDir);
                if (Directory.Exists(workDir)) return workDir;
            }

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
            {
                var p0 = Path.Combine(docs, "Revit_MCP");
                if (Directory.Exists(p0)) return NormalizeWorkDir(p0);
                var p1 = Path.Combine(docs, "Codex_MCP");
                if (Directory.Exists(p1)) return NormalizeWorkDir(p1);
                var p2 = Path.Combine(docs, "Codex");
                if (Directory.Exists(p2)) return NormalizeWorkDir(p2);
            }

            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    var projectsDir = Path.Combine(dir.FullName, "Projects");
                    if (Directory.Exists(projectsDir))
                        return projectsDir;
                    var workDir = Path.Combine(dir.FullName, "Work");
                    if (Directory.Exists(workDir))
                        return workDir;
                    dir = dir.Parent;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static string NormalizeWorkDir(string rootOrWork)
        {
            if (string.IsNullOrWhiteSpace(rootOrWork)) return rootOrWork;

            var trimmed = rootOrWork.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dir = new DirectoryInfo(trimmed);
            var name = dir.Name;

            // If someone passed Codex root, redirect to sibling Projects.
            if (string.Equals(name, "Codex", StringComparison.OrdinalIgnoreCase))
            {
                if (dir.Parent != null && Directory.Exists(dir.Parent.FullName))
                    return Path.Combine(dir.Parent.FullName, "Projects");
            }

            // If someone passed ...\Codex\Projects, redirect to ...\Projects
            if (string.Equals(name, "Projects", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Work", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "UserProjects", StringComparison.OrdinalIgnoreCase))
            {
                var parent = dir.Parent;
                if (parent != null && string.Equals(parent.Name, "Codex", StringComparison.OrdinalIgnoreCase))
                {
                    var root = parent.Parent?.FullName;
                    if (!string.IsNullOrWhiteSpace(root))
                        return Path.Combine(root, "Projects");
                }
                return trimmed;
            }

            return Path.Combine(trimmed, "Projects");
        }

        private static PathsConfig? TryLoadPathsConfig()
        {
            try
            {
                var candidates = new List<string>
                {
                    Path.Combine(LocalRoot, "paths.json")
                };

                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    candidates.Add(Path.Combine(docs, "Revit_MCP", "paths.json"));
                    candidates.Add(Path.Combine(docs, "Codex_MCP", "paths.json"));
                }

                foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(path)) continue;
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var cfg = JsonConvert.DeserializeObject<PathsConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static string? TryGetRootFromCodex(string codexRoot)
        {
            if (string.IsNullOrWhiteSpace(codexRoot)) return null;
            try
            {
                var dir = new DirectoryInfo(codexRoot);
                if (dir == null) return null;
                var name = dir.Name;
                if (string.Equals(name, "Codex", StringComparison.OrdinalIgnoreCase))
                {
                    if (dir.Parent != null && Directory.Exists(dir.Parent.FullName))
                        return dir.Parent.FullName;
                }
                if (string.Equals(name, "Docs", StringComparison.OrdinalIgnoreCase) && dir.Parent != null)
                {
                    if (dir.Parent.Parent != null && Directory.Exists(dir.Parent.Parent.FullName))
                        return dir.Parent.Parent.FullName;
                }
                var docsIdx = codexRoot.IndexOf(Path.Combine("Docs", "Codex"), StringComparison.OrdinalIgnoreCase);
                if (docsIdx >= 0)
                {
                    var root = codexRoot.Substring(0, docsIdx - 1);
                    if (Directory.Exists(root)) return root;
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static void TryCreateDir(string path)
        {
            try { Directory.CreateDirectory(path); } catch { }
        }
    }
}
