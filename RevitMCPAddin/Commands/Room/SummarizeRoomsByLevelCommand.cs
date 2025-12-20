#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class SummarizeRoomsByLevelCommand : IRevitCommandHandler
    {
        public string CommandName => "summarize_rooms_by_level";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.Room>()
                .Where(r => r != null && r.Area > 1e-6)
                .ToList();

            var byLevel = new Dictionary<string, (int count, double areaM2)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rooms)
            {
                var name = r.Level != null ? r.Level.Name : "(No level)";
                var m2 = UnitUtils.ConvertFromInternalUnits(r.Area, UnitTypeId.SquareMeters);
                if (!byLevel.TryGetValue(name, out var cur)) cur = (0, 0.0);
                cur.count += 1; cur.areaM2 += m2;
                byLevel[name] = cur;
            }

            var items = byLevel.Select(kv => new {
                    levelName = kv.Key,
                    rooms = kv.Value.count,
                    totalAreaM2 = Math.Round(kv.Value.areaM2, 2)
                })
                .OrderBy(k => k.levelName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new {
                ok = true,
                items = items,
                totalRooms = rooms.Count,
                totalAreaM2 = Math.Round(items.Sum(i => i.totalAreaM2), 2)
            };
        }
    }
}

