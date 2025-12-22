using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Paint
{
    public class ApplyPaintCommand : IRevitCommandHandler
    {
        public string CommandName => "apply_paint";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                int elementId = p.Value<int>("elementId");
                int faceIndex = p.Value<int>("faceIndex");
                int materialId = p.Value<int>("materialId");

                PaintHelper.ApplyPaint(
                    doc,
                    Autodesk.Revit.DB.ElementIdCompat.From(elementId),
                    faceIndex,
                    Autodesk.Revit.DB.ElementIdCompat.From(materialId)
                );

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

