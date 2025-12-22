// ================================================================
// File: Commands/DatumOps/UpdateLevelNameCommand.cs
// ================================================================
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.DatumOps
{
    public class UpdateLevelNameCommand : IRevitCommandHandler
    {
        public string CommandName => "update_level_name";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());
            var id = Autodesk.Revit.DB.ElementIdCompat.From((int)p.Value<long>("levelId"));
            var newNm = p.Value<string>("name") ?? string.Empty;

            using (var tx = new Transaction(doc, "Update Level Name"))
            {
                tx.Start();
                var lvl = doc.GetElement(id) as Level
                          ?? throw new InvalidOperationException($"Level not found: {id}");
                lvl.Name = newNm;
                tx.Commit();

                return new { ok = true };
            }
        }
    }
}

