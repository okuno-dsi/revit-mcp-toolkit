// RevitMCPAddin/Commands/MEPOps/MoveMepElementCommand.cs (UnitHelper対応)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class MoveMepElementCommand : IRevitCommandHandler
    {
        public string CommandName => "move_mep_element";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var id = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("elementId"));
            double dx = UnitHelper.MmToFt(p.Value<double>("dx"));
            double dy = UnitHelper.MmToFt(p.Value<double>("dy"));
            double dz = UnitHelper.MmToFt(p.Value<double>("dz"));

            using (var tx = new Transaction(doc, "Move MEP Element"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, id, new XYZ(dx, dy, dz));
                tx.Commit();
            }
            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}

