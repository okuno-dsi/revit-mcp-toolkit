#nullable enable
// ================================================================
// File: Commands/AnnotationOps/SetTextCommand.cs
// Purpose : Update TextNote.Text for a given elementId
// Params  : { elementId:int, text:string, refreshView?:bool }
// ================================================================
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class SetTextCommand : IRevitCommandHandler
    {
        public string CommandName => "set_text";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject?)cmd.Params ?? new JObject();
            int elementId = p.Value<int?>("elementId") ?? 0;
            if (elementId <= 0) return new { ok = false, msg = "elementId required." };
            bool refreshView = p.Value<bool?>("refreshView") ?? false;
            string text = (p.Value<string>("text") ?? "").Trim();

            var tn = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId)) as TextNote;
            if (tn == null) return new { ok = false, msg = $"TextNote not found: {elementId}" };

            using (var t = new Transaction(doc, "set_text"))
            {
                try
                {
                    t.Start();
                    try { TxnUtil.ConfigureProceedWithWarnings(t); } catch { }
                    tn.Text = text;
                    t.Commit();
                }
                catch (System.Exception ex)
                {
                    if (t.HasStarted()) t.RollBack();
                    return new { ok = false, msg = "Failed to set text: " + ex.Message };
                }
            }

            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return new { ok = true, elementId = tn.Id.IntValue(), len = text.Length };
        }
    }
}
