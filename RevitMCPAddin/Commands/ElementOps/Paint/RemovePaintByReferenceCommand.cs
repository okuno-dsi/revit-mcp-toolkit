using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Paint
{
    public class RemovePaintByReferenceCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_paint_by_reference";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                int elementId = p.Value<int>("elementId");
                string faceStableReference = p.Value<string>("faceStableReference");

                var faceRef = Reference.ParseFromStableRepresentation(doc, faceStableReference);
                var face = doc.GetElement(new ElementId(elementId))
                              .GetGeometryObjectFromReference(faceRef) as Autodesk.Revit.DB.Face;

                if (face == null) return new { ok = false, msg = "Face が取得できませんでした。" };

                using (var tx = new Transaction(doc, "Remove Paint"))
                {
                    tx.Start();
                    doc.RemovePaint(new ElementId(elementId), face);
                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = $"Remove Paint 失敗: {ex.Message}" };
            }
        }
    }
}
