// File: Commands/ElementOps/Foundation/GetStructuralFoundationTypeParametersCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class GetStructuralFoundationTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_foundation_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            ElementType typeElem = FoundationUnits.ResolveType(doc, p, BuiltInCategory.OST_StructuralFoundation);
            int? sourceElementId = null;

            if (typeElem == null)
            {
                // インスタンス→タイプ
                var inst = FoundationUnits.ResolveInstance(doc, p);
                if (inst == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId または typeId/typeName）。");
                var tid = inst.GetTypeId();
                if (tid == null || tid == ElementId.InvalidElementId) return ResultUtil.Err("インスタンスからタイプIDを取得できませんでした。");
                typeElem = doc.GetElement(tid) as ElementType ?? throw new InvalidOperationException("タイプ要素の取得に失敗しました。");
                sourceElementId = inst.Id.IntegerValue;
            }

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (typeElem.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, scope = "type", elementId = sourceElementId, typeId = typeElem.Id.IntegerValue, uniqueId = typeElem.UniqueId, totalCount, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new { ok = true, scope = "type", elementId = sourceElementId, typeId = typeElem.Id.IntegerValue, uniqueId = typeElem.UniqueId, totalCount, names, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };
            }

            var page = ordered.Skip(skip).Take(count);
            var list = new List<object>();

            foreach (var param in page)
            {
                if (param == null) continue;
                ForgeTypeId spec = null; string dataType = null;
                try { spec = param.Definition?.GetDataType(); dataType = spec?.TypeId; } catch { dataType = null; }

                object value = null;
                try
                {
                    switch (param.StorageType)
                    {
                        case StorageType.Double: value = FoundationUnits.ToUser(param.AsDouble(), spec); break;
                        case StorageType.Integer: value = param.AsInteger(); break;
                        case StorageType.String: value = param.AsString() ?? ""; break;
                        case StorageType.ElementId: value = param.AsElementId()?.IntegerValue ?? -1; break;
                    }
                }
                catch { value = null; }

                list.Add(new { name = param.Definition?.Name ?? "", id = param.Id.IntegerValue, storageType = param.StorageType.ToString(), isReadOnly = param.IsReadOnly, dataType, value });
            }

            return new { ok = true, scope = "type", elementId = sourceElementId, typeId = typeElem.Id.IntegerValue, uniqueId = typeElem.UniqueId, totalCount, parameters = list, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };
        }
    }
}
