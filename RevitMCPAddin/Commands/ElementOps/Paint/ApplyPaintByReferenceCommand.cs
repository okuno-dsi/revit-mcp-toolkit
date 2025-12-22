using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Paint
{
    public class ApplyPaintByReferenceCommand : IRevitCommandHandler
    {
        public string CommandName => "apply_paint_by_reference";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                int elementId = p.Value<int>("elementId");
                string faceStableReference = p.Value<string>("faceStableReference");
                int materialId = p.Value<int>("materialId");

                var faceRef = Reference.ParseFromStableRepresentation(doc, faceStableReference);
                var face = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId))
                              .GetGeometryObjectFromReference(faceRef) as Autodesk.Revit.DB.Face;

                if (face == null) return new { ok = false, msg = "Face が取得できませんでした。" };

                using (var tx = new Transaction(doc, "Apply Paint"))
                {
                    tx.Start();
                    doc.Paint(Autodesk.Revit.DB.ElementIdCompat.From(elementId), face, Autodesk.Revit.DB.ElementIdCompat.From(materialId));
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
                return new { ok = false, msg = $"Apply Paint 失敗: {ex.Message}" };
            }
        }
    }
}

