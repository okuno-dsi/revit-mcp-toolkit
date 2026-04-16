#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.RevitUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitMCPAddin.Commands.LinkOps
{
    internal static class ImportedObjectStylesHelper
    {
        internal sealed class RootScan
        {
            public int RootCategoryId { get; set; }
            public string RootName { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public string RootKind { get; set; } = string.Empty;
            public int DescendantCategoryCount { get; set; }
            public int GraphicsStyleCount { get; set; }
            public List<int> GraphicsStyleIds { get; set; } = new List<int>();
            public List<int> LiveImportInstanceIds { get; set; } = new List<int>();
            public bool LiveImportPresent { get; set; }
            public bool SafeToDelete { get; set; }
            public List<string> BlockedReasons { get; set; } = new List<string>();
            public string DeleteTargetKind { get; set; } = string.Empty;
            public List<string> DeleteFallbackReasons { get; set; } = new List<string>();
            public int WouldDeleteCount { get; set; }
            public int WouldDeleteStyleCount { get; set; }
            public int WouldDeleteDwgPayloadCount { get; set; }
            public int WouldDeleteOtherCount { get; set; }
            public List<object> WouldDeleteDwgPayloads { get; set; } = new List<object>();
            public List<object> WouldDeleteOthers { get; set; } = new List<object>();
            public List<object> Descendants { get; set; } = new List<object>();
        }

        internal sealed class ScanReport
        {
            public bool Ok { get; set; } = true;
            public string Code { get; set; } = "OK";
            public string Msg { get; set; } = "OK";
            public int RevitMajor { get; set; }
            public string SuggestedEngine { get; set; } = "custom";
            public bool NativeCommandLikelyAvailable { get; set; }
            public int? ImportStylesRootCategoryId { get; set; }
            public int TopLevelImportedCategoryCount { get; set; }
            public int TopLevelImportedFileCategoryCount { get; set; }
            public int CombinedCandidateRootCount { get; set; }
            public int LiveImportInstanceCount { get; set; }
            public int LiveRootCount { get; set; }
            public int GhostCandidateCount { get; set; }
            public int SafeGhostCandidateCount { get; set; }
            public int BlockedGhostCandidateCount { get; set; }
            public List<object> LiveRoots { get; set; } = new List<object>();
            public List<RootScan> Candidates { get; set; } = new List<RootScan>();
        }

        internal sealed class DeleteExecution
        {
            public bool Ok { get; set; }
            public string Code { get; set; } = "OK";
            public string Msg { get; set; } = "OK";
            public string EngineRequested { get; set; } = "custom";
            public string EngineUsed { get; set; } = "custom";
            public bool DryRun { get; set; }
            public int RevitMajor { get; set; }
            public bool PostedNativeCommand { get; set; }
            public string NativeMechanism { get; set; } = string.Empty;
            public ScanReport? Analysis { get; set; }
            public int SelectedRootCount { get; set; }
            public int DeletedRootCount { get; set; }
            public int DeletedElementCount { get; set; }
            public int FailedCount { get; set; }
            public List<object> DeletedRoots { get; set; } = new List<object>();
            public List<object> Failed { get; set; } = new List<object>();
            public List<object> Skipped { get; set; } = new List<object>();
        }

        private sealed class TrialDeleteResult
        {
            public bool CanDelete { get; set; }
            public List<string> BlockedReasons { get; set; } = new List<string>();
            public string DeleteTargetKind { get; set; } = string.Empty;
            public List<string> DeleteFallbackReasons { get; set; } = new List<string>();
            public int WouldDeleteCount { get; set; }
            public int WouldDeleteStyleCount { get; set; }
            public int WouldDeleteDwgPayloadCount { get; set; }
            public int WouldDeleteOtherCount { get; set; }
            public List<object> WouldDeleteDwgPayloads { get; set; } = new List<object>();
            public List<object> WouldDeleteOthers { get; set; } = new List<object>();
        }

        private sealed class DeleteAttemptResult
        {
            public ICollection<ElementId> DeletedIds { get; set; } = new List<ElementId>();
            public string DeleteTargetKind { get; set; } = string.Empty;
            public List<string> FallbackReasons { get; set; } = new List<string>();
        }

        private sealed class DwgMaterialSource
        {
            public string SourceKind { get; set; } = string.Empty;
            public int RootCategoryId { get; set; }
            public string RootName { get; set; } = string.Empty;
            public int CategoryId { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public string CategoryPath { get; set; } = string.Empty;
        }

        private sealed class DwgMaterialAccumulator
        {
            public int MaterialId { get; set; }
            public string UniqueId { get; set; } = string.Empty;
            public string MaterialName { get; set; } = string.Empty;
            public string MaterialClass { get; set; } = string.Empty;
            public int AppearanceAssetId { get; set; }
            public string AppearanceAssetName { get; set; } = string.Empty;
            public bool NameMatchesRenderingRgbPattern { get; set; }
            public List<DwgMaterialSource> Sources { get; set; } = new List<DwgMaterialSource>();
        }

        internal static int GetRevitMajor(UIApplication uiapp)
        {
            try
            {
                var raw = uiapp?.Application?.VersionNumber ?? string.Empty;
                if (int.TryParse(raw, out var major)) return major;
            }
            catch { }
            return 0;
        }

        internal static bool TryPostNativePurge(UIApplication uiapp, out string mechanism, out string detail)
        {
            mechanism = string.Empty;
            detail = string.Empty;

            int major = GetRevitMajor(uiapp);
            if (major < 2025)
            {
                detail = "Native purge command is not expected before Revit 2025.";
                return false;
            }

            if (UiCommandHelpers.TryPostByNames(
                uiapp,
                "PurgeUnusedImportedObjectStyles",
                "PurgeUnusedImportObjectStyles",
                "PurgeImportedObjectStylesUnused"))
            {
                mechanism = "postable-command";
                return true;
            }

            if (UiCommandHelpers.TryPostByIds(
                uiapp,
                "ID_PURGE_UNUSED_IMPORTED_OBJECT_STYLES",
                "ID_PURGE_UNUSED_IMPORTED_OBJECT_STYLE",
                "ID_PURGE_UNUSED_IMPORT_OBJECT_STYLES",
                "ID_PURGE_IMPORTED_OBJECT_STYLES_UNUSED"))
            {
                mechanism = "command-id";
                return true;
            }

            detail = "No known native command id matched in this Revit build.";
            return false;
        }

        internal static object ListDwgRelatedMaterials(Document doc, UIApplication uiapp, JObject p)
        {
            if (doc == null)
            {
                return new
                {
                    ok = false,
                    code = "NO_ACTIVE_DOCUMENT",
                    msg = "No active document.",
                    materials = Array.Empty<object>()
                };
            }

            bool includeCategoryMaterials = p.Value<bool?>("includeCategoryMaterials") ?? true;
            bool includeRenderingNamePattern = p.Value<bool?>("includeRenderingNamePattern") ?? true;
            string nameContains = p.Value<string>("nameContains") ?? string.Empty;

            var importRoot = GetImportStylesRoot(doc);
            var styleRoots = includeCategoryMaterials && importRoot != null
                ? EnumerateDirectSubCategories(importRoot)
                : new List<Category>();
            var importedFileRoots = includeCategoryMaterials
                ? EnumerateTopLevelImportedFileCategories(doc)
                : new List<Category>();
            var allRoots = styleRoots
                .Concat(importedFileRoots)
                .Where(x => x != null)
                .GroupBy(x => x.Id.IntValue())
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var map = new Dictionary<int, DwgMaterialAccumulator>();

            foreach (var root in allRoots)
            {
                var subtree = EnumerateSubtree(root);
                foreach (var category in subtree)
                {
                    var material = TryGetCategoryMaterial(category);
                    if (material == null) continue;

                    AddMaterialCandidate(
                        doc,
                        map,
                        material,
                        new DwgMaterialSource
                        {
                            SourceKind = "dwg-category-material",
                            RootCategoryId = root.Id.IntValue(),
                            RootName = root.Name ?? string.Empty,
                            CategoryId = category.Id.IntValue(),
                            CategoryName = category.Name ?? string.Empty,
                            CategoryPath = BuildCategoryPath(category, importRoot)
                        });
                }
            }

            if (includeRenderingNamePattern)
            {
                var materials = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .ToList();

                foreach (var material in materials)
                {
                    var materialName = material.Name ?? string.Empty;
                    if (!LooksLikeRenderingRgbMaterialName(materialName)) continue;

                    AddMaterialCandidate(
                        doc,
                        map,
                        material,
                        new DwgMaterialSource
                        {
                            SourceKind = "rendering-rgb-name-pattern",
                            RootCategoryId = 0,
                            RootName = string.Empty,
                            CategoryId = 0,
                            CategoryName = string.Empty,
                            CategoryPath = string.Empty
                        });
                }
            }

            var rows = map.Values
                .Where(x => string.IsNullOrWhiteSpace(nameContains)
                    || (x.MaterialName ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(x => x.MaterialName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.MaterialId)
                .Select(x => new
                {
                    materialId = x.MaterialId,
                    uniqueId = x.UniqueId,
                    materialName = x.MaterialName,
                    materialClass = x.MaterialClass,
                    appearanceAssetId = x.AppearanceAssetId,
                    appearanceAssetName = x.AppearanceAssetName,
                    nameMatchesRenderingRgbPattern = x.NameMatchesRenderingRgbPattern,
                    sourceCount = x.Sources.Count,
                    sourceKinds = x.Sources.Select(s => s.SourceKind).Distinct().OrderBy(s => s).ToList(),
                    sources = x.Sources
                        .OrderBy(s => s.RootName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(s => s.CategoryPath, StringComparer.OrdinalIgnoreCase)
                        .Cast<object>()
                        .ToList()
                })
                .ToList();

            return new
            {
                ok = true,
                code = "OK",
                msg = $"Found {rows.Count} DWG-related Material candidate(s).",
                revitMajor = GetRevitMajor(uiapp),
                includeCategoryMaterials,
                includeRenderingNamePattern,
                nameContains,
                importStylesRootCategoryId = importRoot?.Id.IntValue(),
                candidateRootCount = allRoots.Count,
                materialCount = rows.Count,
                materials = rows
            };
        }

        internal static ScanReport Analyze(Document doc, UIApplication uiapp, JObject p)
        {
            var report = new ScanReport
            {
                RevitMajor = GetRevitMajor(uiapp),
                SuggestedEngine = "custom",
                NativeCommandLikelyAvailable = GetRevitMajor(uiapp) >= 2025
            };

            if (doc == null)
            {
                report.Ok = false;
                report.Code = "NO_ACTIVE_DOCUMENT";
                report.Msg = "No active document.";
                return report;
            }

            var importRoot = GetImportStylesRoot(doc);
            report.ImportStylesRootCategoryId = importRoot?.Id.IntValue();

            int detailLimit = Math.Max(0, p.Value<int?>("detailLimit") ?? 200);
            var requestedRootIds = (p["rootCategoryIds"] as JArray)?.Values<int>().ToHashSet() ?? new HashSet<int>();
            var requestedRootNames = new HashSet<string>(
                (p["rootNames"] as JArray)?.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)) ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var styleRoots = importRoot != null
                ? EnumerateDirectSubCategories(importRoot)
                : new List<Category>();
            var importedFileRoots = EnumerateTopLevelImportedFileCategories(doc);
            var allRoots = styleRoots
                .Concat(importedFileRoots)
                .Where(x => x != null)
                .GroupBy(x => x.Id.IntValue())
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (allRoots.Count == 0)
            {
                report.Code = "NO_IMPORT_STYLES_ROOT";
                report.Msg = "Imported object style categories were not found in this document.";
                return report;
            }

            report.TopLevelImportedCategoryCount = styleRoots.Count;
            report.TopLevelImportedFileCategoryCount = importedFileRoots.Count;
            report.CombinedCandidateRootCount = allRoots.Count;

            var rootsById = allRoots.ToDictionary(x => x.Id.IntValue(), x => x);
            var liveImportByRoot = CollectLiveImportInstancesByCandidateRoot(doc, rootsById.Keys.ToHashSet());
            report.LiveImportInstanceCount = liveImportByRoot.Values.Sum(v => v.Count);
            report.LiveRootCount = liveImportByRoot.Count;

            foreach (var kv in liveImportByRoot.OrderBy(k => k.Key))
            {
                rootsById.TryGetValue(kv.Key, out var rootCat);
                report.LiveRoots.Add(new
                {
                    rootCategoryId = kv.Key,
                    rootName = rootCat?.Name ?? string.Empty,
                    rootKind = DetermineRootKind(rootCat, importRoot),
                    liveImportInstanceCount = kv.Value.Count,
                    liveImportInstanceIds = kv.Value
                });
            }

            foreach (var root in allRoots)
            {
                if (root == null) continue;

                int rootId = root.Id.IntValue();
                if (requestedRootIds.Count > 0 && !requestedRootIds.Contains(rootId)) continue;
                if (requestedRootNames.Count > 0 && !requestedRootNames.Contains(root.Name ?? string.Empty)) continue;

                var subtree = EnumerateSubtree(root);
                var styleIds = CollectGraphicsStyleIds(subtree);
                var liveImportIds = liveImportByRoot.TryGetValue(rootId, out var ids) ? ids : new List<int>();

                var scan = new RootScan
                {
                    RootCategoryId = rootId,
                    RootName = root.Name ?? string.Empty,
                    FullPath = BuildCategoryPath(root, importRoot),
                    RootKind = DetermineRootKind(root, importRoot),
                    DescendantCategoryCount = subtree.Count,
                    GraphicsStyleCount = styleIds.Count,
                    GraphicsStyleIds = styleIds.Select(x => x.IntValue()).ToList(),
                    LiveImportInstanceIds = new List<int>(liveImportIds),
                    LiveImportPresent = liveImportIds.Count > 0,
                    Descendants = subtree.Take(detailLimit).Select(c => new
                    {
                        categoryId = c.Id.IntValue(),
                        name = c.Name,
                        parentId = c.Parent != null ? (int?)c.Parent.Id.IntValue() : null,
                        graphicsStyleIds = CollectGraphicsStyleIds(new List<Category> { c }).Select(x => x.IntValue()).ToList()
                    }).Cast<object>().ToList()
                };

                if (scan.LiveImportPresent)
                {
                    scan.SafeToDelete = false;
                    scan.BlockedReasons.Add("live-import-instance-present");
                }
                else
                {
                    var trial = TrialDeleteCategoryOrStyles(doc, root, styleIds);
                    scan.SafeToDelete = trial.CanDelete;
                    scan.DeleteTargetKind = trial.DeleteTargetKind;
                    scan.DeleteFallbackReasons = trial.DeleteFallbackReasons;
                    scan.WouldDeleteCount = trial.WouldDeleteCount;
                    scan.WouldDeleteStyleCount = trial.WouldDeleteStyleCount;
                    scan.WouldDeleteDwgPayloadCount = trial.WouldDeleteDwgPayloadCount;
                    scan.WouldDeleteOtherCount = trial.WouldDeleteOtherCount;
                    scan.WouldDeleteDwgPayloads = trial.WouldDeleteDwgPayloads;
                    scan.WouldDeleteOthers = trial.WouldDeleteOthers;
                    foreach (var reason in trial.BlockedReasons)
                        scan.BlockedReasons.Add(reason);
                }

                report.Candidates.Add(scan);
            }

            report.GhostCandidateCount = report.Candidates.Count;
            report.SafeGhostCandidateCount = report.Candidates.Count(x => x.SafeToDelete);
            report.BlockedGhostCandidateCount = report.Candidates.Count(x => !x.SafeToDelete);
            report.Msg = report.SafeGhostCandidateCount > 0
                ? $"Found {report.SafeGhostCandidateCount} deletable unused imported object style root(s)."
                : "No deletable unused imported object style roots found.";

            return report;
        }

        internal static DeleteExecution Purge(Document doc, UIApplication uiapp, JObject p)
        {
            var engineRequested = ((p.Value<string>("engine") ?? "custom").Trim().ToLowerInvariant());
            if (string.IsNullOrWhiteSpace(engineRequested))
                engineRequested = "custom";
            bool dryRun = p.Value<bool?>("dryRun") ?? p.Value<bool?>("dry_run") ?? false;

            var exec = new DeleteExecution
            {
                Ok = true,
                Code = "OK",
                Msg = "OK",
                DryRun = dryRun,
                EngineRequested = engineRequested,
                EngineUsed = engineRequested,
                RevitMajor = GetRevitMajor(uiapp)
            };

            if (doc == null)
            {
                exec.Ok = false;
                exec.Code = "NO_ACTIVE_DOCUMENT";
                exec.Msg = "No active document.";
                return exec;
            }

            if (engineRequested == "native")
            {
                if (dryRun)
                {
                    exec.Code = "NATIVE_DRYRUN_UNSUPPORTED";
                    exec.Msg = "dryRun is not supported when engine=native. Use engine=custom for preview.";
                    exec.Ok = false;
                    return exec;
                }

                if (TryPostNativePurge(uiapp, out var mechanism, out var detail))
                {
                    exec.PostedNativeCommand = true;
                    exec.NativeMechanism = mechanism;
                    exec.Msg = "Posted native Revit command: Purge Unused Imported Object Styles.";
                    return exec;
                }

                exec.Ok = false;
                exec.Code = "NATIVE_COMMAND_NOT_AVAILABLE";
                exec.Msg = detail;
                return exec;
            }

            exec.EngineUsed = "custom";
            var analysis = Analyze(doc, uiapp, p);
            exec.Analysis = analysis;
            exec.SelectedRootCount = analysis.SafeGhostCandidateCount;

            if (!analysis.Ok)
            {
                exec.Ok = false;
                exec.Code = analysis.Code;
                exec.Msg = analysis.Msg;
                return exec;
            }

            if (dryRun)
            {
                exec.Msg = analysis.Msg;
                return exec;
            }

            var safeRoots = analysis.Candidates.Where(x => x.SafeToDelete).ToList();
            if (safeRoots.Count == 0)
            {
                exec.Msg = "No deletable unused imported object style roots found.";
                return exec;
            }

            foreach (var root in safeRoots)
            {
                try
                {
                    var ids = root.GraphicsStyleIds
                        .Where(x => x != 0)
                        .Distinct()
                        .Select(x => ElementIdCompat.From(x))
                        .ToList();

                    var rootCategory = ResolveCategory(doc, root.RootCategoryId, root.RootName);
                    if (rootCategory == null)
                    {
                        exec.Skipped.Add(new
                        {
                            rootCategoryId = root.RootCategoryId,
                            rootName = root.RootName,
                            reason = "category-not-found"
                        });
                        continue;
                    }

                    var deleted = DeleteCategoryOrStyles(doc, rootCategory, ids);
                    int deletedCount = deleted.DeletedIds?.Count ?? 0;

                    exec.DeletedRootCount++;
                    exec.DeletedElementCount += Math.Max(0, deletedCount);
                    exec.DeletedRoots.Add(new
                    {
                        rootCategoryId = root.RootCategoryId,
                        rootName = root.RootName,
                        deleteTargetKind = deleted.DeleteTargetKind,
                        deleteFallbackReasons = deleted.FallbackReasons,
                        deletedElementCount = deletedCount,
                        dwgPayloadCountAtAnalysis = root.WouldDeleteDwgPayloadCount,
                        graphicsStyleCountAtAnalysis = ids.Count
                    });
                }
                catch (Exception ex)
                {
                    exec.FailedCount++;
                    exec.Failed.Add(new
                    {
                        rootCategoryId = root.RootCategoryId,
                        rootName = root.RootName,
                        error = ex.Message
                    });
                }
            }

            if (exec.FailedCount > 0 && exec.DeletedRootCount == 0)
            {
                exec.Ok = false;
                exec.Code = "PURGE_FAILED";
                exec.Msg = $"Failed to purge imported object styles. Failed roots: {exec.FailedCount}.";
            }
            else if (exec.FailedCount > 0)
            {
                exec.Code = "PARTIAL";
                exec.Msg = $"Purged {exec.DeletedRootCount} imported object style root(s). Failed: {exec.FailedCount}.";
            }
            else
            {
                exec.Msg = $"Purged {exec.DeletedRootCount} unused imported object style root(s).";
            }

            return exec;
        }

        private static Category? GetImportStylesRoot(Document doc)
        {
            try { return Category.GetCategory(doc, BuiltInCategory.OST_ImportObjectStyles); }
            catch { return null; }
        }

        private static List<Category> EnumerateTopLevelImportedFileCategories(Document doc)
        {
            var list = new List<Category>();
            if (doc == null) return list;

            try
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat == null) continue;
                    if (cat.Parent != null) continue;
                    if (cat.CategoryType != CategoryType.Model) continue;
                    if (cat.Id.IntValue() <= 0) continue;
                    if (!LooksLikeImportedFileCategory(cat.Name)) continue;
                    list.Add(cat);
                }
            }
            catch { }

            return list
                .GroupBy(x => x.Id.IntValue())
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<Category> EnumerateDirectSubCategories(Category parent)
        {
            var list = new List<Category>();
            if (parent == null) return list;
            try
            {
                foreach (Category sub in parent.SubCategories)
                {
                    if (sub != null) list.Add(sub);
                }
            }
            catch { }
            return list
                .Where(x => x != null)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<Category> EnumerateSubtree(Category root)
        {
            var result = new List<Category>();
            var stack = new Stack<Category>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (cur == null) continue;
                result.Add(cur);

                try
                {
                    foreach (Category sub in cur.SubCategories)
                    {
                        if (sub != null) stack.Push(sub);
                    }
                }
                catch { }
            }

            return result
                .GroupBy(x => x.Id.IntValue())
                .Select(g => g.First())
                .OrderBy(x => BuildCategoryPath(x, null), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildCategoryPath(Category category, Category? stopBefore)
        {
            var parts = new List<string>();
            var cur = category;
            while (cur != null)
            {
                if (stopBefore != null && cur.Id.IntValue() == stopBefore.Id.IntValue())
                    break;
                parts.Add(cur.Name ?? string.Empty);
                cur = cur.Parent;
            }
            parts.Reverse();
            return string.Join(" > ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static string DetermineRootKind(Category? category, Category? importRoot)
        {
            if (category == null) return string.Empty;
            if (importRoot != null && category.Parent != null && category.Parent.Id.IntValue() == importRoot.Id.IntValue())
                return "import-object-style-root";
            if (LooksLikeImportedFileCategory(category.Name))
                return "imported-file-root";
            return "category-root";
        }

        private static bool LooksLikeImportedFileCategory(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            var lowered = trimmed.ToLowerInvariant();
            string[] exts = { ".dwg", ".dxf", ".dgn", ".sat", ".skp", ".3dm" };
            foreach (var ext in exts)
            {
                if (lowered.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (lowered.IndexOf(ext + " ", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (lowered.IndexOf(ext + " (", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static List<ElementId> CollectGraphicsStyleIds(IEnumerable<Category> categories)
        {
            var ids = new HashSet<int>();
            foreach (var cat in categories)
            {
                if (cat == null) continue;
                TryAddGraphicsStyleId(cat, GraphicsStyleType.Projection, ids);
                TryAddGraphicsStyleId(cat, GraphicsStyleType.Cut, ids);
            }

            return ids
                .Where(x => x != 0)
                .Select(x => ElementIdCompat.From(x))
                .ToList();
        }

        private static void TryAddGraphicsStyleId(Category cat, GraphicsStyleType gst, HashSet<int> ids)
        {
            try
            {
                var gs = cat.GetGraphicsStyle(gst);
                if (gs != null) ids.Add(gs.Id.IntValue());
            }
            catch { }
        }

        private static Material? TryGetCategoryMaterial(Category category)
        {
            if (category == null) return null;
            try { return category.Material; }
            catch { return null; }
        }

        private static void AddMaterialCandidate(
            Document doc,
            Dictionary<int, DwgMaterialAccumulator> map,
            Material material,
            DwgMaterialSource source)
        {
            if (material == null) return;
            int materialId = material.Id.IntValue();
            if (materialId <= 0) return;

            if (!map.TryGetValue(materialId, out var acc))
            {
                acc = new DwgMaterialAccumulator
                {
                    MaterialId = materialId,
                    UniqueId = material.UniqueId ?? string.Empty,
                    MaterialName = material.Name ?? string.Empty,
                    MaterialClass = material.MaterialClass ?? string.Empty,
                    AppearanceAssetId = material.AppearanceAssetId != null ? material.AppearanceAssetId.IntValue() : 0,
                    NameMatchesRenderingRgbPattern = LooksLikeRenderingRgbMaterialName(material.Name ?? string.Empty)
                };
                acc.AppearanceAssetName = SafeElementName(TryGetElement(doc, material.AppearanceAssetId));
                map[materialId] = acc;
            }

            bool exists = acc.Sources.Any(s =>
                string.Equals(s.SourceKind, source.SourceKind, StringComparison.OrdinalIgnoreCase)
                && s.RootCategoryId == source.RootCategoryId
                && s.CategoryId == source.CategoryId);
            if (!exists)
                acc.Sources.Add(source);
        }

        private static Element? TryGetElement(Document doc, ElementId id)
        {
            if (doc == null || id == null || id == ElementId.InvalidElementId) return null;
            try { return doc.GetElement(id); }
            catch { return null; }
        }

        private static bool LooksLikeRenderingRgbMaterialName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            return Regex.IsMatch(
                trimmed,
                @"^(レンダリング\s*マテリアル|Rendering\s+Material)\s+\d{1,3}[-,]\d{1,3}[-,]\d{1,3}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static Dictionary<int, List<int>> CollectLiveImportInstancesByCandidateRoot(Document doc, HashSet<int> candidateRootIds)
        {
            var map = new Dictionary<int, List<int>>();
            var imports = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();

            foreach (var inst in imports)
            {
                try
                {
                    var cat = inst.Category;
                    var top = GetTopImportedRoot(cat, candidateRootIds);
                    if (top == null) continue;
                    int key = top.Id.IntValue();
                    if (!map.TryGetValue(key, out var ids))
                    {
                        ids = new List<int>();
                        map[key] = ids;
                    }
                    ids.Add(inst.Id.IntValue());
                }
                catch { }
            }

            foreach (var kv in map)
                kv.Value.Sort();

            return map;
        }

        private static Category? GetTopImportedRoot(Category? category, HashSet<int> candidateRootIds)
        {
            var cur = category;
            while (cur != null)
            {
                if (candidateRootIds.Contains(cur.Id.IntValue()))
                    return cur;
                cur = cur.Parent;
            }
            return null;
        }

        private static Category? ResolveCategory(Document doc, int categoryId, string rootName)
        {
            try
            {
                var byId = Category.GetCategory(doc, ElementIdCompat.From(categoryId));
                if (byId != null) return byId;
            }
            catch { }

            try
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat == null) continue;
                    if (string.Equals(cat.Name, rootName, StringComparison.OrdinalIgnoreCase))
                        return cat;
                }
            }
            catch { }

            var importRoot = GetImportStylesRoot(doc);
            if (importRoot != null)
            {
                foreach (var sub in EnumerateDirectSubCategories(importRoot))
                {
                    if (string.Equals(sub.Name, rootName, StringComparison.OrdinalIgnoreCase))
                        return sub;
                }
            }

            return null;
        }

        private static TrialDeleteResult TrialDeleteCategoryOrStyles(Document doc, Category root, List<ElementId> styleIds)
        {
            var result = new TrialDeleteResult();
            if (doc == null)
            {
                result.BlockedReasons.Add("document-null");
                return result;
            }

            if (root == null)
            {
                result.BlockedReasons.Add("category-root-not-found");
                return result;
            }

            try
            {
                DeleteAttemptResult attempt = new DeleteAttemptResult();
                List<int> deletedRawIds = new List<int>();
                using (var tx = new Transaction(doc, "[MCP] Trial delete imported object style category"))
                {
                    bool started = false;
                    try
                    {
                        tx.Start();
                        started = true;
                        attempt = TryDeleteImportedCategoryTargets(doc, root, styleIds);
                        deletedRawIds = NormalizeRawIds(attempt.DeletedIds);
                    }
                    finally
                    {
                        if (started)
                        {
                            try { tx.RollBack(); }
                            catch { }
                        }
                    }
                }

                result.DeleteTargetKind = attempt.DeleteTargetKind;
                result.DeleteFallbackReasons = new List<string>(attempt.FallbackReasons);
                ClassifyDeletedIds(doc, deletedRawIds, result);

                if (result.WouldDeleteCount <= 0)
                {
                    result.BlockedReasons.Add("delete-returned-zero");
                    foreach (var reason in attempt.FallbackReasons)
                        result.BlockedReasons.Add(reason);
                    return result;
                }

                if (result.WouldDeleteOtherCount > 0)
                {
                    result.BlockedReasons.Add("trial-delete-would-remove-non-import-style-elements");
                    return result;
                }

                result.CanDelete = true;
                return result;
            }
            catch (Exception ex)
            {
                result.BlockedReasons.Add("trial-delete-failed: " + ex.Message);
                return result;
            }
        }

        private static DeleteAttemptResult DeleteCategoryOrStyles(Document doc, Category targetCategory, List<ElementId> styleIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (targetCategory == null) throw new ArgumentNullException(nameof(targetCategory));

            DeleteAttemptResult attempt = new DeleteAttemptResult();
            using (var tx = new Transaction(doc, "[MCP] Delete imported object style category"))
            {
                bool started = false;
                bool committed = false;
                try
                {
                    tx.Start();
                    started = true;
                    attempt = TryDeleteImportedCategoryTargets(doc, targetCategory, styleIds);
                    if (attempt.DeletedIds == null || attempt.DeletedIds.Count == 0)
                    {
                        string detail = attempt.FallbackReasons.Count > 0
                            ? " " + string.Join("; ", attempt.FallbackReasons)
                            : string.Empty;
                        throw new InvalidOperationException("Document.Delete returned zero." + detail);
                    }
                    tx.Commit();
                    committed = true;
                }
                finally
                {
                    if (started && !committed)
                    {
                        try { tx.RollBack(); }
                        catch { }
                    }
                }
            }

            return attempt;
        }

        private static DeleteAttemptResult TryDeleteImportedCategoryTargets(
            Document doc,
            Category targetCategory,
            List<ElementId> styleIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (targetCategory == null) throw new ArgumentNullException(nameof(targetCategory));

            var attempt = new DeleteAttemptResult();
            if (targetCategory.Id == null || targetCategory.Id.IntValue() <= 0)
            {
                attempt.FallbackReasons.Add($"category-id-not-user-deletable: {targetCategory.Id?.IntValue() ?? 0}");
            }
            else
            {
                try
                {
                    var deletedByCategory = doc.Delete(targetCategory.Id);
                    if (deletedByCategory != null && deletedByCategory.Count > 0)
                    {
                        attempt.DeletedIds = deletedByCategory;
                        attempt.DeleteTargetKind = "category-id";
                        return attempt;
                    }

                    attempt.FallbackReasons.Add("category-delete-returned-zero");
                }
                catch (Exception ex)
                {
                    attempt.FallbackReasons.Add("category-delete-failed: " + ex.Message);
                }
            }

            var styleDeleteIds = NormalizeElementIds(styleIds);
            if (styleDeleteIds.Count == 0)
            {
                attempt.DeleteTargetKind = "none";
                attempt.FallbackReasons.Add("no-graphics-style-delete-target-ids");
                attempt.DeletedIds = new List<ElementId>();
                return attempt;
            }

            try
            {
                var deletedByStyles = doc.Delete(styleDeleteIds);
                attempt.DeletedIds = deletedByStyles ?? new List<ElementId>();
                attempt.DeleteTargetKind = "graphics-style-ids";
                if (attempt.DeletedIds.Count == 0)
                    attempt.FallbackReasons.Add("graphics-style-delete-returned-zero");
                return attempt;
            }
            catch (Exception ex)
            {
                attempt.DeleteTargetKind = "graphics-style-ids";
                attempt.FallbackReasons.Add("graphics-style-delete-failed: " + ex.Message);
                throw new InvalidOperationException(string.Join("; ", attempt.FallbackReasons));
            }
        }

        private static List<ElementId> NormalizeElementIds(IEnumerable<ElementId>? ids)
        {
            var result = new List<ElementId>();
            var seen = new HashSet<int>();
            if (ids == null) return result;

            foreach (var id in ids)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                int raw = id.IntValue();
                if (raw == 0) continue;
                if (seen.Add(raw))
                    result.Add(id);
            }

            return result;
        }

        private static List<int> NormalizeRawIds(IEnumerable<ElementId>? ids)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            if (ids == null) return result;

            foreach (var id in ids)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                int raw = id.IntValue();
                if (raw == 0) continue;
                if (seen.Add(raw))
                    result.Add(raw);
            }

            return result;
        }

        private static void ClassifyDeletedIds(Document doc, IEnumerable<int> rawIds, TrialDeleteResult result)
        {
            var uniqueDeleted = (rawIds ?? Array.Empty<int>())
                .Where(x => x != 0)
                .Distinct()
                .ToList();

            result.WouldDeleteCount = uniqueDeleted.Count;

            foreach (var rawId in uniqueDeleted)
            {
                var id = ElementIdCompat.From(rawId);

                try
                {
                    var cat = Category.GetCategory(doc, id);
                    if (cat != null)
                        continue;
                }
                catch { }

                Element? elem = null;
                try { elem = doc.GetElement(id); }
                catch { }

                if (elem is GraphicsStyle)
                {
                    result.WouldDeleteStyleCount++;
                    continue;
                }

                if (IsAllowedCascadeElement(elem))
                {
                    result.WouldDeleteDwgPayloadCount++;
                    result.WouldDeleteDwgPayloads.Add(new
                    {
                        elementId = rawId,
                        className = elem?.GetType().Name ?? "Unknown",
                        categoryName = elem?.Category?.Name ?? string.Empty,
                        name = SafeElementName(elem)
                    });
                    continue;
                }

                result.WouldDeleteOtherCount++;
                result.WouldDeleteOthers.Add(new
                {
                    elementId = rawId,
                    className = elem?.GetType().Name ?? "Unknown",
                    categoryName = elem?.Category?.Name ?? string.Empty,
                    name = SafeElementName(elem)
                });
            }
        }

        private static bool IsAllowedCascadeElement(Element? elem)
        {
            if (elem == null) return false;
            var typeName = elem.GetType().Name;
            return string.Equals(typeName, "Element", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "ElementType", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "ImportSymbol", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "ImportInstance", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "CADLinkType", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "Material", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "AppearanceAssetElement", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "PropertySetElement", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "SiteLocation", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeElementName(Element? elem)
        {
            if (elem == null) return string.Empty;
            try { return elem.Name ?? string.Empty; }
            catch { return string.Empty; }
        }
    }

    [RpcCommand(
        "analyze_unused_imported_object_styles",
        Aliases = new[] { "find_ghost_import_categories", "analyze_ghost_import_categories" },
        Category = "Links",
        Tags = new[] { "cad", "dwg", "import", "object-styles", "purge" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Summary = "Analyze imported object style roots that appear unused after CAD imports/links were removed.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"analyze_unused_imported_object_styles\", \"params\":{ \"detailLimit\": 50 } }"
    )]
    public sealed class AnalyzeUnusedImportedObjectStylesCommand : IRevitCommandHandler
    {
        public string CommandName => "analyze_unused_imported_object_styles|find_ghost_import_categories|analyze_ghost_import_categories";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var p = cmd.Params as JObject ?? new JObject();
            var report = ImportedObjectStylesHelper.Analyze(doc, uiapp, p);
            return new JObject
            {
                ["ok"] = report.Ok,
                ["code"] = report.Code,
                ["msg"] = report.Msg,
                ["data"] = JObject.FromObject(report)
            };
        }
    }

    [RpcCommand(
        "list_dwg_related_materials",
        Aliases = new[] { "list_imported_object_style_materials", "list_ghost_import_materials" },
        Category = "Links",
        Tags = new[] { "cad", "dwg", "import", "object-styles", "material" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Summary = "List Material elements referenced by DWG/import object style categories and imported RGB rendering material name patterns.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"list_dwg_related_materials\", \"params\":{ \"includeRenderingNamePattern\": true } }"
    )]
    public sealed class ListDwgRelatedMaterialsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_dwg_related_materials|list_imported_object_style_materials|list_ghost_import_materials";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var p = cmd.Params as JObject ?? new JObject();
            var result = ImportedObjectStylesHelper.ListDwgRelatedMaterials(doc, uiapp, p);
            return new JObject
            {
                ["ok"] = JToken.FromObject(GetOkFlag(result)),
                ["data"] = JObject.FromObject(result)
            };
        }

        private static bool GetOkFlag(object result)
        {
            try
            {
                var prop = result.GetType().GetProperty("ok");
                if (prop != null && prop.GetValue(result) is bool b) return b;
            }
            catch { }
            return true;
        }
    }

    [RpcCommand(
        "purge_unused_imported_object_styles",
        Aliases = new[] { "purge_ghost_import_categories", "delete_ghost_import_categories" },
        Category = "Links",
        Tags = new[] { "cad", "dwg", "import", "object-styles", "purge" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Summary = "Purge unused imported object style roots. Uses safe custom analysis by default; native UI post is optional on Revit 2025+.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"purge_unused_imported_object_styles\", \"params\":{ \"dryRun\": true, \"engine\": \"custom\" } }"
    )]
    public sealed class PurgeUnusedImportedObjectStylesCommand : IRevitCommandHandler
    {
        public string CommandName => "purge_unused_imported_object_styles|purge_ghost_import_categories|delete_ghost_import_categories";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var p = cmd.Params as JObject ?? new JObject();
            var result = ImportedObjectStylesHelper.Purge(doc, uiapp, p);
            return new JObject
            {
                ["ok"] = result.Ok,
                ["code"] = result.Code,
                ["msg"] = result.Msg,
                ["data"] = JObject.FromObject(result)
            };
        }
    }
}
