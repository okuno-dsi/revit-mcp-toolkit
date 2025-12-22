using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class ExportCurtainWallReportCommand : IRevitCommandHandler
    {
        public string CommandName => "export_curtain_wall_report";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int wallId = (int)p["elementId"]!;
            string outPath = (string)p["outputPath"]!;

            var doc = uiapp.ActiveUIDocument.Document;
            var wall = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wallId)) as Autodesk.Revit.DB.Wall
                       ?? throw new InvalidOperationException("Curtain wall not found");
            var grid = wall.CurtainGrid
                       ?? throw new InvalidOperationException("Curtain grid not found");

            // ガラス面積 (m²) — BBoxのX,Y寸法から近似、ft²→m²へ
            double glassAreaM2 = grid.GetPanelIds()
                .Select(id =>
                {
                    var panel = doc.GetElement(id);
                    var bb = panel?.get_BoundingBox(null);
                    if (bb == null) return 0.0;
                    double dx_ft = bb.Max.X - bb.Min.X;
                    double dy_ft = bb.Max.Y - bb.Min.Y;
                    return UnitHelper.InternalToSqm(dx_ft * dy_ft);
                })
                .Sum();

            // マリオン長さ (m) — ft→mm→m
            double mullionLengthM = grid.GetMullionIds()
                .Select(id =>
                {
                    var mull = doc.GetElement(id) as Mullion;
                    var crv = (mull?.Location as LocationCurve)?.Curve;
                    if (crv == null) return 0.0;
                    return UnitHelper.FtToMm(crv.Length) / 1000.0;
                })
                .Sum();

            var report = new
            {
                ok = true,
                glassArea = Math.Round(glassAreaM2, 3),
                mullionLength = Math.Round(mullionLengthM, 3)
            };
            File.WriteAllText(outPath, JsonConvert.SerializeObject(report, Formatting.Indented));

            return new { ok = true, path = outPath };
        }
    }
}

