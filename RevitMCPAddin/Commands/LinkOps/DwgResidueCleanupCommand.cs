#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitMCPAddin.Commands.LinkOps
{
    internal static class DwgResidueCleanupHelper
    {
        internal sealed class DwgResidueReport
        {
            public bool Ok { get; set; } = true;
            public string Code { get; set; } = "OK";
            public string Msg { get; set; } = "OK";
            public bool DryRun { get; set; }
            public bool DeleteLiveImports { get; set; }
            public bool PurgeAllUnused { get; set; }
            public string UnusedPurgeMode { get; set; } = "materials";
            public int MaxPurgePasses { get; set; }
            public int MaxDeleteElementCount { get; set; }
            public bool DeleteLimitReached { get; set; }
            public bool ExportCandidateList { get; set; }
            public string CandidateListPath { get; set; } = string.Empty;
            public string CandidateListDir { get; set; } = string.Empty;
            public bool UseCandidateFile { get; set; }
            public bool UseCandidateList { get; set; }
            public int CandidateListSelectedRootCount { get; set; }
            public int CandidateRootCount { get; set; }
            public int SelectedRootCount { get; set; }
            public int SkippedLiveRootCount { get; set; }
            public int SkippedLiveImportInstanceCount { get; set; }
            public int DeletedLiveImportRequestCount { get; set; }
            public int DeletedRootCount { get; set; }
            public int DeletedImportCleanupElementCount { get; set; }
            public int PurgePassCount { get; set; }
            public int RequestedUnusedMaterialCount { get; set; }
            public int RequestedUnusedAppearanceAssetCount { get; set; }
            public int RequestedUnusedOtherCount { get; set; }
            public int DeletedUnusedElementCount { get; set; }
            public List<object> CandidateRoots { get; set; } = new List<object>();
            public List<object> DeletedRoots { get; set; } = new List<object>();
            public List<object> SkippedRoots { get; set; } = new List<object>();
            public List<object> FailedRoots { get; set; } = new List<object>();
            public List<object> PurgePasses { get; set; } = new List<object>();
            public List<string> Notes { get; set; } = new List<string>();
        }

        private sealed class ImportedCadRootSnapshot
        {
            public ElementId Id { get; set; } = ElementId.InvalidElementId;
            public int IntId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public int DescendantCategoryCount { get; set; }
            public List<ElementId> LiveImportIds { get; set; } = new List<ElementId>();
        }

        private sealed class DeleteBudget
        {
            public int Max { get; }
            public int Used { get; private set; }
            public bool LimitReached { get; private set; }
            public bool IsUnlimited => Max <= 0;
            public int Remaining => IsUnlimited ? int.MaxValue : Math.Max(0, Max - Used);

            public DeleteBudget(int max)
            {
                Max = Math.Max(0, max);
            }

            public bool CanAttempt => IsUnlimited || Used < Max;

            public bool TryAccept(int actualDeletedCount)
            {
                if (actualDeletedCount <= 0) return true;
                if (IsUnlimited)
                {
                    Used += actualDeletedCount;
                    return true;
                }

                if (Used + actualDeletedCount > Max)
                {
                    LimitReached = true;
                    return false;
                }

                Used += actualDeletedCount;
                if (Used >= Max) LimitReached = true;
                return true;
            }
        }

        internal static DwgResidueReport Run(Document doc, JObject p)
        {
            bool dryRun = p.Value<bool?>("dryRun") ?? p.Value<bool?>("dry_run") ?? true;
            bool deleteLiveImports = p.Value<bool?>("deleteLiveImports") ?? false;
            bool purgeAllUnused = p.Value<bool?>("purgeAllUnused") ?? false;
            int maxPurgePasses = Math.Max(1, p.Value<int?>("maxPurgePasses") ?? p.Value<int?>("maxPasses") ?? 20);
            int maxDeleteElementCount = Math.Max(0, p.Value<int?>("maxDeleteElementCount") ?? p.Value<int?>("max_delete_count") ?? 10000);
            string unusedPurgeMode = NormalizeUnusedPurgeMode(p.Value<string>("unusedPurgeMode"), purgeAllUnused);
            bool exportCandidateList = p.Value<bool?>("exportCandidateList") ?? p.Value<bool?>("export_candidate_list") ?? false;
            string candidateListPath = (p.Value<string>("candidateListPath") ?? p.Value<string>("candidate_list_path") ?? string.Empty).Trim();
            bool? requestedUseCandidateFile = p.Value<bool?>("useCandidateFile") ?? p.Value<bool?>("use_candidate_file");
            bool useCandidateFile = requestedUseCandidateFile ?? !string.IsNullOrWhiteSpace(candidateListPath);
            string candidateListDir = (p.Value<string>("candidateFileDir") ?? p.Value<string>("candidate_file_dir")
                ?? p.Value<string>("cadidateFileDir") ?? p.Value<string>("cadidate_file_dir")
                ?? p.Value<string>("candidateListDir") ?? p.Value<string>("candidate_list_dir")
                ?? p.Value<string>("candidateListOutputDir") ?? p.Value<string>("candidate_list_output_dir")
                ?? string.Empty).Trim();

            var report = new DwgResidueReport
            {
                DryRun = dryRun,
                DeleteLiveImports = deleteLiveImports,
                PurgeAllUnused = purgeAllUnused,
                UnusedPurgeMode = unusedPurgeMode,
                MaxPurgePasses = maxPurgePasses,
                MaxDeleteElementCount = maxDeleteElementCount,
                ExportCandidateList = exportCandidateList,
                CandidateListPath = candidateListPath,
                CandidateListDir = candidateListDir,
                UseCandidateFile = useCandidateFile
            };
            if (unusedPurgeMode == "materials")
                report.Notes.Add("Unused purge is limited to OST_Materials by default. Use unusedPurgeMode=all or purgeAllUnused=true only when broad unused purging is intentional.");
            else if (unusedPurgeMode == "materials-and-assets")
                report.Notes.Add("unusedPurgeMode=materials-and-assets includes uncategorized unused elements; review the result carefully.");
            else if (unusedPurgeMode == "all")
                report.Notes.Add("unusedPurgeMode=all can delete non-DWG unused elements; use candidate lists and dry-run before applying.");

            if (doc == null)
            {
                report.Ok = false;
                report.Code = "NO_ACTIVE_DOCUMENT";
                report.Msg = "No active document.";
                return report;
            }

            var roots = ApplyRootFilters(GetCandidateImportRoots(doc), p);
            report.CandidateRootCount = roots.Count;

            if (roots.Count == 0)
            {
                report.Msg = "No imported CAD category roots were found.";
                return report;
            }

            var liveImportMap = CollectLiveImportInstancesByRoot(doc, roots);
            var rootSnapshots = SnapshotRoots(roots, liveImportMap);
            if (useCandidateFile)
            {
                report.UseCandidateList = true;
                if (string.IsNullOrWhiteSpace(candidateListPath))
                {
                    rootSnapshots = new List<ImportedCadRootSnapshot>();
                    report.CandidateListSelectedRootCount = 0;
                    report.Notes.Add("useCandidateFile=true but candidateListPath is empty; no roots are selected.");
                }
                else
                {
                    var listedIds = ReadCandidateRootIds(candidateListPath, report);
                    rootSnapshots = rootSnapshots
                        .Where(x => listedIds.Contains(x.IntId))
                        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    report.CandidateListSelectedRootCount = rootSnapshots.Count;
                    if (listedIds.Count == 0)
                        report.Notes.Add($"Candidate list did not contain rootCategory rows: {candidateListPath}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(candidateListPath))
            {
                report.Notes.Add($"Candidate list path was ignored because useCandidateFile=false: {candidateListPath}");
            }

            foreach (var root in rootSnapshots)
            {
                report.CandidateRoots.Add(new
                {
                    rootCategoryId = root.IntId,
                    rootName = root.Name,
                    fullPath = root.FullPath,
                    descendantCategoryCount = root.DescendantCategoryCount,
                    liveImportIdCount = root.LiveImportIds.Count,
                    liveImportIds = root.LiveImportIds.Select(x => x.IntValue()).ToList()
                });
            }
            if (exportCandidateList)
                ExportCandidateRootList(doc, rootSnapshots, report);

            if (dryRun)
            {
                report.SelectedRootCount = rootSnapshots.Count(x => deleteLiveImports || !liveImportMap.ContainsKey(x.IntId));
                report.SkippedLiveRootCount = rootSnapshots.Count - report.SelectedRootCount;
                report.SkippedLiveImportInstanceCount = liveImportMap.Values.Sum(x => x.Count(id => IsLiveImportInstanceId(doc, id)));
                report.Msg = $"Dry-run found {report.SelectedRootCount} DWG residue root(s) that can be attempted.";
                return report;
            }

            using (var tg = new TransactionGroup(doc, "[MCP] Purge DWG residue"))
            {
                var budget = new DeleteBudget(maxDeleteElementCount);
                bool started = false;
                bool committed = false;
                try
                {
                    tg.Start();
                    started = true;

                    DeleteImportedCadRoots(doc, rootSnapshots, liveImportMap, deleteLiveImports, budget, report);
                    PurgeUnusedUntilStable(doc, unusedPurgeMode, maxPurgePasses, budget, report);

                    tg.Assimilate();
                    committed = true;
                }
                finally
                {
                    if (started && !committed)
                    {
                        try { tg.RollBack(); } catch { }
                    }
                }

                report.DeleteLimitReached = budget.LimitReached;
            }

            report.Msg =
                $"DWG residue cleanup finished. Deleted roots={report.DeletedRootCount}, " +
                $"deleted imported cleanup elements={report.DeletedImportCleanupElementCount}, " +
                $"purge passes={report.PurgePassCount}, deleted unused elements={report.DeletedUnusedElementCount}.";
            if (report.DeleteLimitReached)
            {
                report.Code = "LIMIT_REACHED";
                report.Msg += $" Delete limit reached at maxDeleteElementCount={report.MaxDeleteElementCount}; run again to continue, or set maxDeleteElementCount=0 only when unlimited deletion is intended.";
            }
            if (report.FailedRoots.Count > 0)
            {
                report.Code = report.DeleteLimitReached
                    ? "PARTIAL_LIMIT_REACHED"
                    : report.DeletedRootCount > 0 || report.DeletedUnusedElementCount > 0 ? "PARTIAL" : "PURGE_FAILED";
                report.Ok = report.Code == "PARTIAL";
                if (report.Code == "PARTIAL_LIMIT_REACHED")
                    report.Ok = true;
                report.Msg += $" Failed roots={report.FailedRoots.Count}.";
            }

            return report;
        }

        private static void DeleteImportedCadRoots(
            Document doc,
            IList<ImportedCadRootSnapshot> roots,
            Dictionary<int, List<ElementId>> liveImportMap,
            bool deleteLiveImports,
            DeleteBudget budget,
            DwgResidueReport report)
        {
            if (deleteLiveImports)
            {
                var liveDeleteIds = liveImportMap
                    .Values
                    .SelectMany(x => x)
                    .Where(IsValidId)
                    .GroupBy(x => x.IntValue())
                    .Select(g => g.First())
                    .ToList();

                if (liveDeleteIds.Count > 0)
                {
                    foreach (var id in liveDeleteIds)
                    {
                        if (!budget.CanAttempt)
                        {
                            report.DeleteLimitReached = true;
                            break;
                        }

                        using (var tx = new Transaction(doc, "[MCP] Delete live CAD import"))
                        {
                            tx.Start();
                            report.DeletedLiveImportRequestCount++;
                            var deleted = SafeDelete(doc, new List<ElementId> { id }, null);
                            if (!budget.TryAccept(deleted.Count))
                            {
                                tx.RollBack();
                                report.DeleteLimitReached = true;
                                report.Notes.Add($"Delete limit reached while deleting live CAD import/type id={id.IntValue()} actualDeleted={deleted.Count}.");
                                break;
                            }

                            report.DeletedImportCleanupElementCount += deleted.Count;
                            tx.Commit();
                        }
                    }
                }
            }
            else
            {
                report.SkippedLiveImportInstanceCount = liveImportMap.Values.Sum(x => x.Count(id => IsLiveImportInstanceId(doc, id)));
            }

            foreach (var root in roots)
            {
                if (root == null) continue;
                if (!budget.CanAttempt)
                {
                    report.DeleteLimitReached = true;
                    report.Notes.Add("Delete limit reached before deleting remaining imported CAD category roots.");
                    break;
                }

                var rootId = root.IntId;
                var rootName = root.Name;
                if (!deleteLiveImports && liveImportMap.ContainsKey(rootId))
                {
                    report.SkippedLiveRootCount++;
                    report.SkippedRoots.Add(new
                    {
                        rootCategoryId = rootId,
                        rootName,
                        reason = "live-import-instance-present"
                    });
                    continue;
                }

                using (var tx = new Transaction(doc, "[MCP] Delete imported CAD category"))
                {
                    tx.Start();
                    var errors = new List<object>();
                    var deleted = SafeDelete(doc, new List<ElementId> { root.Id }, errors);
                    if (deleted.Count > 0)
                    {
                        if (!budget.TryAccept(deleted.Count))
                        {
                            tx.RollBack();
                            report.DeleteLimitReached = true;
                            report.SkippedRoots.Add(new
                            {
                                rootCategoryId = rootId,
                                rootName,
                                reason = "delete-limit-reached",
                                deletedElementCountIfAllowed = deleted.Count,
                                maxDeleteElementCount = budget.Max
                            });
                            break;
                        }

                        report.DeletedRootCount++;
                        report.DeletedImportCleanupElementCount += deleted.Count;
                        report.DeletedRoots.Add(new
                        {
                            rootCategoryId = rootId,
                            rootName,
                            deletedElementCount = deleted.Count,
                            deletedElementIds = deleted.Select(x => x.IntValue()).ToList()
                        });
                    }
                    else
                    {
                        report.FailedRoots.Add(new
                        {
                            rootCategoryId = rootId,
                            rootName,
                            reason = "document-delete-returned-zero",
                            errors
                        });
                    }

                    tx.Commit();
                }
            }
        }

        private static void PurgeUnusedUntilStable(
            Document doc,
            string unusedPurgeMode,
            int maxPasses,
            DeleteBudget budget,
            DwgResidueReport report)
        {
            var categories = BuildUnusedCategoryFilter(unusedPurgeMode);
            if (categories == null)
            {
                report.Notes.Add("Unused purge skipped because unusedPurgeMode=none.");
                return;
            }

            const int maxRequestedIdsPerPass = 1000;
            for (int pass = 1; pass <= maxPasses; pass++)
            {
                if (!budget.CanAttempt)
                {
                    report.DeleteLimitReached = true;
                    report.Notes.Add("Delete limit reached before the unused purge pass.");
                    break;
                }

                var unused = doc.GetUnusedElements(categories);
                var purgeIds = NormalizeIds(unused);
                if (purgeIds.Count == 0)
                    break;

                int availableCount = purgeIds.Count;
                int perPassLimit = budget.IsUnlimited
                    ? maxRequestedIdsPerPass
                    : Math.Min(maxRequestedIdsPerPass, budget.Remaining);
                if (perPassLimit <= 0)
                {
                    report.DeleteLimitReached = true;
                    report.Notes.Add("Delete limit reached before selecting unused purge ids.");
                    break;
                }

                if (purgeIds.Count > perPassLimit)
                    purgeIds = purgeIds.Take(perPassLimit).ToList();

                int materialCount = CountOfType<Material>(doc, purgeIds);
                int appearanceAssetCount = CountOfType<AppearanceAssetElement>(doc, purgeIds);
                int otherCount = Math.Max(0, purgeIds.Count - materialCount - appearanceAssetCount);

                using (var tx = new Transaction(doc, $"[MCP] Purge unused DWG residue pass {pass}"))
                {
                    tx.Start();
                    var deleted = SafeDelete(doc, purgeIds, null);
                    if (deleted.Count == 0)
                    {
                        tx.RollBack();
                        report.Notes.Add($"Purge pass {pass} returned no deleted ids.");
                        break;
                    }

                    if (!budget.TryAccept(deleted.Count))
                    {
                        tx.RollBack();
                        report.DeleteLimitReached = true;
                        report.PurgePasses.Add(new
                        {
                            pass,
                            mode = unusedPurgeMode,
                            availableDeleteCount = availableCount,
                            requestedDeleteCount = purgeIds.Count,
                            requestedMaterialCount = materialCount,
                            requestedAppearanceAssetCount = appearanceAssetCount,
                            requestedOtherCount = otherCount,
                            deletedCountIfAllowed = deleted.Count,
                            rolledBack = true,
                            reason = "delete-limit-reached",
                            maxDeleteElementCount = budget.Max
                        });
                        break;
                    }

                    tx.Commit();
                    report.PurgePassCount++;
                    report.RequestedUnusedMaterialCount += materialCount;
                    report.RequestedUnusedAppearanceAssetCount += appearanceAssetCount;
                    report.RequestedUnusedOtherCount += otherCount;
                    report.DeletedUnusedElementCount += deleted.Count;
                    report.PurgePasses.Add(new
                    {
                        pass,
                        mode = unusedPurgeMode,
                        availableDeleteCount = availableCount,
                        requestedDeleteCount = purgeIds.Count,
                        requestedMaterialCount = materialCount,
                        requestedAppearanceAssetCount = appearanceAssetCount,
                        requestedOtherCount = otherCount,
                        deletedCount = deleted.Count
                    });
                }
            }
        }

        private static string NormalizeUnusedPurgeMode(string? requestedMode, bool purgeAllUnused)
        {
            if (purgeAllUnused) return "all";

            var mode = (requestedMode ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(mode)) return "materials";

            switch (mode)
            {
                case "none":
                case "off":
                case "false":
                case "0":
                case "skip":
                    return "none";
                case "material":
                case "materials":
                case "dwg-materials":
                    return "materials";
                case "materials-and-assets":
                case "materials-assets":
                case "legacy":
                case "legacy-materials-and-uncategorized":
                    return "materials-and-assets";
                case "all":
                case "all-unused":
                case "purge-all-unused":
                    return "all";
                default:
                    return "materials";
            }
        }

        private static ISet<ElementId>? BuildUnusedCategoryFilter(string unusedPurgeMode)
        {
            switch (NormalizeUnusedPurgeMode(unusedPurgeMode, false))
            {
                case "none":
                    return null;
                case "all":
                    return new HashSet<ElementId>();
                case "materials-and-assets":
                    return new HashSet<ElementId>
                    {
                        ElementIdCompat.From(BuiltInCategory.OST_Materials),
                        ElementId.InvalidElementId
                    };
                default:
                    return new HashSet<ElementId>
                    {
                        ElementIdCompat.From(BuiltInCategory.OST_Materials)
                    };
            }
        }

        private static HashSet<int> ReadCandidateRootIds(string candidateListPath, DwgResidueReport report)
        {
            var result = new HashSet<int>();
            try
            {
                if (string.IsNullOrWhiteSpace(candidateListPath))
                    return result;

                if (!File.Exists(candidateListPath))
                {
                    report.Notes.Add($"Candidate list file was not found: {candidateListPath}");
                    return result;
                }

                var lines = File.ReadAllLines(candidateListPath, Encoding.UTF8);
                if (lines.Length == 0)
                    return result;

                var header = SplitCsvLine(lines[0]);
                int kindIndex = FindCsvColumn(header, "resourceKind", "kind");
                int idIndex = FindCsvColumn(header, "elementId", "rootCategoryId", "categoryId", "id");
                int actionIndex = FindCsvColumn(header, "defaultAction", "action");
                if (idIndex < 0)
                {
                    report.Notes.Add($"Candidate list does not contain elementId/rootCategoryId column: {candidateListPath}");
                    return result;
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;

                    var cols = SplitCsvLine(lines[i]);
                    var kind = GetCsvValue(cols, kindIndex).Trim();
                    if (!string.IsNullOrWhiteSpace(kind)
                        && !string.Equals(kind, "rootCategory", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(kind, "dwgRoot", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(kind, "importRoot", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var action = GetCsvValue(cols, actionIndex).Trim();
                    if (IsSkipAction(action))
                        continue;

                    if (int.TryParse(GetCsvValue(cols, idIndex).Trim(), out var id) && id > 0)
                        result.Add(id);
                }
            }
            catch (Exception ex)
            {
                report.Notes.Add($"Candidate list read failed: {ex.Message}");
            }

            return result;
        }

        private static void ExportCandidateRootList(Document doc, IList<ImportedCadRootSnapshot> rootSnapshots, DwgResidueReport report)
        {
            try
            {
                var path = ResolveCandidateListExportPath(doc, report.CandidateListDir, report.CandidateListPath);
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);

                using (var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                {
                    writer.WriteLine("resourceKind,elementId,name,fullPath,descendantCategoryCount,liveImportIdCount,liveImportIds,defaultAction,note");
                    foreach (var root in rootSnapshots)
                    {
                        var action = root.LiveImportIds.Count > 0 ? "skip" : "delete";
                        var note = root.LiveImportIds.Count > 0
                            ? "live-import-instance-present"
                            : string.Empty;
                        writer.WriteLine(string.Join(",",
                            CsvEscape("rootCategory"),
                            CsvEscape(root.IntId.ToString()),
                            CsvEscape(root.Name),
                            CsvEscape(root.FullPath),
                            CsvEscape(root.DescendantCategoryCount.ToString()),
                            CsvEscape(root.LiveImportIds.Count.ToString()),
                            CsvEscape(string.Join(";", root.LiveImportIds.Select(x => x.IntValue()))),
                            CsvEscape(action),
                            CsvEscape(note)));
                    }
                }

                report.CandidateListPath = path;
                report.Notes.Add($"Candidate list exported: {path}");
            }
            catch (Exception ex)
            {
                report.Notes.Add($"Candidate list export failed: {ex.Message}");
            }
        }

        private static string ResolveCandidateListExportPath(Document doc, string requestedDirectory, string legacyRequestedPath)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"dwg_residue_candidates_{stamp}.csv";

            if (!string.IsNullOrWhiteSpace(requestedDirectory))
            {
                var folder = requestedDirectory.Trim();
                if (string.Equals(Path.GetExtension(folder), ".csv", StringComparison.OrdinalIgnoreCase))
                    folder = Path.GetDirectoryName(folder) ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                    return Path.Combine(folder, fileName);
                }
            }

            // Backward compatibility for older runners that passed an output file path
            // through candidateListPath while exportCandidateList=true.
            if (!string.IsNullOrWhiteSpace(legacyRequestedPath)
                && string.Equals(Path.GetExtension(legacyRequestedPath), ".csv", StringComparison.OrdinalIgnoreCase)
                && !File.Exists(legacyRequestedPath))
            {
                return legacyRequestedPath;
            }

            string docKey = "unknown";
            try { docKey = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out _) ?? "unknown"; }
            catch { }

            var defaultFolder = Paths.ResolveManagedProjectFolder(doc?.Title, docKey)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Revit_MCP", "Projects", "Project_unknown");
            Directory.CreateDirectory(defaultFolder);
            return Path.Combine(defaultFolder, fileName);
        }

        private static bool IsSkipAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            switch (action.Trim().ToLowerInvariant())
            {
                case "skip":
                case "keep":
                case "no":
                case "false":
                case "0":
                case "除外":
                case "保持":
                case "いいえ":
                    return true;
                default:
                    return false;
            }
        }

        private static int FindCsvColumn(IList<string> header, params string[] names)
        {
            for (int i = 0; i < header.Count; i++)
            {
                var col = (header[i] ?? string.Empty).Trim();
                foreach (var name in names)
                {
                    if (string.Equals(col, name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            return -1;
        }

        private static string GetCsvValue(IList<string> cols, int index)
        {
            if (index < 0 || index >= cols.Count) return string.Empty;
            return cols[index] ?? string.Empty;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < (line?.Length ?? 0); i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(ch);
            }

            result.Add(sb.ToString());
            return result;
        }

        private static string CsvEscape(string? value)
        {
            var s = value ?? string.Empty;
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static int CountOfType<T>(Document doc, IList<ElementId> ids) where T : Element
        {
            int count = 0;
            foreach (var id in ids)
            {
                if (!IsValidId(id)) continue;
                Element? e = null;
                try { e = doc.GetElement(id); } catch { }
                if (e is T) count++;
            }
            return count;
        }

        private static List<ElementId> SafeDelete(Document doc, IList<ElementId> ids, List<object>? errors)
        {
            var result = new List<ElementId>();
            var distinctIds = NormalizeIds(ids);
            if (distinctIds.Count == 0) return result;

            const int chunkSize = 500;
            for (int i = 0; i < distinctIds.Count; i += chunkSize)
            {
                var chunk = distinctIds.Skip(i).Take(chunkSize).ToList();
                try
                {
                    var deleted = doc.Delete(chunk);
                    result.AddRange(NormalizeIds(deleted));
                }
                catch (Exception chunkEx)
                {
                    foreach (var id in chunk)
                    {
                        try
                        {
                            var deleted = doc.Delete(id);
                            result.AddRange(NormalizeIds(deleted));
                        }
                        catch (Exception ex)
                        {
                            errors?.Add(new
                            {
                                elementId = id.IntValue(),
                                error = ex.Message,
                                chunkError = chunkEx.Message
                            });
                        }
                    }
                }
            }

            return NormalizeIds(result);
        }

        private static List<ElementId> NormalizeIds(IEnumerable<ElementId>? ids)
        {
            var result = new List<ElementId>();
            var seen = new HashSet<int>();
            if (ids == null) return result;

            foreach (var id in ids)
            {
                if (!IsValidId(id)) continue;
                int raw = id.IntValue();
                if (seen.Add(raw))
                    result.Add(id);
            }

            return result;
        }

        private static bool IsValidId(ElementId? id)
        {
            return id != null
                && id != ElementId.InvalidElementId
                && id.IntValue() != 0;
        }

        private static bool IsLiveImportInstanceId(Document doc, ElementId id)
        {
            try { return doc.GetElement(id) is ImportInstance; }
            catch { return false; }
        }

        private static List<Category> ApplyRootFilters(List<Category> roots, JObject p)
        {
            var requestedRootIds = (p["rootCategoryIds"] as JArray)?.Values<int>().ToHashSet() ?? new HashSet<int>();
            var requestedRootNames = new HashSet<string>(
                (p["rootNames"] as JArray)?.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)) ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            string nameContains = p.Value<string>("nameContains") ?? string.Empty;

            return roots
                .Where(x => requestedRootIds.Count == 0 || requestedRootIds.Contains(x.Id.IntValue()))
                .Where(x => requestedRootNames.Count == 0 || requestedRootNames.Contains(x.Name ?? string.Empty))
                .Where(x => string.IsNullOrWhiteSpace(nameContains)
                    || (x.Name ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(x => x.Id.IntValue())
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ImportedCadRootSnapshot> SnapshotRoots(
            IList<Category> roots,
            Dictionary<int, List<ElementId>> liveImportMap)
        {
            var snapshots = new List<ImportedCadRootSnapshot>();
            var seen = new HashSet<int>();

            foreach (var root in roots)
            {
                if (root == null) continue;

                ElementId id;
                try { id = root.Id; }
                catch { continue; }

                if (!IsValidId(id)) continue;

                var intId = id.IntValue();
                if (!seen.Add(intId)) continue;

                var liveIds = liveImportMap.TryGetValue(intId, out var ids)
                    ? NormalizeIds(ids)
                    : new List<ElementId>();

                snapshots.Add(new ImportedCadRootSnapshot
                {
                    Id = id,
                    IntId = intId,
                    Name = SafeCategoryName(root),
                    FullPath = SafeBuildCategoryPath(root),
                    DescendantCategoryCount = SafeDescendantCategoryCount(root),
                    LiveImportIds = liveIds
                });
            }

            return snapshots;
        }

        private static List<Category> GetCandidateImportRoots(Document doc)
        {
            var map = new Dictionary<int, Category>();

            var importRoot = GetImportStylesRoot(doc);
            if (importRoot != null)
            {
                try
                {
                    foreach (Category sub in importRoot.SubCategories)
                    {
                        if (sub == null) continue;
                        if (!IsValidId(sub.Id)) continue;
                        map[sub.Id.IntValue()] = sub;
                    }
                }
                catch { }
            }

            try
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat == null) continue;
                    if (cat.Parent != null) continue;
                    if (cat.CategoryType != CategoryType.Model) continue;
                    if (!IsValidId(cat.Id)) continue;
                    if (!LooksLikeImportedFileCategory(cat.Name)) continue;
                    map[cat.Id.IntValue()] = cat;
                }
            }
            catch { }

            return map.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Category? GetImportStylesRoot(Document doc)
        {
            try { return Category.GetCategory(doc, BuiltInCategory.OST_ImportObjectStyles); }
            catch { return null; }
        }

        private static bool LooksLikeImportedFileCategory(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            var lowered = name.Trim().ToLowerInvariant();
            string[] exts = { ".dwg", ".dxf", ".dgn", ".sat", ".skp", ".3dm" };
            foreach (var ext in exts)
            {
                if (lowered.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
                if (lowered.Contains(ext + " ")) return true;
                if (lowered.Contains(ext + " (")) return true;
            }

            return false;
        }

        private static Dictionary<int, List<ElementId>> CollectLiveImportInstancesByRoot(Document doc, IList<Category> candidateRoots)
        {
            var candidateIds = new HashSet<int>(candidateRoots.Select(x => x.Id.IntValue()));
            var map = new Dictionary<int, List<ElementId>>();

            var imports = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();

            foreach (var inst in imports)
            {
                var topRoot = FindTopCandidateRoot(inst.Category, candidateIds);
                if (topRoot == null) continue;

                int rootId = topRoot.Id.IntValue();
                if (!map.TryGetValue(rootId, out var ids))
                {
                    ids = new List<ElementId>();
                    map[rootId] = ids;
                }

                ids.Add(inst.Id);
                var typeId = inst.GetTypeId();
                if (IsValidId(typeId))
                    ids.Add(typeId);
            }

            foreach (var key in map.Keys.ToList())
                map[key] = NormalizeIds(map[key]);

            return map;
        }

        private static Category? FindTopCandidateRoot(Category? category, HashSet<int> candidateRootIds)
        {
            var current = category;
            while (current != null)
            {
                if (candidateRootIds.Contains(current.Id.IntValue()))
                    return current;
                current = current.Parent;
            }
            return null;
        }

        private static List<Category> EnumerateSubtree(Category root)
        {
            var list = new List<Category>();
            var seen = new HashSet<int>();
            void Walk(Category? c)
            {
                if (c == null || !IsValidId(c.Id)) return;
                if (!seen.Add(c.Id.IntValue())) return;
                list.Add(c);
                try
                {
                    foreach (Category sub in c.SubCategories)
                        Walk(sub);
                }
                catch { }
            }
            Walk(root);
            return list;
        }

        private static string BuildCategoryPath(Category? category)
        {
            if (category == null) return string.Empty;
            var parts = new List<string>();
            var current = category;
            var guard = 0;
            while (current != null && guard++ < 50)
            {
                parts.Add(current.Name ?? string.Empty);
                current = current.Parent;
            }
            parts.Reverse();
            return string.Join(" > ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string SafeCategoryName(Category? category)
        {
            if (category == null) return string.Empty;
            try { return category.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeBuildCategoryPath(Category? category)
        {
            try { return BuildCategoryPath(category); }
            catch { return SafeCategoryName(category); }
        }

        private static int SafeDescendantCategoryCount(Category? category)
        {
            if (category == null) return 0;
            try { return EnumerateSubtree(category).Count; }
            catch { return 0; }
        }
    }

    [RpcCommand(
        "purge_dwg_residue",
        Aliases = new[] { "purge_unused_imported_object_styles_complete", "purge_deleted_dwg_residue", "purge_imported_object_styles_and_unused" },
        Category = "Links",
        Tags = new[] { "cad", "dwg", "import", "object-styles", "materials", "appearance-assets", "purge" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Summary = "Delete DWG/import residue category roots, then purge newly-unused materials and appearance assets.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"purge_dwg_residue\", \"params\":{ \"dryRun\": true, \"purgeAllUnused\": false, \"maxPurgePasses\": 20 } }"
    )]
    public sealed class PurgeDwgResidueRpcCommand : IRevitCommandHandler
    {
        public string CommandName => "purge_dwg_residue|purge_unused_imported_object_styles_complete|purge_deleted_dwg_residue|purge_imported_object_styles_and_unused";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var p = cmd.Params as JObject ?? new JObject();
            var report = DwgResidueCleanupHelper.Run(doc, p);
            return new JObject
            {
                ["ok"] = report.Ok,
                ["code"] = report.Code,
                ["msg"] = report.Msg,
                ["data"] = JObject.FromObject(report)
            };
        }
    }
}
