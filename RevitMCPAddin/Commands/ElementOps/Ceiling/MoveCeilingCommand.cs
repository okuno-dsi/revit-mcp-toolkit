// File: RevitMCPAddin/Commands/ElementOps/Ceiling/MoveCeilingCommand.cs  (UnitHelper化)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Ceiling
{
    public class MoveCeilingCommand : IRevitCommandHandler
    {
        public string CommandName => "move_ceiling";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var id = new ElementId(p.Value<int>("elementId"));
            double dx = UnitHelper.MmToInternalLength(p.Value<double>("dx"));
            double dy = UnitHelper.MmToInternalLength(p.Value<double>("dy"));
            double dz = UnitHelper.MmToInternalLength(p.Value<double>("dz"));

            using var tx = new Transaction(doc, "Move Ceiling");
            tx.Start();
            ElementTransformUtils.MoveElement(doc, id, new XYZ(dx, dy, dz));
            tx.Commit();

            return ResultUtil.Ok();
        }
    }
}
