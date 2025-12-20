#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DatumOps
{
    public class ListLevelsSimpleCommand : IRevitCommandHandler
    {
        public string CommandName => "list_levels_simple";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l => new {
                    id = l.Id.IntegerValue,
                    name = l.Name,
                    elevation = Math.Round(UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters), 6)
                })
                .OrderBy(x => x.elevation)
                .ToList();

            return new { ok = true, items = levels };
        }
    }
}

