#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class ValidateCreateRoomCommand : IRevitCommandHandler
    {
        public string CommandName => "validate_create_room";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            if (!InputPointReader.TryReadXYMm(p, out var xMm, out var yMm))
                return new { ok = false, msg = "x, y (mm) are required." };

            var xyz = new XYZ(UnitHelper.MmToFt(xMm), UnitHelper.MmToFt(yMm), 0.0);

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Autodesk.Revit.DB.Architecture.Room>()
                .ToList();

            foreach (var r in rooms)
            {
                try
                {
                    if (r.IsPointInRoom(xyz))
                    {
                        return new
                        {
                            ok = true,
                            wouldCreate = false,
                            reason = "ALREADY_HAS_ROOM",
                            existingRoomId = r.Id.IntValue(),
                            level = (doc.GetElement(r.LevelId) as Level)?.Name ?? string.Empty
                        };
                    }
                }
                catch { }
            }

            return new { ok = true, wouldCreate = true };
        }
    }
}
