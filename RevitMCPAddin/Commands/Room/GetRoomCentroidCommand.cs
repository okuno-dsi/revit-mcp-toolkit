// ================================================================
// File: Commands/Room/GetRoomCentroidCommand.cs  (UnitHelper統一版)
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class GetRoomCentroidCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_centroid";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("elementId", out var idToken))
                throw new InvalidOperationException("Parameter 'elementId' is required.");

            var room = doc.GetElement(new ElementId(idToken.Value<int>()))
                       as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return new { ok = false, message = $"Room not found: {idToken}" };

            var calc = new SpatialElementGeometryCalculator(doc);
            var res = calc.CalculateSpatialElementGeometry(room);
            Solid solid = res.GetGeometry();

            XYZ c = solid.ComputeCentroid();

            return new
            {
                ok = true,
                centroid = new
                {
                    x = Math.Round(UnitHelper.FtToMm(c.X), 3),
                    y = Math.Round(UnitHelper.FtToMm(c.Y), 3),
                    z = Math.Round(UnitHelper.FtToMm(c.Z), 3)
                },
                units = UnitHelper.DefaultUnitsMeta()
            };
        }
    }
}
