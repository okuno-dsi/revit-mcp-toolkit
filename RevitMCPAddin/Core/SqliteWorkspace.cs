#nullable enable
using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class SqliteWorkspace
    {
        public static void EnsureInitialized()
        {
            try
            {
                var localRoot = Paths.LocalRoot;
                var docsRoot = ResolveDocsRoot();
                var localSqliteRoot = Path.Combine(localRoot, "SQLite");
                var docsSqliteRoot = Path.Combine(docsRoot, "SQLite");

                TryCreateDir(localRoot);
                TryCreateDir(localSqliteRoot);
                TryCreateDir(Path.Combine(localSqliteRoot, "Cache"));
                TryCreateDir(Path.Combine(localSqliteRoot, "Temp"));

                TryCreateDir(docsRoot);
                TryCreateDir(Path.Combine(docsRoot, "Settings"));
                TryCreateDir(docsSqliteRoot);
                TryCreateDir(Path.Combine(docsSqliteRoot, "Manuals"));
                TryCreateDir(Path.Combine(docsSqliteRoot, "Projects"));
                TryCreateDir(Path.Combine(docsSqliteRoot, "Shared"));
                TryCreateDir(Path.Combine(docsSqliteRoot, "Legal"));
                TryCreateDir(Path.Combine(docsSqliteRoot, "Legal", "BuildingCode"));

                EnsurePolicyFile(Path.Combine(localRoot, "sqlite_policy.json"), docsRoot, localRoot);
                EnsurePolicyFile(Path.Combine(docsRoot, "Settings", "sqlite_policy.json"), docsRoot, localRoot);
            }
            catch
            {
                // best-effort only
            }
        }

        public static void EnsureSettingsSection(JObject root)
        {
            if (root == null) return;

            var docsRoot = ResolveDocsRoot();
            var localRoot = Paths.LocalRoot;
            var docsSqliteRoot = Path.Combine(docsRoot, "SQLite");
            var localSqliteRoot = Path.Combine(localRoot, "SQLite");

            var sqlite = root["sqlite"] as JObject;
            if (sqlite == null)
            {
                sqlite = new JObject();
                root["sqlite"] = sqlite;
            }

            SetIfMissing(sqlite, "enabled", true);
            SetIfMissing(sqlite, "mode", "adaptive");
            SetIfMissing(sqlite, "allowManualIndex", true);
            SetIfMissing(sqlite, "allowProjectCache", true);
            SetIfMissing(sqlite, "allowSnapshotCache", true);
            SetIfMissing(sqlite, "allowScheduleCache", true);
            SetIfMissing(sqlite, "allowExcelRoundtripCache", true);
            SetIfMissing(sqlite, "allowAuditLog", true);
            SetIfMissing(sqlite, "authority", "revit");
            SetIfMissing(sqlite, "baseDir", docsSqliteRoot);
            SetIfMissing(sqlite, "localCacheDir", localSqliteRoot);
            SetIfMissing(sqlite, "manualsDbPath", Path.Combine(docsSqliteRoot, "Manuals", "codex_manuals.sqlite"));
            SetIfMissing(sqlite, "sharedDbDir", Path.Combine(docsSqliteRoot, "Shared"));
            SetIfMissing(sqlite, "buildingLawBundleDir", Path.Combine(docsSqliteRoot, "Legal", "BuildingCode"));
            SetIfMissing(sqlite, "buildingLawXmlDbPath", Path.Combine(docsSqliteRoot, "Legal", "BuildingCode", "xml_law_index.sqlite"));
            SetIfMissing(sqlite, "buildingLawGovTextDbPath", Path.Combine(docsSqliteRoot, "Legal", "BuildingCode", "gov_document_text_index.sqlite"));
            SetIfMissing(sqlite, "buildingLawGovPdfDbPath", Path.Combine(docsSqliteRoot, "Legal", "BuildingCode", "gov_document_pdf_index.sqlite"));
            SetIfMissing(sqlite, "projectDbDirName", "SQLite");
            SetIfMissing(sqlite, "snapshotDbFileName", "revit_snapshots.sqlite");
            SetIfMissing(sqlite, "scheduleDbFileName", "schedule_cache.sqlite");
            SetIfMissing(sqlite, "excelRoundtripDbFileName", "excel_roundtrip.sqlite");
        }

        private static void EnsurePolicyFile(string path, string docsRoot, string localRoot)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                JObject root;
                if (File.Exists(path))
                {
                    try
                    {
                        root = JObject.Parse(File.ReadAllText(path));
                    }
                    catch
                    {
                        root = new JObject();
                    }
                }
                else
                {
                    root = new JObject();
                }

                SetIfMissing(root, "schema", "revitmcp.sqlite.policy.v1");
                SetIfMissing(root, "enabled", true);
                SetIfMissing(root, "mode", "adaptive");
                SetIfMissing(root, "authority", "revit");

                var usage = root["usage"] as JArray;
                if (usage == null)
                {
                    usage = new JArray();
                    root["usage"] = usage;
                }
                AppendIfMissing(usage, "manual-index");
                AppendIfMissing(usage, "project-cache");
                AppendIfMissing(usage, "snapshot-cache");
                AppendIfMissing(usage, "schedule-cache");
                AppendIfMissing(usage, "excel-roundtrip-diff");
                AppendIfMissing(usage, "audit-log");
                AppendIfMissing(usage, "legal-readonly-bundle");

                var roots = root["roots"] as JObject;
                if (roots == null)
                {
                    roots = new JObject();
                    root["roots"] = roots;
                }
                SetIfMissing(roots, "documentsRoot", docsRoot);
                SetIfMissing(roots, "documentsSqliteRoot", Path.Combine(docsRoot, "SQLite"));
                SetIfMissing(roots, "localRoot", localRoot);
                SetIfMissing(roots, "localSqliteRoot", Path.Combine(localRoot, "SQLite"));

                var defaults = root["defaults"] as JObject;
                if (defaults == null)
                {
                    defaults = new JObject();
                    root["defaults"] = defaults;
                }
                SetIfMissing(defaults, "manualsDbPath", Path.Combine(docsRoot, "SQLite", "Manuals", "codex_manuals.sqlite"));
                SetIfMissing(defaults, "buildingLawBundleDir", Path.Combine(docsRoot, "SQLite", "Legal", "BuildingCode"));
                SetIfMissing(defaults, "buildingLawXmlDbPath", Path.Combine(docsRoot, "SQLite", "Legal", "BuildingCode", "xml_law_index.sqlite"));
                SetIfMissing(defaults, "buildingLawGovTextDbPath", Path.Combine(docsRoot, "SQLite", "Legal", "BuildingCode", "gov_document_text_index.sqlite"));
                SetIfMissing(defaults, "buildingLawGovPdfDbPath", Path.Combine(docsRoot, "SQLite", "Legal", "BuildingCode", "gov_document_pdf_index.sqlite"));
                SetIfMissing(defaults, "projectDbDirName", "SQLite");
                SetIfMissing(defaults, "snapshotDbFileName", "revit_snapshots.sqlite");
                SetIfMissing(defaults, "scheduleDbFileName", "schedule_cache.sqlite");
                SetIfMissing(defaults, "excelRoundtripDbFileName", "excel_roundtrip.sqlite");

                File.WriteAllText(path, JsonNetCompat.ToIndentedJson(root));
            }
            catch
            {
                // best-effort only
            }
        }

        private static string ResolveDocsRoot()
        {
            try
            {
                var root = Paths.ResolveRoot();
                if (!string.IsNullOrWhiteSpace(root))
                    return root;
            }
            catch
            {
                // ignore
            }

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
                return Path.Combine(Paths.LocalRoot, "DocumentsFallback");

            return Path.Combine(docs, "Revit_MCP");
        }

        private static void SetIfMissing(JObject obj, string name, JToken value)
        {
            if (obj[name] == null)
                obj[name] = value;
        }

        private static void AppendIfMissing(JArray array, string value)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (string.Equals((string?)array[i], value, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            array.Add(value);
        }

        private static void TryCreateDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
