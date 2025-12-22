// ================================================================
// File: Commands/Room/GetRoomBoundaryWallsCommand.cs
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
    public class GetRoomBoundaryWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_boundary_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("elementId", out var eidToken))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            int roomId = eidToken.Value<int>();

            var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(roomId)) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return new { ok = false, message = $"Room not found: {roomId}" };

            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };
            var boundaryLoops = room.GetBoundarySegments(options);

            var wallIds = boundaryLoops
                .SelectMany(loop => loop)
                .Select(bs => bs.ElementId)
                .Distinct()
                .Where(id => doc.GetElement(id) is Wall)
                .Select(id => id.IntValue())
                .ToList();

            return new { ok = true, boundaryLocation = options.SpatialElementBoundaryLocation.ToString(), wallIds };
        }
    }
}


