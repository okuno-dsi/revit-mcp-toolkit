// ================================================================
// File: Commands/Room/GetRoomsCommand.cs  (UnitHelper経由・集約)
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
    public class GetRoomsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_rooms";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
                return new { ok = false, msg = "アクティブドキュメントが見つかりません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            int skip = Math.Max(0, p.Value<int?>("skip") ?? 0);
            int count = p.Value<int?>("count") ?? int.MaxValue;

            string levelFilter = p.Value<string>("level");
            string nameContains = p.Value<string>("nameContains");
            bool compat = p.Value<bool?>("compat") ?? false;

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            var allRooms = collector.OfType<Autodesk.Revit.DB.Architecture.Room>().ToList();

            string GetLevelName(ElementId levelId)
            {
                if (levelId == ElementId.InvalidElementId) return string.Empty;
                return (doc.GetElement(levelId) as Level)?.Name ?? string.Empty;
            }

            IEnumerable<Autodesk.Revit.DB.Architecture.Room> filtered = allRooms;

            if (!string.IsNullOrEmpty(levelFilter))
                filtered = filtered.Where(r => string.Equals(GetLevelName(r.LevelId), levelFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(nameContains))
                filtered = filtered.Where(r =>
                {
                    var n = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty;
                    return n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });

            var filteredList = filtered.ToList();
            int totalCount = filteredList.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
            {
                return new
                {
                    ok = true,
                    totalCount,
                    units = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { length = "ft", area = "ft2" }
                };
            }

            if (skip >= totalCount)
                return new { ok = true, totalCount, rooms = Array.Empty<object>(), units = UnitHelper.DefaultUnitsMeta() };

            int take = Math.Max(0, Math.Min(count, totalCount - skip));
            var page = filteredList.Skip(skip).Take(take).ToList();

            var rooms = new List<object>(page.Count);
            var roomsById = compat ? new Dictionary<int, object>(page.Count) : null;
            var errors = new List<object>();

            foreach (var r in page)
            {
                try
                {
                    string name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? (r.Name ?? string.Empty);

                    double areaM2 = 0.0;
                    try { areaM2 = Math.Round(UnitHelper.InternalToSqm(r.Area), 2); } catch { }

                    string levelName = GetLevelName(r.LevelId);

                    string state;
                    if (r.Location == null) state = "Unplaced";
                    else if (areaM2 <= 0.0) state = "NotEnclosed";
                    else state = "Placed";

                    double x = 0, y = 0, z = 0;
                    if (r.Location is LocationPoint lp && lp.Point != null)
                    {
                        x = Math.Round(UnitHelper.FtToMm(lp.Point.X), 3);
                        y = Math.Round(UnitHelper.FtToMm(lp.Point.Y), 3);
                        z = Math.Round(UnitHelper.FtToMm(lp.Point.Z), 3);
                    }
                    else
                    {
                        var bb = r.get_BoundingBox(null);
                        if (bb != null)
                        {
                            var c = (bb.Min + bb.Max) * 0.5;
                            x = Math.Round(UnitHelper.FtToMm(c.X), 3);
                            y = Math.Round(UnitHelper.FtToMm(c.Y), 3);
                            z = Math.Round(UnitHelper.FtToMm(c.Z), 3);
                        }
                    }

                    int elementId = r.Id.IntegerValue;
                    string uniqueId = r.UniqueId ?? string.Empty;

                    var item = new
                    {
                        elementId,
                        uniqueId,
                        name,
                        level = levelName,
                        area = areaM2,
                        state,
                        center = new { x, y, z }
                    };

                    if (compat)
                    {
                        var compatItem = new
                        {
                            elementId,
                            uniqueId,
                            name,
                            level = levelName,
                            area = areaM2,
                            state,
                            center = new { x, y, z },
                            id = elementId,
                            Center = new { X = x, Y = y, Z = z }
                        };
                        rooms.Add(compatItem);
                        roomsById![elementId] = compatItem;
                    }
                    else
                    {
                        rooms.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new { elementId = r?.Id?.IntegerValue ?? 0, message = ex.Message });
                }
            }

            var baseResp = new
            {
                ok = true,
                totalCount,
                rooms,
                units = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { length = "ft", area = "ft2" },
                issues = new { failures = Array.Empty<string>(), dialogs = Array.Empty<string>(), itemErrors = errors }
            };

            if (compat)
            {
                return new
                {
                    baseResp.ok,
                    baseResp.totalCount,
                    baseResp.rooms,
                    roomsById,
                    baseResp.units,
                    baseResp.internalUnits,
                    baseResp.issues
                };
            }
            return baseResp;
        }
    }
}

