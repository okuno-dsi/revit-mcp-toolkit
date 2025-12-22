using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class HighlightOverlargePanelsCommand : IRevitCommandHandler
    {
        public string CommandName => "highlight_overlarge_panels";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int wallId = (int)p["elementId"]!;
            double maxW = (double)p["maxWidthMm"]!;
            double maxH = (double)p["maxHeightMm"]!;

            var uiDoc = uiapp.ActiveUIDocument;
            var doc = uiDoc.Document;
            var view = uiDoc.ActiveView;
            var wall = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wallId)) as Autodesk.Revit.DB.Wall
                       ?? throw new InvalidOperationException("Curtain wall not found");
            var grid = wall.CurtainGrid
                       ?? throw new InvalidOperationException("Curtain grid not found");

            using (var tx = new Transaction(doc, "Highlight Overlarge Panels"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings()
                              .SetSurfaceTransparency(50)
                              .SetProjectionLineColor(new Color(255, 0, 0));

                foreach (var (id, idx) in grid.GetPanelIds().Select((id, i) => (id, i)))
                {
                    var panel = doc.GetElement(id);
                    var bb = panel?.get_BoundingBox(null);
                    if (bb == null) continue;

                    double w = UnitHelper.InternalToMm(bb.Max.X - bb.Min.X);
                    double h = UnitHelper.InternalToMm(bb.Max.Y - bb.Min.Y);

                    if (w > maxW || h > maxH)
                    {
                        view.SetElementOverrides(id, ogs);
                    }
                }
                tx.Commit();
            }
            return new { ok = true };
        }
    }
}

