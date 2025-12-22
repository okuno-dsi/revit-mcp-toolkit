// RevitMCPAddin/Commands/ElementOps/Window/GetWindowTypesCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class GetWindowTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_window_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            int targetElementId = p.Value<int?>("elementId") ?? p.Value<int?>("windowId") ?? 0;
            string targetUniqueId = p.Value<string>("uniqueId");

            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            int filterFamilyId = p.Value<int?>("familyId") ?? 0;
            string filterFamilyName = p.Value<string>("familyName");
            string nameContains = p.Value<string>("nameContains");

            var allTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Windows)
                .Cast<FamilySymbol>()
                .ToList();

            IEnumerable<FamilySymbol> q = allTypes;

            if (targetElementId > 0 || !string.IsNullOrWhiteSpace(targetUniqueId))
            {
                FamilyInstance inst = null;
                if (targetElementId > 0)
                    inst = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(targetElementId)) as FamilyInstance;
                else
                    inst = doc.GetElement(targetUniqueId) as FamilyInstance;

                if (inst == null)
                    return new { ok = false, msg = "Window インスタンスが見つかりません（elementId/windowId/uniqueId を確認）。" };

                if (inst.Category?.Id.IntValue() != (int)BuiltInCategory.OST_Windows)
                    return new { ok = false, msg = $"要素 {inst.Id.IntValue()} は Window ではありません。" };

                var sym = doc.GetElement(inst.GetTypeId()) as FamilySymbol
                          ?? throw new System.InvalidOperationException("インスタンスのタイプ（FamilySymbol）が取得できませんでした。");

                q = new[] { sym };
            }
            else if (filterTypeId > 0)
            {
                var sym = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(filterTypeId)) as FamilySymbol;
                if (sym == null)
                    return new { ok = false, msg = $"FamilySymbol(typeId={filterTypeId}) が見つかりません。" };
                if (sym.Category?.Id.IntValue() != (int)BuiltInCategory.OST_Windows)
                    return new { ok = false, msg = $"typeId={filterTypeId} は Window タイプではありません。" };

                q = new[] { sym };
            }

            if (!string.IsNullOrWhiteSpace(filterTypeName))
            {
                q = q.Where(s => string.Equals(s.Name, filterTypeName, System.StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(filterFamilyName))
                    q = q.Where(s => string.Equals(s.Family?.Name, filterFamilyName, System.StringComparison.OrdinalIgnoreCase));
            }

            if (filterFamilyId > 0)
                q = q.Where(s => s.Family != null && s.Family.Id.IntValue() == filterFamilyId);

            if (!string.IsNullOrWhiteSpace(filterFamilyName))
                q = q.Where(s => string.Equals(s.Family?.Name, filterFamilyName, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(s => (s.Name ?? string.Empty).IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q
                .Select(s => new
                {
                    s,
                    famName = s.Family != null ? (s.Family.Name ?? "") : "",
                    typeName = s.Name ?? "",
                    typeId = s.Id.IntValue()
                })
                .OrderBy(x => x.famName).ThenBy(x => x.typeName).ThenBy(x => x.typeId)
                .Select(x => x.s)
                .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
            {
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            if (namesOnly)
            {
                var names = ordered
                    .Skip(skip)
                    .Take(limit)
                    .Select(s => s.Name ?? string.Empty)
                    .ToList();

                return new
                {
                    ok = true,
                    totalCount,
                    names,
                    inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            IEnumerable<FamilySymbol> seq = ordered;
            if (skip > 0) seq = seq.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) seq = seq.Take(limit);

            if (idsOnly)
            {
                var typeIds = seq.Select(s => s.Id.IntValue()).ToList();
                return new { ok = true, totalCount, typeIds, inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" }, internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" } };
            }

            var types = seq.Select(s => new
            {
                typeId = s.Id.IntValue(),
                uniqueId = s.UniqueId,
                typeName = s.Name ?? string.Empty,
                familyId = s.Family != null ? s.Family.Id.IntValue() : (int?)null,
                familyName = s.Family != null ? (s.Family.Name ?? string.Empty) : string.Empty
            }).ToList();

            return new
            {
                ok = true,
                totalCount,
                types,
                inputUnits = new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" },
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}


