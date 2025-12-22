// File: Commands/ElementOps/Foundation/GetStructuralFoundationParameterCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class GetStructuralFoundationParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_foundation_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            // 対象: typeId/typeName(+family) または elementId/uniqueId
            Element target = null; string scope = "instance";

            var type = FoundationUnits.ResolveType(doc, p, BuiltInCategory.OST_StructuralFoundation);
            if (type != null) { target = type; scope = "type"; }
            else
            {
                target = FoundationUnits.ResolveInstance(doc, p);
                if (target == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId または typeId/typeName）。");
            }

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName)) return ResultUtil.Err("paramName が必要です。");

            var param = target.LookupParameter(paramName);
            if (param == null) return ResultUtil.Err($"Parameter not found: {paramName}");

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
                    case StorageType.ElementId: value = param.AsElementId()?.IntValue() ?? -1; break;
                }
            }
            catch { value = null; }

            int? elementIdOut = scope == "instance" ? (int?)target.Id.IntValue() : null;
            int? typeIdOut = scope == "type" ? target.Id.IntValue()
                               : (target.GetTypeId() != null && target.GetTypeId() != ElementId.InvalidElementId
                                  ? (int?)target.GetTypeId().IntValue() : null);

            return new
            {
                ok = true,
                scope,
                elementId = elementIdOut,
                typeId = typeIdOut,
                uniqueId = target.UniqueId,
                name = param.Definition?.Name ?? "",
                id = param.Id.IntValue(),
                storageType = param.StorageType.ToString(),
                isReadOnly = param.IsReadOnly,
                dataType,
                value,
                inputUnits = FoundationUnits.InputUnits(),
                internalUnits = FoundationUnits.InternalUnits()
            };
        }
    }
}

