// RevitMCPAddin/Commands/ElementOps/Door/RenameDoorTypeCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class RenameDoorTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_door_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var typeId = new ElementId(p.Value<int>("typeId"));
            var newName = p.Value<string>("newTypeName");

            var symbol = doc.GetElement(typeId) as FamilySymbol
                         ?? throw new InvalidOperationException($"Door type not found: {typeId.IntegerValue}");

            using (var tx = new Transaction(doc, "Rename Door Type"))
            {
                tx.Start();
                symbol.Name = newName;
                tx.Commit();
            }

            return new { ok = true, typeId = typeId.IntegerValue, newName };
        }
    }
}
