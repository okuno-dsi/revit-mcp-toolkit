using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class DuplicateDoorTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_door_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var typeId = new ElementId(p.Value<int>("typeId"));
            var newName = p.Value<string>("newTypeName");

            var symbol = doc.GetElement(typeId) as FamilySymbol
                         ?? throw new global::System.InvalidOperationException($"Door type not found: {typeId.IntegerValue}");

            using var tx = new Transaction(doc, "Duplicate Door Type");
            tx.Start();
            // Duplicate は ElementType を返すので FamilySymbol にキャスト
            var newSymbol = (FamilySymbol)symbol.Duplicate(newName);
            tx.Commit();

            return new
            {
                ok = true,
                newTypeId = newSymbol.Id.IntegerValue,
                newName
            };
        }
    }
}
