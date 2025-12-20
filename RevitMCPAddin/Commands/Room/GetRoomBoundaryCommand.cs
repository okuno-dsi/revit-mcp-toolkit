// ================================================================
// File: Commands/Room/GetRoomBoundaryCommand.cs  (UnitHelper統一版)
// Revit 2023 / .NET Framework 4.8
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class GetRoomBoundaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_boundary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("elementId", out var eidToken))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            int elementId = eidToken.Value<int>();

            var room = doc.GetElement(new ElementId(elementId)) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return new { ok = false, message = $"Room not found: {elementId}" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };
            var boundary = room.GetBoundarySegments(options);
            int totalCount = boundary.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, totalCount, boundaryLocation = options.SpatialElementBoundaryLocation.ToString(), units = UnitHelper.DefaultUnitsMeta() };

            var loops = boundary
                .Skip(skip)
                .Take(count)
                .Select((segs, idx) => new
                {
                    loopIndex = skip + idx,
                    segments = segs.Select(bs =>
                    {
                        var c = bs.GetCurve();
                        var p0 = c.GetEndPoint(0);
                        var p1 = c.GetEndPoint(1);
                        return new
                        {
                            start = new
                            {
                                x = Math.Round(UnitHelper.FtToMm(p0.X), 3),
                                y = Math.Round(UnitHelper.FtToMm(p0.Y), 3),
                                z = Math.Round(UnitHelper.FtToMm(p0.Z), 3)
                            },
                            end = new
                            {
                                x = Math.Round(UnitHelper.FtToMm(p1.X), 3),
                                y = Math.Round(UnitHelper.FtToMm(p1.Y), 3),
                                z = Math.Round(UnitHelper.FtToMm(p1.Z), 3)
                            }
                        };
                    }).ToList()
                })
                .ToList();

            return new { ok = true, totalCount, boundaryLocation = options.SpatialElementBoundaryLocation.ToString(), loops, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}
