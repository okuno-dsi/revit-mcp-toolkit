// File: Commands/ElementOps/Foundation/GetStructuralFoundationTypesCommand.cs (UnitHelper対応/返却整備)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class GetStructuralFoundationTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_foundation_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (Newtonsoft.Json.Linq.JObject)cmd.Params;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            int targetElementId = p.Value<int?>("elementId") ?? 0;
            string targetUniqueId = p.Value<string>("uniqueId");

            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            int filterFamilyId = p.Value<int?>("familyId") ?? 0;
            string nameContains = p.Value<string>("nameContains");

            if (targetElementId > 0 || !string.IsNullOrWhiteSpace(targetUniqueId))
            {
                var inst = targetElementId > 0 ? doc.GetElement(new ElementId(targetElementId)) : doc.GetElement(targetUniqueId);
                if (inst == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId）。");

                var tid = inst.GetTypeId();
                if (tid == null || tid == ElementId.InvalidElementId) return ResultUtil.Err("インスタンスからタイプIDを取得できませんでした。");

                var t = doc.GetElement(tid) as ElementType;
                if (t == null) return ResultUtil.Err("タイプ要素の取得に失敗しました。");

                return new
                {
                    ok = true,
                    totalCount = 1,
                    types = new[] { ToDto(t) },
                    inputUnits = FoundationUnits.InputUnits(),
                    internalUnits = FoundationUnits.InternalUnits()
                };
            }

            var allTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .ToList();

            IEnumerable<ElementType> q = allTypes;

            if (filterTypeId > 0)
            {
                var t = doc.GetElement(new ElementId(filterTypeId)) as ElementType;
                if (t == null) return ResultUtil.Err($"typeId={filterTypeId} のタイプが見つかりません。");
                q = new[] { t };
            }

            if (!string.IsNullOrWhiteSpace(filterTypeName))
            {
                q = q.Where(t => string.Equals(t.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(filterFamilyName))
                    q = q.Where(t => string.Equals(t.FamilyName ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase));
            }

            if (filterFamilyId > 0)
                q = q.Where(t => (t as FamilySymbol)?.Family?.Id.IntegerValue == filterFamilyId);

            if (!string.IsNullOrWhiteSpace(filterFamilyName))
                q = q.Where(t => string.Equals(t.FamilyName ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(t => (t.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q.Select(t => new { t, fam = t.FamilyName ?? "", name = t.Name ?? "", id = t.Id.IntegerValue })
                           .OrderBy(x => x.fam).ThenBy(x => x.name).ThenBy(x => x.id).Select(x => x.t).ToList();

            int total = ordered.Count;

            if (count == 0) return new { ok = true, totalCount = total, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(t => t.Name ?? "").ToList();
                return new { ok = true, totalCount = total, names, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };
            }

            var types = ordered.Skip(skip).Take(count).Select(ToDto).ToList();
            return new { ok = true, totalCount = total, types, inputUnits = FoundationUnits.InputUnits(), internalUnits = FoundationUnits.InternalUnits() };
        }

        private static object ToDto(ElementType t)
        {
            int? familyId = (t as FamilySymbol)?.Family?.Id.IntegerValue;
            return new
            {
                typeId = t.Id.IntegerValue,
                uniqueId = t.UniqueId,
                typeName = t.Name ?? string.Empty,
                familyId,
                familyName = t.FamilyName ?? string.Empty
            };
        }
    }
}
