// File: RevitMCPAddin/Commands/ElementOps/CurtainWall/CheckPanelSizeCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class CheckPanelSizeCommand : IRevitCommandHandler
    {
        public string CommandName => "check_panel_size";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int wallId = (int)p["elementId"]!;
            double maxWidthMm = (double)p["maxWidthMm"]!;
            double maxHeightMm = (double)p["maxHeightMm"]!;

            var doc = uiapp.ActiveUIDocument.Document;
            var wall = doc.GetElement(new ElementId(wallId)) as Autodesk.Revit.DB.Wall
                       ?? throw new InvalidOperationException("Curtain wall not found");
            var grid = wall.CurtainGrid
                       ?? throw new InvalidOperationException("Curtain grid not found");

            var violations = new List<object>();

            foreach (var kv in grid.GetPanelIds().Select((id, idx) => (id, idx)))
            {
                var panel = doc.GetElement(kv.id);
                if (panel == null) continue;
                var bbox = panel.get_BoundingBox(null);
                if (bbox == null) continue;

                double widthMm = UnitHelper.InternalToMm(bbox.Max.X - bbox.Min.X);
                double heightMm = UnitHelper.InternalToMm(bbox.Max.Y - bbox.Min.Y);

                if (widthMm > maxWidthMm || heightMm > maxHeightMm)
                {
                    violations.Add(new
                    {
                        panelIndex = kv.idx,
                        width = Math.Round(widthMm, 2),
                        height = Math.Round(heightMm, 2)
                    });
                }
            }

            return new { ok = true, violations = violations };
        }
    }
}
