// ============================================================================
// File   : Commands/Area/CreateAreasFromRoomsByMaterialCoreCenterCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Purpose:
//   - create_areas_from_rooms_by_material_corecenter
// Notes  :
//   - Designed to reduce JSON-RPC roundtrips by running:
//       1) copy room boundaries -> area boundary lines
//       2) create areas (point-in-room)
//       3) adjust area boundary lines -> specified material core center
//     in a single MCP command.
//   - Uses the existing core-center adjust implementation for robustness.
//   - refreshView is applied only once at the end (optional).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Area
{
    public class CreateAreasFromRoomsByMaterialCoreCenterCommand : IRevitCommandHandler
    {
        public string CommandName => "create_areas_from_rooms_by_material_corecenter";

        private static Curve PreferCurveElementButKeepSegmentEndpoints(Document doc, Curve segmentCurve, Curve curveElementCurve)
        {
            if (segmentCurve == null) return curveElementCurve;
            if (curveElementCurve == null) return segmentCurve;

            double shortTolFt = 1e-6;
            try { shortTolFt = doc?.Application?.ShortCurveTolerance ?? shortTolFt; } catch { }

            XYZ p0 = null;
            XYZ p1 = null;
            try
            {
                p0 = segmentCurve.GetEndPoint(0);
                p1 = segmentCurve.GetEndPoint(1);
            }
            catch
            {
                return curveElementCurve;
            }

            var line = curveElementCurve as Line;
            if (line != null)
            {
                try
                {
                    var unbound = Line.CreateUnbound(line.Origin, line.Direction);
                    var r0 = unbound.Project(p0);
                    var r1 = unbound.Project(p1);
                    var q0 = (r0 != null && r0.XYZPoint != null) ? r0.XYZPoint : p0;
                    var q1 = (r1 != null && r1.XYZPoint != null) ? r1.XYZPoint : p1;
                    if (q0 != null && q1 != null && q0.DistanceTo(q1) > shortTolFt)
                        return Line.CreateBound(q0, q1);
                }
                catch { }

                // Fallback: keep the closed-loop endpoints (even if projection failed)
                try
                {
                    if (p0 != null && p1 != null && p0.DistanceTo(p1) > shortTolFt)
                        return Line.CreateBound(p0, p1);
                }
                catch { }

                return curveElementCurve;
            }

            // Non-line curve elements are rare for Room Separation Lines.
            // To preserve a closed loop, prefer the boundary-segment curve.
            return segmentCurve;
        }

        private static List<ElementId> DistinctIds(IEnumerable<ElementId> ids)
        {
            var seen = new HashSet<int>();
            var list = new List<ElementId>();
            foreach (var id in ids ?? Enumerable.Empty<ElementId>())
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                if (seen.Add(id.IntValue())) list.Add(id);
            }
            return list;
        }

        private static IEnumerable<ElementId> ParseIds(JToken tok)
        {
            if (tok == null) yield break;

            if (tok.Type == JTokenType.Array)
            {
                foreach (var t in (JArray)tok)
                    foreach (var id in ParseIds(t))
                        yield return id;
                yield break;
            }

            if (tok.Type == JTokenType.Integer)
            {
                var v = tok.Value<int>();
                if (v != 0) yield return Autodesk.Revit.DB.ElementIdCompat.From(v);
                yield break;
            }

            if (tok.Type == JTokenType.String)
            {
                if (int.TryParse(tok.Value<string>(), out var v) && v != 0)
                    yield return Autodesk.Revit.DB.ElementIdCompat.From(v);
                yield break;
            }

            if (tok.Type == JTokenType.Object)
            {
                var jo = (JObject)tok;
                int v =
                    jo.Value<int?>("elementId")
                    ?? jo.Value<int?>("id")
                    ?? jo.Value<int?>("roomId")
                    ?? jo.Value<int?>("wallId")
                    ?? 0;
                if (v != 0) yield return Autodesk.Revit.DB.ElementIdCompat.From(v);
            }
        }

        private static List<ElementId> ParseIdsFromKeys(JObject p, params string[] keys)
        {
            var list = new List<ElementId>();
            foreach (var k in keys ?? Array.Empty<string>())
            {
                if (!p.TryGetValue(k, out var tok) || tok == null) continue;
                list.AddRange(ParseIds(tok));
            }
            return DistinctIds(list);
        }

        private static void AddRoomIdsFromElementSelection(Document doc, IEnumerable<ElementId> selectedIds, List<ElementId> roomIds)
        {
            if (doc == null || roomIds == null) return;

            foreach (var id in selectedIds ?? Enumerable.Empty<ElementId>())
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                Element el = null;
                try { el = doc.GetElement(id); } catch { el = null; }
                if (el == null) continue;

                if (el is Autodesk.Revit.DB.Architecture.Room)
                {
                    roomIds.Add(el.Id);
                    continue;
                }

                // RoomTag can be selected instead of the Room element.
                if (el is RoomTag roomTag)
                {
                    try
                    {
                        var r = roomTag.Room;
                        if (r != null) roomIds.Add(r.Id);
                    }
                    catch { /* ignore */ }
                    continue;
                }
            }
        }

        private static bool TryGetRoomPlacementPoint(Document doc, Autodesk.Revit.DB.Architecture.Room room, out XYZ point, out string msg)
        {
            point = null;
            msg = null;
            if (room == null) { msg = "room is null"; return false; }

            try
            {
                var lp = room.Location as LocationPoint;
                if (lp != null && lp.Point != null)
                {
                    point = lp.Point;
                    return true;
                }
            }
            catch { }

            // Fallback (slower): spatial geometry centroid
            try
            {
                var calc = new SpatialElementGeometryCalculator(doc);
                var res = calc.CalculateSpatialElementGeometry(room);
                var solid = res?.GetGeometry();
                if (solid != null && solid.Volume > 1e-9)
                {
                    point = solid.ComputeCentroid();
                    return true;
                }
                msg = "Room placement point not available (no LocationPoint / centroid).";
                return false;
            }
            catch (Exception ex)
            {
                msg = "Room centroid failed: " + ex.Message;
                return false;
            }
        }

        // Simple bucket to reduce duplicate checks (hash collisions are OK; final check uses CurveEquals)
        private static string CurveBucketKeyXY(Curve c, double bucketFt)
        {
            if (c == null) return "null";
            if (bucketFt <= 1e-9) bucketFt = 1.0;
            XYZ p0 = c.GetEndPoint(0);
            XYZ p1 = c.GetEndPoint(1);
            double minX = Math.Min(p0.X, p1.X);
            double minY = Math.Min(p0.Y, p1.Y);
            double maxX = Math.Max(p0.X, p1.X);
            double maxY = Math.Max(p0.Y, p1.Y);
            int ix0 = (int)Math.Round(minX / bucketFt);
            int iy0 = (int)Math.Round(minY / bucketFt);
            int ix1 = (int)Math.Round(maxX / bucketFt);
            int iy1 = (int)Math.Round(maxY / bucketFt);
            int il = (int)Math.Round(c.ApproximateLength / bucketFt);
            return $"{c.GetType().Name}:{ix0}:{iy0}:{ix1}:{iy1}:{il}";
        }

        private static Dictionary<string, List<CurveElement>> BuildCurveBuckets(IEnumerable<CurveElement> curves, double bucketFt)
        {
            var map = new Dictionary<string, List<CurveElement>>(StringComparer.Ordinal);
            foreach (var ce in curves ?? Enumerable.Empty<CurveElement>())
            {
                if (ce == null) continue;
                var gc = ce.GeometryCurve;
                if (gc == null) continue;
                var key = CurveBucketKeyXY(gc, bucketFt);
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<CurveElement>();
                    map[key] = list;
                }
                list.Add(ce);
            }
            return map;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。", units = UnitHelper.DefaultUnitsMeta() };

            var uidoc = uiapp.ActiveUIDocument;
            var p = (JObject)(cmd.Params ?? new JObject());

            bool refreshView = p.Value<bool?>("refreshView") ?? false;
            var options = p["options"] as JObject;

            bool copyBoundaries =
                (options?.Value<bool?>("copy_boundaries"))
                ?? (options?.Value<bool?>("copyBoundaries"))
                ?? p.Value<bool?>("copyBoundaries")
                ?? p.Value<bool?>("copy_boundaries")
                ?? true;

            bool createAreas =
                (options?.Value<bool?>("create_areas"))
                ?? (options?.Value<bool?>("createAreas"))
                ?? p.Value<bool?>("createAreas")
                ?? p.Value<bool?>("create_areas")
                ?? true;

            bool adjustBoundaries =
                (options?.Value<bool?>("adjust_boundaries"))
                ?? (options?.Value<bool?>("adjustBoundaries"))
                ?? p.Value<bool?>("adjustBoundaries")
                ?? p.Value<bool?>("adjust_boundaries")
                ?? true;

            bool collectWallsFromRoomBoundaries =
                (options?.Value<bool?>("collect_walls_from_room_boundaries"))
                ?? (options?.Value<bool?>("collectWallsFromRoomBoundaries"))
                ?? p.Value<bool?>("collectWallsFromRoomBoundaries")
                ?? p.Value<bool?>("collect_walls_from_room_boundaries")
                ?? true;

            bool returnAreaMetrics =
                (options?.Value<bool?>("return_area_metrics"))
                ?? (options?.Value<bool?>("returnAreaMetrics"))
                ?? p.Value<bool?>("returnAreaMetrics")
                ?? p.Value<bool?>("return_area_metrics")
                ?? true;

            bool compareRoomAndArea =
                (options?.Value<bool?>("compare_room_and_area"))
                ?? (options?.Value<bool?>("compareRoomAndArea"))
                ?? p.Value<bool?>("compareRoomAndArea")
                ?? p.Value<bool?>("compare_room_and_area")
                ?? true;

            bool includeDebug =
                (options?.Value<bool?>("include_debug"))
                ?? (options?.Value<bool?>("includeDebug"))
                ?? p.Value<bool?>("includeDebug")
                ?? p.Value<bool?>("include_debug")
                ?? false;

            bool skipExisting =
                (options?.Value<bool?>("skip_existing"))
                ?? (options?.Value<bool?>("skipExisting"))
                ?? p.Value<bool?>("skipExisting")
                ?? p.Value<bool?>("skip_existing")
                ?? true;

            // Duplicate merging tolerance for boundary copy
            double mergeTolMm =
                (options?.Value<double?>("mergeToleranceMm"))
                ?? (options?.Value<double?>("merge_tolerance_mm"))
                ?? p.Value<double?>("mergeToleranceMm")
                ?? p.Value<double?>("merge_tolerance_mm")
                ?? 3.0;
            double mergeTolFt = UnitHelper.MmToFt(mergeTolMm);
            double bucketFt = Math.Max(mergeTolFt * 5.0, UnitHelper.MmToFt(50.0)); // coarse bucket; final equals uses mergeTolFt

            // Paging (optional)
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 999999);

            var totalSw = Stopwatch.StartNew();
            var warnings = new List<string>();

            try
            {
                Level level;
                var vp = AreaBoundaryMaterialCoreCenterUtil.ResolveAreaPlanView(doc, uiapp, p, out level);

                // Room boundary extraction (SpatialElementBoundaryLocation)
                // Default for this workflow is CoreCenter (most area/boundary workflows want wall core centerline),
                // but callers can override via boundaryLocation / options.boundaryLocation.
                string boundaryLocationStr =
                    (options?.Value<string>("boundaryLocation"))
                    ?? (options?.Value<string>("boundary_location"))
                    ?? p.Value<string>("boundaryLocation")
                    ?? p.Value<string>("boundary_location");

                var roomBoundaryOpts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr, SpatialElementBoundaryLocation.CoreCenter)
                };

                // Boundary curve source (copy step)
                // Default is calculated boundary (BoundarySegment.GetCurve()). If PreferLineElements is selected,
                // and a BoundarySegment has ElementId pointing to a CurveElement (e.g. Room Separation Line),
                // its GeometryCurve is used instead.
                string boundaryCurveSourceStr =
                    (options?.Value<string>("boundaryCurveSource"))
                    ?? (options?.Value<string>("boundary_curve_source"))
                    ?? p.Value<string>("boundaryCurveSource")
                    ?? p.Value<string>("boundary_curve_source");

                bool? preferLineElementsOverride =
                    (options?.Value<bool?>("preferLineElements"))
                    ?? (options?.Value<bool?>("prefer_line_elements"))
                    ?? p.Value<bool?>("preferLineElements")
                    ?? p.Value<bool?>("prefer_line_elements");

                var boundaryCurveSource = SpatialUtils.ParseBoundaryCurveSource(boundaryCurveSourceStr, SpatialUtils.BoundaryCurveSource.BoundarySegment);
                if (preferLineElementsOverride.HasValue)
                {
                    boundaryCurveSource = preferLineElementsOverride.Value
                        ? SpatialUtils.BoundaryCurveSource.PreferLineElements
                        : SpatialUtils.BoundaryCurveSource.BoundarySegment;
                }
                bool preferLineElements = boundaryCurveSource == SpatialUtils.BoundaryCurveSource.PreferLineElements;

                // Rooms: params -> selection fallback
                var roomIds = new List<ElementId>();
                roomIds.AddRange(ParseIdsFromKeys(p,
                    "room_element_ids", "roomElementIds", "roomIds", "room_ids",
                    "rooms", "roomIdsFilter"));

                if (roomIds.Count == 0)
                {
                    try
                    {
                        var sel = uidoc?.Selection?.GetElementIds();
                        if (sel != null) AddRoomIdsFromElementSelection(doc, sel, roomIds);
                    }
                    catch { }
                    roomIds = DistinctIds(roomIds);
                }

                // Fallback: selection can be cleared quickly (e.g., when switching views).
                // Use the last non-empty selection stash (very recent only) to keep workflows stable.
                if (roomIds.Count == 0)
                {
                    int maxAgeMs =
                        (options?.Value<int?>("selection_stash_max_age_ms"))
                        ?? (options?.Value<int?>("selectionStashMaxAgeMs"))
                        ?? 5000;

                    try
                    {
                        var snap = SelectionStash.GetLastNonEmptySnapshot();
                        var ageMs = (DateTime.UtcNow - snap.ObservedUtc).TotalMilliseconds;
                        string docPath = string.Empty;
                        try { docPath = doc.PathName ?? string.Empty; } catch { }

                        if (snap.Ids != null && snap.Ids.Length > 0
                            && (maxAgeMs <= 0 || ageMs <= maxAgeMs)
                            && (string.IsNullOrEmpty(snap.DocPath) || string.Equals(snap.DocPath, docPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            var snapIds = new List<ElementId>(snap.Ids.Length);
                            foreach (var i in snap.Ids)
                            {
                                if (i == 0) continue;
                                snapIds.Add(Autodesk.Revit.DB.ElementIdCompat.From(i));
                            }
                            AddRoomIdsFromElementSelection(doc, snapIds, roomIds);
                            roomIds = DistinctIds(roomIds);
                            if (roomIds.Count > 0)
                                warnings.Add($"Room selection fallback: used last non-empty selection stash (ageMs={Math.Round(ageMs)}).");
                        }
                    }
                    catch { }
                }

                // Filter to Rooms on the AreaPlan level
                var roomsResolved = new List<Autodesk.Revit.DB.Architecture.Room>();
                foreach (var rid in roomIds)
                {
                    var r = doc.GetElement(rid) as Autodesk.Revit.DB.Architecture.Room;
                    if (r == null) continue;
                    if (r.LevelId == null || r.LevelId == ElementId.InvalidElementId) continue;
                    if (r.LevelId.IntValue() != level.Id.IntValue()) continue;
                    roomsResolved.Add(r);
                }

                // Stable ordering + paging
                roomsResolved = roomsResolved
                    .OrderBy(r => r.Number ?? "")
                    .ThenBy(r => r.Name ?? "")
                    .ThenBy(r => r.Id.IntValue())
                    .ToList();

                if (roomsResolved.Count == 0)
                    return new
                    {
                        ok = false,
                        msg = "Area Plan と同じレベルのRoomが見つかりません（room_element_ids を指定するか、同レベルのRoom/RoomTag を選択してください）。",
                        warnings = warnings.Count > 0 ? warnings : null,
                        units = UnitHelper.DefaultUnitsMeta()
                    };

                int endExclusive = Math.Min(roomsResolved.Count, startIndex + batchSize);
                var roomsBatch = roomsResolved.Skip(startIndex).Take(endExclusive - startIndex).ToList();
                bool completed = endExclusive >= roomsResolved.Count;
                int? nextIndex = completed ? (int?)null : endExclusive;

                // Walls: params -> selection(walls only) fallback; may be empty, then optionally collect from room boundaries
                var wallIds = ParseIdsFromKeys(p, "wall_element_ids", "wallElementIds", "wallIds", "wall_ids");
                if (wallIds.Count > 0)
                {
                    // Filter to walls only
                    wallIds = DistinctIds(wallIds.Where(id => doc.GetElement(id) is Autodesk.Revit.DB.Wall));
                }

                if (wallIds.Count == 0)
                {
                    try
                    {
                        var sel = uidoc?.Selection?.GetElementIds();
                        if (sel != null)
                        {
                            foreach (var id in sel)
                            {
                                if (doc.GetElement(id) is Autodesk.Revit.DB.Wall)
                                    wallIds.Add(id);
                            }
                        }
                    }
                    catch { }
                    wallIds = DistinctIds(wallIds);
                }
                bool hasExplicitWalls = wallIds.Count > 0;

                // Pre-check: if we will adjust, material must be resolvable
                ElementId materialId = ElementId.InvalidElementId;
                string materialName = null;
                if (adjustBoundaries)
                {
                    if (!AreaBoundaryMaterialCoreCenterUtil.TryResolveMaterialId(doc, p, out materialId, out materialName, out var matErr))
                        return new { ok = false, msg = matErr, units = UnitHelper.DefaultUnitsMeta() };
                }

                var createdBoundaryLineIds = new List<int>();
                var createdAreaIds = new List<int>();
                var roomResults = new List<JObject>();

                long copyMs = 0;
                long createAreasMs = 0;
                long adjustMs = 0;
                long metricsMs = 0;

                // ----------------------------------------------------------------
                // Tx1: copy boundaries (optional) + create areas (optional)
                // ----------------------------------------------------------------
                using (var tx = new Transaction(doc, "Create Areas from Rooms (copy boundaries + create areas)"))
                {
                    tx.Start();
                    TxnUtil.ConfigureProceedWithWarnings(tx);

                    var sp = AreaBoundaryMaterialCoreCenterUtil.GetOrCreateSketchPlane(doc, vp, level);

                    var swCopyBoundaries = new Stopwatch();
                    var swAreaCreate = new Stopwatch();

                    List<CurveElement> existing = skipExisting ? AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList() : new List<CurveElement>();
                    var buckets = BuildCurveBuckets(existing, bucketFt);

                    var boundaryCollectedWalls = new HashSet<int>();

                    // We also keep a flat list for final CurveEquals check within bucket
                    foreach (var room in roomsBatch)
                    {
                        int roomId = room.Id.IntValue();
                        string roomName = room.Name ?? "";
                        string roomNumber = room.Number ?? "";
                        double roomAreaM2 = Math.Round(UnitHelper.ToExternal(room.Area, SpecTypeId.Area) ?? 0.0, 3);

                        // Copy boundaries
                        int createdLinesForRoom = 0;
                        int skippedLinesForRoom = 0;
                        int failedLinesForRoom = 0;
                        int segmentsTotalForRoom = 0;
                        int curveElementsUsedForRoom = 0;

                        if (copyBoundaries) swCopyBoundaries.Start();

                        IList<IList<BoundarySegment>> loops = null;
                        if (copyBoundaries || collectWallsFromRoomBoundaries)
                        {
                            try
                            {
                                loops = room.GetBoundarySegments(roomBoundaryOpts);
                            }
                            catch { loops = null; }
                        }

                        if (loops != null)
                        {
                            foreach (var loop in loops)
                            {
                                if (loop == null) continue;
                                foreach (var seg in loop)
                                {
                                    try
                                    {
                                        if (seg == null) continue;
                                        segmentsTotalForRoom++;

                                        if (collectWallsFromRoomBoundaries)
                                        {
                                            try
                                            {
                                                var eid = seg.ElementId;
                                                if (eid != null && eid != ElementId.InvalidElementId)
                                                    boundaryCollectedWalls.Add(eid.IntValue());
                                            }
                                            catch { }
                                        }

                                        if (!copyBoundaries) continue;

                                        var segCurve = seg.GetCurve();
                                        if (segCurve == null) continue;
                                        var c = segCurve;

                                        if (preferLineElements)
                                        {
                                            try
                                            {
                                                var eid = seg.ElementId;
                                                if (eid != null && eid != ElementId.InvalidElementId)
                                                {
                                                    var e = doc.GetElement(eid);
                                                    if (e is CurveElement ce && ce.GeometryCurve != null)
                                                    {
                                                        c = PreferCurveElementButKeepSegmentEndpoints(doc, segCurve, ce.GeometryCurve);
                                                        curveElementsUsedForRoom++;
                                                    }
                                                }
                                            }
                                            catch { }
                                        }

                                        var cFlat = AreaBoundaryMaterialCoreCenterUtil.FlattenCurveToZ(c, level.Elevation, out _);
                                        if (cFlat == null) continue;

                                        bool duplicate = false;
                                        var key = CurveBucketKeyXY(cFlat, bucketFt);
                                        if (buckets.TryGetValue(key, out var candidates))
                                        {
                                            foreach (var ce in candidates)
                                            {
                                                if (ce == null) continue;
                                                if (AreaCommon.CurveEquals(ce.GeometryCurve, cFlat, mergeTolFt)) { duplicate = true; break; }
                                            }
                                        }

                                        if (duplicate)
                                        {
                                            skippedLinesForRoom++;
                                            continue;
                                        }

                                        var ceNew = doc.Create.NewAreaBoundaryLine(sp, cFlat, vp);
                                        createdBoundaryLineIds.Add(ceNew.Id.IntValue());
                                        createdLinesForRoom++;

                                        // Update bucket
                                        if (!buckets.TryGetValue(key, out var list))
                                        {
                                            list = new List<CurveElement>();
                                            buckets[key] = list;
                                        }
                                        list.Add(ceNew);
                                    }
                                    catch
                                    {
                                        if (copyBoundaries) failedLinesForRoom++;
                                    }
                                }
                            }
                        }
                        else if (copyBoundaries || collectWallsFromRoomBoundaries)
                        {
                            warnings.Add($"Room {roomId} ({roomNumber} {roomName}): boundary segments not available.");
                        }

                        if (copyBoundaries) swCopyBoundaries.Stop();

                        // Create Area
                        int? areaIdCreated = null;
                        if (createAreas)
                        {
                            swAreaCreate.Start();
                            if (!TryGetRoomPlacementPoint(doc, room, out var rp, out var rpErr))
                            {
                                warnings.Add($"Room {roomId} ({roomNumber} {roomName}): area placement point not found. {rpErr}");
                            }
                            else
                            {
                                try
                                {
                                    var uv = new UV(rp.X, rp.Y);
                                    var area = doc.Create.NewArea(vp, uv);
                                    areaIdCreated = area.Id.IntValue();
                                    createdAreaIds.Add(area.Id.IntValue());
                                }
                                catch (Exception ex)
                                {
                                    warnings.Add($"Room {roomId} ({roomNumber} {roomName}): create Area failed: {ex.Message}");
                                }
                            }
                            swAreaCreate.Stop();
                        }

                        roomResults.Add(new JObject
                        {
                            ["roomId"] = roomId,
                            ["roomNumber"] = roomNumber,
                            ["roomName"] = roomName,
                            ["roomAreaM2"] = roomAreaM2,
                            ["createdAreaId"] = areaIdCreated.HasValue ? (JToken)new JValue(areaIdCreated.Value) : JValue.CreateNull(),
                            ["boundaryLines"] = new JObject
                            {
                                ["created"] = createdLinesForRoom,
                                ["skipped"] = skippedLinesForRoom,
                                ["failed"] = failedLinesForRoom,
                                ["segmentsTotal"] = segmentsTotalForRoom,
                                ["curveElementsUsed"] = curveElementsUsedForRoom
                            }
                        });
                    }

                    copyMs = swCopyBoundaries.ElapsedMilliseconds;
                    createAreasMs = swAreaCreate.ElapsedMilliseconds;

                    tx.Commit();

                    // If walls were not explicitly provided, use walls collected from boundary segments (walls only)
                    if (!hasExplicitWalls && collectWallsFromRoomBoundaries && boundaryCollectedWalls.Count > 0)
                    {
                        var wset = new HashSet<int>();
                        foreach (var wi in boundaryCollectedWalls)
                        {
                            if (wset.Contains(wi)) continue;
                            var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wi));
                            if (e is Autodesk.Revit.DB.Wall) wset.Add(wi);
                        }
                        wallIds = wset.Select(x => Autodesk.Revit.DB.ElementIdCompat.From(x)).ToList();
                    }
                }

                wallIds = DistinctIds(wallIds);

                // ----------------------------------------------------------------
                // Tx2: adjust boundaries to material core center (optional)
                // ----------------------------------------------------------------
                object adjustResult = null;
                if (adjustBoundaries)
                {
                    if (wallIds.Count == 0)
                        return new { ok = false, msg = "wall_element_ids（または選択した壁）が必要です。", units = UnitHelper.DefaultUnitsMeta() };

                    if (createdAreaIds.Count == 0)
                        return new { ok = false, msg = "Area が作成されていないため調整できません。options.create_areas=true を指定してください。", units = UnitHelper.DefaultUnitsMeta() };

                    var swAdj = Stopwatch.StartNew();

                    var adjustParams = new JObject
                    {
                        ["viewId"] = vp.Id.IntValue(),
                        ["materialId"] = materialId.IntValue(),
                        ["area_element_ids"] = new JArray(createdAreaIds),
                        ["wall_element_ids"] = new JArray(wallIds.Select(x => x.IntValue())),
                        ["refreshView"] = false
                    };

                    // carry-through options (keep defaults of the adjust command)
                    var adjOpt = new JObject();
                    if (options != null)
                    {
                        if (options.TryGetValue("tolerance_mm", out var tolMmTok)) adjOpt["tolerance_mm"] = tolMmTok.DeepClone();
                        if (options.TryGetValue("toleranceMm", out var tolMmTok2)) adjOpt["toleranceMm"] = tolMmTok2.DeepClone();
                        if (options.TryGetValue("corner_tolerance_mm", out var cTolMmTok)) adjOpt["corner_tolerance_mm"] = cTolMmTok.DeepClone();
                        if (options.TryGetValue("cornerToleranceMm", out var cTolMmTok2)) adjOpt["cornerToleranceMm"] = cTolMmTok2.DeepClone();
                        if (options.TryGetValue("match_strategy", out var msTok)) adjOpt["match_strategy"] = msTok.DeepClone();
                        if (options.TryGetValue("matchStrategy", out var msTok2)) adjOpt["matchStrategy"] = msTok2.DeepClone();
                        if (options.TryGetValue("corner_resolution", out var crTok)) adjOpt["corner_resolution"] = crTok.DeepClone();
                        if (options.TryGetValue("cornerResolution", out var crTok2)) adjOpt["cornerResolution"] = crTok2.DeepClone();
                        if (options.TryGetValue("parallel_threshold", out var ptTok)) adjOpt["parallel_threshold"] = ptTok.DeepClone();
                        if (options.TryGetValue("parallelThreshold", out var ptTok2)) adjOpt["parallelThreshold"] = ptTok2.DeepClone();
                        if (options.TryGetValue("include_debug", out var idTok)) adjOpt["include_debug"] = idTok.DeepClone();
                        if (options.TryGetValue("includeDebug", out var idTok2)) adjOpt["includeDebug"] = idTok2.DeepClone();
                        if (options.TryGetValue("include_layer_details", out var ildTok)) adjOpt["include_layer_details"] = ildTok.DeepClone();
                        if (options.TryGetValue("includeLayerDetails", out var ildTok2)) adjOpt["includeLayerDetails"] = ildTok2.DeepClone();
                        if (options.TryGetValue("include_noncore", out var incTok)) adjOpt["include_noncore"] = incTok.DeepClone();
                        if (options.TryGetValue("includeNonCore", out var incTok2)) adjOpt["includeNonCore"] = incTok2.DeepClone();
                        if (options.TryGetValue("fallback_to_core_centerline", out var fbTok)) adjOpt["fallback_to_core_centerline"] = fbTok.DeepClone();
                        if (options.TryGetValue("fallbackToCoreCenterline", out var fbTok2)) adjOpt["fallbackToCoreCenterline"] = fbTok2.DeepClone();
                    }
                    // Default behavior for area workflows: do not skip walls that lack the specified material;
                    // fall back to core centerline (or wall centerline when no core exists).
                    if (!adjOpt.TryGetValue("fallback_to_core_centerline", out var _) && !adjOpt.TryGetValue("fallbackToCoreCenterline", out var _2))
                        adjOpt["fallback_to_core_centerline"] = true;
                    adjustParams["options"] = adjOpt;

                    var adjCmd = new AreaBoundaryAdjustByMaterialCoreCenterCommand();
                    adjustResult = adjCmd.Execute(uiapp, new RequestCommand { Params = adjustParams });

                    swAdj.Stop();
                    adjustMs = swAdj.ElapsedMilliseconds;
                }

                // ----------------------------------------------------------------
                // Post: metrics (optional)
                // ----------------------------------------------------------------
                if (!createAreas) compareRoomAndArea = false;

                List<object> areaMetrics = null;
                object areaRoomComparisonSummary = null;

                bool needPostAreaRead = createdAreaIds.Count > 0 && (returnAreaMetrics || compareRoomAndArea);
                if (needPostAreaRead)
                {
                    var swMet = Stopwatch.StartNew();
                    var opts = new SpatialElementBoundaryOptions();

                    // In some projects (Revit 2024), newly created/adjusted Areas may keep Area=0
                    // until a Regenerate is executed under a transaction.
                    bool regenBeforeMetrics =
                        (options?.Value<bool?>("regenerate_before_metrics"))
                        ?? (options?.Value<bool?>("regenerateBeforeMetrics"))
                        ?? true;

                    if (regenBeforeMetrics)
                    {
                        try
                        {
                            if (!doc.IsModifiable)
                            {
                                using (var tx = new Transaction(doc, "Regenerate (Area metrics)"))
                                {
                                    tx.Start();
                                    try { TxnUtil.ConfigureProceedWithWarnings(tx); } catch { }
                                    doc.Regenerate();
                                    tx.Commit();
                                }
                            }
                            else
                            {
                                doc.Regenerate();
                            }
                        }
                        catch (Exception ex)
                        {
                            warnings.Add("Area metrics regenerate failed: " + ex.Message);
                        }
                    }

                    var areaM2ById = new Dictionary<int, double>();
                    if (returnAreaMetrics) areaMetrics = new List<object>();

                    foreach (var aid in createdAreaIds)
                    {
                        var area = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(aid)) as Autodesk.Revit.DB.Area;
                        if (area == null) continue;

                        double aM2 = Math.Round(UnitHelper.ToExternal(area.Area, SpecTypeId.Area) ?? 0.0, 3);
                        areaM2ById[aid] = aM2;

                        if (returnAreaMetrics)
                        {
                            double perFt = 0.0;
                            try
                            {
                                var loops = area.GetBoundarySegments(opts);
                                if (loops != null)
                                    perFt = loops.SelectMany(loop => loop).Where(s => s != null && s.GetCurve() != null).Select(s => s.GetCurve().Length).Sum();
                            }
                            catch { perFt = 0.0; }

                            double perMm = Math.Round(UnitHelper.FtToMm(perFt), 3);
                            string lvlName = (doc.GetElement(area.LevelId) as Level)?.Name ?? "";

                            areaMetrics.Add(new
                            {
                                areaId = aid,
                                areaM2 = aM2,
                                perimeterMm = perMm,
                                level = lvlName
                            });
                        }
                    }

                    if (compareRoomAndArea && roomResults.Count > 0 && areaM2ById.Count > 0)
                    {
                        int compared = 0;
                        double sumAbsDelta = 0.0;
                        double maxAbsDelta = 0.0;
                        int? maxAbsDeltaRoomId = null;
                        int? maxAbsDeltaAreaId = null;

                        foreach (var rr in roomResults)
                        {
                            int? createdAreaId = rr.Value<int?>("createdAreaId");
                            if (!createdAreaId.HasValue) continue;

                            if (!areaM2ById.TryGetValue(createdAreaId.Value, out var areaM2)) continue;
                            double roomM2 = rr.Value<double?>("roomAreaM2") ?? 0.0;

                            double delta = Math.Round(areaM2 - roomM2, 3);
                            double absDelta = Math.Abs(delta);

                            rr["createdAreaM2"] = areaM2;
                            rr["areaDeltaM2"] = delta;
                            rr["areaDeltaPercent"] = (Math.Abs(roomM2) > 1e-9) ? Math.Round((delta / roomM2) * 100.0, 2) : (double?)null;

                            compared++;
                            sumAbsDelta += absDelta;
                            if (absDelta > maxAbsDelta)
                            {
                                maxAbsDelta = absDelta;
                                maxAbsDeltaRoomId = rr.Value<int?>("roomId");
                                maxAbsDeltaAreaId = createdAreaId.Value;
                            }
                        }

                        if (compared > 0)
                        {
                            areaRoomComparisonSummary = new
                            {
                                comparedRooms = compared,
                                avgAbsDeltaM2 = Math.Round(sumAbsDelta / compared, 3),
                                maxAbsDeltaM2 = Math.Round(maxAbsDelta, 3),
                                maxAbsDeltaRoomId,
                                maxAbsDeltaAreaId
                            };
                        }
                    }
                    swMet.Stop();
                    metricsMs = swMet.ElapsedMilliseconds;
                }

                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }

                totalSw.Stop();

                return new
                {
                    ok = true,
                    viewId = vp.Id.IntValue(),
                    levelId = level.Id.IntValue(),
                    boundaryLocation = roomBoundaryOpts.SpatialElementBoundaryLocation.ToString(),
                    boundaryCurveSource = boundaryCurveSource.ToString(),
                    materialId = adjustBoundaries ? materialId.IntValue() : (int?)null,
                    materialName = adjustBoundaries ? materialName : null,
                    requestedRooms = roomsResolved.Count,
                    processedRooms = roomsBatch.Count,
                    completed,
                    nextIndex,
                    createdAreaIds,
                    createdBoundaryLineIds,
                    wallsForAdjust = wallIds.Select(x => x.IntValue()).ToList(),
                    roomResults,
                    areaMetrics,
                    areaRoomComparisonSummary,
                    adjustResult,
                    timingsMs = new
                    {
                        copyBoundaries = copyMs,
                        createAreas = createAreasMs,
                        adjust = adjustMs,
                        metrics = metricsMs,
                        total = totalSw.ElapsedMilliseconds
                    },
                    warnings = warnings.Count > 0 ? warnings : null,
                    debug = includeDebug ? (object)new
                    {
                        mergeToleranceMm = mergeTolMm,
                        mergeToleranceFt = mergeTolFt,
                        bucketFt = bucketFt
                    } : null,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message, warnings = warnings.Count > 0 ? warnings : null, units = UnitHelper.DefaultUnitsMeta() };
            }
        }
    }
}


