// ======================================================================
// File   : Commands/Room/GetRoomFinishTakeoffContextCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Command: get_room_finish_takeoff_context
// Purpose:
//   Collect a room "finish takeoff context" bundle for downstream quantity
//   extraction:
//   - room info + height (ceiling/unbounded height)
//   - boundary segments (with element linkage + segment length)
//   - nearby walls matched to boundary segments (2D geometry analysis)
//   - columns inside/near the room
//   - door/window inserts hosted by the matched walls (grouped per wall)
//
// Notes:
//   - Uses mm for coordinates/length and m2 for area in the response.
//   - Optionally toggles "Room Bounding" for specified/auto-detected columns
//     inside a TransactionGroup that is always rolled back.
// ======================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class GetRoomFinishTakeoffContextCommand : IRevitCommandHandler
    {
        public string CommandName => "get_room_finish_takeoff_context";

        // Default: "takeoff objects" (not drafting/reference lines).
        // Used only when includeInteriorElements=true and interiorCategories is omitted/empty.
        private static readonly BuiltInCategory[] DefaultInteriorElementCategoriesForTakeoff = new[]
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_FurnitureSystems,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_SpecialityEquipment,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
        };

        private sealed class BoundarySegmentInfo
        {
            public int LoopIndex { get; }
            public int SegmentIndex { get; }
            public GeometryUtils.Segment2 Seg2 { get; }

            public BoundarySegmentInfo(int loopIndex, int segmentIndex, GeometryUtils.Segment2 seg2)
            {
                LoopIndex = loopIndex;
                SegmentIndex = segmentIndex;
                Seg2 = seg2;
            }
        }

        private sealed class WallMatch
        {
            public int WallId { get; set; }
            public string UniqueId { get; set; } = string.Empty;
            public int TypeId { get; set; }
            public string TypeName { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;

            public List<(int loopIndex, int segmentIndex)> Segments { get; } =
                new List<(int loopIndex, int segmentIndex)>();

            public double MinDistanceMm { get; set; } = double.MaxValue;
            public double MaxOverlapMm { get; set; } = 0.0;
            public XYZ StartFt { get; set; } = XYZ.Zero;
            public XYZ EndFt { get; set; } = XYZ.Zero;
            public XYZ Orientation { get; set; } = XYZ.Zero;
        }

        private sealed class WallMatchResult
        {
            public object[] Walls { get; set; } = Array.Empty<object>();
            public List<Autodesk.Revit.DB.Wall> MatchedWallElements { get; set; } =
                new List<Autodesk.Revit.DB.Wall>();
            public object? Debug { get; set; }
        }

        private sealed class WallTypeLayersDto
        {
            public int typeId { get; set; }
            public string typeName { get; set; } = string.Empty;
            public string kind { get; set; } = string.Empty;
            public double widthMm { get; set; }
            public object[] layers { get; set; } = Array.Empty<object>();
        }

        private sealed class SegmentOutRef
        {
            public int LoopIndex { get; }
            public int SegmentIndex { get; }
            public XYZ StartFt { get; }
            public XYZ EndFt { get; }
            public Dictionary<string, object>? Out { get; }

            public SegmentOutRef(
                int loopIndex,
                int segmentIndex,
                XYZ startFt,
                XYZ endFt,
                Dictionary<string, object>? outObj)
            {
                LoopIndex = loopIndex;
                SegmentIndex = segmentIndex;
                StartFt = startFt;
                EndFt = endFt;
                Out = outObj;
            }

            public string Key => string.Concat(LoopIndex.ToString(), ":", SegmentIndex.ToString());
        }

        private sealed class BBoxCandidate
        {
            public int ElementId { get; set; }
            public string UniqueId { get; set; } = string.Empty;
            public int TypeId { get; set; }
            public string TypeName { get; set; } = string.Empty;
            public string CategoryName { get; set; } = string.Empty;
            public int LevelId { get; set; }
            public string LevelName { get; set; } = string.Empty;
            public BoundingBoxXYZ? BBox { get; set; }
            public double HeightFromRoomLevelMm { get; set; }
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)(cmd.Params ?? new JObject());

            // ----------------------------
            // Resolve target room
            // ----------------------------
            Autodesk.Revit.DB.Architecture.Room? room = null;
            int roomId = 0;

            if (p.TryGetValue("roomId", out var roomIdToken) && roomIdToken.Type == JTokenType.Integer)
            {
                roomId = roomIdToken.Value<int>();
                if (roomId > 0)
                {
                    room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(roomId)) as Autodesk.Revit.DB.Architecture.Room;
                }
            }

            bool fromSelection = p.Value<bool?>("fromSelection") ?? (room == null);
            if (room == null && fromSelection)
            {
                room = TryResolveSelectedRoom(doc, uidoc.Selection, out roomId);
            }

            if (room == null)
            {
                return ResultUtil.Err("roomId を指定するか、Room を選択してください。");
            }

            // ----------------------------
            // Options
            // ----------------------------
            bool includeIslands = p.Value<bool?>("includeIslands") ?? true;
            bool includeSegments = p.Value<bool?>("includeSegments") ?? true;

            string? boundaryLocationStr = p.Value<string>("boundaryLocation")
                ?? p.Value<string>("boundary_location")
                ?? "Finish";

            // Wall matching options
            bool includeWallMatches = p.Value<bool?>("includeWallMatches") ?? true;
            double wallMaxOffsetMm = p.Value<double?>("wallMaxOffsetMm") ?? 300.0;
            double wallMinOverlapMm = p.Value<double?>("wallMinOverlapMm") ?? 100.0;
            double wallMaxAngleDeg = p.Value<double?>("wallMaxAngleDeg") ?? 3.0;
            double wallSearchMarginMm = p.Value<double?>("wallSearchMarginMm") ?? 5000.0;

            // Inserts (doors/windows) options
            bool includeInserts = p.Value<bool?>("includeInserts") ?? true;

            // Wall type layer (reference) options
            bool includeWallTypeLayers = p.Value<bool?>("includeWallTypeLayers") ?? true;

            // Column detection options (for takeoff context; not required for wall matching)
            bool autoDetectColumns = p.Value<bool?>("autoDetectColumnsInRoom") ?? false;
            double searchMarginMm = p.Value<double?>("searchMarginMm") ?? 1000.0;

            // If columns are supplied (or auto-detected), we can optionally toggle Room Bounding
            // temporarily (rolled back) to reflect columns in Room boundary calculation.
            bool tempEnableRoomBoundingOnColumns = p.Value<bool?>("tempEnableRoomBoundingOnColumns") ?? true;

            // Column ↔ wall touch approximation (BoundingBox)
            double columnWallTouchMarginMm = p.Value<double?>("columnWallTouchMarginMm") ?? 50.0;

            // Floor/Ceiling context (for reporting; not required for wall matching)
            bool includeFloorCeilingInfo = p.Value<bool?>("includeFloorCeilingInfo") ?? true;
            double floorCeilingSearchMarginMm = p.Value<double?>("floorCeilingSearchMarginMm") ?? 1000.0;
            double segmentInteriorInsetMm = p.Value<double?>("segmentInteriorInsetMm") ?? 100.0;
            bool floorCeilingSameLevelOnly = p.Value<bool?>("floorCeilingSameLevelOnly") ?? true;

            // Interior elements in room (walls etc.)
            bool includeInteriorElements = p.Value<bool?>("includeInteriorElements") ?? false;
            double interiorElementSearchMarginMm = p.Value<double?>("interiorElementSearchMarginMm") ?? 1000.0;
            double interiorElementSampleStepMm = p.Value<double?>("interiorElementSampleStepMm") ?? 200.0;
            bool interiorElementPointBboxProbe = p.Value<bool?>("interiorElementPointBboxProbe") ?? true;
            var interiorCategories = ResolveCategories(
                p["interiorCategories"] as JArray,
                DefaultInteriorElementCategoriesForTakeoff);

            // Sanitize
            if (wallMaxOffsetMm < 0) wallMaxOffsetMm = 0;
            if (wallMinOverlapMm < 0) wallMinOverlapMm = 0;
            if (wallMaxAngleDeg < 0) wallMaxAngleDeg = 0;
            if (wallSearchMarginMm < 0) wallSearchMarginMm = 0;
            if (searchMarginMm < 0) searchMarginMm = 0;
            if (columnWallTouchMarginMm < 0) columnWallTouchMarginMm = 0;
            if (floorCeilingSearchMarginMm < 0) floorCeilingSearchMarginMm = 0;
            if (segmentInteriorInsetMm < 0) segmentInteriorInsetMm = 0;
            if (interiorElementSearchMarginMm < 0) interiorElementSearchMarginMm = 0;
            if (interiorElementSampleStepMm <= 0) interiorElementSampleStepMm = 200.0;

            // Column IDs (explicit list)
            var columnIds = new List<ElementId>();
            if (p.TryGetValue("columnIds", out var colArrayToken) && colArrayToken is JArray colArray)
            {
                foreach (var t in colArray)
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        int id = t.Value<int>();
                        if (id > 0) columnIds.Add(Autodesk.Revit.DB.ElementIdCompat.From(id));
                    }
                }
            }

            // Auto detect columns (if requested)
            IList<ElementId> autoDetectedColumns = new List<ElementId>();
            if (autoDetectColumns)
            {
                try
                {
                    autoDetectedColumns = AutoDetectColumnsInRoom(doc, room, searchMarginMm);
                    foreach (var eid in autoDetectedColumns)
                    {
                        if (!columnIds.Contains(eid)) columnIds.Add(eid);
                    }
                }
                catch
                {
                    autoDetectedColumns = new List<ElementId>();
                }
            }

            var boundaryOptions = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };

            // Height (for wall area estimation)
            var roomHeightInfo = TryGetRoomHeightMm(room);

            // Output buffers
            var loopsOut = new List<object>();
            var boundarySegmentsForWallMatch = (includeWallMatches ? new List<BoundarySegmentInfo>() : null);
            var segmentOutRefs = new List<SegmentOutRef>();

            double perimeterFt = 0.0;
            double wallPerimeterFt = 0.0;
            double columnPerimeterFt = 0.0;
            int wallBoundarySegmentCount = 0;
            int columnBoundarySegmentCount = 0;
            int otherBoundarySegmentCount = 0;

            var toggledColumnIds = new List<int>();

            // ----------------------------
            // Compute boundary (optionally with temporary column RoomBounding)
            // ----------------------------
            using (var tg = new TransactionGroup(doc, "Room finish takeoff context (temp)"))
            {
                tg.Start();

                if (tempEnableRoomBoundingOnColumns && columnIds.Count > 0)
                {
                    using (var t = new Transaction(doc, "Temp enable Room Bounding (columns)"))
                    {
                        t.Start();
                        foreach (var id in columnIds)
                        {
                            var e = doc.GetElement(id);
                            if (e == null) continue;

                            var pRoomBound = e.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                            if (pRoomBound != null && !pRoomBound.IsReadOnly)
                            {
                                try
                                {
                                    pRoomBound.Set(1);
                                    toggledColumnIds.Add(id.IntValue());
                                }
                                catch
                                {
                                    // ignore
                                }
                            }
                        }
                        t.Commit();
                    }

                    try { doc.Regenerate(); } catch { /* ignore */ }
                }

                IList<IList<BoundarySegment>>? boundaryLoops = null;
                try { boundaryLoops = room.GetBoundarySegments(boundaryOptions); }
                catch { boundaryLoops = null; }

                if (boundaryLoops != null)
                {
                    int loopIndex = 0;
                    foreach (var loop in boundaryLoops)
                    {
                        if (!includeIslands && loopIndex > 0) break;

                        var segObjs = new List<object>();
                        int segIndex = 0;

                        foreach (var bs in loop)
                        {
                            var curve = bs?.GetCurve();
                            if (curve == null)
                            {
                                segIndex++;
                                continue;
                            }

                            double lenFt = 0.0;
                            try { lenFt = curve.Length; } catch { lenFt = 0.0; }
                            perimeterFt += lenFt;

                            var p0 = curve.GetEndPoint(0);
                            var p1 = curve.GetEndPoint(1);

                            double x0mm = UnitHelper.FtToMm(p0.X);
                            double y0mm = UnitHelper.FtToMm(p0.Y);
                            double x1mm = UnitHelper.FtToMm(p1.X);
                            double y1mm = UnitHelper.FtToMm(p1.Y);

                            int boundaryElementId = 0;
                            string boundaryElementClass = string.Empty;
                            string boundaryCategoryName = string.Empty;
                            string boundaryKind = "Other";

                            try
                            {
                                var eid = bs.ElementId;
                                if (eid != null && eid != ElementId.InvalidElementId)
                                {
                                    boundaryElementId = eid.IntValue();
                                    var be = doc.GetElement(eid);
                                    boundaryElementClass = be?.GetType()?.Name ?? string.Empty;
                                    boundaryCategoryName = be?.Category?.Name ?? string.Empty;

                                    if (be is Autodesk.Revit.DB.Wall)
                                    {
                                        boundaryKind = "Wall";
                                        wallPerimeterFt += lenFt;
                                        wallBoundarySegmentCount++;
                                    }
                                    else if (IsColumnElement(be))
                                    {
                                        boundaryKind = "Column";
                                        columnPerimeterFt += lenFt;
                                        columnBoundarySegmentCount++;
                                    }
                                    else
                                    {
                                        boundaryKind = "Other";
                                        otherBoundarySegmentCount++;
                                    }
                                }
                                else
                                {
                                    otherBoundarySegmentCount++;
                                }
                            }
                            catch
                            {
                                otherBoundarySegmentCount++;
                            }

                            double lenMm = UnitHelper.FtToMm(lenFt);

                            Dictionary<string, object>? segOutObj = null;
                            if (includeSegments)
                            {
                                segOutObj = new Dictionary<string, object>
                                {
                                    ["loopIndex"] = loopIndex,
                                    ["segmentIndex"] = segIndex,
                                    ["boundaryElementId"] = boundaryElementId,
                                    ["boundaryKind"] = boundaryKind,
                                    ["boundaryElementClass"] = boundaryElementClass,
                                    ["boundaryCategoryName"] = boundaryCategoryName,
                                    ["curveType"] = curve.GetType().Name,
                                    ["lengthMm"] = Math.Round(lenMm, 3),
                                    ["start"] = new Dictionary<string, object>
                                    {
                                        ["x"] = Math.Round(x0mm, 3),
                                        ["y"] = Math.Round(y0mm, 3),
                                        ["z"] = Math.Round(UnitHelper.FtToMm(p0.Z), 3)
                                    },
                                    ["end"] = new Dictionary<string, object>
                                    {
                                        ["x"] = Math.Round(x1mm, 3),
                                        ["y"] = Math.Round(y1mm, 3),
                                        ["z"] = Math.Round(UnitHelper.FtToMm(p1.Z), 3)
                                    }
                                };

                                segObjs.Add(segOutObj);
                            }

                            segmentOutRefs.Add(new SegmentOutRef(loopIndex, segIndex, p0, p1, segOutObj));

                            if (boundarySegmentsForWallMatch != null)
                            {
                                // Skip very short segments to keep matching stable when columns are room-bounding.
                                if (lenMm >= Math.Max(1.0, wallMinOverlapMm * 0.5))
                                {
                                    var seg2 = new GeometryUtils.Segment2(
                                        new GeometryUtils.Vec2(x0mm, y0mm),
                                        new GeometryUtils.Vec2(x1mm, y1mm)
                                    );
                                    boundarySegmentsForWallMatch.Add(new BoundarySegmentInfo(loopIndex, segIndex, seg2));
                                }
                            }

                            segIndex++;
                        }

                        if (includeSegments)
                        {
                            loopsOut.Add(new { loopIndex, segments = segObjs });
                        }

                        loopIndex++;
                    }
                }

                // Always roll back (do not persist temporary RoomBounding toggles)
                tg.RollBack();
            }

            double perimeterMm = UnitHelper.FtToMm(perimeterFt);
            double wallPerimeterMm = UnitHelper.FtToMm(wallPerimeterFt);
            double columnPerimeterMm = UnitHelper.FtToMm(columnPerimeterFt);

            // Estimated wall area (to room height; openings not subtracted here)
            double? estWallAreaM2 = null;
            if (roomHeightInfo.heightMm.HasValue && roomHeightInfo.heightMm.Value > 1e-6)
            {
                estWallAreaM2 = (wallPerimeterMm / 1000.0) * (roomHeightInfo.heightMm.Value / 1000.0);
            }

            // ----------------------------
            // Wall matching
            // ----------------------------
            object? wallsOutObj = null;
            object? wallMatchDebug = null;
            var matchedWallElements = new List<Autodesk.Revit.DB.Wall>();

            if (includeWallMatches && boundarySegmentsForWallMatch != null && boundarySegmentsForWallMatch.Count > 0)
            {
                var wallResult = MatchWallsNearRoomBoundary(
                    doc,
                    room,
                    boundarySegmentsForWallMatch,
                    wallMaxOffsetMm,
                    wallMinOverlapMm,
                    wallMaxAngleDeg,
                    wallSearchMarginMm);

                wallsOutObj = wallResult.Walls;
                wallMatchDebug = wallResult.Debug;
                matchedWallElements = wallResult.MatchedWallElements ?? new List<Autodesk.Revit.DB.Wall>();
            }

            // Map segment -> walls (for quick lookup in Python/agent)
            var wallIdsBySegKey = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            if (wallsOutObj is object[] arrWalls)
            {
                // arrWalls contains anonymous objects created in MatchWallsNearRoomBoundary
                // -> use JObject conversion for safe extraction.
                foreach (var w in arrWalls)
                {
                    JObject jw;
                    try { jw = JObject.FromObject(w); }
                    catch { continue; }

                    int wid = jw.Value<int?>("wallId") ?? 0;
                    var segs = jw["segments"] as JArray;
                    if (wid <= 0 || segs == null) continue;
                    foreach (var seg in segs)
                    {
                        var o = seg as JObject;
                        if (o == null) continue;
                        int li = o.Value<int?>("loopIndex") ?? 0;
                        int si = o.Value<int?>("segmentIndex") ?? 0;
                        string key = $"{li}:{si}";
                        if (!wallIdsBySegKey.TryGetValue(key, out var list))
                        {
                            list = new List<int>();
                            wallIdsBySegKey[key] = list;
                        }
                        if (!list.Contains(wid)) list.Add(wid);
                    }
                }
            }

            // ----------------------------
            // Floor/Ceiling info (room context)
            // ----------------------------
            object? floorsOut = null;
            object? ceilingsOut = null;
            object? ceilingIdsBySegmentKeyOut = null;

            if (includeFloorCeilingInfo)
            {
                try
                {
                    var fc = CollectFloorCeilingContext(
                        doc,
                        room,
                        segmentOutRefs,
                        floorCeilingSearchMarginMm,
                        segmentInteriorInsetMm,
                        floorCeilingSameLevelOnly);
                    floorsOut = fc.floors;
                    ceilingsOut = fc.ceilings;
                    ceilingIdsBySegmentKeyOut = fc.ceilingIdsBySegmentKey;

                    // Also annotate segment objects for convenience (if segments are included)
                    foreach (var kv in fc.ceilingHeightsBySegmentKey)
                    {
                        string key = kv.Key;
                        var segRef = segmentOutRefs.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                        if (segRef?.Out == null) continue;
                        segRef.Out["ceilingHeightsFromRoomLevelMm"] = kv.Value.Select(x => Math.Round(x, 3)).ToArray();
                    }
                }
                catch
                {
                    floorsOut = null;
                    ceilingsOut = null;
                    ceilingIdsBySegmentKeyOut = null;
                }
            }

            // ----------------------------
            // Interior elements in room (walls etc.)
            // ----------------------------
            object? interiorElementsOut = null;
            if (includeInteriorElements)
            {
                try
                {
                    interiorElementsOut = CollectInteriorElementsInRoom(
                        doc,
                        room,
                        segmentOutRefs,
                        interiorCategories,
                        interiorElementSearchMarginMm,
                        interiorElementSampleStepMm,
                        interiorElementPointBboxProbe);
                }
                catch
                {
                    interiorElementsOut = new { ok = false, msg = "Failed to collect interior elements in room." };
                }
            }

            // ----------------------------
            // Inserts (doors/windows) hosted by matched walls
            // ----------------------------
            object? insertsByWallOut = null;
            object? insertsOut = null;

            if (includeInserts && matchedWallElements.Count > 0)
            {
                var insertDoorList = new List<object>();
                var insertWindowList = new List<object>();
                var perWall = new List<object>();

                foreach (var w in matchedWallElements)
                {
                    if (w == null) continue;

                    var doorIds = new List<int>();
                    var windowIds = new List<int>();
                    var allInsertIds = new List<int>();

                    ICollection<ElementId> insertIds;
                    try
                    {
                        insertIds = w.FindInserts(true, true, true, true);
                    }
                    catch
                    {
                        insertIds = new List<ElementId>();
                    }

                    foreach (var iid in insertIds)
                    {
                        if (iid == null || iid == ElementId.InvalidElementId) continue;
                        int iidInt = iid.IntValue();
                        if (iidInt <= 0) continue;
                        allInsertIds.Add(iidInt);

                        var e = doc.GetElement(iid);
                        if (e == null) continue;
                        var catId = e.Category?.Id;
                        int catInt = catId?.IntValue() ?? 0;

                        if (catInt == (int)BuiltInCategory.OST_Doors)
                        {
                            doorIds.Add(iidInt);
                            insertDoorList.Add(MapInsertInstance(doc, e, w.Id.IntValue(), "Door"));
                        }
                        else if (catInt == (int)BuiltInCategory.OST_Windows)
                        {
                            windowIds.Add(iidInt);
                            insertWindowList.Add(MapInsertInstance(doc, e, w.Id.IntValue(), "Window"));
                        }
                    }

                    perWall.Add(new
                    {
                        wallId = w.Id.IntValue(),
                        doorIds = doorIds.Distinct().OrderBy(x => x).ToArray(),
                        windowIds = windowIds.Distinct().OrderBy(x => x).ToArray(),
                        insertIds = allInsertIds.Distinct().OrderBy(x => x).ToArray()
                    });
                }

                insertsByWallOut = perWall;
                insertsOut = new
                {
                    doors = insertDoorList,
                    windows = insertWindowList
                };
            }

            // ----------------------------
            // Column info + column-wall touch map (approx)
            // ----------------------------
            object? columnsOut = null;
            if (columnIds.Count > 0)
            {
                var colInfos = new List<object>();
                var wallBbs = new Dictionary<int, BoundingBoxXYZ>();
                if (matchedWallElements.Count > 0)
                {
                    foreach (var w in matchedWallElements)
                    {
                        if (w == null) continue;
                        var bb = w.get_BoundingBox(null);
                        if (bb == null) continue;
                        wallBbs[w.Id.IntValue()] = bb;
                    }
                }

                double marginFt = UnitHelper.MmToFt(columnWallTouchMarginMm);

                foreach (var cid in columnIds)
                {
                    var e = doc.GetElement(cid);
                    if (e == null) continue;

                    var bb = e.get_BoundingBox(null);
                    var touched = new List<int>();
                    if (bb != null && wallBbs.Count > 0)
                    {
                        var bbExp = ExpandBoundingBox(bb, marginFt);
                        foreach (var kv in wallBbs)
                        {
                            if (BoundingBoxIntersectsXYAndZ(bbExp, kv.Value))
                                touched.Add(kv.Key);
                        }
                    }

                    var fi = e as FamilyInstance;
                    string typeName = string.Empty;
                    int typeId = 0;
                    try
                    {
                        typeId = e.GetTypeId()?.IntValue() ?? 0;
                        var sym = doc.GetElement(e.GetTypeId()) as FamilySymbol;
                        typeName = sym?.Name ?? string.Empty;
                    }
                    catch { /* ignore */ }

                    object? locationObj = null;
                    try
                    {
                        var lp = fi?.Location as LocationPoint;
                        var pt = lp?.Point;
                        if (pt != null)
                        {
                            locationObj = new
                            {
                                x = Math.Round(UnitHelper.FtToMm(pt.X), 3),
                                y = Math.Round(UnitHelper.FtToMm(pt.Y), 3),
                                z = Math.Round(UnitHelper.FtToMm(pt.Z), 3)
                            };
                        }
                    }
                    catch { /* ignore */ }

                    colInfos.Add(new
                    {
                        elementId = cid.IntValue(),
                        uniqueId = e.UniqueId ?? string.Empty,
                        category = e.Category?.Name ?? string.Empty,
                        typeId,
                        typeName,
                        touchedWallIds = touched.Distinct().OrderBy(x => x).ToArray(),
                        location = locationObj
                    });
                }

                columnsOut = colInfos;
            }

            // ----------------------------
            // WallType layers (reference-only)
            // ----------------------------
            object? wallTypeLayersOut = null;
            if (includeWallTypeLayers && matchedWallElements.Count > 0)
            {
                wallTypeLayersOut = BuildWallTypeLayers(doc, matchedWallElements);
            }

            // ----------------------------
            // Room info
            // ----------------------------
            var level = doc.GetElement(room.LevelId) as Level;
            string levelName = level?.Name ?? string.Empty;
            double levelElevMm = UnitHelper.FtToMm(level?.Elevation ?? 0.0);

            var baseOffset = TryGetParamMm(room, BuiltInParameter.ROOM_LOWER_OFFSET);

            var perimeterParam = TryGetParamMm(room, BuiltInParameter.ROOM_PERIMETER);
            string? perimeterParamDisplay = TryGetParamValueString(room, BuiltInParameter.ROOM_PERIMETER);

            object? roomLocationObj = null;
            try
            {
                var lp = room.Location as LocationPoint;
                var pt = lp?.Point;
                if (pt != null)
                {
                    roomLocationObj = new
                    {
                        x = Math.Round(UnitHelper.FtToMm(pt.X), 3),
                        y = Math.Round(UnitHelper.FtToMm(pt.Y), 3),
                        z = Math.Round(UnitHelper.FtToMm(pt.Z), 3)
                    };
                }
            }
            catch { /* ignore */ }

            return ResultUtil.Ok(new
            {
                room = new
                {
                    roomId = room.Id.IntValue(),
                    uniqueId = room.UniqueId ?? string.Empty,
                    name = room.Name ?? string.Empty,
                    number = room.Number ?? string.Empty,
                    levelId = room.LevelId.IntValue(),
                    levelName,
                    levelElevationMm = Math.Round(levelElevMm, 3),
                    baseOffsetMm = baseOffset.HasValue ? Math.Round(baseOffset.Value, 3) : (double?)null,
                    baseOffsetDisplay = TryGetParamValueString(room, BuiltInParameter.ROOM_LOWER_OFFSET) ?? string.Empty,
                    location = roomLocationObj
                },
                metrics = new
                {
                    roomHeightMm = roomHeightInfo.heightMm,
                    roomHeightSource = roomHeightInfo.source,
                    perimeterMm = Math.Round(perimeterMm, 3),
                    perimeterParamMm = perimeterParam.HasValue ? Math.Round(perimeterParam.Value, 3) : (double?)null,
                    perimeterParamDisplay = perimeterParamDisplay ?? string.Empty,
                    wallPerimeterMm = Math.Round(wallPerimeterMm, 3),
                    columnPerimeterMm = Math.Round(columnPerimeterMm, 3),
                    estWallAreaToCeilingM2 = estWallAreaM2.HasValue ? Math.Round(estWallAreaM2.Value, 6) : (double?)null,
                    boundarySegmentCounts = new
                    {
                        wall = wallBoundarySegmentCount,
                        column = columnBoundarySegmentCount,
                        other = otherBoundarySegmentCount
                    }
                },
                basis = new
                {
                    fromSelection,
                    includeIslands,
                    includeSegments,
                    boundaryLocation = boundaryOptions.SpatialElementBoundaryLocation.ToString(),
                    includeWallMatches,
                    wallMaxOffsetMm,
                    wallMinOverlapMm,
                    wallMaxAngleDeg,
                    wallSearchMarginMm,
                    includeInserts,
                    includeWallTypeLayers,
                    autoDetectColumnsInRoom = autoDetectColumns,
                    searchMarginMm,
                    tempEnableRoomBoundingOnColumns,
                    columnWallTouchMarginMm,
                    includeFloorCeilingInfo,
                    floorCeilingSearchMarginMm,
                    segmentInteriorInsetMm,
                    floorCeilingSameLevelOnly,
                    includeInteriorElements,
                    interiorElementSearchMarginMm,
                    interiorElementSampleStepMm,
                    interiorElementPointBboxProbe,
                    interiorCategories = interiorCategories.Select(x => x.ToString()).ToArray(),
                    autoDetectedColumnIds = autoDetectedColumns.Select(x => x.IntValue()).ToArray(),
                    toggledColumnIds = toggledColumnIds.ToArray()
                },
                loops = loopsOut,
                wallIdsBySegmentKey = wallIdsBySegKey,
                wallMatchDebug,
                walls = wallsOutObj,
                floors = floorsOut,
                ceilings = ceilingsOut,
                ceilingIdsBySegmentKey = ceilingIdsBySegmentKeyOut,
                interiorElements = interiorElementsOut,
                insertsByWall = insertsByWallOut,
                inserts = insertsOut,
                columns = columnsOut,
                wallTypeLayers = wallTypeLayersOut,
                units = new
                {
                    Length = "mm",
                    Area = "m2"
                }
            });
        }

        private static double? TryGetParamMm(Element e, BuiltInParameter bip)
        {
            if (e == null) return null;
            try
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double ft = 0.0;
                    try { ft = p.AsDouble(); } catch { ft = 0.0; }
                    return UnitHelper.FtToMm(ft);
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static string? TryGetParamValueString(Element e, BuiltInParameter bip)
        {
            if (e == null) return null;
            try
            {
                var p = e.get_Parameter(bip);
                if (p == null) return null;
                return p.AsValueString();
            }
            catch { return null; }
        }

        private static List<BuiltInCategory> ResolveCategories(JArray? categoriesToken, BuiltInCategory[] defaultCategories)
        {
            var resolved = new List<BuiltInCategory>();
            if (categoriesToken == null || categoriesToken.Count == 0)
            {
                resolved.AddRange(defaultCategories ?? Array.Empty<BuiltInCategory>());
                return resolved.Distinct().ToList();
            }

            foreach (var t in categoriesToken)
            {
                try
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        int v = t.Value<int>();
                        var bic = (BuiltInCategory)v;
                        if (bic != BuiltInCategory.INVALID) resolved.Add(bic);
                        continue;
                    }

                    if (t.Type != JTokenType.String) continue;
                    var s = (t.Value<string>() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(s)) continue;

                    // Friendly names + enum names
                    var map = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "Walls", BuiltInCategory.OST_Walls },
                        { "Wall", BuiltInCategory.OST_Walls },
                        { "壁", BuiltInCategory.OST_Walls },
                        { "Structural Framing", BuiltInCategory.OST_StructuralFraming },
                        { "Structural Frame", BuiltInCategory.OST_StructuralFraming },
                        { "構造フレーム", BuiltInCategory.OST_StructuralFraming },
                        { "Structural Columns", BuiltInCategory.OST_StructuralColumns },
                        { "Structural Column", BuiltInCategory.OST_StructuralColumns },
                        { "建築柱", BuiltInCategory.OST_Columns },
                        { "構造柱", BuiltInCategory.OST_StructuralColumns },
                        { "Columns", BuiltInCategory.OST_Columns },
                        { "Column", BuiltInCategory.OST_Columns },
                        { "柱", BuiltInCategory.OST_Columns },
                        { "Floors", BuiltInCategory.OST_Floors },
                        { "床", BuiltInCategory.OST_Floors },
                        { "Ceilings", BuiltInCategory.OST_Ceilings },
                        { "天井", BuiltInCategory.OST_Ceilings },
                        { "Doors", BuiltInCategory.OST_Doors },
                        { "Windows", BuiltInCategory.OST_Windows },
                        { "Furniture", BuiltInCategory.OST_Furniture },
                        { "家具", BuiltInCategory.OST_Furniture },
                        { "Furniture Systems", BuiltInCategory.OST_FurnitureSystems },
                        { "Furniture System", BuiltInCategory.OST_FurnitureSystems },
                        { "家具システム", BuiltInCategory.OST_FurnitureSystems },
                        { "Casework", BuiltInCategory.OST_Casework },
                        { "造作", BuiltInCategory.OST_Casework },
                        { "造作家具", BuiltInCategory.OST_Casework },
                        { "Specialty Equipment", BuiltInCategory.OST_SpecialityEquipment },
                        { "Speciality Equipment", BuiltInCategory.OST_SpecialityEquipment },
                        { "SpecialtyEquipment", BuiltInCategory.OST_SpecialityEquipment },
                        { "特別設備", BuiltInCategory.OST_SpecialityEquipment },
                        { "Generic Models", BuiltInCategory.OST_GenericModel },
                        { "Generic Model", BuiltInCategory.OST_GenericModel },
                        { "汎用モデル", BuiltInCategory.OST_GenericModel },
                        { "Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment },
                        { "MechanicalEquipment", BuiltInCategory.OST_MechanicalEquipment },
                        { "機械設備", BuiltInCategory.OST_MechanicalEquipment },
                        { "設備機器", BuiltInCategory.OST_MechanicalEquipment },
                        { "Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures },
                        { "PlumbingFixtures", BuiltInCategory.OST_PlumbingFixtures },
                        { "衛生器具", BuiltInCategory.OST_PlumbingFixtures },
                        { "Lighting Fixtures", BuiltInCategory.OST_LightingFixtures },
                        { "LightingFixtures", BuiltInCategory.OST_LightingFixtures },
                        { "照明器具", BuiltInCategory.OST_LightingFixtures },
                        { "Electrical Equipment", BuiltInCategory.OST_ElectricalEquipment },
                        { "ElectricalEquipment", BuiltInCategory.OST_ElectricalEquipment },
                        { "電気機器", BuiltInCategory.OST_ElectricalEquipment },
                        { "Electrical Fixtures", BuiltInCategory.OST_ElectricalFixtures },
                        { "ElectricalFixtures", BuiltInCategory.OST_ElectricalFixtures },
                        { "電気器具", BuiltInCategory.OST_ElectricalFixtures },
                    };

                    if (map.TryGetValue(s, out var bic1))
                    {
                        if (bic1 != BuiltInCategory.INVALID) resolved.Add(bic1);
                        continue;
                    }

                    if (Enum.TryParse<BuiltInCategory>(s, true, out var bic2) && bic2 != BuiltInCategory.INVALID)
                    {
                        resolved.Add(bic2);
                        continue;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (resolved.Count == 0)
                resolved.AddRange(defaultCategories ?? Array.Empty<BuiltInCategory>());

            return resolved.Distinct().ToList();
        }

        private static ElementId? ResolveElementLevelId(Element e)
        {
            try
            {
                switch (e)
                {
                    case Autodesk.Revit.DB.Wall w:
                        return w.LevelId;
                    case Autodesk.Revit.DB.Floor f:
                        return f.LevelId;
                    case Autodesk.Revit.DB.Ceiling c:
                        return c.LevelId;
                    case Autodesk.Revit.DB.RoofBase rb:
                        return rb.LevelId;
                    case Autodesk.Revit.DB.FamilyInstance fi:
                        if (fi.LevelId != ElementId.InvalidElementId)
                            return fi.LevelId;
                        break;
                    case Autodesk.Revit.DB.Architecture.Room r:
                        return r.LevelId;
                    case Autodesk.Revit.DB.Mechanical.Space s:
                        return s.LevelId;
                }

                var pLevel = e.get_Parameter(BuiltInParameter.LEVEL_PARAM)
                             ?? e.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                             ?? e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                             ?? e.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (pLevel != null && pLevel.StorageType == StorageType.ElementId)
                    return pLevel.AsElementId();
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static bool TryGetElementBbox(Element e, out BoundingBoxXYZ? bb)
        {
            bb = null;
            if (e == null) return false;
            try
            {
                bb = e.get_BoundingBox(null);
                return bb != null;
            }
            catch
            {
                bb = null;
                return false;
            }
        }

        private static bool TryGetBboxWorldMinMaxXY(BoundingBoxXYZ bb, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = double.PositiveInfinity;
            minY = double.PositiveInfinity;
            maxX = double.NegativeInfinity;
            maxY = double.NegativeInfinity;

            if (bb == null) return false;

            try
            {
                var tr = bb.Transform ?? Transform.Identity;
                var corners = new[]
                {
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
                }.Select(c => tr.OfPoint(c));

                foreach (var p in corners)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }

                if (!(minX < maxX && minY < maxY)) return false;
                return true;
            }
            catch
            {
                minX = minY = double.PositiveInfinity;
                maxX = maxY = double.NegativeInfinity;
                return false;
            }
        }

        private static bool CrossesZPlane(BoundingBoxXYZ bb, double zFt, double tolFt)
        {
            if (bb == null) return false;
            double z0 = Math.Min(bb.Min.Z, bb.Max.Z);
            double z1 = Math.Max(bb.Min.Z, bb.Max.Z);
            return (z0 - tolFt) <= zFt && zFt <= (z1 + tolFt);
        }

        private static double ComputeRoomProbeZFt(Autodesk.Revit.DB.Architecture.Room room, Level? level, double roomBaseZFt)
        {
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb != null)
                    return 0.5 * (bb.Min.Z + bb.Max.Z);
            }
            catch { /* ignore */ }

            try
            {
                var lp = room.Location as LocationPoint;
                if (lp?.Point != null)
                    return lp.Point.Z;
            }
            catch { /* ignore */ }

            try
            {
                double lvlZ = level?.Elevation ?? roomBaseZFt;
                return lvlZ + UnitHelper.MmToFt(1000.0);
            }
            catch { /* ignore */ }

            return roomBaseZFt;
        }

        private static (double insideMm, double totalMm) ComputeCurveInsideLengthInRoom(
            Curve curve,
            Autodesk.Revit.DB.Architecture.Room room,
            double zProbeFt,
            double sampleStepMm)
        {
            if (curve == null || room == null) return (0.0, 0.0);
            double stepFt = UnitHelper.MmToFt(sampleStepMm);
            if (stepFt <= 1e-9) stepFt = UnitHelper.MmToFt(200.0);

            IList<XYZ>? tess = null;
            try { tess = curve.Tessellate(); } catch { tess = null; }

            var basePts = new List<XYZ>();
            if (tess != null && tess.Count >= 2)
            {
                basePts.AddRange(tess);
            }
            else
            {
                try
                {
                    basePts.Add(curve.GetEndPoint(0));
                    basePts.Add(curve.GetEndPoint(1));
                }
                catch
                {
                    return (0.0, 0.0);
                }
            }

            var pts = new List<XYZ>();
            pts.Add(basePts[0]);

            for (int i = 0; i < basePts.Count - 1; i++)
            {
                var a = basePts[i];
                var b = basePts[i + 1];
                double segLenFt = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                int div = Math.Max(1, (int)Math.Ceiling(segLenFt / stepFt));
                for (int j = 1; j <= div; j++)
                {
                    double t = (double)j / (double)div;
                    pts.Add(a + (b - a) * t);
                }
            }

            double insideFt = 0.0;
            double totalFt = 0.0;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                double lenFt = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (lenFt <= 1e-12) continue;
                totalFt += lenFt;

                var mid = (a + b) * 0.5;
                var probe = new XYZ(mid.X, mid.Y, zProbeFt);
                bool inside = false;
                try { inside = room.IsPointInRoom(probe); } catch { inside = false; }
                if (inside) insideFt += lenFt;
            }

            return (UnitHelper.FtToMm(insideFt), UnitHelper.FtToMm(totalFt));
        }

        private static bool IsPointInBboxXY(XYZ ptFt, BoundingBoxXYZ bb)
        {
            if (bb == null) return false;
            return ptFt.X >= bb.Min.X && ptFt.X <= bb.Max.X
                && ptFt.Y >= bb.Min.Y && ptFt.Y <= bb.Max.Y;
        }

        private static (object[] floors, object[] ceilings, Dictionary<string, int[]> ceilingIdsBySegmentKey, Dictionary<string, double[]> ceilingHeightsBySegmentKey)
            CollectFloorCeilingContext(
                Document doc,
                Autodesk.Revit.DB.Architecture.Room room,
                List<SegmentOutRef> segments,
                double searchMarginMm,
                double segmentInsetMm,
                bool sameLevelOnly)
        {
            var level = doc.GetElement(room.LevelId) as Level;
            double roomLevelElevMm = UnitHelper.FtToMm(level?.Elevation ?? 0.0);
            int roomLevelIdInt = room.LevelId.IntValue();

            // Z probe for IsPointInRoom (keep it inside the room volume)
            double zProbeFt = 0.0;
            try
            {
                var bbRoom = room.get_BoundingBox(null);
                if (bbRoom != null) zProbeFt = (bbRoom.Min.Z + bbRoom.Max.Z) * 0.5;
            }
            catch { /* ignore */ }

            if (Math.Abs(zProbeFt) < 1e-9)
            {
                try
                {
                    var lp = room.Location as LocationPoint;
                    if (lp?.Point != null) zProbeFt = lp.Point.Z;
                }
                catch { /* ignore */ }
            }

            if (Math.Abs(zProbeFt) < 1e-9)
            {
                try { zProbeFt = (level?.Elevation ?? 0.0) + UnitHelper.MmToFt(1000.0); } catch { /* ignore */ }
            }

            // Boundary bbox (XY) in ft
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in segments)
            {
                minX = Math.Min(minX, Math.Min(s.StartFt.X, s.EndFt.X));
                minY = Math.Min(minY, Math.Min(s.StartFt.Y, s.EndFt.Y));
                maxX = Math.Max(maxX, Math.Max(s.StartFt.X, s.EndFt.X));
                maxY = Math.Max(maxY, Math.Max(s.StartFt.Y, s.EndFt.Y));
            }

            double marginFt = UnitHelper.MmToFt(searchMarginMm);
            var outline = new Outline(
                new XYZ(minX - marginFt, minY - marginFt, zProbeFt - UnitHelper.MmToFt(100000.0)),
                new XYZ(maxX + marginFt, maxY + marginFt, zProbeFt + UnitHelper.MmToFt(100000.0)));

            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            // Candidates
            var ceilingCandidates = new List<BBoxCandidate>();
            try
            {
                foreach (var c in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbFilter)
                    .ToElements()
                    .OfType<Autodesk.Revit.DB.Ceiling>())
                {
                    BoundingBoxXYZ? bb = null;
                    try { bb = c.get_BoundingBox(null); } catch { bb = null; }
                    if (bb == null) continue;

                    int typeId = 0;
                    string typeName = string.Empty;
                    try
                    {
                        var tid = c.GetTypeId();
                        typeId = tid?.IntValue() ?? 0;
                        var t = doc.GetElement(tid) as ElementType;
                        typeName = t?.Name ?? string.Empty;
                    }
                    catch { /* ignore */ }

                    double absElevMm = 0.0;
                    try { absElevMm = UnitHelper.CeilingElevationMm(doc, c); } catch { absElevMm = UnitHelper.FtToMm(bb.Min.Z); }

                    int levelIdInt = 0;
                    string levelName = string.Empty;
                    try
                    {
                        var lid = ResolveElementLevelId(c);
                        if (lid != null && lid != ElementId.InvalidElementId)
                        {
                            levelIdInt = lid.IntValue();
                            var l = doc.GetElement(lid) as Level;
                            levelName = l?.Name ?? string.Empty;
                        }
                    }
                    catch { /* ignore */ }

                    ceilingCandidates.Add(new BBoxCandidate
                    {
                        ElementId = c.Id.IntValue(),
                        UniqueId = c.UniqueId ?? string.Empty,
                        TypeId = typeId,
                        TypeName = typeName,
                        CategoryName = c.Category?.Name ?? string.Empty,
                        LevelId = levelIdInt,
                        LevelName = levelName,
                        BBox = bb,
                        HeightFromRoomLevelMm = absElevMm - roomLevelElevMm
                    });
                }
            }
            catch
            {
                ceilingCandidates = new List<BBoxCandidate>();
            }

            var floorCandidates = new List<BBoxCandidate>();
            try
            {
                foreach (var fl in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbFilter)
                    .ToElements()
                    .OfType<Autodesk.Revit.DB.Floor>())
                {
                    BoundingBoxXYZ? bb = null;
                    try { bb = fl.get_BoundingBox(null); } catch { bb = null; }
                    if (bb == null) continue;

                    int typeId = 0;
                    string typeName = string.Empty;
                    try
                    {
                        var tid = fl.GetTypeId();
                        typeId = tid?.IntValue() ?? 0;
                        var t = doc.GetElement(tid) as ElementType;
                        typeName = t?.Name ?? string.Empty;
                    }
                    catch { /* ignore */ }

                    double topMm = UnitHelper.FtToMm(bb.Max.Z);

                    int levelIdInt = 0;
                    string levelName = string.Empty;
                    try
                    {
                        var lid = ResolveElementLevelId(fl);
                        if (lid != null && lid != ElementId.InvalidElementId)
                        {
                            levelIdInt = lid.IntValue();
                            var l = doc.GetElement(lid) as Level;
                            levelName = l?.Name ?? string.Empty;
                        }
                    }
                    catch { /* ignore */ }

                    floorCandidates.Add(new BBoxCandidate
                    {
                        ElementId = fl.Id.IntValue(),
                        UniqueId = fl.UniqueId ?? string.Empty,
                        TypeId = typeId,
                        TypeName = typeName,
                        CategoryName = fl.Category?.Name ?? string.Empty,
                        LevelId = levelIdInt,
                        LevelName = levelName,
                        BBox = bb,
                        HeightFromRoomLevelMm = topMm - roomLevelElevMm
                    });
                }
            }
            catch
            {
                floorCandidates = new List<BBoxCandidate>();
            }

            if (sameLevelOnly)
            {
                // By default, do not mix floors/ceilings from other levels.
                // This keeps the output closer to a "room finish takeoff" context.
                try
                {
                    ceilingCandidates = ceilingCandidates.Where(x => x.LevelId == roomLevelIdInt).ToList();
                    floorCandidates = floorCandidates.Where(x => x.LevelId == roomLevelIdInt).ToList();
                }
                catch
                {
                    // ignore
                }
            }

            var ceilingIdsBySegKey = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            var ceilingHeightsBySegKey = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

            var usedCeilingIds = new HashSet<int>();
            var usedFloorIds = new HashSet<int>();

            double insetFt = UnitHelper.MmToFt(segmentInsetMm);

            foreach (var s in segments)
            {
                // segment direction in XY
                var v = s.EndFt - s.StartFt;
                var vxy = new XYZ(v.X, v.Y, 0.0);
                double len = 0.0;
                try { len = vxy.GetLength(); } catch { len = 0.0; }
                if (len < 1e-9) continue;

                XYZ dir = XYZ.Zero;
                try { dir = vxy.Normalize(); } catch { dir = XYZ.BasisX; }
                XYZ left = new XYZ(-dir.Y, dir.X, 0.0);
                XYZ right = new XYZ(dir.Y, -dir.X, 0.0);

                // Determine interior normal (by testing a midpoint offset)
                XYZ mid = s.StartFt + (s.EndFt - s.StartFt) * 0.5;
                XYZ? interiorNormal = null;
                try
                {
                    var pL = new XYZ(mid.X + left.X * insetFt, mid.Y + left.Y * insetFt, zProbeFt);
                    if (room.IsPointInRoom(pL)) interiorNormal = left;
                    else
                    {
                        var pR = new XYZ(mid.X + right.X * insetFt, mid.Y + right.Y * insetFt, zProbeFt);
                        if (room.IsPointInRoom(pR)) interiorNormal = right;
                    }
                }
                catch
                {
                    interiorNormal = null;
                }

                if (interiorNormal == null) continue;

                // Sample 3 points along the segment (25/50/75%)
                var ts = new[] { 0.25, 0.5, 0.75 };

                var segCeilIds = new HashSet<int>();
                var segFloorIds = new HashSet<int>();

                foreach (var t in ts)
                {
                    XYZ basePt = s.StartFt + (s.EndFt - s.StartFt) * t;
                    var n = interiorNormal;
                    if (n == null) continue;
                    var insidePt = new XYZ(
                        basePt.X + n.X * insetFt,
                        basePt.Y + n.Y * insetFt,
                        zProbeFt);

                    bool inside = false;
                    try { inside = room.IsPointInRoom(insidePt); } catch { inside = false; }
                    if (!inside) continue;

                    foreach (var cc in ceilingCandidates)
                    {
                        if (cc.BBox == null) continue;
                        if (!IsPointInBboxXY(insidePt, cc.BBox)) continue;
                        segCeilIds.Add(cc.ElementId);
                    }

                    foreach (var fc in floorCandidates)
                    {
                        if (fc.BBox == null) continue;
                        if (!IsPointInBboxXY(insidePt, fc.BBox)) continue;
                        segFloorIds.Add(fc.ElementId);
                    }
                }

                if (segCeilIds.Count > 0)
                {
                    ceilingIdsBySegKey[s.Key] = segCeilIds.OrderBy(x => x).ToArray();
                    usedCeilingIds.UnionWith(segCeilIds);

                    var heights = ceilingCandidates
                        .Where(c => segCeilIds.Contains(c.ElementId))
                        .Select(c => c.HeightFromRoomLevelMm)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToArray();
                    ceilingHeightsBySegKey[s.Key] = heights;
                }
                else
                {
                    ceilingIdsBySegKey[s.Key] = Array.Empty<int>();
                    ceilingHeightsBySegKey[s.Key] = Array.Empty<double>();
                }

                if (segFloorIds.Count > 0)
                {
                    usedFloorIds.UnionWith(segFloorIds);
                }
            }

            var floorsOut = floorCandidates
                .Where(f => usedFloorIds.Contains(f.ElementId))
                .OrderBy(f => f.HeightFromRoomLevelMm)
                .ThenBy(f => f.ElementId)
                .Select(f => (object)new
                {
                    elementId = f.ElementId,
                    uniqueId = f.UniqueId,
                    categoryName = f.CategoryName,
                    typeId = f.TypeId,
                    typeName = f.TypeName,
                    levelId = f.LevelId,
                    levelName = f.LevelName,
                    topHeightFromRoomLevelMm = Math.Round(f.HeightFromRoomLevelMm, 3)
                })
                .ToArray();

            var ceilingsOut = ceilingCandidates
                .Where(c => usedCeilingIds.Contains(c.ElementId))
                .OrderBy(c => c.HeightFromRoomLevelMm)
                .ThenBy(c => c.ElementId)
                .Select(c => (object)new
                {
                    elementId = c.ElementId,
                    uniqueId = c.UniqueId,
                    categoryName = c.CategoryName,
                    typeId = c.TypeId,
                    typeName = c.TypeName,
                    levelId = c.LevelId,
                    levelName = c.LevelName,
                    heightFromRoomLevelMm = Math.Round(c.HeightFromRoomLevelMm, 3)
                })
                .ToArray();

            return (floorsOut, ceilingsOut, ceilingIdsBySegKey, ceilingHeightsBySegKey);
        }

        private static object CollectInteriorElementsInRoom(
            Document doc,
            Autodesk.Revit.DB.Architecture.Room room,
            List<SegmentOutRef> segments,
            List<BuiltInCategory> categories,
            double searchMarginMm,
            double sampleStepMm,
            bool pointBboxProbe)
        {
            if (doc == null || room == null)
                return new { ok = false, msg = "doc/room is null." };

            var level = doc.GetElement(room.LevelId) as Level;

            double baseOffsetFt = 0.0;
            try
            {
                var p = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
                if (p != null && p.StorageType == StorageType.Double)
                    baseOffsetFt = p.AsDouble();
            }
            catch { /* ignore */ }

            double roomBaseZFt = (level?.Elevation ?? 0.0) + baseOffsetFt;
            double zProbeFt = ComputeRoomProbeZFt(room, level, roomBaseZFt);

            // Outline around room boundary bbox
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in segments)
            {
                minX = Math.Min(minX, Math.Min(s.StartFt.X, s.EndFt.X));
                minY = Math.Min(minY, Math.Min(s.StartFt.Y, s.EndFt.Y));
                maxX = Math.Max(maxX, Math.Max(s.StartFt.X, s.EndFt.X));
                maxY = Math.Max(maxY, Math.Max(s.StartFt.Y, s.EndFt.Y));
            }

            if (double.IsInfinity(minX))
            {
                var bbRoom = room.get_BoundingBox(null);
                if (bbRoom != null)
                {
                    minX = bbRoom.Min.X; minY = bbRoom.Min.Y;
                    maxX = bbRoom.Max.X; maxY = bbRoom.Max.Y;
                }
            }

            double marginFt = UnitHelper.MmToFt(searchMarginMm);
            var outline = new Outline(
                new XYZ(minX - marginFt, minY - marginFt, roomBaseZFt - UnitHelper.MmToFt(100000.0)),
                new XYZ(maxX + marginFt, maxY + marginFt, roomBaseZFt + UnitHelper.MmToFt(100000.0)));
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            var items = new List<object>();
            var countsByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            double tolFt = UnitHelper.MmToFt(1.0);

            foreach (var bic in categories.Distinct())
            {
                var catKey = bic.ToString();
                int added = 0;

                IList<Element> elems;
                try
                {
                    elems = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .WherePasses(bbFilter)
                        .ToElements();
                }
                catch
                {
                    continue;
                }

                foreach (var e in elems)
                {
                    if (e == null) continue;

                    // Level filter: same level OR spans through the room base plane
                    bool sameLevel = false;
                    try
                    {
                        var eid = ResolveElementLevelId(e);
                        if (eid != null && eid != ElementId.InvalidElementId && eid == room.LevelId)
                            sameLevel = true;
                    }
                    catch { /* ignore */ }

                    bool crosses = false;
                    if (!sameLevel)
                    {
                        if (TryGetElementBbox(e, out var bb) && bb != null)
                            crosses = CrossesZPlane(bb, roomBaseZFt, tolFt);
                    }

                    if (!sameLevel && !crosses)
                        continue;

                    // Type info
                    int typeId = 0;
                    string typeName = string.Empty;
                    try
                    {
                        var tid = e.GetTypeId();
                        typeId = tid != null ? tid.IntValue() : 0;
                        var et = tid != null ? doc.GetElement(tid) as ElementType : null;
                        typeName = et?.Name ?? string.Empty;
                    }
                    catch { /* ignore */ }

                    // Determine inside / length in room
                    double? insideLenMm = null;
                    double? totalLenMm = null;
                    double? insideRatio = null;
                    string method = "unknown";

                    bool include = false;

                    var lc = e.Location as LocationCurve;
                    if (lc?.Curve != null)
                    {
                        var (insideMm, totalMm) = ComputeCurveInsideLengthInRoom(lc.Curve, room, zProbeFt, sampleStepMm);
                        totalLenMm = Math.Round(totalMm, 3);
                        insideLenMm = Math.Round(insideMm, 3);
                        insideRatio = (totalMm > 1e-6) ? Math.Round(insideMm / totalMm, 6) : (double?)null;
                        method = "LocationCurve.sample2d.midpoint@zProbe";
                        include = insideMm > 1e-3;
                    }
                    else
                    {
                        XYZ? pt = null;
                        var lp = e.Location as LocationPoint;
                        if (lp?.Point != null)
                        {
                            pt = lp.Point;
                            method = "LocationPoint@zProbe";
                        }
                        else if (TryGetElementBbox(e, out var bb) && bb != null)
                        {
                            pt = (bb.Min + bb.Max) * 0.5;
                            method = "BBoxCenter@zProbe";
                        }

                        if (pt != null)
                        {
                            bool referencePointInside = false;
                            object? referencePointMmObj = null;
                            try
                            {
                                var probe = new XYZ(pt.X, pt.Y, zProbeFt);
                                referencePointInside = room.IsPointInRoom(probe);
                                var probeMm = UnitHelper.XyzToMm(probe);
                                referencePointMmObj = new
                                {
                                    x = Math.Round(probeMm.x, 3),
                                    y = Math.Round(probeMm.y, 3),
                                    z = Math.Round(probeMm.z, 3)
                                };
                            }
                            catch
                            {
                                referencePointInside = false;
                                referencePointMmObj = null;
                            }

                            int bboxProbeSampleCount = 0;
                            int bboxProbeInsideCount = 0;
                            bool includedByBboxProbe = false;

                            if (!referencePointInside && pointBboxProbe)
                            {
                                try
                                {
                                    if (TryGetElementBbox(e, out var bb2) && bb2 != null)
                                    {
                                        if (TryGetBboxWorldMinMaxXY(bb2, out double bx0, out double by0, out double bx1, out double by1))
                                        {
                                            double epsFt = UnitHelper.MmToFt(1.0);
                                            double x0 = bx0 + epsFt;
                                            double x1 = bx1 - epsFt;
                                            double y0 = by0 + epsFt;
                                            double y1 = by1 - epsFt;
                                            if (x1 < x0) { x0 = bx0; x1 = bx1; }
                                            if (y1 < y0) { y0 = by0; y1 = by1; }

                                            double xMid = 0.5 * (bx0 + bx1);
                                            double yMid = 0.5 * (by0 + by1);

                                            var probes = new List<XYZ>
                                            {
                                                new XYZ(xMid, yMid, zProbeFt),
                                                new XYZ(x0, y0, zProbeFt),
                                                new XYZ(x1, y0, zProbeFt),
                                                new XYZ(x1, y1, zProbeFt),
                                                new XYZ(x0, y1, zProbeFt),
                                                new XYZ(xMid, y0, zProbeFt),
                                                new XYZ(x1, yMid, zProbeFt),
                                                new XYZ(xMid, y1, zProbeFt),
                                                new XYZ(x0, yMid, zProbeFt),
                                            };

                                            bboxProbeSampleCount = probes.Count;
                                            foreach (var q in probes)
                                            {
                                                bool insideQ = false;
                                                try { insideQ = room.IsPointInRoom(q); } catch { insideQ = false; }
                                                if (insideQ) bboxProbeInsideCount++;
                                            }

                                            if (bboxProbeInsideCount > 0)
                                            {
                                                includedByBboxProbe = true;
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    bboxProbeSampleCount = 0;
                                    bboxProbeInsideCount = 0;
                                    includedByBboxProbe = false;
                                }
                            }

                            include = referencePointInside || includedByBboxProbe;

                            // Add diagnostic fields for point-based inclusion (so the caller can tell when
                            // the reference point was outside but the element still intersects the room).
                            if (include)
                            {
                                insideLenMm = null;
                                totalLenMm = null;
                                insideRatio = null;

                                // Store diagnostics in locals for output below via closures
                                // (captured in the anonymous object).
                                var referenceInsideLocal = referencePointInside;
                                var referencePointMmLocal = referencePointMmObj;
                                var bboxProbeSampleCountLocal = bboxProbeSampleCount;
                                var bboxProbeInsideCountLocal = bboxProbeInsideCount;
                                var includedByBboxProbeLocal = includedByBboxProbe;

                                items.Add(new
                                {
                                    elementId = e.Id.IntValue(),
                                    uniqueId = e.UniqueId ?? string.Empty,
                                    elementClass = e.GetType().Name,
                                    categoryId = e.Category?.Id?.IntValue() ?? 0,
                                    categoryName = e.Category?.Name ?? string.Empty,
                                    typeId,
                                    typeName,
                                    relation = new
                                    {
                                        sameLevel,
                                        crossesRoomBasePlane = crosses,
                                        roomLevelId = room.LevelId.IntValue(),
                                        roomBaseZMm = Math.Round(UnitHelper.FtToMm(roomBaseZFt), 3),
                                        zProbeMm = Math.Round(UnitHelper.FtToMm(zProbeFt), 3)
                                    },
                                    measure = new
                                    {
                                        method,
                                        sampleStepMm = Math.Round(sampleStepMm, 3),
                                        referencePointMm = referencePointMmLocal,
                                        referencePointInside = referenceInsideLocal,
                                        bboxProbe = new
                                        {
                                            enabled = pointBboxProbe,
                                            sampleCount = bboxProbeSampleCountLocal,
                                            insideCount = bboxProbeInsideCountLocal,
                                            used = includedByBboxProbeLocal
                                        },
                                        insideLengthMm = insideLenMm,
                                        totalLengthMm = totalLenMm,
                                        insideRatio
                                    }
                                });

                                added++;
                                continue;
                            }
                        }
                    }

                    if (!include)
                        continue;

                    items.Add(new
                    {
                        elementId = e.Id.IntValue(),
                        uniqueId = e.UniqueId ?? string.Empty,
                        elementClass = e.GetType().Name,
                        categoryId = e.Category?.Id?.IntValue() ?? 0,
                        categoryName = e.Category?.Name ?? string.Empty,
                        typeId,
                        typeName,
                        relation = new
                        {
                            sameLevel,
                            crossesRoomBasePlane = crosses,
                            roomLevelId = room.LevelId.IntValue(),
                            roomBaseZMm = Math.Round(UnitHelper.FtToMm(roomBaseZFt), 3),
                            zProbeMm = Math.Round(UnitHelper.FtToMm(zProbeFt), 3)
                        },
                        measure = new
                        {
                            method,
                            sampleStepMm = Math.Round(sampleStepMm, 3),
                            referencePointMm = (object?)null,
                            referencePointInside = (bool?)null,
                            bboxProbe = (object?)null,
                            insideLengthMm = insideLenMm,
                            totalLengthMm = totalLenMm,
                            insideRatio
                        }
                    });

                    added++;
                }

                if (added > 0)
                {
                    countsByCategory[catKey] = added;
                }
            }

            return new
            {
                ok = true,
                roomId = room.Id.IntValue(),
                roomLevelId = room.LevelId.IntValue(),
                searchMarginMm = Math.Round(searchMarginMm, 3),
                sampleStepMm = Math.Round(sampleStepMm, 3),
                pointBboxProbe = pointBboxProbe,
                categories = categories.Select(x => x.ToString()).ToArray(),
                countsByCategory,
                itemCount = items.Count,
                items = items.ToArray()
            };
        }

        private static Autodesk.Revit.DB.Architecture.Room? TryResolveSelectedRoom(
            Document doc,
            Selection selection,
            out int roomId)
        {
            roomId = 0;
            if (doc == null || selection == null) return null;

            try
            {
                foreach (var id in selection.GetElementIds())
                {
                    var e = doc.GetElement(id);
                    var r = e as Autodesk.Revit.DB.Architecture.Room;
                    if (r != null)
                    {
                        roomId = r.Id.IntValue();
                        return r;
                    }

                    // RoomTag -> Room
                    var tag = e as Autodesk.Revit.DB.Architecture.RoomTag;
                    if (tag != null)
                    {
                        try
                        {
                            // Try common patterns across Revit versions:
                            // - tag.Room : Room
                            // - tag.RoomId / tag.TaggedLocalRoomId / tag.TaggedRoomId : ElementId
                            var tagType = tag.GetType();

                            // 1) Property "Room" -> Room
                            try
                            {
                                var piRoom = tagType.GetProperty("Room", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                if (piRoom != null)
                                {
                                    var rObj = piRoom.GetValue(tag, null) as Autodesk.Revit.DB.Architecture.Room;
                                    if (rObj != null)
                                    {
                                        roomId = rObj.Id.IntValue();
                                        return rObj;
                                    }
                                }
                            }
                            catch { /* ignore */ }

                            // 2) Property candidates -> ElementId
                            var idPropNames = new[]
                            {
                                "RoomId",
                                "TaggedLocalRoomId",
                                "TaggedRoomId",
                                "TaggedLocalElementId",
                                "TaggedElementId"
                            };

                            ElementId rid = ElementId.InvalidElementId;
                            foreach (var name in idPropNames)
                            {
                                try
                                {
                                    var pi = tagType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                    if (pi == null) continue;
                                    if (pi.PropertyType != typeof(ElementId)) continue;
                                    var v = pi.GetValue(tag, null) as ElementId;
                                    if (v != null && v != ElementId.InvalidElementId)
                                    {
                                        rid = v;
                                        break;
                                    }
                                }
                                catch { /* ignore */ }
                            }

                            if (rid != ElementId.InvalidElementId)
                            {
                                var r2 = doc.GetElement(rid) as Autodesk.Revit.DB.Architecture.Room;
                                if (r2 != null)
                                {
                                    roomId = r2.Id.IntValue();
                                    return r2;
                                }
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static (double? heightMm, string source) TryGetRoomHeightMm(Autodesk.Revit.DB.Architecture.Room room)
        {
            if (room == null) return (null, "none");

            try
            {
                var p = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double ft = 0.0;
                    try { ft = p.AsDouble(); } catch { ft = 0.0; }
                    double mm = UnitHelper.FtToMm(ft);
                    if (mm > 1e-3) return (Math.Round(mm, 3), "BuiltInParameter.ROOM_HEIGHT");
                }
            }
            catch { /* ignore */ }

            try
            {
                var p2 = room.LookupParameter("Unbounded Height");
                if (p2 != null && p2.StorageType == StorageType.Double)
                {
                    double ft = 0.0;
                    try { ft = p2.AsDouble(); } catch { ft = 0.0; }
                    double mm = UnitHelper.FtToMm(ft);
                    if (mm > 1e-3) return (Math.Round(mm, 3), "LookupParameter(Unbounded Height)");
                }
            }
            catch { /* ignore */ }

            // As a last resort, try a common localized name (Japanese)
            try
            {
                var p3 = room.LookupParameter("高さ");
                if (p3 != null && p3.StorageType == StorageType.Double)
                {
                    double ft = 0.0;
                    try { ft = p3.AsDouble(); } catch { ft = 0.0; }
                    double mm = UnitHelper.FtToMm(ft);
                    if (mm > 1e-3) return (Math.Round(mm, 3), "LookupParameter(高さ)");
                }
            }
            catch { /* ignore */ }

            return (null, "unavailable");
        }

        private static bool IsColumnElement(Element? e)
        {
            if (e == null) return false;
            try
            {
                var cat = e.Category;
                if (cat == null) return false;
                int catId = cat.Id?.IntValue() ?? 0;
                return catId == (int)BuiltInCategory.OST_Columns
                    || catId == (int)BuiltInCategory.OST_StructuralColumns;
            }
            catch
            {
                return false;
            }
        }

        private static object MapInsertInstance(Document doc, Element e, int hostWallId, string kind)
        {
            int typeId = 0;
            string typeName = string.Empty;
            string familyName = string.Empty;
            object? locationObj = null;

            try
            {
                var fi = e as FamilyInstance;
                if (fi != null)
                {
                    typeId = fi.GetTypeId().IntValue();
                    var sym = doc.GetElement(fi.GetTypeId()) as FamilySymbol;
                    typeName = sym?.Name ?? string.Empty;
                    familyName = sym?.Family?.Name ?? string.Empty;

                    var lp = fi.Location as LocationPoint;
                    var pt = lp?.Point;
                    if (pt != null)
                    {
                        locationObj = new
                        {
                            x = Math.Round(UnitHelper.FtToMm(pt.X), 3),
                            y = Math.Round(UnitHelper.FtToMm(pt.Y), 3),
                            z = Math.Round(UnitHelper.FtToMm(pt.Z), 3)
                        };
                    }
                }
            }
            catch { /* ignore */ }

            return new
            {
                elementId = e.Id.IntValue(),
                uniqueId = e.UniqueId ?? string.Empty,
                kind,
                category = e.Category?.Name ?? string.Empty,
                typeId,
                typeName,
                familyName,
                hostWallId,
                location = locationObj
            };
        }

        // ----------------------------------------------------------
        // Wall matching around room boundary (XY 2D)
        // (Copied and slightly extended from GetRoomPerimeterWithColumnsAndWallsCommand)
        // ----------------------------------------------------------
        private static WallMatchResult MatchWallsNearRoomBoundary(
            Document doc,
            Autodesk.Revit.DB.Architecture.Room room,
            List<BoundarySegmentInfo> boundarySegments,
            double maxOffsetMm,
            double minOverlapMm,
            double maxAngleDeg,
            double searchMarginMm)
        {
            double levelElevMm = 0.0;
            try
            {
                var level = doc.GetElement(room.LevelId) as Level;
                levelElevMm = UnitHelper.FtToMm(level?.Elevation ?? 0.0);
            }
            catch { /* ignore */ }

            // Bounding box of boundary in mm (XY)
            double minXmm = double.MaxValue, minYmm = double.MaxValue;
            double maxXmm = double.MinValue, maxYmm = double.MinValue;

            foreach (var bs in boundarySegments)
            {
                var a = bs.Seg2.A;
                var b = bs.Seg2.B;
                minXmm = Math.Min(minXmm, Math.Min(a.X, b.X));
                minYmm = Math.Min(minYmm, Math.Min(a.Y, b.Y));
                maxXmm = Math.Max(maxXmm, Math.Max(a.X, b.X));
                maxYmm = Math.Max(maxYmm, Math.Max(a.Y, b.Y));
            }

            if (boundarySegments.Count == 0 || double.IsInfinity(minXmm))
            {
                return new WallMatchResult
                {
                    Walls = Array.Empty<object>(),
                    MatchedWallElements = new List<Autodesk.Revit.DB.Wall>(),
                    Debug = new
                    {
                        candidateWallCount = 0,
                        matchedWallCount = 0,
                        levelId = room.LevelId.IntValue(),
                        levelElevationMm = levelElevMm,
                        maxOffsetMm,
                        minOverlapMm,
                        maxAngleDeg,
                        searchMarginMm
                    }
                };
            }

            // Collect all walls (then filter by level span + bbox)
            var allWalls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .ToElements()
                .OfType<Autodesk.Revit.DB.Wall>()
                .ToList();

            var candidateWalls = new List<Autodesk.Revit.DB.Wall>();
            foreach (var w in allWalls)
            {
                try
                {
                    var baseLevelId = w.LevelId;
                    var baseLevel = baseLevelId != null && baseLevelId != ElementId.InvalidElementId
                        ? doc.GetElement(baseLevelId) as Level
                        : null;
                    if (baseLevel == null) continue;

                    double baseMm = UnitHelper.FtToMm(baseLevel.Elevation);

                    // Height: WALL_USER_HEIGHT_PARAM (unconnected height) if positive, else bbox Z span
                    double loMm = baseMm;
                    double hiMm = baseMm;

                    double heightFt = 0.0;
                    var hParam = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (hParam != null && hParam.StorageType == StorageType.Double)
                    {
                        try { heightFt = hParam.AsDouble(); } catch { heightFt = 0.0; }
                    }

                    double heightMm = UnitHelper.FtToMm(heightFt);
                    if (heightMm > 1e-3)
                    {
                        hiMm = baseMm + heightMm;
                    }
                    else
                    {
                        var bb = w.get_BoundingBox(null);
                        if (bb != null)
                        {
                            loMm = UnitHelper.FtToMm(bb.Min.Z);
                            hiMm = UnitHelper.FtToMm(bb.Max.Z);
                        }
                    }

                    bool intersectsZ = !(hiMm < levelElevMm - 1.0 || loMm > levelElevMm + 50000.0);
                    if (!intersectsZ) continue;

                    // XY bbox coarse filter
                    var wbb = w.get_BoundingBox(null);
                    if (wbb == null) continue;

                    double wMinX = UnitHelper.FtToMm(wbb.Min.X);
                    double wMinY = UnitHelper.FtToMm(wbb.Min.Y);
                    double wMaxX = UnitHelper.FtToMm(wbb.Max.X);
                    double wMaxY = UnitHelper.FtToMm(wbb.Max.Y);

                    if (wMaxX < minXmm - searchMarginMm || wMinX > maxXmm + searchMarginMm ||
                        wMaxY < minYmm - searchMarginMm || wMinY > maxYmm + searchMarginMm)
                        continue;

                    candidateWalls.Add(w);
                }
                catch
                {
                    // ignore
                }
            }

            var tol = new GeometryUtils.Tolerance(distMm: 1.0, angleDeg: maxAngleDeg);

            var matches = new Dictionary<int, WallMatch>();

            foreach (var bs in boundarySegments)
            {
                var seg = bs.Seg2;

                foreach (var w in candidateWalls)
                {
                    if (w == null) continue;

                    Curve curve = null;
                    try
                    {
                        var lc = w.Location as LocationCurve;
                        curve = lc?.Curve;
                    }
                    catch { curve = null; }

                    if (curve == null) continue;

                    // Wall location segment in mm (XY)
                    XYZ p0 = curve.GetEndPoint(0);
                    XYZ p1 = curve.GetEndPoint(1);

                    var wSeg = new GeometryUtils.Segment2(
                        new GeometryUtils.Vec2(UnitHelper.FtToMm(p0.X), UnitHelper.FtToMm(p0.Y)),
                        new GeometryUtils.Vec2(UnitHelper.FtToMm(p1.X), UnitHelper.FtToMm(p1.Y))
                    );

                    var analysis = GeometryUtils.AnalyzeSegments2D(seg, wSeg, tol);
                    if (!analysis.ok) continue;
                    if (!analysis.isParallel) continue;

                    // Distance between parallel lines
                    double distMm = analysis.distanceBetweenParallelMm ?? double.MaxValue;
                    if (distMm > maxOffsetMm) continue;

                    // Overlap length along boundary segment axis (mm)
                    // NOTE: GeometryUtils.AnalyzeSegments2D only computes overlapLength for colinear segments.
                    // For finish-takeoff we must accept parallel offset lines (Finish/Core/etc), so we compute
                    // overlap by 1D projection here.
                    double overlapMm = ComputeProjectedOverlapMm(seg, wSeg);
                    if (overlapMm < minOverlapMm) continue;

                    int wid = w.Id.IntValue();
                    if (!matches.TryGetValue(wid, out var m))
                    {
                        string typeName = string.Empty;
                        int typeId = 0;
                        string kind = string.Empty;
                        try
                        {
                            typeId = w.GetTypeId().IntValue();
                            var wt = doc.GetElement(w.GetTypeId()) as WallType;
                            typeName = wt?.Name ?? string.Empty;
                            kind = wt == null ? string.Empty :
                                (wt.Kind == WallKind.Basic ? "Basic" : wt.Kind == WallKind.Curtain ? "Curtain" : "Stacked");
                        }
                        catch { /* ignore */ }

                        m = new WallMatch
                        {
                            WallId = wid,
                            UniqueId = w.UniqueId ?? string.Empty,
                            TypeId = typeId,
                            TypeName = typeName,
                            Kind = kind,
                            MinDistanceMm = distMm,
                            MaxOverlapMm = overlapMm,
                            StartFt = p0,
                            EndFt = p1,
                            Orientation = SafeWallOrientation(w)
                        };
                        matches[wid] = m;
                    }
                    else
                    {
                        if (distMm < m.MinDistanceMm) m.MinDistanceMm = distMm;
                        if (overlapMm > m.MaxOverlapMm) m.MaxOverlapMm = overlapMm;
                    }

                    m.Segments.Add((bs.LoopIndex, bs.SegmentIndex));
                }
            }

            var wallDtos = matches.Values
                .OrderByDescending(x => x.MaxOverlapMm)
                .ThenBy(x => x.MinDistanceMm)
                .Select(m => new
                {
                    wallId = m.WallId,
                    uniqueId = m.UniqueId,
                    typeId = m.TypeId,
                    typeName = m.TypeName,
                    kind = m.Kind,
                    minDistanceMm = Math.Round(m.MinDistanceMm, 3),
                    maxOverlapMm = Math.Round(m.MaxOverlapMm, 3),
                    start = new
                    {
                        x = Math.Round(UnitHelper.FtToMm(m.StartFt.X), 3),
                        y = Math.Round(UnitHelper.FtToMm(m.StartFt.Y), 3),
                        z = Math.Round(UnitHelper.FtToMm(m.StartFt.Z), 3)
                    },
                    end = new
                    {
                        x = Math.Round(UnitHelper.FtToMm(m.EndFt.X), 3),
                        y = Math.Round(UnitHelper.FtToMm(m.EndFt.Y), 3),
                        z = Math.Round(UnitHelper.FtToMm(m.EndFt.Z), 3)
                    },
                    orientation = new
                    {
                        x = Math.Round(m.Orientation.X, 6),
                        y = Math.Round(m.Orientation.Y, 6),
                        z = Math.Round(m.Orientation.Z, 6)
                    },
                    segments = m.Segments.Select(s => new { loopIndex = s.loopIndex, segmentIndex = s.segmentIndex }).ToArray()
                })
                .ToArray();

            var matchedWallEls = new List<Autodesk.Revit.DB.Wall>();
            foreach (var wid in matches.Keys)
            {
                try
                {
                    var w = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wid)) as Autodesk.Revit.DB.Wall;
                    if (w != null) matchedWallEls.Add(w);
                }
                catch { /* ignore */ }
            }

            return new WallMatchResult
            {
                Walls = wallDtos,
                MatchedWallElements = matchedWallEls,
                Debug = new
                {
                    candidateWallCount = candidateWalls.Count,
                    matchedWallCount = wallDtos.Length,
                    levelId = room.LevelId.IntValue(),
                    levelElevationMm = levelElevMm,
                    maxOffsetMm,
                    minOverlapMm,
                    maxAngleDeg,
                    searchMarginMm
                }
            };
        }

        private static double ComputeProjectedOverlapMm(GeometryUtils.Segment2 axisSeg, GeometryUtils.Segment2 otherSeg)
        {
            // axis: [0, axisLen] on the axisSeg direction
            double ax = axisSeg.A.X;
            double ay = axisSeg.A.Y;
            double dx = axisSeg.B.X - ax;
            double dy = axisSeg.B.Y - ay;
            double axisLen = Math.Sqrt(dx * dx + dy * dy);
            if (axisLen <= 1e-9) return 0.0;

            double ux = dx / axisLen;
            double uy = dy / axisLen;

            double p1 = (otherSeg.A.X - ax) * ux + (otherSeg.A.Y - ay) * uy;
            double p2 = (otherSeg.B.X - ax) * ux + (otherSeg.B.Y - ay) * uy;
            double lo2 = Math.Min(p1, p2);
            double hi2 = Math.Max(p1, p2);

            double lo = Math.Max(0.0, lo2);
            double hi = Math.Min(axisLen, hi2);

            if (hi <= lo) return 0.0;
            return hi - lo;
        }

        private static XYZ SafeWallOrientation(Autodesk.Revit.DB.Wall w)
        {
            if (w == null) return XYZ.Zero;
            try
            {
                var o = w.Orientation;
                if (o == null) return XYZ.Zero;
                return o;
            }
            catch
            {
                return XYZ.Zero;
            }
        }

        // Column auto-detection (reused)
        private static IList<ElementId> AutoDetectColumnsInRoom(Document doc, Autodesk.Revit.DB.Architecture.Room room, double searchMarginMm)
        {
            var result = new List<ElementId>();
            if (doc == null || room == null) return result;

            var roomBb = room.get_BoundingBox(null);
            if (roomBb == null) return result;

            double marginFt = UnitHelper.MmToFt(searchMarginMm);
            var min = new XYZ(roomBb.Min.X - marginFt, roomBb.Min.Y - marginFt, roomBb.Min.Z - marginFt);
            var max = new XYZ(roomBb.Max.X + marginFt, roomBb.Max.Y + marginFt, roomBb.Max.Z + marginFt);
            var outline = new Outline(min, max);
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            var filters = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_Columns),
                new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns)
            };
            var catFilter = new LogicalOrFilter(filters);

            var collector = new FilteredElementCollector(doc)
                .WherePasses(catFilter)
                .WherePasses(bbFilter)
                .WhereElementIsNotElementType();

            foreach (var e in collector)
            {
                var fi = e as FamilyInstance;
                if (fi == null) continue;
                var bb = fi.get_BoundingBox(null);
                if (bb == null) continue;

                if (IntersectsRoomApprox(room, bb, roomBb))
                    result.Add(fi.Id);
            }

            return result;
        }

        private static bool IntersectsRoomApprox(Autodesk.Revit.DB.Architecture.Room room, BoundingBoxXYZ colBb, BoundingBoxXYZ roomBb)
        {
            if (room == null || colBb == null || roomBb == null) return false;

            double zMin = Math.Max(colBb.Min.Z, roomBb.Min.Z);
            double zMax = Math.Min(colBb.Max.Z, roomBb.Max.Z);
            if (zMax <= zMin) return false;

            double zMid = 0.5 * (zMin + zMax);

            double xMin = colBb.Min.X, xMax = colBb.Max.X;
            double yMin = colBb.Min.Y, yMax = colBb.Max.Y;

            double xMid = 0.5 * (xMin + xMax);
            double yMid = 0.5 * (yMin + yMax);

            var pts = new[]
            {
                new XYZ(xMid, yMid, zMid),
                new XYZ(xMin, yMin, zMid),
                new XYZ(xMax, yMin, zMid),
                new XYZ(xMax, yMax, zMid),
                new XYZ(xMin, yMax, zMid)
            };

            foreach (var pt in pts)
            {
                try
                {
                    if (room.IsPointInRoom(pt)) return true;
                }
                catch
                {
                    // ignore
                }
            }

            return false;
        }

        private static BoundingBoxXYZ ExpandBoundingBox(BoundingBoxXYZ bb, double marginFt)
        {
            if (bb == null) return null;
            var min = new XYZ(bb.Min.X - marginFt, bb.Min.Y - marginFt, bb.Min.Z - marginFt);
            var max = new XYZ(bb.Max.X + marginFt, bb.Max.Y + marginFt, bb.Max.Z + marginFt);
            return new BoundingBoxXYZ { Min = min, Max = max };
        }

        private static bool BoundingBoxIntersectsXYAndZ(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null) return false;
            if (a.Max.X < b.Min.X || a.Min.X > b.Max.X) return false;
            if (a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y) return false;
            if (a.Max.Z < b.Min.Z || a.Min.Z > b.Max.Z) return false;
            return true;
        }

        private static object[] BuildWallTypeLayers(Document doc, List<Autodesk.Revit.DB.Wall> walls)
        {
            var result = new List<WallTypeLayersDto>();
            if (doc == null || walls == null || walls.Count == 0) return result.ToArray();

            var typeIds = walls
                .Select(w => w?.GetTypeId())
                .Where(id => id != null && id != ElementId.InvalidElementId)
                .Select(id => id.IntValue())
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            foreach (var tid in typeIds)
            {
                var wt = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid)) as WallType;
                if (wt == null) continue;

                string kind = wt.Kind == WallKind.Basic ? "Basic" : wt.Kind == WallKind.Curtain ? "Curtain" : "Stacked";
                double widthMm = 0.0;
                try { widthMm = UnitHelper.FtToMm(wt.Width); } catch { widthMm = 0.0; }

                var layers = new List<object>();
                if (wt.Kind == WallKind.Basic)
                {
                    try
                    {
                        var cs = wt.GetCompoundStructure();
                        var raw = cs?.GetLayers();
                        if (raw != null)
                        {
                            int exteriorShell = 0;
                            int interiorShell = 0;
                            int firstCore = -1;
                            int lastCore = -1;
                            try
                            {
                                exteriorShell = cs.GetNumberOfShellLayers(ShellLayerType.Exterior);
                                interiorShell = cs.GetNumberOfShellLayers(ShellLayerType.Interior);
                                firstCore = cs.GetFirstCoreLayerIndex();
                                lastCore = cs.GetLastCoreLayerIndex();
                            }
                            catch { /* ignore */ }

                            for (int i = 0; i < raw.Count; i++)
                            {
                                var l = raw[i];
                                var mat = doc.GetElement(l.MaterialId) as Autodesk.Revit.DB.Material;

                                bool isCore = (firstCore >= 0 && lastCore >= 0 && i >= firstCore && i <= lastCore);
                                bool isExteriorShell = (i < exteriorShell);
                                bool isInteriorShell = (i >= raw.Count - interiorShell);

                                layers.Add(new
                                {
                                    index = i,
                                    function = l.Function.ToString(),
                                    materialId = l.MaterialId.IntValue(),
                                    materialName = mat?.Name ?? string.Empty,
                                    thicknessMm = Math.Round(UnitHelper.FtToMm(l.Width), 3),
                                    isCore,
                                    isExteriorShell,
                                    isInteriorShell
                                });
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }

                result.Add(new WallTypeLayersDto
                {
                    typeId = tid,
                    typeName = wt.Name ?? string.Empty,
                    kind = kind,
                    widthMm = Math.Round(widthMm, 3),
                    layers = layers.ToArray()
                });
            }

            return result.Cast<object>().ToArray();
        }
    }
}
