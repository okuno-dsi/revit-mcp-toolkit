// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/MoveArchitecturalColumnCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class MoveArchitecturalColumnCommand : IRevitCommandHandler
    {
        public string CommandName => "move_architectural_column";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("elementId");

            double dx = UnitHelper.MmToFt(p.Value<double>("dx"));
            double dy = UnitHelper.MmToFt(p.Value<double>("dy"));
            double dz = UnitHelper.MmToFt(p.Value<double>("dz"));

            using var tx = new Transaction(doc, "Move Architectural Column");
            tx.Start();
            ElementTransformUtils.MoveElement(doc, new ElementId(id), new XYZ(dx, dy, dz));
            tx.Commit();

            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}
