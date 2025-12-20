using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class ChangeCurtainWallTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_curtain_wall_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int id = (int)p["elementId"]!;
            int typeId = (int)p["typeId"]!;

            var doc = uiapp.ActiveUIDocument.Document;
            var wall = doc.GetElement(new ElementId(id))
                       ?? throw new InvalidOperationException("Curtain wall not found");

            using (var tx = new Transaction(doc, "Change Curtain Wall Type"))
            {
                tx.Start();
                wall.ChangeTypeId(new ElementId(typeId));
                tx.Commit();
            }

            return new
            {
                ok = true,
                newTypeId = typeId
            };
        }
    }
}
