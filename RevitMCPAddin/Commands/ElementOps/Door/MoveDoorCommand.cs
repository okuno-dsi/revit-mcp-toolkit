// RevitMCPAddin/Commands/ElementOps/Door/MoveDoorCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class MoveDoorCommand : IRevitCommandHandler
    {
        public string CommandName => "move_door";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var id = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("elementId"));

            // mm → ft: UnitHelper へ統一
            double dx = UnitHelper.MmToFt(p.Value<double?>("dx") ?? 0.0);
            double dy = UnitHelper.MmToFt(p.Value<double?>("dy") ?? 0.0);
            double dz = UnitHelper.MmToFt(p.Value<double?>("dz") ?? 0.0);

            using (var tx = new Transaction(doc, "Move Door"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, id, new XYZ(dx, dy, dz));
                tx.Commit();
            }
            return new { ok = true, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
        }
    }
}

