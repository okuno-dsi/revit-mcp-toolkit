// -------------------------
// UpdateWallGeometryCommand.cs (UnitHelper対応版)
// -------------------------
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class UpdateWallGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "update_wall_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var elementId = new ElementId(p.Value<int>("elementId"));
            var wall = doc.GetElement(elementId) as Autodesk.Revit.DB.Wall
                        ?? throw new InvalidOperationException($"Wall not found: {elementId.IntegerValue}");
            var loc = wall.Location as LocationCurve
                        ?? throw new InvalidOperationException("Wall does not have LocationCurve");

            var s = p["start"]; var e = p["end"];
            var newStart = UnitHelper.MmToXyz(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z"));
            var newEnd = UnitHelper.MmToXyz(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z"));

            using (var tx = new Transaction(doc, "Update Wall Geometry"))
            {
                tx.Start();
                loc.Curve = Line.CreateBound(newStart, newEnd);
                tx.Commit();
            }
            return new { ok = true, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
        }
    }
}
