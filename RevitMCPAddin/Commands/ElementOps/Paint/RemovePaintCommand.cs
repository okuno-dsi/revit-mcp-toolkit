using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Paint
{
    public class RemovePaintCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_paint";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int elementId = p.Value<int>("elementId");
            int faceIndex = p.Value<int>("faceIndex");

            PaintHelper.RemovePaint(
                doc,
                new ElementId(elementId),
                faceIndex
            );

            return new { ok = true };
        }
    }
}
