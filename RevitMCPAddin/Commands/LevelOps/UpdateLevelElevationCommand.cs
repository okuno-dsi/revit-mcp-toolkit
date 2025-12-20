// ================================================================
// File: Commands/DatumOps/UpdateLevelElevationCommand.cs (UnitHelper統一版)
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DatumOps
{
    public class UpdateLevelElevationCommand : IRevitCommandHandler
    {
        public string CommandName => "update_level_elevation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());
            var id = new ElementId(p.Value<int>("levelId"));

            double elevMm = p.Value<double>("elevation");
            double elevFt = UnitHelper.MmToInternal(elevMm, doc);

            using (var tx = new Transaction(doc, "Update Level Elevation"))
            {
                tx.Start();
                var lvl = doc.GetElement(id) as Level
                          ?? throw new InvalidOperationException($"Level not found: {id.IntegerValue}");
                lvl.Elevation = elevFt;
                tx.Commit();

                return new
                {
                    ok = true,
                    elevation = Math.Round(UnitHelper.InternalToMm(lvl.Elevation, doc), 3),
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
        }
    }
}
