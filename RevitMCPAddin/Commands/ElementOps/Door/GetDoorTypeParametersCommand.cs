// RevitMCPAddin/Commands/ElementOps/Door/GetDoorTypeParametersCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class GetDoorTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_door_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            FamilySymbol sym = DoorUtil.ResolveDoorType(doc, p);
            if (sym == null)
            {
                var el = DoorUtil.ResolveElement(doc, p);
                var fi = el as FamilyInstance;
                if (fi != null && fi.Category?.Id.IntValue() == (int)BuiltInCategory.OST_Doors)
                    sym = doc.GetElement(fi.GetTypeId()) as FamilySymbol;
            }
            if (sym == null) return new { ok = false, msg = "Door FamilySymbol が見つかりません。" };

            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            var shape = p["_shape"] as JObject;
            var pageObj = shape?["page"] as JObject;
            int skip = Math.Max(0, pageObj?.Value<int?>("skip") ?? pageObj?.Value<int?>("offset") ?? legacySkip);
            int limit = Math.Max(0, pageObj?.Value<int?>("limit") ?? legacyCount);
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            var ordered = (sym.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || legacyCount == 0 || limit == 0) return new { ok = true, typeId = sym.Id.IntValue(), uniqueId = sym.UniqueId, totalCount, inputUnits = DoorUtil.UnitsIn(), internalUnits = DoorUtil.UnitsInt() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new { ok = true, typeId = sym.Id.IntValue(), uniqueId = sym.UniqueId, totalCount, names, inputUnits = DoorUtil.UnitsIn(), internalUnits = DoorUtil.UnitsInt() };
            }

            var page = ordered.Skip(skip).Take(limit);
            var list = new List<object>();
            foreach (var pa in page)
            {
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object val = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double: val = DoorUtil.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                        case StorageType.Integer: val = pa.AsInteger(); break;
                        case StorageType.String: val = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: val = pa.AsElementId()?.IntValue() ?? -1; break;
                    }
                }
                catch { val = null; }

                list.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntValue(),
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    value = val
                });
            }

            return new { ok = true, typeId = sym.Id.IntValue(), uniqueId = sym.UniqueId, totalCount, parameters = list, inputUnits = DoorUtil.UnitsIn(), internalUnits = DoorUtil.UnitsInt() };
        }
    }
}

