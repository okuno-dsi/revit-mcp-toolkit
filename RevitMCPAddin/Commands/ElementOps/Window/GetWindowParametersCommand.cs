// RevitMCPAddin/Commands/ElementOps/Window/GetWindowParametersCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    /// <summary>
    /// Window のインスタンスまたはタイプのパラメータ一覧を返す（Spec で SI 正規化）。
    /// </summary>
    public class GetWindowParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_window_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;

            Element target = null;
            bool isTypeScope = false;

            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            if (typeId > 0)
            {
                var sym = doc.GetElement(new ElementId(typeId)) as FamilySymbol
                          ?? throw new InvalidOperationException($"FamilySymbol(typeId={typeId}) が見つかりません。");
                if (sym.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_Windows)
                    return new { ok = false, msg = $"typeId={typeId} は Window タイプではありません。" };

                target = sym;
                isTypeScope = true;
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

                var list = syms
                    .OrderBy(s => s.Family?.Name ?? "")
                    .ThenBy(s => s.Name ?? "")
                    .ThenBy(s => s.Id.IntegerValue)
                    .ToList();

                if (list.Count == 0)
                    return new { ok = false, msg = $"typeName='{typeName}' の Window タイプが見つかりません。" };

                target = list.First();
                isTypeScope = true;
            }
            else
            {
                int eid = p.Value<int?>("elementId") ?? p.Value<int?>("windowId") ?? p.Value<int?>("wallId") ?? 0;
                FamilyInstance inst = null;

                if (eid > 0) inst = doc.GetElement(new ElementId(eid)) as FamilyInstance;
                else
                {
                    var uid = p.Value<string>("uniqueId");
                    if (!string.IsNullOrWhiteSpace(uid))
                        inst = doc.GetElement(uid) as FamilyInstance;
                }

                if (inst == null)
                    return new { ok = false, msg = "Window インスタンスが見つかりません（elementId/windowId/wallId/uniqueId を確認）。" };

                if (inst.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_Windows)
                    return new { ok = false, msg = $"要素 {inst.Id.IntegerValue} は Window ではありません。" };

                target = inst;
                isTypeScope = false;
            }

            // Paging and shaping
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            var shape = p["_shape"] as JObject;
            var pageObj = shape?["page"] as JObject;
            int skip = Math.Max(0, pageObj?.Value<int?>("skip") ?? pageObj?.Value<int?>("offset") ?? legacySkip);
            int limit = Math.Max(0, pageObj?.Value<int?>("limit") ?? legacyCount);
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            var parametersEnum = target.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>();
            var orderedParams = parametersEnum
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
                    scope = isTypeScope ? "type" : "instance",
                    elementId = isTypeScope ? (int?)null : target.Id.IntegerValue,
                    typeId = isTypeScope ? target.Id.IntegerValue : (target as FamilyInstance)?.GetTypeId().IntegerValue,
                    uniqueId = target.UniqueId,
                    totalCount,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            if (namesOnly)
            {
                var names = orderedParams
                    .Skip(skip)
                    .Take(limit)
                    .Select(prm => prm?.Definition?.Name ?? string.Empty)
                    .ToList();

                return new
                {
                    ok = true,
                    scope = isTypeScope ? "type" : "instance",
                    elementId = isTypeScope ? (int?)null : target.Id.IntegerValue,
                    typeId = isTypeScope ? target.Id.IntegerValue : (target as FamilyInstance)?.GetTypeId().IntegerValue,
                    uniqueId = target.UniqueId,
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
                            if (fdt != null)
                                value = UnitHelper.ConvertDoubleBySpec(raw, fdt, 3);
                            else
                                value = System.Math.Round(raw, 3);
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
                scope = isTypeScope ? "type" : "instance",
                elementId = isTypeScope ? (int?)null : target.Id.IntegerValue,
                typeId = isTypeScope ? target.Id.IntegerValue : (target as FamilyInstance)?.GetTypeId().IntegerValue,
                uniqueId = target.UniqueId,
                totalCount,
                parameters,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}
