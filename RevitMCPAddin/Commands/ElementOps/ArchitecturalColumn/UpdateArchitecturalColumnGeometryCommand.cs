// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/UpdateArchitecturalColumnGeometryCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class UpdateArchitecturalColumnGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "update_architectural_column_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("elementId");

            var fi = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as FamilyInstance
                     ?? throw new InvalidOperationException($"要素が見つかりません: {id}");
            var loc = fi.Location as LocationCurve
                      ?? throw new InvalidOperationException("LocationCurve を持たない要素です");

            var s = (JObject)p["start"];
            var e = (JObject)p["end"];
            var newStart = UnitHelper.MmToXyz(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z"));
            var newEnd = UnitHelper.MmToXyz(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z"));

            using var tx = new Transaction(doc, "Update Column Geometry");
            tx.Start();
            loc.Curve = Line.CreateBound(newStart, newEnd);
            tx.Commit();

            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}

