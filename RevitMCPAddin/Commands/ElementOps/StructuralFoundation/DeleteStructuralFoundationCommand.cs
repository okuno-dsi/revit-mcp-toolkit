// File: Commands/ElementOps/Foundation/DeleteStructuralFoundationCommand.cs (UnitHelper対応/返却整備)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class DeleteStructuralFoundationCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_structural_foundation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            var e = FoundationUnits.ResolveInstance(doc, p);
            if (e == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId）。");

            using (var tx = new Transaction(doc, "Delete Structural Foundation"))
            {
                tx.Start();
                var deleted = doc.Delete(e.Id);
                tx.Commit();
            }

            return new { ok = true, elementId = e.Id.IntegerValue, uniqueId = e.UniqueId };
        }
    }
}
