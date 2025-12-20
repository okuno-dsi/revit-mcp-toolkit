// File: RevitMCPAddin/Commands/ElementOps/Mass/DeleteMassInstanceCommand.cs
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    public class DeleteMassInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_mass_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // elementId の取得
            if (!p.TryGetValue("elementId", out var elemTok))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            int elementId = elemTok.Value<int>();

            // 要素取得
            var element = doc.GetElement(new ElementId(elementId));
            if (element == null)
            {
                return new { ok = false, message = $"Element not found: {elementId}" };
            }

            // トランザクションで削除実施
            using var tx = new Transaction(doc, "Delete Mass Element");
            tx.Start();
            var deletedIds = doc.Delete(new ElementId(elementId));
            tx.Commit();

            // 削除結果の判定
            bool ok = deletedIds.Any(d => d.IntegerValue == elementId);
            string message = ok
                ? string.Empty
                : $"Element {elementId} の削除に失敗しました。";

            return new { ok, message };
        }
    }
}
