// RevitMCPAddin/Commands/ElementOps/Window/GetWindowsCommand.cs
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class GetWindowsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_windows";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;

            // ★ ここを pageObj にリネーム
            var pageObj = shape?["page"] as JObject;
            int limit = Math.Max(0, pageObj?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, pageObj?.Value<int?>("skip") ?? pageObj?.Value<int?>("offset") ?? legacySkip);

            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            int targetEid = p.Value<int?>("elementId") ?? p.Value<int?>("windowId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");

            int filterWallId = p.Value<int?>("wallId") ?? 0;
            string filterHostUid = p.Value<string>("hostUniqueId");

            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");

            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            var typeIds = all.Select(w => w.GetTypeId().IntValue()).Distinct().ToList();
            var typeMap = new Dictionary<int, FamilySymbol>(typeIds.Count);
            foreach (var id in typeIds)
                typeMap[id] = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as FamilySymbol;

            var levelIds = all.Select(w => w.LevelId.IntValue()).Distinct().ToList();
            var levelMap = new Dictionary<int, Level>(levelIds.Count);
            foreach (var id in levelIds)
                levelMap[id] = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as Level;

            IEnumerable<FamilyInstance> q = all;
            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                FamilyInstance target = null;
                if (targetEid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(targetEid)) as FamilyInstance;
                else target = doc.GetElement(targetUid) as FamilyInstance;

                q = target == null ? Enumerable.Empty<FamilyInstance>() : new[] { target };
            }

            if (filterTypeId > 0)
                q = q.Where(w => w.GetTypeId().IntValue() == filterTypeId);

            if (!string.IsNullOrWhiteSpace(filterTypeName))
            {
                q = q.Where(w =>
                {
                    typeMap.TryGetValue(w.GetTypeId().IntValue(), out var sym);
                    if (sym == null) return false;
                    bool nameOk = string.Equals(sym.Name, filterTypeName, StringComparison.OrdinalIgnoreCase);
                    if (!nameOk) return false;
                    if (!string.IsNullOrWhiteSpace(filterFamilyName))
                        return string.Equals(sym.Family?.Name, filterFamilyName, StringComparison.OrdinalIgnoreCase);
                    return true;
                });
            }

            if (filterWallId > 0 || !string.IsNullOrWhiteSpace(filterHostUid))
            {
                q = q.Where(w =>
                {
                    if (w.Host is Autodesk.Revit.DB.Wall hw)
                    {
                        if (filterWallId > 0 && hw.Id.IntValue() != filterWallId) return false;
                        if (!string.IsNullOrWhiteSpace(filterHostUid)
                            && !string.Equals(hw.UniqueId, filterHostUid, StringComparison.OrdinalIgnoreCase)) return false;
                        return true;
                    }
                    return false;
                });
            }

            if (filterLevelId > 0)
                q = q.Where(w => w.LevelId.IntValue() == filterLevelId);

            if (!string.IsNullOrWhiteSpace(filterLevelName))
            {
                q = q.Where(w =>
                {
                    levelMap.TryGetValue(w.LevelId.IntValue(), out var lv);
                    return lv != null && string.Equals(lv.Name, filterLevelName, StringComparison.OrdinalIgnoreCase);
                });
            }

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                q = q.Where(w =>
                {
                    var instName = w.Name ?? string.Empty;
                    if (instName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;

                    typeMap.TryGetValue(w.GetTypeId().IntValue(), out var sym);
                    var tName = sym?.Name ?? string.Empty;
                    return tName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            var ordered = q
                .Select(w =>
                {
                    typeMap.TryGetValue(w.GetTypeId().IntValue(), out var sym);
                    string tName = sym?.Name ?? "";
                    return new { w, tName };
                })
                .OrderBy(x => x.tName)
                .ThenBy(x => x.w.Id.IntValue())
                .Select(x => x.w)
                .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
            {
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = new { Length = "mm" },
                    internalUnits = new { Length = "ft" }
                };
            }

            if (namesOnly)
            {
                var names = ordered
                    .Skip(skip)
                    .Take(limit)
                    .Select(w =>
                    {
                        var name = w.Name ?? string.Empty;
                        if (!string.IsNullOrEmpty(name)) return name;
                        typeMap.TryGetValue(w.GetTypeId().IntValue(), out var sym);
                        return sym?.Name ?? string.Empty;
                    })
                    .ToList();

                return new
                {
                    ok = true,
                    totalCount,
                    names,
                    inputUnits = new { Length = "mm" },
                    internalUnits = new { Length = "ft" }
                };
            }

            IEnumerable<FamilyInstance> seq = ordered;
            if (skip > 0) seq = seq.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) seq = seq.Take(limit);

            if (idsOnly)
            {
                var ids = seq.Select(w => w.Id.IntValue()).ToList();
                return new { ok = true, totalCount, elementIds = ids, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
            }

            // ★ ここを pageList にリネーム（List<FamilyInstance>）
            var pageList = seq.ToList();

            // ★ 以降は pageList を使うので、JObject の pageObj とは混同しない
            var windows = pageList.Select(w =>
            {
                // 位置（mmへ変換）
                double x = 0, y = 0, z = 0;
                if (w.Location is LocationPoint lp && lp.Point != null)
                {
                    var pt = lp.Point;
                    x = Math.Round(UnitHelper.InternalToMm(pt.X), 3);
                    y = Math.Round(UnitHelper.InternalToMm(pt.Y), 3);
                    z = Math.Round(UnitHelper.InternalToMm(pt.Z), 3);
                }

                typeMap.TryGetValue(w.GetTypeId().IntValue(), out var sym);
                string typeName = sym?.Name ?? string.Empty;
                string familyName = sym?.Family?.Name ?? string.Empty;

                levelMap.TryGetValue(w.LevelId.IntValue(), out var lv);
                string levelName = lv?.Name ?? string.Empty;

                int? hostWallId = null;
                if (w.Host is Autodesk.Revit.DB.Wall hw)
                    hostWallId = hw.Id.IntValue();

                return new
                {
                    elementId = w.Id.IntValue(),
                    uniqueId = w.UniqueId,
                    typeId = w.GetTypeId().IntValue(),
                    typeName,
                    familyName,
                    levelId = w.LevelId.IntValue(),
                    levelName,
                    hostWallId,
                    location = new { x, y, z }
                };
            }).ToList();

            return new
            {
                ok = true,
                totalCount,
                windows,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }
}


