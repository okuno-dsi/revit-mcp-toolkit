// File: RevitMCPAddin/Commands/ElementOps/Ceiling/GetCeilingsCommand.cs  (UnitHelper化)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using CeilingElement = Autodesk.Revit.DB.Ceiling;

namespace RevitMCPAddin.Commands.ElementOps.Ceiling
{
    public class GetCeilingsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_ceilings";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)(cmd.Params ?? new JObject());

            // legacy paging (backward compatible)
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            bool includeParameters = p.Value<bool?>("includeParameters") ?? false;

            // shape/paging + lightweight
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeLocation = p.Value<bool?>("includeLocation") ?? true;

            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            int filterTypeId = p.Value<int?>("typeId") ?? p.Value<int?>("ceilingTypeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingElement))
                .WhereElementIsNotElementType()
                .Cast<CeilingElement>()
                .ToList();

            IEnumerable<CeilingElement> q = all;

            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                CeilingElement target = null;
                if (targetEid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(targetEid)) as CeilingElement;
                else target = doc.GetElement(targetUid) as CeilingElement;
                q = (target != null) ? new[] { target } : Enumerable.Empty<CeilingElement>();
            }

            if (filterTypeId > 0)
                q = q.Where(c => c.GetTypeId().IntValue() == filterTypeId);

            if (!string.IsNullOrWhiteSpace(filterTypeName) || !string.IsNullOrWhiteSpace(filterFamilyName))
            {
                q = q.Where(c =>
                {
                    var ct = doc.GetElement(c.GetTypeId()) as ElementType;
                    if (ct == null) return false;
                    if (!string.IsNullOrWhiteSpace(filterTypeName) && !string.Equals(ct.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (!string.IsNullOrWhiteSpace(filterFamilyName) && !string.Equals(ct.FamilyName ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase))
                        return false;
                    return true;
                });
            }

            if (filterLevelId > 0)
                q = q.Where(c => c.LevelId.IntValue() == filterLevelId);

            if (!string.IsNullOrWhiteSpace(filterLevelName))
                q = q.Where(c =>
                {
                    var lv = doc.GetElement(c.LevelId) as Level;
                    return lv != null && string.Equals(lv.Name ?? "", filterLevelName, StringComparison.OrdinalIgnoreCase);
                });

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(c =>
                {
                    var instName = c.Name ?? "";
                    if (instName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var ct = doc.GetElement(c.GetTypeId()) as ElementType;
                    return (ct?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });

            // materialize and precompute type names
            var filtered = q.ToList();
            var typeNameMap = new Dictionary<int, string>();
            var familyNameMap = new Dictionary<int, string>();
            foreach (var tid in filtered.Select(c => c.GetTypeId().IntValue()).Distinct())
            {
                var et = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid)) as ElementType;
                typeNameMap[tid] = et?.Name ?? string.Empty;
                familyNameMap[tid] = et?.FamilyName ?? string.Empty;
            }

            var ordered = filtered
                .OrderBy(c => typeNameMap.TryGetValue(c.GetTypeId().IntValue(), out var tn) ? tn : string.Empty)
                .ThenBy(c => c.Id.IntValue())
                .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return ResultUtil.Ok(new
                {
                    totalCount,
                    inputUnits = new { Length = "mm", Area = "m2" },
                    internalUnits = new { Length = "ft", Area = "ft2" }
                });

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(c =>
                {
                    var n = c.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    var tid = c.GetTypeId().IntValue();
                    return typeNameMap.TryGetValue(tid, out var tnm) ? tnm : string.Empty;
                }).ToList();

                return ResultUtil.Ok(new
                {
                    totalCount,
                    names,
                    inputUnits = new { Length = "mm", Area = "m2" },
                    internalUnits = new { Length = "ft", Area = "ft2" }
                });
            }

            // paging and idsOnly
            IEnumerable<CeilingElement> paged = ordered;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(c => c.Id.IntValue()).ToList();
                return ResultUtil.Ok(new { totalCount, elementIds = ids });
            }

            var ceilings = paged.Select(c =>
            {
                var tid = c.GetTypeId().IntValue();
                var tName = typeNameMap.TryGetValue(tid, out var tn2) ? tn2 : string.Empty;
                var fName = familyNameMap.TryGetValue(tid, out var fn2) ? fn2 : string.Empty;
                var lv = doc.GetElement(c.LevelId) as Level;
                int? categoryId = c.Category?.Id?.IntValue();
                string categoryName = c.Category?.Name ?? "";

                // 面積（m2）
                double areaM2 = 0;
                try
                {
                    var pArea = c.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (pArea != null && pArea.StorageType == StorageType.Double)
                        areaM2 = Math.Round(UnitHelper.Ft2ToM2(pArea.AsDouble()), 3);
                }
                catch { }

                // 標高(mm)
                double elevationMm = UnitHelper.CeilingElevationMm(doc, c);

                // 位置（BB中心 mm）
                object location = null;
                if (includeLocation)
                {
                    try
                    {
                        var bb = c.get_BoundingBox(null);
                        if (bb != null)
                        {
                            var center = (bb.Min + bb.Max) / 2.0;
                            location = new
                            {
                                x = Math.Round(UnitHelper.InternalToMm(center.X), 3),
                                y = Math.Round(UnitHelper.InternalToMm(center.Y), 3),
                                z = Math.Round(UnitHelper.InternalToMm(center.Z), 3)
                            };
                        }
                    }
                    catch { }
                }

                // 周長（mm）
                double perimeterMm = 0;
                try
                {
                    var pPeri = c.get_Parameter(BuiltInParameter.HOST_PERIMETER_COMPUTED);
                    if (pPeri != null && pPeri.StorageType == StorageType.Double)
                        perimeterMm = Math.Round(UnitHelper.FtToMm(pPeri.AsDouble()), 3);
                }
                catch { }

                // パラメータ（SI値）
                List<object> parameters = null;
                if (includeParameters)
                {
                    parameters = new List<object>();
                    foreach (var pa in c.Parameters.Cast<Parameter>())
                    {
                        var infoObj = UnitHelper.ParamToSiInfo(pa);                 // object（匿名型 or 既存の戻り）
                        var info = infoObj as JObject ?? JObject.FromObject(infoObj); // JObject化
                        var val = info["value"];
                        if (val != null && !(val.Type == JTokenType.String && string.IsNullOrEmpty(val.Value<string>())))
                            parameters.Add(info);
                    }
                }

                return new
                {
                    elementId = c.Id.IntValue(),
                    uniqueId = c.UniqueId,
                    categoryId,
                    categoryName,
                    typeId = tid,
                    typeName = tName,
                    familyName = fName,
                    levelId = c.LevelId.IntValue(),
                    levelName = lv?.Name ?? "",
                    elevationMm,
                    areaM2,
                    perimeterMm,
                    location,
                    parameters
                };
            }).ToList();

            return ResultUtil.Ok(new
            {
                totalCount,
                ceilings,
                inputUnits = new { Length = "mm", Area = "m2" },
                internalUnits = new { Length = "ft", Area = "ft2" }
            });
        }
    }
}


