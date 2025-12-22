// ================================================================
// File: Commands/DatumOps/GetLevelsCommand.cs (UnitHelper統一版)
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DatumOps
{
    public class GetLevelsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_levels";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, message = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(lvl => lvl.Elevation)
                .ToList();

            int totalCount = allLevels.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, totalCount, units = UnitHelper.DefaultUnitsMeta() };

            var slice = allLevels.Skip(skip).Take(count).ToList();

            var levels = slice.Select(lvl =>
            {
                int id = lvl.Id.IntValue();
                double elevMm = Math.Round(UnitHelper.InternalToMm(lvl.Elevation, doc), 3);
                return new
                {
                    levelId = id,
                    elementId = id,            // alias
                    uniqueId = lvl.UniqueId,
                    name = lvl.Name,
                    elevation = elevMm
                };
            }).ToList();

            var levelsById = levels.ToDictionary(x => x.levelId, x => (object)x);

            return new
            {
                ok = true,
                totalCount,
                units = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { length = "ft" },
                levels,
                levelsById
            };
        }
    }
}

