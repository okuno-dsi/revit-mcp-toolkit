// ================================================================
// File: Commands/AnnotationOps/DeleteTextNoteCommand.cs
// Purpose : Delete a TextNote
// Params  : { elementId:int }
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class DeleteTextNoteCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_text_note";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int elementId = p.Value<int?>("elementId") ?? 0;
            if (elementId <= 0) return new { ok = false, msg = "elementId required." };

            var tn = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId)) as TextNote;
            if (tn == null) return new { ok = false, msg = $"TextNote not found: {elementId}" };

            using (var t = new Transaction(doc, "delete_text_note"))
            {
                t.Start();
                doc.Delete(tn.Id);
                t.Commit();
                return new { ok = true, deleted = elementId };
            }
        }
    }
}

