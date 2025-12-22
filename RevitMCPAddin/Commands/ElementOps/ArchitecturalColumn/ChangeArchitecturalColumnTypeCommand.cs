// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/ChangeArchitecturalColumnTypeCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class ChangeArchitecturalColumnTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_architectural_column_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int id = p.Value<int>("elementId");
            int newType = p.Value<int>("typeId");

            var doc = uiapp.ActiveUIDocument.Document;
            var fi = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as FamilyInstance
                       ?? throw new InvalidOperationException($"要素が見つかりません: {id}");

            using (var tx = new Transaction(doc, "Change Column Type"))
            {
                tx.Start();
                fi.ChangeTypeId(Autodesk.Revit.DB.ElementIdCompat.From(newType));
                tx.Commit();
            }

            return new { ok = true, newTypeId = newType, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}

