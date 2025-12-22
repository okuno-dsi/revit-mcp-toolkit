// File: Commands/TypeOps/RenameTypesBulkCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.TypeOps
{
    /// <summary>
    /// Bulk rename ElementTypes across categories in a single transaction slice.
    /// Params:
    ///   items: [{ typeId:int? | uniqueId:string?, newName:string }]
    ///   startIndex?: int, batchSize?: int, dryRun?: bool
    ///   conflictPolicy?: "skip" | "appendNumber" | "fail" (default: skip)
    /// Returns: { ok, processed, renamed, skipped, items:[{ ok, typeId, oldName, newName?, reason? }], nextIndex?, completed, totalCount }
    /// </summary>
    public class RenameTypesBulkCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_types_bulk";

        private class ItemSpec
        {
            public int? TypeId;
            public string UniqueId;
            public string NewName;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var items = ParseItems(p["items"]);
            if (items.Count == 0) return new { ok = false, msg = "items is required." };

            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? items.Count);
            bool dryRun = p.Value<bool?>("dryRun") ?? false;
            string conflictPolicy = p.Value<string>("conflictPolicy") ?? "skip"; // skip|appendNumber|fail

            int total = items.Count;
            var slice = items.Skip(startIndex).Take(batchSize).ToList();

            var resultItems = new List<object>(slice.Count);
            int processed = 0, renamed = 0, skipped = 0;

            // Preload name sets per category for conflict checks
            var nameSets = new Dictionary<int, HashSet<string>>();

            using (var tx = new Transaction(doc, "Rename Types (Bulk)"))
            {
                if (!dryRun) tx.Start();

                foreach (var it in slice)
                {
                    processed++;
                    try
                    {
                        ElementType et = null;
                        if (it.TypeId.HasValue)
                        {
                            et = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(it.TypeId.Value)) as ElementType;
                        }
                        if (et == null && !string.IsNullOrWhiteSpace(it.UniqueId))
                        {
                            et = doc.GetElement(it.UniqueId) as ElementType;
                        }
                        if (et == null)
                        {
                            skipped++; resultItems.Add(new { ok = false, typeId = it.TypeId ?? 0, reason = "not_found" });
                            continue;
                        }

                        string newName = it.NewName ?? string.Empty;
                        string oldName = et.Name ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, oldName, StringComparison.Ordinal))
                        {
                            skipped++; resultItems.Add(new { ok = true, typeId = et.Id.IntValue(), oldName, reason = "no_change" });
                            continue;
                        }

                        int catId = et.Category?.Id?.IntValue() ?? 0;
                        if (catId != 0 && !nameSets.TryGetValue(catId, out var set))
                        {
                            set = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var t in new FilteredElementCollector(doc).WhereElementIsElementType().Where(e => e.Category != null && e.Category.Id.IntValue() == catId))
                            {
                                try { set.Add(((ElementType)t).Name); } catch { }
                            }
                            nameSets[catId] = set;
                        }

                        string resolvedName = ResolveConflict(newName, nameSets.ContainsKey(catId) ? nameSets[catId] : null, conflictPolicy);
                        if (resolvedName == null)
                        {
                            skipped++; resultItems.Add(new { ok = false, typeId = et.Id.IntValue(), oldName, reason = "name_conflict" });
                            continue;
                        }

                        if (!dryRun)
                        {
                            et.Name = resolvedName;
                            if (catId != 0) nameSets[catId].Add(resolvedName);
                        }

                        renamed++;
                        resultItems.Add(new { ok = true, typeId = et.Id.IntValue(), oldName, newName = resolvedName });
                    }
                    catch (Exception ex)
                    {
                        skipped++; resultItems.Add(new { ok = false, typeId = it.TypeId ?? 0, reason = ex.Message });
                    }
                }

                if (!dryRun) tx.Commit();
            }

            int next = startIndex + slice.Count;
            bool completed = next >= total;
            return new { ok = true, processed, renamed, skipped, items = resultItems, nextIndex = completed ? (int?)null : next, completed, totalCount = total };
        }

        private static string ResolveConflict(string desired, HashSet<string> set, string policy)
        {
            if (set == null || !set.Contains(desired)) return desired;
            switch ((policy ?? "skip").ToLowerInvariant())
            {
                case "skip": return null; // signal conflict
                case "fail": return null;
                case "appendnumber":
                    {
                        int i = 2;
                        string candidate;
                        do { candidate = desired + " (" + i.ToString() + ")"; i++; } while (set.Contains(candidate));
                        return candidate;
                    }
                default: return null;
            }
        }

        private static List<ItemSpec> ParseItems(JToken tok)
        {
            var list = new List<ItemSpec>();
            if (tok is JArray arr)
            {
                foreach (var t in arr.OfType<JObject>())
                {
                    var s = new ItemSpec
                    {
                        TypeId = t.Value<int?>("typeId"),
                        UniqueId = t.Value<string>("uniqueId"),
                        NewName = t.Value<string>("newName")
                    };
                    if ((s.TypeId.HasValue || !string.IsNullOrWhiteSpace(s.UniqueId)) && !string.IsNullOrWhiteSpace(s.NewName))
                        list.Add(s);
                }
            }
            return list;
        }
    }
}



