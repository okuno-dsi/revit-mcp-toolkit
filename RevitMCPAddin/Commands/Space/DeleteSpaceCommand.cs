// ================================================================
// File: Commands/Space/DeleteSpaceCommand.cs (UnitHelper統一：※単位変換なし)
// ================================================================
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Space
{
    public class DeleteSpaceCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_space";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");
            var p = (JObject)(cmd.Params ?? new JObject());

            using var tx = new Transaction(doc, "Delete Space");
            tx.Start();
            doc.Delete(new ElementId(p.Value<int>("elementId")));
            tx.Commit();

            return new { ok = true };
        }
    }
}
