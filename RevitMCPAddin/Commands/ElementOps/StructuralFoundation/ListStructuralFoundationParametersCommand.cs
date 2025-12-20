// File: Commands/ElementOps/Foundation/ListStructuralFoundationParametersCommand.cs (UnitHelper対応/返却整備)
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class ListStructuralFoundationParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "list_structural_foundation_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            Element target = FoundationUnits.ResolveType(doc, p, BuiltInCategory.OST_StructuralFoundation) as Element
                             ?? FoundationUnits.ResolveInstance(doc, p);
            string scope = (target is ElementType) ? "type" : "instance";
            if (target == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId または typeId/typeName）。");

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (target.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.pa).ToList();

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
            var defs = new List<object>();
            foreach (var prm in page)
            {
                string dataType = null;
                try { dataType = prm.Definition?.GetDataType()?.TypeId; } catch { dataType = null; }

                defs.Add(new { name = prm.Definition?.Name ?? "", id = prm.Id.IntegerValue, storageType = prm.StorageType.ToString(), dataType, isReadOnly = prm.IsReadOnly });
            }

            return new { ok = true, scope, elementId = elementIdOut, typeId = typeIdOut, uniqueId = target.UniqueId, totalCount, definitions = defs, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };
        }
    }
}
