using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class MoveWindowCommand : IRevitCommandHandler
    {
        public string CommandName => "move_window";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var id = new ElementId(p.Value<int>("elementId"));

            // mm → ft に変換
            double dx = UnitHelper.MmToInternal(p.Value<double>("dx"));
            double dy = UnitHelper.MmToInternal(p.Value<double>("dy"));
            double dz = UnitHelper.MmToInternal(p.Value<double>("dz"));

            using var tx = new Transaction(doc, "Move Window");
            tx.Start();
            ElementTransformUtils.MoveElement(doc, id, new XYZ(dx, dy, dz));
            tx.Commit();

            return new { ok = true };
        }
    }
}
