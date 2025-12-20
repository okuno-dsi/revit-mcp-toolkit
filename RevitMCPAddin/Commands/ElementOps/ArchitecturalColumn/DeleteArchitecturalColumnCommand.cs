// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/DeleteArchitecturalColumnCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class DeleteArchitecturalColumnCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_architectural_column";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int id = ((JObject)cmd.Params).Value<int>("elementId");

            using var tx = new Transaction(doc, "Delete Architectural Column");
            tx.Start();
            doc.Delete(new ElementId(id));
            tx.Commit();

            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}
