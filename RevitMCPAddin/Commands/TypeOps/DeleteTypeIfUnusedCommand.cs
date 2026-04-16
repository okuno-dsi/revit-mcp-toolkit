// ================================================================
// File: Commands/TypeOps/DeleteTypeIfUnusedCommand.cs
// Purpose:
//   - Delete an ElementType safely.
//   - Default: delete only if the type is not used by any instances.
//   - Options:
//       * purgeAllUnusedInCategory: bulk-purge unused types in a category
//       * force + reassignTo*: reassign instances then delete type
//       * force + deleteInstances: delete instances then delete type (danger)
//   - Returns plain { ok, ... } (router wraps JSON-RPC).
// Notes:
//   - CRITICAL: Never access ElementType 'target' after doc.Delete(target.Id).
//     Cache all necessary fields before deletion.
// Target: .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.TypeOps
{
    public class DeleteTypeIfUnusedCommand : IRevitCommandHandler
    {
        // Accept 3 method names with one handler
        public string CommandName => "delete_type_if_unused|purge_unused_types|force_delete_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();

            // ---- Target spec (either of) ----
            int? typeIdOpt = p.Value<int?>("typeId");
            string? uniqueId = p.Value<string>("uniqueId");
            string? typeName = p.Value<string>("typeName");
            string? familyName = p.Value<string>("familyName");
            string? categoryName = p.Value<string>("category");

            // ---- Options ----
            bool dryRun = p.Value<bool?>("dryRun") ?? false;

            bool forceDelete = p.Value<bool?>("force") ?? false;
            bool deleteInstances = p.Value<bool?>("deleteInstances") ?? false; // valid only with force

            int? reassignToTypeId = p.Value<int?>("reassignToTypeId");
            string? reassignToUniqueId = p.Value<string>("reassignToUniqueId");
            string? reassignToTypeName = p.Value<string>("reassignToTypeName");
            string? reassignToFamilyName = p.Value<string>("reassignToFamilyName");

            bool purgeAllUnusedInCategory = p.Value<bool?>("purgeAllUnusedInCategory") ?? false;
            var bulkTypeIds = p["typeIds"] as JArray;

            var steps = new List<object>();

            try
            {
                if (bulkTypeIds != null && bulkTypeIds.Count > 0)
                    return ExecuteBulkTypeIds(doc, bulkTypeIds, dryRun);

                // ------------------------------------------------------------
                // Bulk purge (category)
                // ------------------------------------------------------------
                if (purgeAllUnusedInCategory)
                {
                    if (string.IsNullOrWhiteSpace(categoryName))
                        return new { ok = false, msg = "purgeAllUnusedInCategory requires 'category'." };

                    var cat = ResolveCategory(doc, categoryName);
                    if (cat == null)
                        return new { ok = false, msg = $"Category not found: '{categoryName}'", errorCode = "NOT_FOUND" };

                    var typeIds = new FilteredElementCollector(doc)
                        .OfCategoryId(cat.Id)
                        .WhereElementIsElementType()
                        .ToElementIds()
                        .ToList();

                    var candidates = new List<int>();
                    foreach (var tid in typeIds)
                    {
                        var et = doc.GetElement(tid) as ElementType;
                        if (et == null) continue;
                        if (!HasInstancesOfType(doc, et))
                            candidates.Add(et.Id.IntValue());
                    }

                    if (dryRun)
                    {
                        return new
                        {
                            ok = true,
                            purgedCount = candidates.Count,
                            deletedTypeIds = candidates,
                            dryRun = true,
                            msg = $"[DryRun] Purge {candidates.Count} unused types in category '{categoryName}'.",
                            steps
                        };
                    }

                    int deleted = 0;
                    var deletedList = new List<int>();
                    using (var tx = new Transaction(doc, "[MCP] Purge unused types in category"))
                    {
                        tx.Start();
                        foreach (int tid in candidates)
                        {
                            try
                            {
                                var deletedIds = doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(tid));
                                if (deletedIds != null && deletedIds.Count > 0)
                                {
                                    deleted++;
                                    deletedList.Add(tid);
                                    steps.Add(new { step = "delete", typeId = tid, ok = true, deletedCount = deletedIds.Count });
                                }
                                else
                                {
                                    steps.Add(new { step = "delete", typeId = tid, ok = true, deletedCount = 0 });
                                }
                            }
                            catch (Exception ex)
                            {
                                steps.Add(new { step = "delete", typeId = tid, ok = false, reason = ex.Message });
                            }
                        }
                        tx.Commit();
                    }

                    return new
                    {
                        ok = true,
                        purgedCount = deleted,
                        deletedTypeIds = deletedList,
                        msg = $"Purged {deleted} unused types in category '{categoryName}'.",
                        steps
                    };
                }

                // ------------------------------------------------------------
                // Single target
                // ------------------------------------------------------------
                ElementType? target = FindTypeByAny(doc, typeIdOpt, uniqueId, typeName, familyName, categoryName, steps);
                if (target == null)
                    return new { ok = false, msg = "Target type not found by given keys.", errorCode = "NOT_FOUND" };

                // --- Cache everything BEFORE any deletion. Never touch 'target' after Delete. ---
                int tId = target.Id.IntValue();
                string tCatName = target.Category?.Name ?? "?";
                string? tFamName = TryGetFamilyName(target);
                string tTypeName2 = target.Name;
                string tLabel = $"[{tCatName}] {tFamName ?? "(no family)"} : {tTypeName2} (Id:{tId})";

                // Guard: some special types
                if (target is ViewFamilyType)
                    return new { ok = false, msg = "Deleting ViewFamilyType is not supported.", errorCode = "unsupported-type" };

                bool used = HasInstancesOfType(doc, target);
                steps.Add(new { step = "check-used", typeId = tId, used });

                // ---------- DryRun for single ----------
                if (dryRun)
                {
                    if (!used)
                    {
                        return new
                        {
                            ok = true,
                            dryRun = true,
                            wouldDeleteTypeId = tId,
                            msg = $"[DryRun] Would delete unused type: {tLabel}",
                            steps
                        };
                    }
                    else
                    {
                        var hint = forceDelete
                            ? "Provide 'reassignTo*' or 'deleteInstances:true' with 'force:true'."
                            : "Enable 'force:true' with reassignment or deleting instances.";
                        return new
                        {
                            ok = false,
                            dryRun = true,
                            errorCode = "type-in-use",
                            msg = $"[DryRun] Type is in use and cannot be deleted without 'force'. {hint} Target: {tLabel}",
                            steps
                        };
                    }
                }

                // ---------- Not used → simple delete ----------
                if (!used)
                {
                    using (var tx = new Transaction(doc, "[MCP] Delete unused type"))
                    {
                        tx.Start();
                        var deletedIds = doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(tId));
                        tx.Commit();
                        steps.Add(new
                        {
                            step = "delete-type",
                            typeId = tId,
                            ok = true,
                            deletedCount = deletedIds?.Count ?? 0
                        });
                    }

                    return new
                    {
                        ok = true,
                        deletedTypeId = tId,
                        deletedInstanceCount = 0,
                        reassignedCount = 0,
                        msg = $"Deleted unused type: {tLabel}",
                        steps
                    };
                }

                // ---------- Used ----------
                if (!forceDelete)
                {
                    return new
                    {
                        ok = false,
                        msg = $"Type is in use and cannot be deleted without 'force'. {tLabel}",
                        errorCode = "type-in-use",
                        steps
                    };
                }

                // Resolve reassignment target if any
                ElementType? reassignTo = null;
                if (reassignToTypeId.HasValue || !string.IsNullOrWhiteSpace(reassignToUniqueId) ||
                    (!string.IsNullOrWhiteSpace(reassignToTypeName) && !string.IsNullOrWhiteSpace(reassignToFamilyName)))
                {
                    reassignTo = FindTypeByAny(doc, reassignToTypeId, reassignToUniqueId, reassignToTypeName, reassignToFamilyName, categoryName, steps);
                    if (reassignTo == null)
                        return new { ok = false, msg = "Reassign target type not found.", errorCode = "reassign-target-not-found", steps };

                    if (!CategoryCompatible(target, reassignTo))
                        return new { ok = false, msg = "Reassign target type is not in the same category.", errorCode = "reassign-category-mismatch", steps };
                }

                int reassigned = 0;
                int deletedInst = 0;

                using (var tx = new Transaction(doc, "[MCP] Force delete type"))
                {
                    tx.Start();

                    var (instances, _) = CollectInstancesOfType(doc, target);
                    if (instances.Count == 0)
                    {
                        // Race-safety: instances disappeared between checks; just delete type.
                        try
                        {
                            var del0 = doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(tId));
                            steps.Add(new { step = "delete-type", typeId = tId, ok = true, deletedCount = del0?.Count ?? 0 });
                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            steps.Add(new { step = "delete-type", typeId = tId, ok = false, reason = ex.Message });
                            tx.RollBack();
                            return new { ok = false, msg = "Failed to delete type.", errorCode = "delete-type-failed", steps };
                        }

                        return new
                        {
                            ok = true,
                            deletedTypeId = tId,
                            deletedInstanceCount = 0,
                            reassignedCount = 0,
                            msg = $"Type deleted (forced): {tLabel}",
                            steps
                        };
                    }

                    if (reassignTo != null)
                    {
                        foreach (var inst in instances)
                        {
                            try
                            {
                                if (inst is FamilyInstance fi)
                                {
                                    if (reassignTo is FamilySymbol sym && sym.Family?.FamilyCategoryId == fi.Symbol?.Family?.FamilyCategoryId)
                                    {
                                        fi.Symbol = sym; // FamilyInstance type change
                                        reassigned++;
                                        steps.Add(new { step = "reassign", elementId = fi.Id.IntValue(), ok = true, toTypeId = sym.Id.IntValue() });
                                    }
                                    else
                                    {
                                        inst.ChangeTypeId(reassignTo.Id);
                                        reassigned++;
                                        steps.Add(new { step = "reassign", elementId = inst.Id.IntValue(), ok = true, toTypeId = reassignTo.Id.IntValue() });
                                    }
                                }
                                else
                                {
                                    inst.ChangeTypeId(reassignTo.Id);
                                    reassigned++;
                                    steps.Add(new { step = "reassign", elementId = inst.Id.IntValue(), ok = true, toTypeId = reassignTo.Id.IntValue() });
                                }
                            }
                            catch (Exception ex)
                            {
                                steps.Add(new { step = "reassign", elementId = inst.Id.IntValue(), ok = false, reason = ex.Message });
                            }
                        }
                    }
                    else if (deleteInstances)
                    {
                        var ids = instances.Select(x => x.Id).ToList();
                        try
                        {
                            var deletedIds = doc.Delete(ids);
                            deletedInst = deletedIds?.Count ?? ids.Count;
                            steps.Add(new { step = "delete-instances", ok = true, count = deletedInst });
                        }
                        catch (Exception ex)
                        {
                            steps.Add(new { step = "delete-instances", ok = false, reason = ex.Message });
                            tx.RollBack();
                            return new { ok = false, msg = "Failed to delete instances for force deletion.", errorCode = "delete-instances-failed", steps };
                        }
                    }
                    else
                    {
                        tx.RollBack();
                        return new
                        {
                            ok = false,
                            msg = "Type is in use. Provide 'reassignTo*' or 'deleteInstances:true' with 'force:true'.",
                            errorCode = "force-options-required",
                            steps
                        };
                    }

                    // Delete the type itself (NEVER touch 'target' after this call)
                    try
                    {
                        var del = doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(tId));
                        steps.Add(new { step = "delete-type", typeId = tId, ok = true, deletedCount = del?.Count ?? 0 });
                    }
                    catch (Exception ex)
                    {
                        steps.Add(new { step = "delete-type", typeId = tId, ok = false, reason = ex.Message });
                        tx.RollBack();
                        return new { ok = false, msg = "Failed to delete type after operations.", errorCode = "delete-type-failed", steps };
                    }

                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    deletedTypeId = tId,
                    deletedInstanceCount = deleteInstances ? deletedInst : 0,
                    reassignedCount = (!deleteInstances) ? reassigned : 0,
                    msg = $"Type deleted (forced): {tLabel}",
                    steps
                };
            }
            catch (Exception ex)
            {
                RevitLogger.Error($"DeleteTypeIfUnused failed: {ex}");
                return new { ok = false, msg = ex.Message, errorCode = "exception" };
            }
        }

        // --------------------- helpers ---------------------

        private sealed class BulkTypeTarget
        {
            public int InputIndex { get; set; }
            public int TypeId { get; set; }
            public string CategoryName { get; set; } = "";
            public string FamilyName { get; set; } = "";
            public string TypeName { get; set; } = "";
        }

        private static object ExecuteBulkTypeIds(Document doc, JArray rawTypeIds, bool dryRun)
        {
            var requested = ParseBulkTypeIds(rawTypeIds);
            var skipped = new List<object>();
            var candidates = new List<BulkTypeTarget>();
            var seen = new HashSet<int>();

            if (requested.Count == 0)
                return new { ok = false, dryRun, msg = "typeIds is empty.", errorCode = "empty-typeIds" };

            var usedTypeIds = CollectUsedTypeIds(doc);

            foreach (var item in requested)
            {
                int inputIndex = item.inputIndex;
                int typeId = item.typeId;
                if (typeId <= 0)
                {
                    skipped.Add(new { inputIndex, typeId, reason = "invalid-type-id" });
                    continue;
                }

                if (!seen.Add(typeId))
                {
                    skipped.Add(new { inputIndex, typeId, reason = "duplicate-type-id" });
                    continue;
                }

                var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId));
                if (!(element is ElementType elementType))
                {
                    skipped.Add(new { inputIndex, typeId, reason = "not-element-type" });
                    continue;
                }

                if (elementType is ViewFamilyType)
                {
                    skipped.Add(new
                    {
                        inputIndex,
                        typeId,
                        reason = "unsupported-type",
                        className = elementType.GetType().Name,
                        categoryName = elementType.Category?.Name ?? "",
                        typeName = elementType.Name
                    });
                    continue;
                }

                if (usedTypeIds.Contains(typeId))
                {
                    skipped.Add(new
                    {
                        inputIndex,
                        typeId,
                        reason = "type-in-use",
                        className = elementType.GetType().Name,
                        categoryName = elementType.Category?.Name ?? "",
                        familyName = TryGetFamilyName(elementType) ?? "",
                        typeName = elementType.Name
                    });
                    continue;
                }

                candidates.Add(new BulkTypeTarget
                {
                    InputIndex = inputIndex,
                    TypeId = typeId,
                    CategoryName = elementType.Category?.Name ?? "",
                    FamilyName = TryGetFamilyName(elementType) ?? "",
                    TypeName = elementType.Name
                });
            }

            if (dryRun)
            {
                return new
                {
                    ok = true,
                    dryRun = true,
                    inputCount = rawTypeIds.Count,
                    uniqueTypeIdCount = seen.Count,
                    candidateCount = candidates.Count,
                    skippedCount = skipped.Count,
                    candidateTypeIds = candidates.Select(x => x.TypeId).ToList(),
                    candidates = candidates.Select(ToBulkTargetDto).ToList(),
                    skipped,
                    msg = $"[DryRun] Would delete {candidates.Count} unused ElementType(s)."
                };
            }

            var deleted = new List<object>();
            var deletedTypeIds = new List<int>();
            var failed = new List<object>();
            int deletedElementCount = 0;

            if (candidates.Count > 0)
            {
                using (var tx = new Transaction(doc, "[MCP] Delete unused types batch"))
                {
                    tx.Start();
                    foreach (var target in candidates)
                    {
                        try
                        {
                            if (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(target.TypeId)) == null)
                            {
                                skipped.Add(new
                                {
                                    inputIndex = target.InputIndex,
                                    typeId = target.TypeId,
                                    reason = "already-deleted",
                                    categoryName = target.CategoryName,
                                    familyName = target.FamilyName,
                                    typeName = target.TypeName
                                });
                                continue;
                            }

                            var deletedIds = doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(target.TypeId));
                            int count = deletedIds?.Count ?? 0;
                            deletedElementCount += count;
                            deletedTypeIds.Add(target.TypeId);
                            deleted.Add(new
                            {
                                inputIndex = target.InputIndex,
                                typeId = target.TypeId,
                                deletedElementCount = count,
                                categoryName = target.CategoryName,
                                familyName = target.FamilyName,
                                typeName = target.TypeName
                            });
                        }
                        catch (Exception ex)
                        {
                            failed.Add(new
                            {
                                inputIndex = target.InputIndex,
                                typeId = target.TypeId,
                                reason = ex.Message,
                                categoryName = target.CategoryName,
                                familyName = target.FamilyName,
                                typeName = target.TypeName
                            });
                        }
                    }
                    tx.Commit();
                }
            }

            return new
            {
                ok = failed.Count == 0,
                dryRun = false,
                inputCount = rawTypeIds.Count,
                uniqueTypeIdCount = seen.Count,
                candidateCount = candidates.Count,
                deletedCount = deleted.Count,
                deletedElementCount,
                failedCount = failed.Count,
                skippedCount = skipped.Count,
                deletedTypeIds,
                deleted,
                failed,
                skipped,
                msg = $"Deleted {deleted.Count}/{candidates.Count} unused ElementType(s)."
            };
        }

        private static object ToBulkTargetDto(BulkTypeTarget target)
        {
            return new
            {
                inputIndex = target.InputIndex,
                typeId = target.TypeId,
                categoryName = target.CategoryName,
                familyName = target.FamilyName,
                typeName = target.TypeName
            };
        }

        private static List<(int inputIndex, int typeId)> ParseBulkTypeIds(JArray rawTypeIds)
        {
            var result = new List<(int inputIndex, int typeId)>();
            int inputIndex = 0;
            foreach (var token in rawTypeIds)
            {
                inputIndex++;
                if (TryReadBulkTypeId(token, out int typeId))
                    result.Add((inputIndex, typeId));
                else
                    result.Add((inputIndex, -1));
            }
            return result;
        }

        private static bool TryReadBulkTypeId(JToken token, out int typeId)
        {
            typeId = -1;
            try
            {
                if (token.Type == JTokenType.Integer || token.Type == JTokenType.String)
                    return int.TryParse(token.ToString(), out typeId);

                if (token.Type == JTokenType.Object)
                {
                    var obj = (JObject)token;
                    return int.TryParse(
                        (obj.Value<string>("typeId")
                         ?? obj.Value<string>("elementTypeId")
                         ?? obj.Value<string>("elementId")
                         ?? "").Trim(),
                        out typeId);
                }
            }
            catch
            {
                typeId = -1;
            }
            return false;
        }

        private static HashSet<int> CollectUsedTypeIds(Document doc)
        {
            var usedTypeIds = new HashSet<int>();
            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                try
                {
                    var typeId = element.GetTypeId();
                    if (typeId != null && typeId != ElementId.InvalidElementId)
                        usedTypeIds.Add(typeId.IntValue());
                }
                catch
                {
                    // Some internal elements do not expose a stable TypeId. Ignore them.
                }
            }
            return usedTypeIds;
        }

        private static ElementType? FindTypeByAny(
            Document doc,
            int? typeIdOpt,
            string? uniqueId,
            string? typeName,
            string? familyName,
            string? categoryName,
            List<object> steps)
        {
            // 1) by typeId
            if (typeIdOpt.HasValue && typeIdOpt.Value > 0)
            {
                var el = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeIdOpt.Value)) as ElementType;
                if (el != null) return el;
                steps.Add(new { step = "resolve-typeId", ok = false, typeId = typeIdOpt.Value });
            }

            // 2) by uniqueId
            if (!string.IsNullOrWhiteSpace(uniqueId))
            {
                var el = doc.GetElement(uniqueId) as ElementType;
                if (el != null) return el;
                steps.Add(new { step = "resolve-uniqueId", ok = false, uniqueId });
            }

            // 3) by names (with optional category)
            IEnumerable<Element> q = new FilteredElementCollector(doc).WhereElementIsElementType();

            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                var cat = ResolveCategory(doc, categoryName);
                if (cat != null)
                    q = q.Where(e => e.Category != null && e.Category.Id == cat.Id);
            }

            var candidates = q.Cast<ElementType>().ToList();

            if (!string.IsNullOrWhiteSpace(familyName))
                candidates = candidates.Where(t => TryGetFamilyName(t)?.Equals(familyName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            if (!string.IsNullOrWhiteSpace(typeName))
                candidates = candidates.Where(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (candidates.Count == 1) return candidates[0];

            steps.Add(new
            {
                step = "resolve-name",
                familyName,
                typeName,
                categoryName,
                candidates = candidates.Select(c => new { id = c.Id.IntValue(), fam = TryGetFamilyName(c), name = c.Name }).ToList()
            });

            // Fallback: first candidate (best-effort)
            return candidates.FirstOrDefault();
        }

        private static bool HasInstancesOfType(Document doc, ElementType type)
        {
            var (instances, _) = CollectInstancesOfType(doc, type);
            return instances.Count > 0;
        }

        private static (List<Element> instances, ElementId? categoryId) CollectInstancesOfType(Document doc, ElementType type)
        {
            var catId = type.Category?.Id;
            IEnumerable<Element> instQ;

            if (catId != null)
            {
                instQ = new FilteredElementCollector(doc)
                    .OfCategoryId(catId)
                    .WhereElementIsNotElementType()
                    .Where(e => e.GetTypeId() == type.Id);
            }
            else
            {
                // Rare case: no category on type → full scan
                instQ = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.GetTypeId() == type.Id);
            }
            return (instQ.ToList(), catId);
        }

        private static Category? ResolveCategory(Document doc, string categoryName)
        {
            foreach (BuiltInCategory bic in Enum.GetValues(typeof(BuiltInCategory)))
            {
                try
                {
                    var cat = Category.GetCategory(doc, bic);
                    if (cat != null && cat.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                        return cat;
                }
                catch { /* ignore */ }
            }
            return null;
        }

        private static bool CategoryCompatible(ElementType a, ElementType b)
        {
            var ca = a.Category?.Id;
            var cb = b.Category?.Id;
            if (ca == null || cb == null) return false;
            return ca == cb;
        }

        private static string? TryGetFamilyName(ElementType t)
        {
            if (t is FamilySymbol fs) return fs?.Family?.Name;
            return null; // system family types
        }
    }
}


