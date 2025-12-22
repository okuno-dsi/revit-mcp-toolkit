// RenameFloorTypeCommand.cs
using System;
using ARDB = Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class RenameFloorTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_floor_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            ARDB.Document doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var id = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("floorTypeId"));
            var ft = doc.GetElement(id) as ARDB.FloorType
                     ?? throw new global::System.InvalidOperationException("FloorType not found.");

            using (var tx = new ARDB.Transaction(doc, "Rename FloorType"))
            {
                tx.Start();
                ft.Name = p.Value<string>("newName");
                tx.Commit();
                return new { ok = true };
            }
        }
    }
}

