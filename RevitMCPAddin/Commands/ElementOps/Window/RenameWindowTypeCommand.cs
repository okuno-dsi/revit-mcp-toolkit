using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class RenameWindowTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_window_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("typeId", out var tidToken))
                throw new ArgumentException("Parameter 'typeId' is required.");
            int typeId = tidToken.Value<int>();

            string newName = p.Value<string>("newTypeName")
                             ?? throw new ArgumentException("Parameter 'newTypeName' is required.");
            if (string.IsNullOrWhiteSpace(newName))
                return new { ok = false, msg = "New type name cannot be empty." };

            var symbol = doc.GetElement(new ElementId(typeId)) as FamilySymbol;
            if (symbol == null)
                return new { ok = false, msg = $"Window type not found: {typeId}" };

            try
            {
                using var tx = new Transaction(doc, "Rename Window Type");
                tx.Start();
                symbol.Name = newName;
                tx.Commit();
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }

            return new { ok = true, typeId, newName };
        }
    }
}
