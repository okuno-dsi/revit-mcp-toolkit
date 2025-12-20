// RevitMCPAddin/Commands/ElementOps/FloorOps/MoveFloorCommand.cs
using ARDB = Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class MoveFloorCommand : IRevitCommandHandler
    {
        public string CommandName => "move_floor";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var id = new ARDB.ElementId(p.Value<int>("elementId"));
            var floorElem = doc.GetElement(id) as ARDB.Floor
                            ?? throw new System.InvalidOperationException("Floor not found.");

            var off = (JObject)p["offset"];
            var offset = new ARDB.XYZ(
                UnitHelper.MmToFt(off.Value<double>("x")),
                UnitHelper.MmToFt(off.Value<double>("y")),
                UnitHelper.MmToFt(off.Value<double>("z")));

            using (var tx = new ARDB.Transaction(doc, "Move Floor"))
            {
                tx.Start();
                ARDB.ElementTransformUtils.MoveElement(doc, floorElem.Id, offset);
                tx.Commit();
            }

            return new { ok = true };
        }
    }
}
