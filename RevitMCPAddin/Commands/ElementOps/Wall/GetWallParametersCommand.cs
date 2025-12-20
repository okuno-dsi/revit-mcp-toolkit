// RevitMCPAddin/Commands/ElementOps/Wall/GetWallParametersCommand.cs
// UnitHelper化: ResolveUnitsMode + MapParameter に統一、メタ返却も UnitHelper メタへ
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class GetWallParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            Autodesk.Revit.DB.Wall wall = null;
            int elementId = p.Value<int?>("elementId") ?? p.Value<int?>("wallId") ?? 0;
            if (elementId > 0) wall = doc.GetElement(new ElementId(elementId)) as Autodesk.Revit.DB.Wall;
            else
            {
                var uid = p.Value<string>("uniqueId");
                if (!string.IsNullOrWhiteSpace(uid)) wall = doc.GetElement(uid) as Autodesk.Revit.DB.Wall;
                if (wall != null) elementId = wall.Id.IntegerValue;
            }
            if (wall == null) return new { ok = false, msg = "Wall not found. Provide elementId/wallId or uniqueId." };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            var allParams = wall.Parameters?.Cast<Parameter>()
                .Select(par => new { par, name = par?.Definition?.Name ?? "", id = par?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.par)
                .ToList() ?? new System.Collections.Generic.List<Parameter>();

            int totalCount = allParams.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
            {
                return new
                {
                    ok = true,
                    elementId,
                    uniqueId = wall.UniqueId ?? string.Empty,
                    totalCount,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            var mode = UnitHelper.ResolveUnitsMode(doc, p);
            var list = allParams.Skip(skip).Take(count)
                .Select(pa => UnitHelper.MapParameter(pa, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3))
                .ToList();

            return new
            {
                ok = true,
                elementId,
                uniqueId = wall.UniqueId ?? string.Empty,
                totalCount,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta(),
                parameters = list
            };
        }
    }
}
