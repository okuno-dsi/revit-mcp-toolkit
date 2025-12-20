// File: Commands/ElementOps/Foundation/GetStructuralFoundationParametersCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class GetStructuralFoundationParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_foundation_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            // 対象解決（タイプ優先）
            Element target = null; string scope = "instance";

            var type = FoundationUnits.ResolveType(doc, p, BuiltInCategory.OST_StructuralFoundation);
            if (type != null) { target = type; scope = "type"; }
            else
            {
                target = FoundationUnits.ResolveInstance(doc, p);
                if (target == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId または typeId/typeName）。");
            }

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (target.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            int? elementIdOut = scope == "instance" ? (int?)target.Id.IntegerValue : null;
            int? typeIdOut = scope == "type" ? target.Id.IntegerValue
                               : (target.GetTypeId() != null && target.GetTypeId() != ElementId.InvalidElementId
                                  ? (int?)target.GetTypeId().IntegerValue : null);

            if (count == 0)
                return new { ok = true, scope, elementId = elementIdOut, typeId = typeIdOut, uniqueId = target.UniqueId, totalCount, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new { ok = true, scope, elementId = elementIdOut, typeId = typeIdOut, uniqueId = target.UniqueId, totalCount, names, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };
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

            return new { ok = true, scope, elementId = elementIdOut, typeId = typeIdOut, uniqueId = target.UniqueId, totalCount, parameters = list, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };
        }
    }
}
