// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/DuplicateArchitecturalColumnTypeCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class DuplicateArchitecturalColumnTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_architectural_column_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int typeId = p.Value<int>("typeId");
            string newName = p.Value<string>("newTypeName");

            using var tx = new Transaction(doc, "Duplicate Column Type");
            tx.Start();
            var original = doc.GetElement(new ElementId(typeId)) as FamilySymbol
                           ?? throw new InvalidOperationException($"タイプが見つかりません: {typeId}");
            var dup = original.Duplicate(newName) as FamilySymbol;
            tx.Commit();

            return new { ok = true, newTypeId = dup.Id.IntegerValue, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}
