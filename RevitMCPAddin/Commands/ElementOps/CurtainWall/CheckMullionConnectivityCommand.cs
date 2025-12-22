using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class CheckMullionConnectivityCommand : IRevitCommandHandler
    {
        public string CommandName => "check_mullion_connectivity";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int wallId = (int)p["elementId"]!;

            var doc = uiapp.ActiveUIDocument.Document;
            var wall = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wallId)) as Autodesk.Revit.DB.
                Wall
                       ?? throw new InvalidOperationException("Curtain wall not found");
            var grid = wall.CurtainGrid
                       ?? throw new InvalidOperationException("Curtain grid not found");

            // U/V 方向のすべてのグリッド線 ID を取得 :contentReference[oaicite:2]{index=2}
            var gridLineIds = grid.GetUGridLineIds()
                                .Concat(grid.GetVGridLineIds());

            // 各グリッド線の既存セグメント曲線を全部集約 :contentReference[oaicite:3]{index=3}
            var gridLineCurves = gridLineIds
                .SelectMany(gid => {
                    var gl = doc.GetElement(gid) as CurtainGridLine;
                    return gl?.ExistingSegmentCurves
                             .Cast<Curve>()
                             ?? Enumerable.Empty<Curve>();
                })
                .ToList();

            var errors = new List<object>();

            foreach (var (id, idx) in grid.GetMullionIds().Select((id, i) => (id, i)))
            {
                var mullion = doc.GetElement(id) as Mullion;
                var locCurve = mullion?.Location as LocationCurve;
                if (locCurve?.Curve == null)
                {
                    errors.Add(new
                    {
                        mullionIndex = idx,
                        message = "LocationCurve not available"
                    });
                    continue;
                }

                var crv = locCurve.Curve;
                var start = crv.GetEndPoint(0);
                var end = crv.GetEndPoint(1);

                bool startOk = gridLineCurves.Any(gc => gc.Distance(start) < 1e-6);
                bool endOk = gridLineCurves.Any(gc => gc.Distance(end) < 1e-6);

                if (!startOk || !endOk)
                {
                    errors.Add(new
                    {
                        mullionIndex = idx,
                        message = !startOk
                                           ? "Start point not connected to any grid line"
                                           : "End point not connected to any grid line"
                    });
                }
            }

            return new
            {
                ok = true,
                errors = errors
            };
        }
    }
}

