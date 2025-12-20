// DuplicateFloorTypeCommand.cs
using System;
using ARDB = Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class DuplicateFloorTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_floor_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            ARDB.Document doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var id = new ARDB.ElementId(p.Value<int>("floorTypeId"));
            var ft = doc.GetElement(id) as ARDB.FloorType
                     ?? throw new global::System.InvalidOperationException("FloorType not found.");

            using (var tx = new ARDB.Transaction(doc, "Duplicate FloorType"))
            {
                tx.Start();
                var newFt = ft.Duplicate(p.Value<string>("newName")) as ARDB.FloorType;
                tx.Commit();
                return new { ok = true, newTypeId = newFt.Id.IntegerValue };
            }
        }
    }
}
