// RevitMCPAddin/Commands/ElementOps/Ceiling/DeleteCeilingCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Ceiling
{
    /// <summary>
    /// 天井(Ceiling) 要素を削除するコマンド
    /// </summary>
    public class DeleteCeilingCommand : IRevitCommandHandler
    {
        // JSON-RPC でのコマンド名
        public string CommandName => "delete_ceiling";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            Document doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            ElementId id = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("elementId"));

            // 存在チェック
            if (doc.GetElement(id) == null)
                return new { ok = false, message = "Ceiling not found." };

            // トランザクションを切って削除
            using (var tx = new Transaction(doc, "Delete Ceiling"))
            {
                tx.Start();
                doc.Delete(id);
                tx.Commit();
            }

            return new { ok = true };
        }
    }
}

