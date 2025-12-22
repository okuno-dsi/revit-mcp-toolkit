// ================================================================
// File: Commands/Space/MoveSpaceCommand.cs (UnitHelper完全統一版)
// ================================================================
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Space
{
    public class MoveSpaceCommand : IRevitCommandHandler
    {
        public string CommandName => "move_space";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");
            var p = (JObject)(cmd.Params ?? new JObject());

            int id = p.Value<int>("elementId");
            var space = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as Autodesk.Revit.DB.Mechanical.Space
                        ?? throw new System.InvalidOperationException($"Space not found: {id}");

            var dx = UnitHelper.MmToInternal(p.Value<double>("dx"), doc);
            var dy = UnitHelper.MmToInternal(p.Value<double>("dy"), doc);
            var dz = UnitHelper.MmToInternal(p.Value<double>("dz"), doc);

            using var tx = new Transaction(doc, "Move Space");
            tx.Start();
            ElementTransformUtils.MoveElement(doc, space.Id, new XYZ(dx, dy, dz));
            tx.Commit();

            return new { ok = true, msg = string.Empty };
        }
    }
}

