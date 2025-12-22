using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class RenameWallTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_wall_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var typeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("typeId"));
            var wType = doc.GetElement(typeId) as WallType
                        ?? throw new InvalidOperationException($"WallType not found: {typeId.IntValue()}");

            var newName = p.Value<string>("newName") ?? throw new InvalidOperationException("newName is required.");

            using var tx = new Transaction(doc, "Rename WallType");
            tx.Start();
            wType.Name = newName;
            tx.Commit();

            return new { ok = true, typeId = wType.Id.IntValue(), typeName = wType.Name };
        }
    }
}


