using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class DeleteCurtainWallCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_curtain_wall";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            int id = (int)((JObject)cmd.Params)["elementId"]!;

            var doc = uiapp.ActiveUIDocument.Document;
            using (var tx = new Transaction(doc, "Delete Curtain Wall"))
            {
                tx.Start();
                doc.Delete(new ElementId(id));
                tx.Commit();
            }

            return new { ok = true };
        }
    }
}
