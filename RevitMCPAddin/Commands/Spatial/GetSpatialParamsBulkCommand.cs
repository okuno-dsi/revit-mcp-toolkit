// ================================================================
// File: Commands/Spatial/GetSpatialParamsBulkCommand.cs
// Target : Revit 2023 / .NET Framework 4.8 / C# 8
// Purpose: Bulk parameter fetch for Room / Space / Area
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitRoom = Autodesk.Revit.DB.Architecture.Room;
using RevitArea = Autodesk.Revit.DB.Area;
using RevitSpace = Autodesk.Revit.DB.Mechanical.Space;

namespace RevitMCPAddin.Commands.Spatial
{
    public class GetSpatialParamsBulkCommand : IRevitCommandHandler
    {
        public string CommandName => "get_spatial_params_bulk";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, message = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());

            var kinds = ReadKinds(p);
            if (kinds.Count == 0)
                kinds.Add("room"); // safe default

            var elementIds = ReadElementIds(p);
            bool fetchAll = p.Value<bool?>("all") ?? (elementIds.Count == 0);

            int elementSkip = p.Value<int?>("elementSkip") ?? p.Value<int?>("skip") ?? 0;
            int elementCount = p.Value<int?>("elementCount") ?? p.Value<int?>("count") ?? int.MaxValue;
            int paramSkip = p.Value<int?>("paramSkip") ?? 0;
            int paramCount = p.Value<int?>("paramCount") ?? int.MaxValue;

            var mode = UnitHelper.ResolveUnitsMode(doc, p);

            var elements = new List<(string kind, Element elem)>();

            if (fetchAll)
            {
                foreach (var kind in kinds)
                    elements.AddRange(CollectAllByKind(doc, kind));
            }
            else
            {
                foreach (var id in elementIds)
                {
                    var elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id));
                    if (elem == null) continue;
                    var kind = DetectKind(elem);
                    if (!string.IsNullOrEmpty(kind) && kinds.Contains(kind))
                        elements.Add((kind, elem));
                }
            }

            int totalCount = elements.Count;
            var sliced = elements.Skip(elementSkip).Take(elementCount).ToList();

            var items = new List<object>(sliced.Count);
            foreach (var it in sliced)
            {
                var elem = it.elem;
                var allParams = elem.Parameters
                    .Cast<Parameter>()
                    .Select(pa => UnitHelper.MapParameter(pa, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3))
                    .ToList();

                int totalParams = allParams.Count;
                var parameters = allParams.Skip(paramSkip).Take(paramCount).ToList();

                TryResolveLevelInfo(doc, it.kind, elem, out int? levelId, out string levelName);

                items.Add(new
                {
                    kind = it.kind,
                    elementId = elem.Id.IntValue(),
                    uniqueId = elem.UniqueId,
                    name = elem.Name,
                    levelId,
                    levelName,
                    totalParams,
                    parameters
                });
            }

            return new
            {
                ok = true,
                totalCount,
                elementSkip,
                elementCount = sliced.Count,
                paramSkip,
                paramCount,
                units = UnitHelper.DefaultUnitsMeta(),
                mode = mode.ToString(),
                items
            };
        }

        private static List<string> ReadKinds(JObject p)
        {
            var kinds = new List<string>();
            var kindSingle = p.Value<string>("kind");
            if (!string.IsNullOrWhiteSpace(kindSingle))
                kinds.Add(NormalizeKind(kindSingle));

            if (p["kinds"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    var s = t?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(s))
                        kinds.Add(NormalizeKind(s));
                }
            }

            return kinds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<int> ReadElementIds(JObject p)
        {
            var ids = new List<int>();
            if (p["elementIds"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (t == null) continue;
                    int id;
                    if (int.TryParse(t.ToString(), out id))
                        ids.Add(id);
                }
            }
            return ids;
        }

        private static string NormalizeKind(string kind)
        {
            var k = kind.Trim().ToLowerInvariant();
            if (k == "rooms") k = "room";
            if (k == "spaces") k = "space";
            if (k == "areas") k = "area";
            return k;
        }

        private static string DetectKind(Element elem)
        {
            if (elem is RevitRoom) return "room";
            if (elem is RevitSpace) return "space";
            if (elem is RevitArea) return "area";
            return null;
        }

        private static IEnumerable<(string kind, Element elem)> CollectAllByKind(Document doc, string kind)
        {
            BuiltInCategory bic;
            switch (NormalizeKind(kind))
            {
                case "room":
                    bic = BuiltInCategory.OST_Rooms;
                    break;
                case "space":
                    bic = BuiltInCategory.OST_MEPSpaces;
                    break;
                case "area":
                    bic = BuiltInCategory.OST_Areas;
                    break;
                default:
                    yield break;
            }

            var elems = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in elems)
            {
                var k = DetectKind(e);
                if (!string.IsNullOrEmpty(k))
                    yield return (k, e);
            }
        }

        private static void TryResolveLevelInfo(Document doc, string kind, Element elem, out int? levelId, out string levelName)
        {
            levelId = null;
            levelName = null;

            switch (NormalizeKind(kind))
            {
                case "room":
                    var room = elem as RevitRoom;
                    if (room?.LevelId == null || room.LevelId == ElementId.InvalidElementId) return;
                    levelId = room.LevelId.IntValue();
                    levelName = (doc.GetElement(room.LevelId) as Level)?.Name;
                    break;
                case "space":
                    var space = elem as RevitSpace;
                    if (space?.LevelId == null || space.LevelId == ElementId.InvalidElementId) return;
                    levelId = space.LevelId.IntValue();
                    levelName = (doc.GetElement(space.LevelId) as Level)?.Name;
                    break;
                case "area":
                    var area = elem as RevitArea;
                    if (area?.LevelId == null || area.LevelId == ElementId.InvalidElementId) return;
                    levelId = area.LevelId.IntValue();
                    levelName = (doc.GetElement(area.LevelId) as Level)?.Name;
                    break;
                default:
                    return;
            }
        }
    }
}
