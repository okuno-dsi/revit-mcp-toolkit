// RevitMCPAddin/Commands/ElementOps/StructuralColumn/GetStructuralColumnsCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.StructuralColumn
{
    public class GetStructuralColumnsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_columns";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            // paging / options
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            bool withParameters = p.Value<bool?>("withParameters") ?? false;

            // single targets / filters（元実装どおり）
            int targetEid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");
            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");
            bool? pinned = p.Value<bool?>("pinned");

            var all = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // caches
            var typeMap = all.Select(c => c.GetTypeId().IntegerValue)
                             .Distinct().ToDictionary(id => id, id => doc.GetElement(new ElementId(id)) as FamilySymbol);
            var levelMap = all.Select(c => c.LevelId.IntegerValue)
                              .Distinct().ToDictionary(id => id, id => doc.GetElement(new ElementId(id)) as Level);

            IEnumerable<FamilyInstance> q = all;

            // single target
            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                var target = targetEid > 0 ? doc.GetElement(new ElementId(targetEid)) as FamilyInstance
                                           : doc.GetElement(targetUid) as FamilyInstance;
                q = (target == null) ? Enumerable.Empty<FamilyInstance>() : new[] { target };
            }

            // filters（元実装どおり）
            if (filterTypeId > 0) q = q.Where(c => c.GetTypeId().IntegerValue == filterTypeId);
            if (!string.IsNullOrWhiteSpace(filterTypeName))
                q = q.Where(c =>
                {
                    typeMap.TryGetValue(c.GetTypeId().IntegerValue, out var sym);
                    if (sym == null) return false;
                    var ok = string.Equals(sym.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase);
                    if (!ok) return false;
                    if (!string.IsNullOrWhiteSpace(filterFamilyName))
                        return string.Equals(sym.Family?.Name ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase);
                    return true;
                });
            if (filterLevelId > 0) q = q.Where(c => c.LevelId.IntegerValue == filterLevelId);
            if (!string.IsNullOrWhiteSpace(filterLevelName))
                q = q.Where(c => { levelMap.TryGetValue(c.LevelId.IntegerValue, out var lv); return lv != null && string.Equals(lv.Name ?? "", filterLevelName, StringComparison.OrdinalIgnoreCase); });
            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(c =>
                {
                    var instName = c.Name ?? "";
                    if (instName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    typeMap.TryGetValue(c.GetTypeId().IntegerValue, out var sym);
                    return (sym?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            if (pinned.HasValue) q = q.Where(c => c.Pinned == pinned.Value);

            var ordered = q.Select(c =>
            {
                typeMap.TryGetValue(c.GetTypeId().IntegerValue, out var sym);
                return new { c, tName = sym?.Name ?? "" };
            })
            .OrderBy(x => x.tName).ThenBy(x => x.c.Id.IntegerValue).Select(x => x.c).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, totalCount, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(c =>
                {
                    var n = c.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    typeMap.TryGetValue(c.GetTypeId().IntegerValue, out var sym);
                    return sym?.Name ?? "";
                }).ToList();
                return new { ok = true, totalCount, names, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
            }

            var page = ordered.Skip(skip).Take(count).ToList();

            var cols = page.Select(col =>
            {
                // 位置（mm）
                var lp = col.Location as LocationPoint;
                var xyz = lp?.Point;
                var location = (xyz == null) ? null : new
                {
                    x = Math.Round(UnitHelper.FtToMm(xyz.X), 3),
                    y = Math.Round(UnitHelper.FtToMm(xyz.Y), 3),
                    z = Math.Round(UnitHelper.FtToMm(xyz.Z), 3)
                };

                typeMap.TryGetValue(col.GetTypeId().IntegerValue, out var sym);
                levelMap.TryGetValue(col.LevelId.IntegerValue, out var lv);

                // withParameters: SI 正規化で返却
                List<object> parameters = null;
                if (withParameters)
                {
                    parameters = (col.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                        .Select(pa => UnitHelper.MapParameter(pa, doc, UnitsMode.SI, includeDisplay: true, includeRaw: true))
                        .ToList();
                }

                return new
                {
                    elementId = col.Id.IntegerValue,
                    uniqueId = col.UniqueId,
                    typeId = col.GetTypeId().IntegerValue,
                    typeName = sym?.Name ?? "",
                    familyName = sym?.Family?.Name ?? "",
                    levelId = col.LevelId.IntegerValue,
                    levelName = lv?.Name ?? "",
                    location,
                    parameters
                };
            }).ToList();

            return new { ok = true, totalCount, structuralColumns = cols, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
        }
    }
}
