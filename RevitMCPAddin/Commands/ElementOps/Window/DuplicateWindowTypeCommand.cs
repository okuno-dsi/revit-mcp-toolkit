// RevitMCPAddin/Commands/ElementOps/Window/DuplicateWindowTypeCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class DuplicateWindowTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_window_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var typeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("typeId"));
            var newName = p.Value<string>("newTypeName");

            var symbol = doc.GetElement(typeId) as FamilySymbol
                         ?? throw new System.InvalidOperationException($"Window type not found: {typeId.IntValue()}");

            using var tx = new Transaction(doc, "Duplicate Window Type");
            tx.Start();
            var newSymbol = (FamilySymbol)symbol.Duplicate(newName);
            tx.Commit();

            return new
            {
                ok = true,
                newTypeId = newSymbol.Id.IntValue(),
                newName
            };
        }
    }
}


