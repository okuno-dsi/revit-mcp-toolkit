// RevitMCPAddin/Commands/ElementOps/Wall/GetWallTypesCommand.cs
// UnitHelper化: 幅の mm 変換を UnitHelper.InternalToMm で、units メタも整備
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class GetWallTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            string nameContains = p.Value<string>("nameContains");

            var q = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>();

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                var needle = nameContains.Trim();
                q = q.Where(wt => wt?.Name != null &&
                                  wt.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var allTypes = q.OrderBy(wt => wt.Name).ToList();
            int totalCount = allTypes.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
            {
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = new { Length = "mm" },
                    internalUnits = new { Length = "ft" }
                };
            }

            var slice = allTypes.Skip(skip).Take(count).ToList();

            var types = slice.Select(wt =>
            {
                int id = wt.Id.IntegerValue;
                double widthMm = Math.Round(UnitHelper.InternalToMm(wt.Width), 3);

                return new
                {
                    typeId = id,
                    elementId = id,
                    uniqueId = wt.UniqueId,
                    typeName = wt.Name ?? string.Empty,
                    width = widthMm
                };
            }).ToList();

            var typesById = types.ToDictionary(x => x.typeId, x => (object)x);

            return new
            {
                ok = true,
                totalCount,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" },
                types,
                typesById
            };
        }
    }
}
