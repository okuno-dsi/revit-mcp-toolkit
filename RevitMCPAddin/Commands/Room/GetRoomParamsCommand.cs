// ================================================================
// File: Commands/Room/GetRoomParamsCommand.cs  (UnitHelper)
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitRoom = Autodesk.Revit.DB.Architecture.Room;

namespace RevitMCPAddin.Commands.Room
{
    public class GetRoomParamsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_params";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("roomId", out var idToken))
                throw new InvalidOperationException("Parameter 'roomId' is required.");
            int roomId = idToken.Value<int>();

            var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(roomId)) as RevitRoom
                       ?? throw new InvalidOperationException($"Room not found: {roomId}");

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            // unitsMode: SI | Project | Raw | Both  iw SIj
            var mode = UnitHelper.ResolveUnitsMode(doc, p);

            var allParams = room.Parameters
                .Cast<Parameter>()
                .Select(pa => UnitHelper.MapParameter(pa, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3))
                .ToList();

            int totalCount = allParams.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, totalCount, units = UnitHelper.DefaultUnitsMeta(), mode = mode.ToString() };

            var parameters = allParams.Skip(skip).Take(count).ToList();
            return new { ok = true, totalCount, parameters, units = UnitHelper.DefaultUnitsMeta(), mode = mode.ToString() };
        }
    }
}

