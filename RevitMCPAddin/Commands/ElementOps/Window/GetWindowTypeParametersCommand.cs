// RevitMCPAddin/Commands/ElementOps/Window/GetWindowTypeParametersCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class GetWindowTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_window_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;

            FamilySymbol symbol = null;
            int? sourceElementId = null;

            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            if (typeId > 0)
            {
                symbol = doc.GetElement(new ElementId(typeId)) as FamilySymbol;
                if (symbol == null)
                    return new { ok = false, msg = $"FamilySymbol(typeId={typeId}) が見つかりません。" };
                if (symbol.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_Windows)
                    return new { ok = false, msg = $"typeId={typeId} は Window タイプではありません。" };
            }
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                var syms = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(familyName))
                    syms = syms.Where(s => string.Equals(s.Family?.Name, familyName, StringComparison.OrdinalIgnoreCase));

                symbol = syms
                    .OrderBy(s => s.Family?.Name ?? "")
                    .ThenBy(s => s.Name ?? "")
                    .ThenBy(s => s.Id.IntegerValue)
                    .FirstOrDefault();

                if (symbol == null)
                    return new { ok = false, msg = $"typeName='{typeName}' の Window タイプが見つかりません。" };
            }
            else
            {
                int eid = p.Value<int?>("elementId") ?? p.Value<int?>("windowId") ?? 0;
                FamilyInstance inst = null;
                if (eid > 0) inst = doc.GetElement(new ElementId(eid)) as FamilyInstance;
                else
                {
                    var uid = p.Value<string>("uniqueId");
                    if (!string.IsNullOrWhiteSpace(uid))
                        inst = doc.GetElement(uid) as FamilyInstance;
                }

                if (inst == null)
                    return new { ok = false, msg = "Window インスタンスが見つかりません（elementId/windowId/uniqueId を確認）。" };

                if (inst.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_Windows)
                    return new { ok = false, msg = $"要素 {inst.Id.IntegerValue} は Window ではありません。" };

                symbol = doc.GetElement(inst.GetTypeId()) as FamilySymbol
                         ?? throw new InvalidOperationException("インスタンスのタイプ（FamilySymbol）が取得できませんでした。");
                sourceElementId = inst.Id.IntegerValue;
            }

            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            var shape = p["_shape"] as JObject;
            var pageObj = shape?["page"] as JObject;
            int skip = Math.Max(0, pageObj?.Value<int?>("skip") ?? pageObj?.Value<int?>("offset") ?? legacySkip);
            int limit = Math.Max(0, pageObj?.Value<int?>("limit") ?? legacyCount);
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            var orderedParams = (symbol.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(prm => new { prm, name = prm?.Definition?.Name ?? string.Empty, id = prm?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.prm)
                .ToList();

            int totalCount = orderedParams.Count;

            if (summaryOnly || legacyCount == 0 || limit == 0)
            {
                return new
                {
                    ok = true,
                    scope = "type",
                    elementId = sourceElementId,
                    typeId = symbol.Id.IntegerValue,
                    uniqueId = symbol.UniqueId,
                    totalCount,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            if (namesOnly)
            {
                var names = orderedParams.Skip(skip).Take(limit)
                    .Select(prm => prm?.Definition?.Name ?? string.Empty)
                    .ToList();

                return new
                {
                    ok = true,
                    scope = "type",
                    elementId = sourceElementId,
                    typeId = symbol.Id.IntegerValue,
                    uniqueId = symbol.UniqueId,
                    totalCount,
                    names,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            var page = orderedParams.Skip(skip).Take(limit);
            var parameters = new List<object>(Math.Max(0, Math.Min(limit, totalCount - skip)));

            foreach (var prm in page)
            {
                if (prm == null) continue;

                string name = prm.Definition?.Name ?? string.Empty;
                int pid = prm.Id.IntegerValue;
                string storageType = prm.StorageType.ToString();
                bool isReadOnly = prm.IsReadOnly;

                ForgeTypeId fdt = null;
                string dataType = null;
                try { fdt = prm.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object value = null;
                try
                {
                    switch (prm.StorageType)
                    {
                        case StorageType.Double:
                            double raw = prm.AsDouble();
                            value = (fdt != null)
                                ? UnitHelper.ConvertDoubleBySpec(raw, fdt, 3)
                                : (object)System.Math.Round(raw, 3);
                            break;
                        case StorageType.Integer:
                            value = prm.AsInteger(); break;
                        case StorageType.String:
                            value = prm.AsString() ?? string.Empty; break;
                        case StorageType.ElementId:
                            value = prm.AsElementId()?.IntegerValue ?? -1; break;
                        default:
                            value = null; break;
                    }
                }
                catch { value = null; }

                parameters.Add(new
                {
                    name,
                    id = pid,
                    storageType,
                    isReadOnly,
                    dataType,
                    value
                });
            }

            return new
            {
                ok = true,
                scope = "type",
                elementId = sourceElementId,
                typeId = symbol.Id.IntegerValue,
                uniqueId = symbol.UniqueId,
                totalCount,
                parameters,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}
