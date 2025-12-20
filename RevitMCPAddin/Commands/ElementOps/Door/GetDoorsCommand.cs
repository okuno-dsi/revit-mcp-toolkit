// RevitMCPAddin/Commands/ElementOps/Door/GetDoorsCommand.cs
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class GetDoorsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_doors";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            // Legacy paging params (kept for backward compatibility)
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // New shape/paging options
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? count);
            int skip2 = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? skip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeLocation = p.Value<bool?>("includeLocation") ?? true;

            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");
            int levelId = p.Value<int?>("levelId") ?? 0;
            string levelName = p.Value<string>("levelName");
            int hostId = p.Value<int?>("hostWallId") ?? 0;
            string hostUid = p.Value<string>("hostUniqueId");
            string nameContains = p.Value<string>("nameContains");

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>();

            IEnumerable<FamilyInstance> q = collector;

            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                FamilyInstance target = null;
                if (targetEid > 0) target = doc.GetElement(new ElementId(targetEid)) as FamilyInstance;
                else target = doc.GetElement(targetUid) as FamilyInstance;
                q = target == null ? Enumerable.Empty<FamilyInstance>() : new[] { target };
            }

            if (typeId > 0) q = q.Where(d => d.GetTypeId().IntegerValue == typeId);
            if (!string.IsNullOrWhiteSpace(typeName) || !string.IsNullOrWhiteSpace(familyName))
                q = q.Where(d =>
                {
                    var sym = doc.GetElement(d.GetTypeId()) as FamilySymbol;
                    if (sym == null) return false;
                    if (!string.IsNullOrWhiteSpace(typeName) && !string.Equals(sym.Name ?? "", typeName, System.StringComparison.OrdinalIgnoreCase)) return false;
                    if (!string.IsNullOrWhiteSpace(familyName) && !string.Equals(sym.Family?.Name ?? "", familyName, System.StringComparison.OrdinalIgnoreCase)) return false;
                    return true;
                });

            if (levelId > 0) q = q.Where(d => d.LevelId.IntegerValue == levelId);
            if (!string.IsNullOrWhiteSpace(levelName))
                q = q.Where(d =>
                {
                    var lv = doc.GetElement(d.LevelId) as Level;
                    return lv != null && string.Equals(lv.Name ?? "", levelName, System.StringComparison.OrdinalIgnoreCase);
                });

            if (hostId > 0 || !string.IsNullOrWhiteSpace(hostUid))
                q = q.Where(d =>
                {
                    var host = d.Host as Autodesk.Revit.DB.Wall;
                    if (host == null) return false;
                    if (hostId > 0 && host.Id.IntegerValue != hostId) return false;
                    if (!string.IsNullOrWhiteSpace(hostUid) && !string.Equals(host.UniqueId, hostUid, System.StringComparison.OrdinalIgnoreCase)) return false;
                    return true;
                });

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(d =>
                {
                    var n = d.Name ?? "";
                    if (n.IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var sym = doc.GetElement(d.GetTypeId()) as FamilySymbol;
                    return (sym?.Name ?? "").IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0;
                });

            // Materialize once for count and sorting
            var filtered = q.ToList();
            int totalCount = filtered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };

            // Precompute type/family name maps to avoid repeated GetElement per instance
            var typeNameMap = new Dictionary<int, string>();
            var familyNameMap = new Dictionary<int, string>();
            foreach (var tid in filtered.Select(d => d.GetTypeId().IntegerValue).Distinct())
            {
                var sym = doc.GetElement(new ElementId(tid)) as FamilySymbol;
                typeNameMap[tid] = sym?.Name ?? string.Empty;
                familyNameMap[tid] = sym?.Family?.Name ?? string.Empty;
            }

            var ordered = filtered
                .OrderBy(d => typeNameMap.TryGetValue(d.GetTypeId().IntegerValue, out var tn) ? tn : string.Empty)
                .ThenBy(d => d.Id.IntegerValue)
                .ToList();

            if (namesOnly)
            {
                var names = ordered.Skip(skip2).Take(limit).Select(d =>
                {
                    var n = d.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    var tid2 = d.GetTypeId().IntegerValue;
                    return typeNameMap.TryGetValue(tid2, out var tn2) ? tn2 : string.Empty;
                }).ToList();

                return new { ok = true, totalCount, names, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
            }

            // Paging and lightweight idsOnly
            IEnumerable<FamilyInstance> paged = ordered;
            if (skip2 > 0) paged = paged.Skip(skip2);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(d => d.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, elementIds = ids };
            }

            var list = paged.Select(d =>
            {
                object location = null;
                if (includeLocation)
                {
                    var lp = d.Location as LocationPoint;
                    if (lp != null && lp.Point != null)
                    {
                        location = new
                        {
                            x = System.Math.Round(UnitHelper.FtToMm(lp.Point.X), 3),
                            y = System.Math.Round(UnitHelper.FtToMm(lp.Point.Y), 3),
                            z = System.Math.Round(UnitHelper.FtToMm(lp.Point.Z), 3)
                        };
                    }
                }

                int? hostWallId = null; string levelName2 = "";
                if (d.Host is Autodesk.Revit.DB.Wall hw) hostWallId = hw.Id.IntegerValue;
                var lv = doc.GetElement(d.LevelId) as Level;
                if (lv != null) levelName2 = lv.Name ?? "";

                var tid = d.GetTypeId().IntegerValue;
                var tName = typeNameMap.TryGetValue(tid, out var tnm) ? tnm : string.Empty;
                var fName = familyNameMap.TryGetValue(tid, out var fnm) ? fnm : string.Empty;
                return new
                {
                    elementId = d.Id.IntegerValue,
                    uniqueId = d.UniqueId,
                    typeId = tid,
                    typeName = tName,
                    familyName = fName,
                    levelId = d.LevelId.IntegerValue,
                    levelName = levelName2,
                    hostWallId,
                    location
                };
            }).ToList();

            return new { ok = true, totalCount, doors = list, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
        }
    }
}
