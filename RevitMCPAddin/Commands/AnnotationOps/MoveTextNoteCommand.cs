// ================================================================
// File: Commands/AnnotationOps/MoveTextNoteCommand.cs
// Purpose : Move a TextNote by offset vector (project units aware)
// Params  : { elementId:int, dx:number, dy:number, dz?:number, unit?:"mm|cm|m|in|ft" }
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class MoveTextNoteCommand : IRevitCommandHandler
    {
        public string CommandName => "move_text_note";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int elementId = p.Value<int?>("elementId") ?? 0;
            if (elementId <= 0) return new { ok = false, msg = "elementId required." };

            string? unitOpt = p.Value<string>("unit");
            double dx = p.Value<double?>("dx") ?? 0.0;
            double dy = p.Value<double?>("dy") ?? 0.0;
            double dz = p.Value<double?>("dz") ?? 0.0;

            var tn = doc.GetElement(new ElementId(elementId)) as TextNote;
            if (tn == null) return new { ok = false, msg = $"TextNote not found: {elementId}" };

            var v = new XYZ(
                TextNoteUnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, dx, unitOpt),
                TextNoteUnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, dy, unitOpt),
                TextNoteUnitsHelper.ToInternalByProjectUnits(doc, SpecTypeId.Length, dz, unitOpt)
            );

            using (var t = new Transaction(doc, "move_text_note"))
            {
                t.Start();
                ElementTransformUtils.MoveElement(doc, tn.Id, v);
                t.Commit();
                return new { ok = true, elementId = tn.Id.IntegerValue };
            }
        }
    }
}
