// ================================================================
// File: Core/Snapshot/SnapshotManager.cs
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    internal static class SnapshotManager
    {
        public static SnapshotResultMeta Run(Document doc, RequestMeta meta, string timingLabel)
        {
            var opt = meta.SnapshotOptions ?? new SnapshotOptions();
            var snapshotId = MakeSnapshotId();
            var root = ResolveRootDir(opt, snapshotId, timingLabel);

            var files = new List<CategoryFile>();
            foreach (var cat in opt.Categories)
            {
                var cf = SnapshotWriter.WriteCategoryJsonl(doc, root, cat, opt, meta.UnitsMode);
                files.Add(cf);
            }

            var manifest = BuildManifest(root, snapshotId, files, opt);
            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));

            return new SnapshotResultMeta
            {
                SnapshotId = snapshotId,
                RootDir = root,
                ManifestPath = manifestPath,
                Files = files
            };
        }

        private static string ResolveRootDir(SnapshotOptions opt, string snapshotId, string timing)
        {
            var baseDir = !string.IsNullOrEmpty(opt.BaseDir)
                ? opt.BaseDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP", "data");
            var dir = Path.Combine(baseDir, $"{snapshotId}_{timing}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string MakeSnapshotId()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        private static SnapshotManifest BuildManifest(string root, string snapshotId, List<CategoryFile> files, SnapshotOptions opt)
        {
            var man = new SnapshotManifest
            {
                SnapshotId = snapshotId,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK"),
                Units = new { Length = "mm", Area = "m2", Volume = "m3" },
                Categories = new List<CategoryEntry>()
            };

            foreach (var f in files)
            {
                var entry = new CategoryEntry
                {
                    Name = f.Category,
                    Rows = f.RowCount,
                    SchemaHash = f.SchemaHash,
                    Columns = new List<string>(), // 省略（必要なら先頭行キーから導出）
                    Path = f.Path
                };

                // サンプル
                var sample = new List<Dictionary<string, object?>>();
                try
                {
                    int cnt = 0;
                    using (var sr = new StreamReader(f.Path))
                    {
                        while (!sr.EndOfStream && cnt < Math.Max(1, opt.SampleRows))
                        {
                            var line = sr.ReadLine();
                            if (string.IsNullOrWhiteSpace(line)) break;
                            var obj = JsonConvert.DeserializeObject<Dictionary<string, object?>>(line ?? "{}");
                            if (obj != null) sample.Add(obj);
                            cnt++;
                        }
                    }

                    if (sample.Count > 0)
                    {
                        entry.Columns = sample[0].Keys.ToList();
                    }
                }
                catch { /* ignore */ }

                entry.Sample = sample;
                man.Categories.Add(entry);
            }
            return man;
        }
    }
}
