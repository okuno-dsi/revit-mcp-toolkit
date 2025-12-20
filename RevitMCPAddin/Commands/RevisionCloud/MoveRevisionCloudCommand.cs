// File: RevitMCPAddin/Commands/RevisionCloud/MoveRevisionCloudCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    public class MoveRevisionCloudCommand : IRevitCommandHandler
    {
        public string CommandName => "move_revision_cloud";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int id = p.Value<int>("elementId");
            double dx = UnitUtils.ConvertToInternalUnits(p.Value<double>("dx"), UnitTypeId.Millimeters);
            double dy = UnitUtils.ConvertToInternalUnits(p.Value<double>("dy"), UnitTypeId.Millimeters);
            double dz = UnitUtils.ConvertToInternalUnits(p.Value<double>("dz"), UnitTypeId.Millimeters);

            using (var tx = new Transaction(doc, "Move Revision Cloud"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(
                    doc,
                    new ElementId(id),
                    new XYZ(dx, dy, dz)
                );
                tx.Commit();
            }

            return new { ok = true };
        }
    }
}
