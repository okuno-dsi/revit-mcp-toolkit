// ================================================================
// File: RevitMCPAddin/Commands/ElementOps/Mass/MassCommands.cs
// 仕様: 両対応ターゲット(elementId/uniqueId or typeId/typeName(+familyName))
//      paging(skip/count; count=0=metaのみ) / namesOnly / 安定ソート
//      Double は SpecTypeId に基づき mm/mm2/mm3/deg ⇔ ft/ft2/ft3/rad 変換
//      返却一貫: elementId/uniqueId/typeId 等と units メタ
//      .NET Framework 4.8 / C# 8 互換
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    internal static class MassUnits
    {
        public static object InputUnits() => new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" };
        public static object InternalUnits() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };

        public static object ConvertDoubleBySpec(double raw, ForgeTypeId fdt)
        {
            try
            {
                if (fdt != null)
                {
                    if (fdt.Equals(SpecTypeId.Length)) return Math.Round(UnitHelper.FtToMm(raw), 3);
                    if (fdt.Equals(SpecTypeId.Area)) return Math.Round(UnitHelper.Ft2ToMm2(raw), 3);
                    if (fdt.Equals(SpecTypeId.Volume)) return Math.Round(UnitHelper.Ft3ToMm3(raw), 3);
                    if (fdt.Equals(SpecTypeId.Angle)) return Math.Round(UnitHelper.RadToDeg(raw), 3);
                }
            }
            catch { }
            return Math.Round(raw, 3); // 既定：内部値のまま
        }
    }

    // ============================================================
    // 1) インスタンス一覧: get_mass_instances
    // ============================================================
    public class GetMassInstancesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_mass_instances";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            // legacy paging
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            // shape/paging
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var pageObj = shape?["page"] as JObject;
            int limit = Math.Max(0, pageObj?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, pageObj?.Value<int?>("skip") ?? pageObj?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeLocation = p.Value<bool?>("includeLocation") ?? true;

            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string elementTypeFilter = p.Value<string>("elementType"); // "FamilyInstance" | "DirectShape"
            string nameContains = p.Value<string>("nameContains");

            var allElems = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Mass)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();

            IEnumerable<Element> q = allElems;

            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                Element target = null;
                if (targetEid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(targetEid));
                else target = doc.GetElement(targetUid);
                q = target == null ? Enumerable.Empty<Element>() : new[] { target };
            }

            if (!string.IsNullOrWhiteSpace(elementTypeFilter))
            {
                if (string.Equals(elementTypeFilter, "FamilyInstance", StringComparison.OrdinalIgnoreCase))
                    q = q.Where(e => e is FamilyInstance);
                else if (string.Equals(elementTypeFilter, "DirectShape", StringComparison.OrdinalIgnoreCase))
                    q = q.Where(e => e is DirectShape);
            }

            if (filterTypeId > 0)
                q = q.Where(e => e.GetTypeId()?.IntValue() == filterTypeId);

            if (!string.IsNullOrWhiteSpace(filterTypeName) || !string.IsNullOrWhiteSpace(filterFamilyName))
            {
                q = q.Where(e =>
                {
                    var tid = e.GetTypeId();
                    var et = tid != null && tid != ElementId.InvalidElementId ? doc.GetElement(tid) as ElementType : null;
                    if (et == null) return false;
                    if (!string.IsNullOrWhiteSpace(filterTypeName) && !string.Equals(et.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (!string.IsNullOrWhiteSpace(filterFamilyName) && !string.Equals(et.FamilyName ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase))
                        return false;
                    return true;
                });
            }

            if (filterLevelId > 0 || !string.IsNullOrWhiteSpace(filterLevelName))
            {
                q = q.Where(e =>
                {
                    int? levelId = null;
                    if (e is FamilyInstance fi) levelId = fi.LevelId.IntValue();
                    else
                    {
                        var pLevel = e.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                                  ?? e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)
                                  ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                                  ?? e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                        if (pLevel != null && pLevel.StorageType == StorageType.ElementId)
                            levelId = pLevel.AsElementId()?.IntValue();
                    }
                    if (!levelId.HasValue) return false;
                    if (filterLevelId > 0 && levelId.Value != filterLevelId) return false;
                    if (!string.IsNullOrWhiteSpace(filterLevelName))
                    {
                        var lv = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId.Value)) as Level;
                        if (lv == null || !string.Equals(lv.Name ?? "", filterLevelName, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    return true;
                });
            }

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                q = q.Where(e =>
                {
                    var n = e.Name ?? "";
                    if (n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                    if ((et?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    return false;
                });
            }

            var ordered = q.Select(e =>
            {
                string eType = e is FamilyInstance ? "FamilyInstance" : (e is DirectShape ? "DirectShape" : e.GetType().Name);
                var et = doc.GetElement(e.GetTypeId()) as ElementType;
                string tName = et?.Name ?? "";
                return new { e, eType, tName };
            })
            .OrderBy(x => x.eType)
            .ThenBy(x => x.tName)
            .ThenBy(x => x.e.Id.IntValue())
            .Select(x => x.e)
            .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(e =>
                {
                    var n = e.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    var et = doc.GetElement(e.GetTypeId()) as ElementType;
                    return et?.Name ?? "";
                }).ToList();
                return new { ok = true, totalCount, names, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
            }

            IEnumerable<Element> paged = ordered;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(e => e.Id.IntValue()).ToList();
                return new { ok = true, totalCount, elementIds = ids, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
            }

            var page = paged.Select(e =>
            {
                string eType = e is FamilyInstance ? "FamilyInstance" : (e is DirectShape ? "DirectShape" : e.GetType().Name);
                var et = doc.GetElement(e.GetTypeId()) as ElementType;
                string typeName = et?.Name ?? "";
                string familyName = et?.FamilyName ?? "";
                int? levelId = null; string levelName = "";
                XYZ loc = null;

                if (e is FamilyInstance fi)
                {
                    levelId = fi.LevelId.IntValue();
                    var lv = doc.GetElement(fi.LevelId) as Level;
                    levelName = lv?.Name ?? "";
                    var lp = fi.Location as LocationPoint;
                    if (lp?.Point != null && includeLocation) loc = lp.Point;
                }
                else
                {
                    if (includeLocation)
                    {
                        var bb = e.get_BoundingBox(null);
                        if (bb != null) loc = (bb.Min + bb.Max) / 2.0;
                    }

                    var pLevel = e.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                               ?? e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)
                               ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                               ?? e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                    if (pLevel != null && pLevel.StorageType == StorageType.ElementId)
                    {
                        var lid = pLevel.AsElementId();
                        if (lid != null && lid != ElementId.InvalidElementId)
                        {
                            levelId = lid.IntValue();
                            var lv = doc.GetElement(lid) as Level;
                            levelName = lv?.Name ?? "";
                        }
                    }
                }

                object location = null;
                if (loc != null)
                {
                    location = new
                    {
                        x = Math.Round(UnitHelper.FtToMm(loc.X), 3),
                        y = Math.Round(UnitHelper.FtToMm(loc.Y), 3),
                        z = Math.Round(UnitHelper.FtToMm(loc.Z), 3),
                    };
                }

                return new
                {
                    elementId = e.Id.IntValue(),
                    uniqueId = e.UniqueId,
                    elementType = eType,
                    typeId = e.GetTypeId()?.IntValue(),
                    typeName,
                    familyName,
                    levelId,
                    levelName,
                    location
                };
            }).ToList();

            return new
            {
                ok = true,
                totalCount,
                masses = page,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }

    // ============================================================
    // 2) インスタンスのパラメータ一覧: get_mass_instance_parameters
    // ============================================================
    public class GetMassInstanceParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_mass_instance_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // elementId / uniqueId 両対応
            Element element = null;
            int elementId = p.Value<int?>("elementId") ?? 0;
            string uniqueId = p.Value<string>("uniqueId");
            if (elementId > 0) element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            else if (!string.IsNullOrWhiteSpace(uniqueId)) element = doc.GetElement(uniqueId);
            if (element == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (element.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, elementId = element.Id.IntValue(), uniqueId = element.UniqueId, totalCount, inputUnits = MassUnits.InputUnits(), internalUnits = MassUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new { ok = true, elementId = element.Id.IntValue(), uniqueId = element.UniqueId, totalCount, names, inputUnits = MassUnits.InputUnits(), internalUnits = MassUnits.InternalUnits() };
            }

            var page = ordered.Skip(skip).Take(count);
            var parameters = new List<object>();
            foreach (var pa in page)
            {
                if (pa == null) continue;
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object value = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double: value = MassUnits.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                        case StorageType.Integer: value = pa.AsInteger(); break;
                        case StorageType.String: value = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: value = pa.AsElementId()?.IntValue() ?? -1; break;
                    }
                }
                catch { value = null; }

                parameters.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntValue(),
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    value
                });
            }

            return new
            {
                ok = true,
                elementId = element.Id.IntValue(),
                uniqueId = element.UniqueId,
                totalCount,
                parameters,
                inputUnits = MassUnits.InputUnits(),
                internalUnits = MassUnits.InternalUnits()
            };
        }
    }

    // ============================================================
    // 3) タイプのパラメータ一覧: get_mass_type_parameters
    // ============================================================
    public class GetMassTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_mass_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // typeId / typeName(+familyName) / elementId/uniqueId→type
            ElementType typeElem = null;

            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            if (typeId > 0)
            {
                typeElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as ElementType;
            }
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_Mass)
                    .Cast<ElementType>()
                    .Where(t => string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(t => string.Equals(t.FamilyName ?? "", familyName, StringComparison.OrdinalIgnoreCase));
                typeElem = q.OrderBy(t => t.FamilyName ?? "").ThenBy(t => t.Name ?? "").FirstOrDefault();
            }
            else
            {
                // インスタンスからタイプ解決
                Element inst = null;
                int eid = p.Value<int?>("elementId") ?? 0;
                string uid = p.Value<string>("uniqueId");
                if (eid > 0) inst = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                else if (!string.IsNullOrWhiteSpace(uid)) inst = doc.GetElement(uid);
                if (inst != null)
                {
                    var tid = inst.GetTypeId();
                    if (tid != null && tid != ElementId.InvalidElementId)
                        typeElem = doc.GetElement(tid) as ElementType;
                }
            }

            if (typeElem == null)
                return new { ok = false, msg = "Mass タイプが見つかりません（typeId/typeName か elementId/uniqueId を確認）。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (typeElem.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, scope = "type", typeId = typeElem.Id.IntValue(), uniqueId = typeElem.UniqueId, totalCount, inputUnits = MassUnits.InputUnits(), internalUnits = MassUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new { ok = true, scope = "type", typeId = typeElem.Id.IntValue(), uniqueId = typeElem.UniqueId, totalCount, names, inputUnits = MassUnits.InputUnits(), internalUnits = MassUnits.InternalUnits() };
            }

            var page = ordered.Skip(skip).Take(count);
            var parameters = new List<object>();
            foreach (var pa in page)
            {
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object value = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double: value = MassUnits.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                        case StorageType.Integer: value = pa.AsInteger(); break;
                        case StorageType.String: value = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: value = pa.AsElementId()?.IntValue() ?? -1; break;
                    }
                }
                catch { value = null; }

                parameters.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntValue(),
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    value
                });
            }

            return new
            {
                ok = true,
                scope = "type",
                typeId = typeElem.Id.IntValue(),
                uniqueId = typeElem.UniqueId,
                totalCount,
                parameters,
                inputUnits = MassUnits.InputUnits(),
                internalUnits = MassUnits.InternalUnits()
            };
        }
    }

    // ============================================================
    // 4) タイプ一覧: get_mass_types
    // ============================================================
    public class GetMassTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_mass_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Mass)
                .Cast<FamilySymbol>()
                .ToList();

            IEnumerable<FamilySymbol> q = all;

            if (!string.IsNullOrWhiteSpace(filterTypeName))
                q = q.Where(s => string.Equals(s.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(filterFamilyName))
                q = q.Where(s => string.Equals(s.Family?.Name ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(s => (s.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q
                .Select(s => new { s, fam = s.Family != null ? (s.Family.Name ?? "") : "", name = s.Name ?? "", id = s.Id.IntValue() })
                .OrderBy(x => x.fam)
                .ThenBy(x => x.name)
                .ThenBy(x => x.id)
                .Select(x => x.s)
                .ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, totalCount, inputUnits = MassUnits.InputUnits(), internalUnits = MassUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(s => s.Name ?? "").ToList();
                return new { ok = true, totalCount, names, inputUnits = MassUnits.InputUnits(), internalUnits = MassUnits.InternalUnits() };
            }

            var types = ordered.Skip(skip).Take(count)
                .Select(s => new
                {
                    typeId = s.Id.IntValue(),
                    uniqueId = s.UniqueId,
                    typeName = s.Name ?? "",
                    familyName = s.Family != null ? (s.Family.Name ?? "") : ""
                }).ToList();

            return new { ok = true, totalCount, types, inputUnits = MassUnits.InputUnits(), internalUnits = MassUnits.InternalUnits() };
        }
    }
}


