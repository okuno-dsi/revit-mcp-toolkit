// ======================================================================
// File   : Commands/Room/ApplyFinishWallsOnRoomBoundaryCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Command: room.apply_finish_wall_type_on_room_boundary
// Purpose :
//   Create overlay finish walls on the room side, along room boundary
//   segment lengths (not necessarily the full host wall length).
// ======================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    [RpcCommand("room.apply_finish_wall_type_on_room_boundary",
        Aliases = new[] { "apply_finish_wall_type_on_room_boundary" },
        Category = "Room",
        Tags = new[] { "Room", "Wall", "Column", "Create" },
        Risk = RiskLevel.High,
        Summary = "Create overlay finish walls along room boundary segments (room-side).",
        Requires = new[] { "newWallTypeNameOrId" },
        Constraints = new[]
        {
            "roomId/elementId is optional; when omitted, the selected Room is used.",
            "boundaryLocation: Finish|Center|CoreCenter|CoreBoundary (default Finish).",
            "onlyRcCore=true filters host walls by coreMaterialNameContains (default '*コンクリート').",
            "excludeWallsBetweenRooms=true skips wall boundary segments when another Room is detected on the opposite side (default false).",
            "adjacencyProbeDistancesMm controls probe distances (mm) used by excludeWallsBetweenRooms (default [250,500,750]).",
            "includeBoundaryColumns=true also processes boundary segments whose element is a Column/StructuralColumn (default true).",
            "restrictBoundaryColumnsToEligibleWalls=true limits boundary column segments to those adjacent to eligible wall segments (default false).",
            "tempEnableRoomBoundingOnColumns=true temporarily enables Room Bounding on candidate columns so columns appear in Room.GetBoundarySegments (default true).",
            "autoDetectColumnsInRoom=true auto-detects candidate columns near the room bbox (default true).",
            "skipExisting=true attempts to detect existing finish walls of the same type on the room side.",
            "Units: mm for numeric params; internal units are ft."
        })]
    public sealed class ApplyFinishWallsOnRoomBoundaryCommand : IRevitCommandHandler
    {
        public string CommandName => "apply_finish_wall_type_on_room_boundary";

        private sealed class SegTask
        {
            public int LoopIndex;
            public int SegmentIndex;
            public Curve BoundaryCurve = null!;
            public Curve? BaselineCurve;
            public int BoundaryElementId;
            public string BoundaryElementKind = string.Empty; // Wall | Column
            public int? BoundaryCategoryId;
            public string BoundaryCategoryName = string.Empty;
            public int? HostWallId;
            public int? HostColumnId;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = cmd?.Params as JObject ?? new JObject();

            // ----------------------------
            // Resolve room
            // ----------------------------
            int roomId = p.Value<int?>("roomId")
                ?? p.Value<int?>("elementId")
                ?? p.Value<int?>("id")
                ?? 0;

            bool fromSelection = p.Value<bool?>("fromSelection") ?? (roomId <= 0);
            Autodesk.Revit.DB.Architecture.Room? room = null;

            if (roomId > 0)
                room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(roomId)) as Autodesk.Revit.DB.Architecture.Room;

            if (room == null && fromSelection)
            {
                try
                {
                    var selIds = uiapp.ActiveUIDocument.Selection.GetElementIds();
                    foreach (var id in selIds)
                    {
                        var r = doc.GetElement(id) as Autodesk.Revit.DB.Architecture.Room;
                        if (r != null) { room = r; roomId = id.IntValue(); break; }
                    }
                }
                catch { /* ignore */ }
            }

            if (room == null)
                return ResultUtil.Err("Room を取得できません。roomId を指定するか、Room を選択してください。", "INVALID_PARAMS");

            // ----------------------------
            // Resolve finish wall type
            // ----------------------------
            string typeKey = (p.Value<string>("newWallTypeNameOrId")
                ?? p.Value<string>("wallTypeNameOrId")
                ?? p.Value<string>("newWallTypeName")
                ?? p.Value<string>("wallTypeName")
                ?? p.Value<string>("wallTypeId")
                ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(typeKey))
                return ResultUtil.Err("newWallTypeNameOrId が必要です。", "INVALID_PARAMS");

            var finishWallType = FindWallType(doc, typeKey);
            if (finishWallType == null)
                return ResultUtil.Err("WallType が見つかりません: " + typeKey, "NOT_FOUND");

            // ----------------------------
            // Room vertical info
            // ----------------------------
            double baseOffsetFt = 0.0;
            try
            {
                var pBaseOff = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
                if (pBaseOff != null && pBaseOff.StorageType == StorageType.Double)
                    baseOffsetFt = pBaseOff.AsDouble();
            }
            catch { baseOffsetFt = 0.0; }

            try
            {
                var bom = p.Value<double?>("baseOffsetMm");
                if (bom.HasValue) baseOffsetFt = UnitHelper.MmToFt(bom.Value);
            }
            catch { /* ignore */ }

            double? roomHeightFt = TryGetRoomHeightFt(room);
            string roomHeightSource = "auto";
            if (!roomHeightFt.HasValue || roomHeightFt.Value <= 1e-9)
            {
                var hm = p.Value<double?>("heightMm") ?? p.Value<double?>("roomHeightMm");
                if (hm.HasValue && hm.Value > 1e-3)
                {
                    roomHeightFt = UnitHelper.MmToFt(hm.Value);
                    roomHeightSource = "override";
                }
            }

            if (!roomHeightFt.HasValue || roomHeightFt.Value <= 1e-9)
                return ResultUtil.Err("Room height が取得できません。heightMm を指定するか、部屋の高さパラメータを確認してください。", "ROOM_HEIGHT_UNAVAILABLE");

            double sampleZFt = ComputeRoomSampleZ(doc, room, baseOffsetFt, roomHeightFt.Value);

            // ----------------------------
            // Boundary options
            // ----------------------------
            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var boundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr, SpatialElementBoundaryLocation.Finish);
            var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = boundaryLocation };

            bool includeIslands = p.Value<bool?>("includeIslands") ?? false;
            double minSegmentLengthMm = p.Value<double?>("minSegmentLengthMm") ?? 1.0;
            if (minSegmentLengthMm < 0) minSegmentLengthMm = 0;

            // ----------------------------
            // Host wall filter (RC core)
            // ----------------------------
            bool onlyRcCore = p.Value<bool?>("onlyRcCore") ?? true;
            string coreMatContains = (p.Value<string>("coreMaterialNameContains") ?? "*コンクリート").Trim();
            var coreTokens = SplitTokens(coreMatContains);
            var wallTypeRcCache = new Dictionary<int, bool>();

            // ----------------------------
            // Host wall adjacency filter (rooms on both sides)
            // ----------------------------
            bool excludeWallsBetweenRooms = p.Value<bool?>("excludeWallsBetweenRooms") ?? false;
            double[] adjacencyProbeDistancesMm = ParseMmDistances(p, "adjacencyProbeDistancesMm", new[] { 250.0, 500.0, 750.0 });
            bool restrictBoundaryColumnsToEligibleWalls = p.Value<bool?>("restrictBoundaryColumnsToEligibleWalls") ?? false;

            bool includeBoundaryColumns = p.Value<bool?>("includeBoundaryColumns") ?? true;

            // ----------------------------
            // Column detection / temporary Room Bounding
            // ----------------------------
            bool autoDetectColumnsInRoom = p.Value<bool?>("autoDetectColumnsInRoom") ?? includeBoundaryColumns;
            double searchMarginMm = p.Value<double?>("searchMarginMm") ?? 1000.0;
            if (searchMarginMm < 0) searchMarginMm = 0;
            bool tempEnableRoomBoundingOnColumns = p.Value<bool?>("tempEnableRoomBoundingOnColumns") ?? includeBoundaryColumns;

            var columnIds = new List<ElementId>();
            var columnIdSet = new HashSet<int>();
            if (p.TryGetValue("columnIds", out var colTok) && colTok is JArray colArr)
            {
                foreach (var t in colArr)
                {
                    if (t.Type != JTokenType.Integer) continue;
                    int idInt = t.Value<int>();
                    if (idInt <= 0) continue;
                    if (columnIdSet.Add(idInt))
                        columnIds.Add(Autodesk.Revit.DB.ElementIdCompat.From(idInt));
                }
            }

            var autoDetectedColumns = new List<ElementId>();
            if (includeBoundaryColumns && autoDetectColumnsInRoom)
            {
                try
                {
                    var detected = AutoDetectColumnsInRoom(doc, room, searchMarginMm);
                    if (detected != null)
                    {
                        foreach (var eid in detected)
                        {
                            int idInt = eid.IntValue();
                            if (idInt <= 0) continue;
                            if (columnIdSet.Add(idInt))
                                columnIds.Add(eid);
                            autoDetectedColumns.Add(eid);
                        }
                    }
                }
                catch
                {
                    autoDetectedColumns = new List<ElementId>();
                }
            }

            int[] autoDetectedColumnIds = autoDetectedColumns
                .Select(x => x != null ? x.IntValue() : 0)
                .Where(x => x > 0)
                .Distinct()
                .ToArray();

            // ----------------------------
            // Existing finish walls (best-effort)
            // ----------------------------
            bool skipExisting = p.Value<bool?>("skipExisting") ?? true;
            double existingMaxOffsetMm = p.Value<double?>("existingMaxOffsetMm") ?? 120.0;
            double existingMinOverlapMm = p.Value<double?>("existingMinOverlapMm") ?? 100.0;
            double existingMaxAngleDeg = p.Value<double?>("existingMaxAngleDeg") ?? 3.0;
            double existingSearchMarginMm = p.Value<double?>("existingSearchMarginMm") ?? 2000.0;

            if (existingMaxOffsetMm < 0) existingMaxOffsetMm = 0;
            if (existingMinOverlapMm < 0) existingMinOverlapMm = 0;
            if (existingMaxAngleDeg < 0) existingMaxAngleDeg = 0;
            if (existingSearchMarginMm < 0) existingSearchMarginMm = 0;

            var tol = new GeometryUtils.Tolerance(existingMaxOffsetMm, existingMaxAngleDeg);

            // ----------------------------
            // Create walls
            // ----------------------------
            bool joinEnds = p.Value<bool?>("joinEnds") ?? true;
            string joinTypeStr = (p.Value<string>("joinType") ?? "miter").Trim();
            var joinType = ParseJoinType(joinTypeStr);

            bool setRoomBoundingFalse = !(p.Value<bool?>("roomBounding") ?? false);
            double probeDistMm = p.Value<double?>("probeDistMm") ?? 200.0;
            if (probeDistMm < 1.0) probeDistMm = 1.0;

            bool cornerTrim = p.Value<bool?>("cornerTrim") ?? true;
            double cornerTrimMaxExtensionMm = p.Value<double?>("cornerTrimMaxExtensionMm") ?? 2000.0;
            if (cornerTrimMaxExtensionMm < 0) cornerTrimMaxExtensionMm = 0;
            int cornerTrimAppliedCount = 0;
            int cornerTrimSkippedCount = 0;
            int excludedWallsBetweenRoomsCount = 0;
            int excludedBoundaryColumnSegmentsCount = 0;
            int[] excludedWallIdsBetweenRooms = Array.Empty<int>();
            int[] excludedBoundaryColumnIds = Array.Empty<int>();
            int[] eligibleBoundaryColumnIds = Array.Empty<int>();

            var tasks = new List<SegTask>();
            var skipped = new List<object>();
            var toggledColumnIds = new List<int>();
            var loopSegmentCountByIndex = new Dictionary<int, int>();

            var createdWallIds = new List<int>();
            var perSegment = new List<object>();

            using (var tx = new Transaction(doc, "MCP: Apply Finish Walls (Room Boundary)"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                // (1) Temporarily enable Room Bounding on candidate columns so they appear in Room.GetBoundarySegments
                Dictionary<int, int> originalRoomBoundingByColumnId = new Dictionary<int, int>();
                if (includeBoundaryColumns && tempEnableRoomBoundingOnColumns && columnIds.Count > 0)
                {
                    foreach (var id in columnIds)
                    {
                        if (id == null || id == ElementId.InvalidElementId) continue;
                        var e = doc.GetElement(id);
                        if (e == null) continue;
                        if (!IsColumnBoundaryElement(e)) continue;

                        var pRoomBound = e.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                        if (pRoomBound == null || pRoomBound.IsReadOnly) continue;

                        int orig = 0;
                        try { orig = pRoomBound.AsInteger(); } catch { orig = 0; }
                        originalRoomBoundingByColumnId[id.IntValue()] = orig;

                        if (orig != 1)
                        {
                            try
                            {
                                pRoomBound.Set(1);
                                toggledColumnIds.Add(id.IntValue());
                            }
                            catch { /* ignore */ }
                        }
                    }

                    try { doc.Regenerate(); } catch { /* ignore */ }
                }

                // (2) Get boundary loops (after temp RoomBounding)
                IList<IList<BoundarySegment>>? loops = null;
                try { loops = room.GetBoundarySegments(options); } catch { loops = null; }

                if (loops == null || loops.Count == 0)
                {
                    tx.RollBack();
                    return ResultUtil.Err("部屋の境界線を取得できませんでした（Room が閉じていない可能性があります）。", "ROOM_BOUNDARY_UNAVAILABLE");
                }

                int loopCountToProcess = includeIslands ? loops.Count : Math.Min(1, loops.Count);

                for (int li = 0; li < loopCountToProcess; li++)
                {
                    var loop = loops[li];
                    if (loop == null) continue;
                    if (!loopSegmentCountByIndex.ContainsKey(li))
                        loopSegmentCountByIndex[li] = loop.Count;
                    for (int si = 0; si < loop.Count; si++)
                    {
                        var seg = loop[si];
                        if (seg == null) continue;

                        Curve? c = null;
                        try { c = seg.GetCurve(); } catch { c = null; }
                        if (c == null)
                        {
                            skipped.Add(new { loopIndex = li, segmentIndex = si, reason = "no curve" });
                            continue;
                        }

                        double lenMm = 0.0;
                        try { lenMm = UnitHelper.FtToMm(c.ApproximateLength); } catch { lenMm = 0.0; }
                        if (lenMm < minSegmentLengthMm)
                        {
                            skipped.Add(new { loopIndex = li, segmentIndex = si, reason = "too short", lengthMm = Math.Round(lenMm, 3) });
                            continue;
                        }

                        var boundaryElement = seg.ElementId != ElementId.InvalidElementId ? doc.GetElement(seg.ElementId) : null;
                        if (boundaryElement == null)
                        {
                            skipped.Add(new { loopIndex = li, segmentIndex = si, boundaryElementId = seg.ElementId.IntValue(), reason = "boundary element not found" });
                            continue;
                        }

                        var catId = GetCategoryIdIntSafe(boundaryElement);
                        var catName = GetCategoryNameSafe(boundaryElement);

                        // ---- Wall boundary ----
                        var hostWall = boundaryElement as Autodesk.Revit.DB.Wall;
                        if (hostWall != null)
                        {
                            bool rcOk = true;
                            if (onlyRcCore)
                            {
                                int tid = 0;
                                try { tid = hostWall.GetTypeId().IntValue(); } catch { tid = 0; }
                                if (tid <= 0)
                                {
                                    rcOk = false;
                                }
                                else if (wallTypeRcCache.TryGetValue(tid, out var cached))
                                {
                                    rcOk = cached;
                                }
                                else
                                {
                                    bool isMatch = IsWallTypeCoreMaterialMatch(doc, hostWall, coreTokens);
                                    wallTypeRcCache[tid] = isMatch;
                                    rcOk = isMatch;
                                }
                            }

                            if (!rcOk)
                            {
                                skipped.Add(new { loopIndex = li, segmentIndex = si, hostWallId = hostWall.Id.IntValue(), reason = "core material not matched" });
                                continue;
                            }

                            tasks.Add(new SegTask
                            {
                                LoopIndex = li,
                                SegmentIndex = si,
                                BoundaryElementId = hostWall.Id.IntValue(),
                                BoundaryElementKind = "Wall",
                                BoundaryCategoryId = catId,
                                BoundaryCategoryName = catName,
                                HostWallId = hostWall.Id.IntValue(),
                                HostColumnId = null,
                                BoundaryCurve = c
                            });
                            continue;
                        }

                        // ---- Column boundary ----
                        if (includeBoundaryColumns && IsColumnBoundaryElement(boundaryElement))
                        {
                            tasks.Add(new SegTask
                            {
                                LoopIndex = li,
                                SegmentIndex = si,
                                BoundaryElementId = boundaryElement.Id.IntValue(),
                                BoundaryElementKind = "Column",
                                BoundaryCategoryId = catId,
                                BoundaryCategoryName = catName,
                                HostWallId = null,
                                HostColumnId = boundaryElement.Id.IntValue(),
                                BoundaryCurve = c
                            });
                            continue;
                        }

                        skipped.Add(new
                        {
                            loopIndex = li,
                            segmentIndex = si,
                            boundaryElementId = boundaryElement.Id.IntValue(),
                            boundaryElementCategoryId = catId,
                            boundaryElementCategoryName = catName,
                            reason = includeBoundaryColumns ? "unsupported boundary element" : "columns disabled or unsupported boundary element"
                        });
                    }
                }

                if (tasks.Count == 0)
                {
                    tx.RollBack();
                    return ResultUtil.Ok(new
                    {
                        roomId = room.Id.IntValue(),
                        roomName = room.Name ?? string.Empty,
                        levelId = room.LevelId.IntValue(),
                        boundaryLocation = boundaryLocation.ToString(),
                        wallTypeId = finishWallType.Id.IntValue(),
                        wallTypeName = finishWallType.Name ?? string.Empty,
                        onlyRcCore,
                        coreMaterialNameContains = coreMatContains,
                        excludeWallsBetweenRooms,
                        adjacencyProbeDistancesMm,
                        excludedWallsBetweenRoomsCount,
                        excludedWallIdsBetweenRooms,
                        includeBoundaryColumns,
                        restrictBoundaryColumnsToEligibleWalls,
                        eligibleBoundaryColumnIds,
                        excludedBoundaryColumnSegmentsCount,
                        excludedBoundaryColumnIds,
                        tempEnableRoomBoundingOnColumns,
                        autoDetectColumnsInRoom,
                        searchMarginMm,
                        autoDetectedColumnIds,
                        toggledColumnIds = toggledColumnIds.ToArray(),
                        skipExisting,
                        createdCount = 0,
                        createdWallIds = Array.Empty<int>(),
                        roomHeightMm = Math.Round(UnitHelper.FtToMm(roomHeightFt.Value), 3),
                        roomHeightSource,
                        baseOffsetMm = Math.Round(UnitHelper.FtToMm(baseOffsetFt), 3),
                        joinEnds,
                        joinType = joinType.ToString(),
                        segments = Array.Empty<object>(),
                        skipped,
                        note = "No eligible boundary segments."
                    });
                }

                // (3) Precompute baselines (offset inside room by half thickness)
                double halfThicknessFt = 0.0;
                try { halfThicknessFt = finishWallType.Width * 0.5; } catch { halfThicknessFt = 0.0; }

                foreach (var t in tasks)
                {
                    try
                    {
                        t.BaselineCurve = ComputeFinishWallBaselineInsideRoom(room, t.BoundaryCurve, halfThicknessFt, sampleZFt, probeDistMm);
                    }
                    catch
                    {
                        t.BaselineCurve = null;
                    }
                }

                // (3.5) Optionally filter wall segments that appear to have rooms on BOTH sides
                // (i.e., exclude walls between rooms; keep walls that do not have a room on the opposite side)
                if (excludeWallsBetweenRooms && adjacencyProbeDistancesMm.Length > 0)
                {
                    BoundingBoxXYZ? boundaryBbox = null;
                    try
                    {
                        double maxProbe = adjacencyProbeDistancesMm.Length > 0 ? adjacencyProbeDistancesMm.Max() : 0.0;
                        boundaryBbox = ComputeBoundaryBBoxFt(tasks, Math.Max(0.0, maxProbe + 500.0));
                    }
                    catch { boundaryBbox = null; }

                    // Keep the room inclusion check consistent with get_candidate_exterior_walls:
                    // - Same Level
                    // - XY-based inclusion via Room.IsPointInRoom (Z is evaluated near each Room's base level)
                    var otherRooms = CollectOtherRoomsForAdjacency(doc, room, boundaryBbox);

                    var removed = new List<SegTask>();
                    var removedWallIds = new List<int>();

                    foreach (var t in tasks)
                    {
                        if (t == null) continue;
                        if (!string.Equals(t.BoundaryElementKind, "Wall", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!t.HostWallId.HasValue || t.HostWallId.Value <= 0) continue;

                        Autodesk.Revit.DB.Wall? hostWallElem = null;
                        try { hostWallElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(t.HostWallId.Value)) as Autodesk.Revit.DB.Wall; }
                        catch { hostWallElem = null; }
                        if (hostWallElem == null) continue;

                        Autodesk.Revit.DB.Architecture.Room? neighbor = null;
                        try
                        {
                            neighbor = FindOtherRoomAcrossWallLikeExteriorLogic(
                                doc,
                                room,
                                hostWallElem,
                                otherRooms,
                                t.BoundaryCurve,
                                sampleZFt,
                                adjacencyProbeDistancesMm);
                        }
                        catch { neighbor = null; }

                        if (neighbor != null)
                        {
                            removed.Add(t);
                            excludedWallsBetweenRoomsCount++;
                            removedWallIds.Add(t.HostWallId.Value);

                            skipped.Add(new
                            {
                                loopIndex = t.LoopIndex,
                                segmentIndex = t.SegmentIndex,
                                hostWallId = t.HostWallId.Value,
                                reason = "excluded: other room found across wall",
                                otherRoomId = neighbor.Id.IntValue(),
                                otherRoomName = neighbor.Name ?? string.Empty,
                                adjacencyProbeDistancesMm = adjacencyProbeDistancesMm
                            });
                        }
                    }

                    foreach (var t in removed) tasks.Remove(t);
                    excludedWallIdsBetweenRooms = removedWallIds.Where(x => x > 0).Distinct().ToArray();
                }

                // (3.6) Optionally restrict boundary columns to those adjacent to eligible wall segments
                if (restrictBoundaryColumnsToEligibleWalls)
                {
                    var wallSegKeys = new HashSet<string>();
                    foreach (var t in tasks)
                    {
                        if (t == null) continue;
                        if (!string.Equals(t.BoundaryElementKind, "Wall", StringComparison.OrdinalIgnoreCase)) continue;
                        wallSegKeys.Add(t.LoopIndex + ":" + t.SegmentIndex);
                    }

                    var eligibleCols = new HashSet<int>();
                    foreach (var t in tasks)
                    {
                        if (t == null) continue;
                        if (!string.Equals(t.BoundaryElementKind, "Column", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!t.HostColumnId.HasValue || t.HostColumnId.Value <= 0) continue;

                        int segCount = -1;
                        if (loopSegmentCountByIndex != null && loopSegmentCountByIndex.TryGetValue(t.LoopIndex, out var c))
                            segCount = c;
                        if (segCount <= 0) continue;

                        int prev = t.SegmentIndex - 1;
                        if (prev < 0) prev = segCount - 1;
                        int next = t.SegmentIndex + 1;
                        if (next >= segCount) next = 0;

                        if (wallSegKeys.Contains(t.LoopIndex + ":" + prev) || wallSegKeys.Contains(t.LoopIndex + ":" + next))
                            eligibleCols.Add(t.HostColumnId.Value);
                    }

                    var removed = new List<SegTask>();
                    var removedColIds = new List<int>();
                    foreach (var t in tasks)
                    {
                        if (t == null) continue;
                        if (!string.Equals(t.BoundaryElementKind, "Column", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!t.HostColumnId.HasValue || t.HostColumnId.Value <= 0) continue;
                        if (eligibleCols.Contains(t.HostColumnId.Value)) continue;

                        removed.Add(t);
                        excludedBoundaryColumnSegmentsCount++;
                        removedColIds.Add(t.HostColumnId.Value);

                        skipped.Add(new
                        {
                            loopIndex = t.LoopIndex,
                            segmentIndex = t.SegmentIndex,
                            hostColumnId = t.HostColumnId.Value,
                            reason = "excluded: column not connected to eligible wall segments"
                        });
                    }

                    foreach (var t in removed) tasks.Remove(t);
                    excludedBoundaryColumnIds = removedColIds.Where(x => x > 0).Distinct().ToArray();
                    eligibleBoundaryColumnIds = eligibleCols.Where(x => x > 0).Distinct().ToArray();
                }

                if (tasks.Count == 0)
                {
                    tx.RollBack();
                    return ResultUtil.Ok(new
                    {
                        roomId = room.Id.IntValue(),
                        roomName = room.Name ?? string.Empty,
                        levelId = room.LevelId.IntValue(),
                        boundaryLocation = boundaryLocation.ToString(),
                        wallTypeId = finishWallType.Id.IntValue(),
                        wallTypeName = finishWallType.Name ?? string.Empty,
                        onlyRcCore,
                        coreMaterialNameContains = coreMatContains,
                        excludeWallsBetweenRooms,
                        adjacencyProbeDistancesMm,
                        excludedWallsBetweenRoomsCount,
                        excludedWallIdsBetweenRooms,
                        includeBoundaryColumns,
                        restrictBoundaryColumnsToEligibleWalls,
                        eligibleBoundaryColumnIds,
                        excludedBoundaryColumnSegmentsCount,
                        excludedBoundaryColumnIds,
                        tempEnableRoomBoundingOnColumns,
                        autoDetectColumnsInRoom,
                        searchMarginMm,
                        autoDetectedColumnIds,
                        toggledColumnIds = toggledColumnIds.ToArray(),
                        skipExisting,
                        createdCount = 0,
                        createdWallIds = Array.Empty<int>(),
                        roomHeightMm = Math.Round(UnitHelper.FtToMm(roomHeightFt.Value), 3),
                        roomHeightSource,
                        baseOffsetMm = Math.Round(UnitHelper.FtToMm(baseOffsetFt), 3),
                        joinEnds,
                        joinType = joinType.ToString(),
                        segments = Array.Empty<object>(),
                        skipped,
                        note = "No eligible boundary segments (after filters)."
                    });
                }

                // (4) Corner-trim baselines so consecutive segments connect (miter-like)
                if (cornerTrim)
                {
                    try
                    {
                        CornerTrimBaselines(room, tasks, loopSegmentCountByIndex, sampleZFt, cornerTrimMaxExtensionMm, ref cornerTrimAppliedCount, ref cornerTrimSkippedCount);
                    }
                    catch
                    {
                        // best-effort
                    }
                }

                // (5) Collect existing finish walls (inside the same transaction / temp-bounding state)
                var existingFinishWalls = new List<Autodesk.Revit.DB.Wall>();
                if (skipExisting)
                {
                    var roomBbox = ComputeBoundaryBBoxFt(tasks, existingSearchMarginMm);
                    try
                    {
                        int finishTypeIdInt = finishWallType.Id.IntValue();
                        var allWalls = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Walls)
                            .WhereElementIsNotElementType()
                            .Cast<Autodesk.Revit.DB.Wall>();

                        foreach (var w in allWalls)
                        {
                            if (w == null) continue;
                            int tidInt = 0;
                            try { tidInt = w.GetTypeId().IntValue(); } catch { tidInt = 0; }
                            if (tidInt <= 0) continue;
                            if (tidInt != finishTypeIdInt) continue;

                            if (roomBbox != null)
                            {
                                BoundingBoxXYZ? bb = null;
                                try { bb = w.get_BoundingBox(null); } catch { bb = null; }
                                if (bb == null) continue;
                                if (!BboxIntersects(roomBbox, bb)) continue;
                            }

                            existingFinishWalls.Add(w);
                        }
                    }
                    catch { existingFinishWalls = new List<Autodesk.Revit.DB.Wall>(); }
                }

                // (6) Create walls
                foreach (var t in tasks)
                {
                    try
                    {
                        var baseline = t.BaselineCurve;
                        if (baseline == null)
                        {
                            perSegment.Add(new
                            {
                                loopIndex = t.LoopIndex,
                                segmentIndex = t.SegmentIndex,
                                boundaryElementId = t.BoundaryElementId,
                                boundaryElementKind = t.BoundaryElementKind,
                                boundaryElementCategoryId = t.BoundaryCategoryId,
                                boundaryElementCategoryName = t.BoundaryCategoryName,
                                hostWallId = t.HostWallId,
                                hostColumnId = t.HostColumnId,
                                status = "skip",
                                reason = "baseline failed"
                            });
                            continue;
                        }

                        if (skipExisting && HasExistingFinishWallOnSegment(room, baseline, existingFinishWalls, tol, existingMinOverlapMm, sampleZFt))
                        {
                            perSegment.Add(new
                            {
                                loopIndex = t.LoopIndex,
                                segmentIndex = t.SegmentIndex,
                                boundaryElementId = t.BoundaryElementId,
                                boundaryElementKind = t.BoundaryElementKind,
                                boundaryElementCategoryId = t.BoundaryCategoryId,
                                boundaryElementCategoryName = t.BoundaryCategoryName,
                                hostWallId = t.HostWallId,
                                hostColumnId = t.HostColumnId,
                                status = "skip",
                                reason = "already exists"
                            });
                            continue;
                        }

                        var newWall = Autodesk.Revit.DB.Wall.Create(
                            doc,
                            baseline,
                            finishWallType.Id,
                            room.LevelId,
                            roomHeightFt.Value,
                            baseOffsetFt,
                            false,
                            false);

                        if (newWall == null)
                        {
                            perSegment.Add(new
                            {
                                loopIndex = t.LoopIndex,
                                segmentIndex = t.SegmentIndex,
                                boundaryElementId = t.BoundaryElementId,
                                boundaryElementKind = t.BoundaryElementKind,
                                boundaryElementCategoryId = t.BoundaryCategoryId,
                                boundaryElementCategoryName = t.BoundaryCategoryName,
                                hostWallId = t.HostWallId,
                                hostColumnId = t.HostColumnId,
                                status = "error",
                                reason = "Wall.Create returned null"
                            });
                            continue;
                        }

                        if (setRoomBoundingFalse)
                        {
                            try
                            {
                                var prb = newWall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                                if (prb != null && !prb.IsReadOnly) prb.Set(0);
                            }
                            catch { /* ignore */ }
                        }

                        createdWallIds.Add(newWall.Id.IntValue());
                        perSegment.Add(new
                        {
                            loopIndex = t.LoopIndex,
                            segmentIndex = t.SegmentIndex,
                            boundaryElementId = t.BoundaryElementId,
                            boundaryElementKind = t.BoundaryElementKind,
                            boundaryElementCategoryId = t.BoundaryCategoryId,
                            boundaryElementCategoryName = t.BoundaryCategoryName,
                            hostWallId = t.HostWallId,
                            hostColumnId = t.HostColumnId,
                            createdWallId = newWall.Id.IntValue(),
                            status = "created"
                        });
                    }
                    catch (Exception ex)
                    {
                        perSegment.Add(new
                        {
                            loopIndex = t.LoopIndex,
                            segmentIndex = t.SegmentIndex,
                            boundaryElementId = t.BoundaryElementId,
                            boundaryElementKind = t.BoundaryElementKind,
                            boundaryElementCategoryId = t.BoundaryCategoryId,
                            boundaryElementCategoryName = t.BoundaryCategoryName,
                            hostWallId = t.HostWallId,
                            hostColumnId = t.HostColumnId,
                            status = "error",
                            reason = ex.Message
                        });
                    }
                }

                if (joinEnds && createdWallIds.Count > 0)
                {
                    try { doc.Regenerate(); } catch { /* ignore */ }
                    foreach (var idInt in createdWallIds)
                    {
                        try
                        {
                            var w = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idInt)) as Autodesk.Revit.DB.Wall;
                            if (w == null) continue;
                            SafeAllowJoin(w, 0);
                            SafeAllowJoin(w, 1);
                            TrySetJoinType(w, joinType, 0);
                            TrySetJoinType(w, joinType, 1);
                        }
                        catch { /* ignore */ }
                    }
                }

                // (7) Restore Room Bounding on columns (best-effort, but if it fails we rollback for safety)
                if (originalRoomBoundingByColumnId.Count > 0)
                {
                    var restoreFailed = new List<int>();
                    foreach (var kv in originalRoomBoundingByColumnId)
                    {
                        try
                        {
                            var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(kv.Key));
                            if (e == null) continue;
                            var pRoomBound = e.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                            if (pRoomBound == null || pRoomBound.IsReadOnly) continue;
                            pRoomBound.Set(kv.Value);
                        }
                        catch
                        {
                            restoreFailed.Add(kv.Key);
                        }
                    }

                    if (restoreFailed.Count > 0)
                    {
                        tx.RollBack();
                        return ResultUtil.Err("柱の Room Bounding を元に戻せませんでした: " + string.Join(",", restoreFailed), "COLUMN_ROOM_BOUNDING_RESTORE_FAILED");
                    }

                    try { doc.Regenerate(); } catch { /* ignore */ }
                }

                try { doc.Regenerate(); } catch { /* ignore */ }

                var st = tx.Commit();
                if (st != TransactionStatus.Committed)
                {
                    tx.RollBack();
                    return ResultUtil.Err("Transaction が Commit されませんでした: " + st, "TX_NOT_COMMITTED");
                }
            }

                return ResultUtil.Ok(new
            {
                roomId = room.Id.IntValue(),
                roomName = room.Name ?? string.Empty,
                levelId = room.LevelId.IntValue(),
                boundaryLocation = boundaryLocation.ToString(),
                wallTypeId = finishWallType.Id.IntValue(),
                wallTypeName = finishWallType.Name ?? string.Empty,
                onlyRcCore,
                coreMaterialNameContains = coreMatContains,
                excludeWallsBetweenRooms,
                adjacencyProbeDistancesMm,
                excludedWallsBetweenRoomsCount,
                excludedWallIdsBetweenRooms,
                includeBoundaryColumns,
                restrictBoundaryColumnsToEligibleWalls,
                eligibleBoundaryColumnIds,
                excludedBoundaryColumnSegmentsCount,
                excludedBoundaryColumnIds,
                tempEnableRoomBoundingOnColumns,
                autoDetectColumnsInRoom,
                searchMarginMm,
                autoDetectedColumnIds,
                toggledColumnIds = toggledColumnIds.ToArray(),
                skipExisting,
                cornerTrim,
                cornerTrimMaxExtensionMm,
                cornerTrimAppliedCount,
                cornerTrimSkippedCount,
                createdCount = createdWallIds.Count,
                createdWallIds = createdWallIds.ToArray(),
                roomHeightMm = Math.Round(UnitHelper.FtToMm(roomHeightFt.Value), 3),
                roomHeightSource,
                baseOffsetMm = Math.Round(UnitHelper.FtToMm(baseOffsetFt), 3),
                joinEnds,
                joinType = joinType.ToString(),
                segments = perSegment.ToArray(),
                skipped
            });
        }

        private static Autodesk.Revit.DB.JoinType ParseJoinType(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Autodesk.Revit.DB.JoinType.Miter;
            switch (s.Trim().ToLowerInvariant())
            {
                case "butt":
                case "abut":
                    return Autodesk.Revit.DB.JoinType.Abut;
                case "square":
                case "squareoff":
                    return Autodesk.Revit.DB.JoinType.SquareOff;
                case "miter":
                default:
                    return Autodesk.Revit.DB.JoinType.Miter;
            }
        }

        private static void SafeAllowJoin(Autodesk.Revit.DB.Wall w, int end)
        {
            try { WallUtils.AllowWallJoinAtEnd(w, end); }
            catch { /* ignore */ }
        }

        private static bool TrySetJoinType(Autodesk.Revit.DB.Wall w, Autodesk.Revit.DB.JoinType jt, int end)
        {
            try
            {
                var lc = w.Location as LocationCurve;
                if (lc == null) return false;
                var arr = lc.get_ElementsAtJoin(end);
                if (arr == null || arr.Size == 0) return false;
                lc.set_JoinType(end, jt);
                return true;
            }
            catch { return false; }
        }

        private static bool IsColumnBoundaryElement(Autodesk.Revit.DB.Element e)
        {
            if (e == null) return false;
            try
            {
                var cat = e.Category;
                if (cat == null) return false;
                int cid = cat.Id.IntValue();
                return cid == (int)BuiltInCategory.OST_Columns
                    || cid == (int)BuiltInCategory.OST_StructuralColumns;
            }
            catch { return false; }
        }

        private static int? GetCategoryIdIntSafe(Autodesk.Revit.DB.Element e)
        {
            try { return e?.Category != null ? (int?)e.Category.Id.IntValue() : null; }
            catch { return null; }
        }

        private static string GetCategoryNameSafe(Autodesk.Revit.DB.Element e)
        {
            try { return e?.Category != null ? (e.Category.Name ?? string.Empty) : string.Empty; }
            catch { return string.Empty; }
        }

        private static Autodesk.Revit.DB.WallType? FindWallType(Document doc, string nameOrId)
        {
            if (doc == null) return null;
            if (string.IsNullOrWhiteSpace(nameOrId)) return null;
            var s = nameOrId.Trim();

            if (int.TryParse(s, out var idInt) && idInt > 0)
                return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idInt)) as Autodesk.Revit.DB.WallType;

            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.WallType))
                .Cast<Autodesk.Revit.DB.WallType>()
                .ToList();

            var hit = types.FirstOrDefault(t => string.Equals(t.Name, s, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;

            return types.FirstOrDefault(t => (t.Name ?? string.Empty).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string[] SplitTokens(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
            return s
                .Split(new[] { ',', ';', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        private static bool IsWallTypeCoreMaterialMatch(Document doc, Autodesk.Revit.DB.Wall wall, string[] tokens)
        {
            if (doc == null || wall == null) return false;
            if (tokens == null || tokens.Length == 0) return false;

            Autodesk.Revit.DB.WallType? wt = null;
            try { wt = doc.GetElement(wall.GetTypeId()) as Autodesk.Revit.DB.WallType; } catch { wt = null; }
            if (wt == null) return false;
            if (wt.Kind != WallKind.Basic) return false;

            CompoundStructure? cs = null;
            try { cs = wt.GetCompoundStructure(); } catch { cs = null; }
            if (cs == null) return false;

            IList<CompoundStructureLayer>? layers = null;
            try { layers = cs.GetLayers(); } catch { layers = null; }
            if (layers == null || layers.Count == 0) return false;

            int firstCore = -1;
            int lastCore = -1;
            try { firstCore = cs.GetFirstCoreLayerIndex(); lastCore = cs.GetLastCoreLayerIndex(); }
            catch { firstCore = -1; lastCore = -1; }

            if (firstCore < 0 || lastCore < 0 || lastCore < firstCore) return false;
            firstCore = Math.Max(0, firstCore);
            lastCore = Math.Min(layers.Count - 1, lastCore);

            for (int i = firstCore; i <= lastCore; i++)
            {
                try
                {
                    var mat = doc.GetElement(layers[i].MaterialId) as Autodesk.Revit.DB.Material;
                    var name = mat != null ? (mat.Name ?? string.Empty) : string.Empty;
                    foreach (var t in tokens)
                    {
                        if (string.IsNullOrWhiteSpace(t)) continue;
                        if (name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
                catch { /* ignore */ }
            }

            return false;
        }

        private static double[] ParseMmDistances(JObject p, string key, double[] defaultValues)
        {
            if (p == null) return defaultValues ?? Array.Empty<double>();
            if (string.IsNullOrWhiteSpace(key)) return defaultValues ?? Array.Empty<double>();

            try
            {
                if (p.TryGetValue(key, out var tok) && tok != null)
                {
                    var list = new List<double>();

                    if (tok is JArray arr)
                    {
                        foreach (var t in arr)
                        {
                            if (t == null) continue;
                            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
                            {
                                double v = 0.0;
                                try { v = t.Value<double>(); } catch { v = 0.0; }
                                if (v > 0.0) list.Add(v);
                            }
                            else if (t.Type == JTokenType.String)
                            {
                                var s = (t.Value<string>() ?? string.Empty).Trim();
                                if (double.TryParse(s, out var v) && v > 0.0) list.Add(v);
                            }
                        }
                    }
                    else if (tok.Type == JTokenType.String)
                    {
                        var s = (tok.Value<string>() ?? string.Empty).Trim();
                        foreach (var part in SplitTokens(s))
                        {
                            if (double.TryParse(part, out var v) && v > 0.0) list.Add(v);
                        }
                    }
                    else if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float)
                    {
                        double v = 0.0;
                        try { v = tok.Value<double>(); } catch { v = 0.0; }
                        if (v > 0.0) list.Add(v);
                    }

                    var arr2 = list
                        .Where(x => x > 0.0)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToArray();

                    if (arr2.Length > 0) return arr2;
                }
            }
            catch { /* ignore */ }

            return defaultValues ?? Array.Empty<double>();
        }

        private static IList<Autodesk.Revit.DB.Architecture.Room> CollectOtherRoomsForAdjacency(
            Document doc,
            Autodesk.Revit.DB.Architecture.Room targetRoom,
            BoundingBoxXYZ? boundaryBbox)
        {
            var rooms = new List<Autodesk.Revit.DB.Architecture.Room>();
            if (doc == null || targetRoom == null) return rooms;

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();

                if (boundaryBbox != null)
                {
                    try
                    {
                        var outline = new Outline(boundaryBbox.Min, boundaryBbox.Max);
                        collector = collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
                    }
                    catch { /* ignore */ }
                }

                foreach (var e in collector)
                {
                    var r = e as Autodesk.Revit.DB.Architecture.Room;
                    if (r == null) continue;
                    if (r.Id.IntValue() == targetRoom.Id.IntValue()) continue;

                    // Same level only (practical for adjacency on plan)
                    try
                    {
                        if (r.LevelId != targetRoom.LevelId) continue;
                    }
                    catch { /* ignore */ }

                    // Skip unplaced rooms (Area==0)
                    try
                    {
                        if (r.Area <= 1e-9) continue;
                    }
                    catch { /* ignore */ }

                    rooms.Add(r);
                }
            }
            catch
            {
                rooms = new List<Autodesk.Revit.DB.Architecture.Room>();
            }

            return rooms;
        }

        private static Autodesk.Revit.DB.Architecture.Room? TryFindRoomInInteriorRoomVolume(
            Document doc,
            IList<Autodesk.Revit.DB.Architecture.Room> rooms,
            XYZ p)
        {
            if (doc == null || rooms == null || rooms.Count == 0) return null;
            if (p == null) return null;

            foreach (var r in rooms)
            {
                if (r == null) continue;

                try
                {
                    double zTest = p.Z;
                    try
                    {
                        var baseLevel = doc.GetElement(r.LevelId) as Level;
                        if (baseLevel != null)
                        {
                            zTest = baseLevel.Elevation + 0.1; // same as get_candidate_exterior_walls
                        }
                    }
                    catch
                    {
                        zTest = p.Z;
                    }

                    var testPt = new XYZ(p.X, p.Y, zTest);
                    if (r.IsPointInRoom(testPt)) return r;
                }
                catch
                {
                    // ignore and continue
                }
            }

            return null;
        }

        private static Autodesk.Revit.DB.Architecture.Room? FindOtherRoomAcrossWallLikeExteriorLogic(
            Document doc,
            Autodesk.Revit.DB.Architecture.Room targetRoom,
            Autodesk.Revit.DB.Wall hostWall,
            IList<Autodesk.Revit.DB.Architecture.Room> otherRooms,
            Curve boundaryCurve,
            double sampleZFt,
            double[] probeDistancesMm)
        {
            if (doc == null || targetRoom == null || hostWall == null) return null;
            if (otherRooms == null || otherRooms.Count == 0) return null;
            if (boundaryCurve == null) return null;
            if (probeDistancesMm == null || probeDistancesMm.Length == 0) return null;

            // Wall normal direction in XY (same as get_candidate_exterior_walls)
            XYZ orient = null;
            try
            {
                orient = hostWall.Orientation;
                if (orient != null && orient.GetLength() > 1e-9)
                    orient = orient.Normalize();
            }
            catch { orient = null; }

            if (orient == null || Math.Abs(orient.Z) > 1e-3) return null;

            orient = new XYZ(orient.X, orient.Y, 0.0);
            if (orient.GetLength() > 1e-9) orient = orient.Normalize();
            if (orient.GetLength() <= 1e-9) return null;

            // Sample along the boundary segment length (segment-local), using the same sample ratios as get_candidate_exterior_walls
            double[] ts = { 0.1, 0.3, 0.5, 0.7, 0.9 };
            foreach (double t in ts)
            {
                XYZ basePt;
                try { basePt = boundaryCurve.Evaluate(t, true); }
                catch { continue; }

                var baseZ = new XYZ(basePt.X, basePt.Y, sampleZFt);

                foreach (var mm in probeDistancesMm)
                {
                    if (mm <= 0.0) continue;
                    double lenFt = UnitHelper.MmToFt(mm);
                    var pA = baseZ + orient * lenFt;
                    var pB = baseZ - orient * lenFt;

                    var rA = TryFindRoomInInteriorRoomVolume(doc, otherRooms, pA);
                    if (rA != null) return rA;

                    var rB = TryFindRoomInInteriorRoomVolume(doc, otherRooms, pB);
                    if (rB != null) return rB;
                }
            }

            return null;
        }

        private static double? TryGetRoomHeightFt(Autodesk.Revit.DB.Architecture.Room room)
        {
            if (room == null) return null;

            try
            {
                var p = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    var ft = p.AsDouble();
                    if (ft > 1e-9) return ft;
                }
            }
            catch { /* ignore */ }

            try
            {
                var p2 = room.LookupParameter("Unbounded Height");
                if (p2 != null && p2.StorageType == StorageType.Double)
                {
                    var ft = p2.AsDouble();
                    if (ft > 1e-9) return ft;
                }
            }
            catch { /* ignore */ }

            try
            {
                var p3 = room.LookupParameter("高さ");
                if (p3 != null && p3.StorageType == StorageType.Double)
                {
                    var ft = p3.AsDouble();
                    if (ft > 1e-9) return ft;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static double ComputeRoomSampleZ(Document doc, Autodesk.Revit.DB.Architecture.Room room, double baseOffsetFt, double heightFt)
        {
            double z = 0.0;
            try
            {
                var level = doc.GetElement(room.LevelId) as Level;
                var levelZ = level != null ? level.Elevation : 0.0;
                var dz = Math.Min(UnitHelper.MmToFt(300.0), Math.Max(UnitHelper.MmToFt(50.0), heightFt * 0.25));
                z = levelZ + baseOffsetFt + dz;
            }
            catch
            {
                z = baseOffsetFt + Math.Min(UnitHelper.MmToFt(300.0), heightFt * 0.25);
            }
            return z;
        }

        /// <summary>
        /// Auto-detect candidate columns near the room bbox (expanded by searchMarginMm),
        /// then keep those that appear to intersect the room (best-effort).
        /// </summary>
        private static IList<ElementId> AutoDetectColumnsInRoom(Document doc, Autodesk.Revit.DB.Architecture.Room room, double searchMarginMm)
        {
            var result = new List<ElementId>();
            if (doc == null || room == null) return result;

            BoundingBoxXYZ? roomBb = null;
            try { roomBb = room.get_BoundingBox(null); } catch { roomBb = null; }
            if (roomBb == null) return result;

            if (searchMarginMm < 0) searchMarginMm = 0;
            double marginFt = UnitUtils.ConvertToInternalUnits(searchMarginMm, UnitTypeId.Millimeters);

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

                BoundingBoxXYZ? bb = null;
                try { bb = fi.get_BoundingBox(null); } catch { bb = null; }
                if (bb == null) continue;

                if (IntersectsRoomApprox(room, bb, roomBb))
                    result.Add(fi.Id);
            }

            return result;
        }

        /// <summary>
        /// Best-effort intersection: sample points (center + 4 corners) at a mid height where
        /// the column bbox and room bbox overlap in Z, and check Room.IsPointInRoom.
        /// </summary>
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
                    // ignore per-point failures
                }
            }

            return false;
        }

        private static BoundingBoxXYZ? ComputeBoundaryBBoxFt(List<SegTask> tasks, double marginMm)
        {
            if (tasks == null || tasks.Count == 0) return null;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (var t in tasks)
            {
                var c = t.BoundaryCurve;
                if (c == null) continue;
                try
                {
                    var p0 = c.GetEndPoint(0);
                    var p1 = c.GetEndPoint(1);
                    minX = Math.Min(minX, Math.Min(p0.X, p1.X));
                    minY = Math.Min(minY, Math.Min(p0.Y, p1.Y));
                    minZ = Math.Min(minZ, Math.Min(p0.Z, p1.Z));
                    maxX = Math.Max(maxX, Math.Max(p0.X, p1.X));
                    maxY = Math.Max(maxY, Math.Max(p0.Y, p1.Y));
                    maxZ = Math.Max(maxZ, Math.Max(p0.Z, p1.Z));
                }
                catch { /* ignore */ }
            }

            if (minX == double.MaxValue) return null;

            var m = UnitHelper.MmToFt(marginMm);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX - m, minY - m, minZ - m),
                Max = new XYZ(maxX + m, maxY + m, maxZ + m),
            };
        }

        private static bool BboxIntersects(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null) return false;
            if (a.Max.X < b.Min.X || a.Min.X > b.Max.X) return false;
            if (a.Max.Y < b.Min.Y || a.Min.Y > b.Max.Y) return false;
            if (a.Max.Z < b.Min.Z || a.Min.Z > b.Max.Z) return false;
            return true;
        }

        private static bool HasExistingFinishWallOnSegment(
            Autodesk.Revit.DB.Architecture.Room room,
            Curve boundaryCurve,
            List<Autodesk.Revit.DB.Wall> existingFinishWalls,
            GeometryUtils.Tolerance tol,
            double minOverlapMm,
            double sampleZFt)
        {
            if (room == null || boundaryCurve == null) return false;
            if (existingFinishWalls == null || existingFinishWalls.Count == 0) return false;

            GeometryUtils.Segment2 segRoom;
            try
            {
                var aFt = boundaryCurve.GetEndPoint(0);
                var bFt = boundaryCurve.GetEndPoint(1);
                var aMm = UnitHelper.XyzToMm(aFt);
                var bMm = UnitHelper.XyzToMm(bFt);
                segRoom = new GeometryUtils.Segment2(
                    new GeometryUtils.Vec2(aMm.x, aMm.y),
                    new GeometryUtils.Vec2(bMm.x, bMm.y));
            }
            catch { return false; }

            foreach (var w in existingFinishWalls)
            {
                if (w == null) continue;
                try
                {
                    var lc = w.Location as LocationCurve;
                    var c = lc != null ? lc.Curve : null;
                    if (c == null) continue;

                    // Room-side check: midpoint must be inside room (best-effort).
                    var mid = c.Evaluate(0.5, true);
                    bool inside = false;
                    try { inside = room.IsPointInRoom(new XYZ(mid.X, mid.Y, sampleZFt)); } catch { inside = false; }
                    if (!inside) continue;

                    var p0 = c.GetEndPoint(0);
                    var p1 = c.GetEndPoint(1);
                    var p0mm = UnitHelper.XyzToMm(p0);
                    var p1mm = UnitHelper.XyzToMm(p1);
                    var segW = new GeometryUtils.Segment2(
                        new GeometryUtils.Vec2(p0mm.x, p0mm.y),
                        new GeometryUtils.Vec2(p1mm.x, p1mm.y));

                    var a = GeometryUtils.AnalyzeSegments2D(segRoom, segW, tol);
                    if (!a.ok) continue;
                    if (!a.isParallel) continue;
                    if (!a.distanceBetweenParallelMm.HasValue) continue;
                    if (a.distanceBetweenParallelMm.Value < 0 || a.distanceBetweenParallelMm.Value > tol.DistMm) continue;

                    double overlapMm = a.overlapLengthMm ?? 0.0;
                    if (overlapMm <= 0 && a.overlapStart.HasValue && a.overlapEnd.HasValue)
                    {
                        var os = a.overlapStart.Value;
                        var oe = a.overlapEnd.Value;
                        overlapMm = Math.Sqrt(Math.Pow(oe.x - os.x, 2) + Math.Pow(oe.y - os.y, 2));
                    }
                    if (overlapMm >= minOverlapMm) return true;
                }
                catch { /* ignore */ }
            }

            return false;
        }

        private static Curve? ComputeFinishWallBaselineInsideRoom(
            Autodesk.Revit.DB.Architecture.Room room,
            Curve boundaryCurve,
            double halfThicknessFt,
            double sampleZFt,
            double probeDistMm)
        {
            if (room == null || boundaryCurve == null) return null;
            if (halfThicknessFt <= 1e-9) return null;

            Curve? offPos = null;
            Curve? offNeg = null;
            try { offPos = boundaryCurve.CreateOffset(halfThicknessFt, XYZ.BasisZ); } catch { offPos = null; }
            try { offNeg = boundaryCurve.CreateOffset(-halfThicknessFt, XYZ.BasisZ); } catch { offNeg = null; }

            // Prefer the offset whose midpoint is inside the room.
            if (offPos != null)
            {
                try
                {
                    var mid = offPos.Evaluate(0.5, true);
                    bool inside = false;
                    try { inside = room.IsPointInRoom(new XYZ(mid.X, mid.Y, sampleZFt)); } catch { inside = false; }
                    if (inside) return offPos;
                }
                catch { /* ignore */ }
            }

            if (offNeg != null)
            {
                try
                {
                    var mid = offNeg.Evaluate(0.5, true);
                    bool inside = false;
                    try { inside = room.IsPointInRoom(new XYZ(mid.X, mid.Y, sampleZFt)); } catch { inside = false; }
                    if (inside) return offNeg;
                }
                catch { /* ignore */ }
            }

            // Fallback: infer interior side from normals (best-effort).
            try
            {
                var mid = boundaryCurve.Evaluate(0.5, true);
                var deriv = boundaryCurve.ComputeDerivatives(0.5, true);
                var v = deriv != null ? deriv.BasisX : XYZ.Zero;
                v = new XYZ(v.X, v.Y, 0);
                if (v.GetLength() < 1e-9)
                {
                    var p0 = boundaryCurve.GetEndPoint(0);
                    var p1 = boundaryCurve.GetEndPoint(1);
                    v = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                }

                if (v.GetLength() > 1e-9)
                {
                    v = v.Normalize();
                    var n1 = XYZ.BasisZ.CrossProduct(v);
                    n1 = new XYZ(n1.X, n1.Y, 0);
                    if (n1.GetLength() > 1e-9) n1 = n1.Normalize();
                    var n2 = new XYZ(-n1.X, -n1.Y, 0);
                    double d = UnitHelper.MmToFt(probeDistMm);

                    var p1t = new XYZ(mid.X + n1.X * d, mid.Y + n1.Y * d, sampleZFt);
                    var p2t = new XYZ(mid.X + n2.X * d, mid.Y + n2.Y * d, sampleZFt);

                    bool i1 = false, i2 = false;
                    try { i1 = room.IsPointInRoom(p1t); } catch { i1 = false; }
                    try { i2 = room.IsPointInRoom(p2t); } catch { i2 = false; }

                    if (i1 && offPos != null) return offPos;
                    if (i2 && offNeg != null) return offNeg;
                }
            }
            catch { /* ignore */ }

            return offPos ?? offNeg;
        }

        private static void CornerTrimBaselines(
            Autodesk.Revit.DB.Architecture.Room room,
            List<SegTask> tasks,
            Dictionary<int, int> loopSegmentCountByIndex,
            double sampleZFt,
            double maxExtensionMm,
            ref int appliedCount,
            ref int skippedCount)
        {
            if (room == null || tasks == null || tasks.Count == 0) return;

            // Per task start/end override points
            var startOverride = new Dictionary<SegTask, XYZ>();
            var endOverride = new Dictionary<SegTask, XYZ>();

            var byLoop = tasks.GroupBy(t => t.LoopIndex).ToList();
            foreach (var g in byLoop)
            {
                int loopIndex = g.Key;
                int loopSegCount = -1;
                if (loopSegmentCountByIndex != null && loopSegmentCountByIndex.TryGetValue(loopIndex, out var c))
                    loopSegCount = c;

                var map = new Dictionary<int, SegTask>();
                foreach (var t in g)
                {
                    if (!map.ContainsKey(t.SegmentIndex))
                        map[t.SegmentIndex] = t;
                }

                foreach (var kv in map)
                {
                    var curr = kv.Value;
                    if (curr == null) continue;

                    int nextIndex = curr.SegmentIndex + 1;
                    if (loopSegCount > 0 && curr.SegmentIndex == loopSegCount - 1)
                        nextIndex = 0;

                    if (!map.TryGetValue(nextIndex, out var next) || next == null) continue;

                    // Only treat truly consecutive segments (avoid trimming across skipped segments)
                    if (loopSegCount > 0)
                    {
                        bool isWrap = curr.SegmentIndex == loopSegCount - 1 && next.SegmentIndex == 0;
                        bool isConsecutive = next.SegmentIndex == curr.SegmentIndex + 1;
                        if (!isWrap && !isConsecutive) continue;
                    }
                    else
                    {
                        if (next.SegmentIndex != curr.SegmentIndex + 1) continue;
                    }

                    var aLine = curr.BaselineCurve as Line;
                    var bLine = next.BaselineCurve as Line;
                    if (aLine == null || bLine == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var a0 = aLine.GetEndPoint(0);
                    var a1 = aLine.GetEndPoint(1);
                    var b0 = bLine.GetEndPoint(0);
                    var b1 = bLine.GetEndPoint(1);

                    if (!TryIntersectInfiniteLines2D(a0, a1, b0, b1, out var ip))
                    {
                        skippedCount++;
                        continue;
                    }

                    // Limit extension so we do not create extreme spikes
                    double daMm = UnitHelper.FtToMm(Math.Sqrt(Math.Pow(ip.X - a1.X, 2) + Math.Pow(ip.Y - a1.Y, 2)));
                    double dbMm = UnitHelper.FtToMm(Math.Sqrt(Math.Pow(ip.X - b0.X, 2) + Math.Pow(ip.Y - b0.Y, 2)));
                    if ((maxExtensionMm > 0 && daMm > maxExtensionMm) || (maxExtensionMm > 0 && dbMm > maxExtensionMm))
                    {
                        skippedCount++;
                        continue;
                    }

                    // Midpoint inside-room safety check (best-effort)
                    var aMid = new XYZ(0.5 * (a0.X + ip.X), 0.5 * (a0.Y + ip.Y), sampleZFt);
                    var bMid = new XYZ(0.5 * (ip.X + b1.X), 0.5 * (ip.Y + b1.Y), sampleZFt);
                    bool aOk = false;
                    bool bOk = false;
                    try { aOk = room.IsPointInRoom(aMid); } catch { aOk = false; }
                    try { bOk = room.IsPointInRoom(bMid); } catch { bOk = false; }
                    if (!aOk || !bOk)
                    {
                        skippedCount++;
                        continue;
                    }

                    endOverride[curr] = new XYZ(ip.X, ip.Y, a1.Z);
                    startOverride[next] = new XYZ(ip.X, ip.Y, b0.Z);
                    appliedCount++;
                }
            }

            // Apply overrides
            foreach (var t in tasks)
            {
                var line = t.BaselineCurve as Line;
                if (line == null) continue;

                var s = line.GetEndPoint(0);
                var e = line.GetEndPoint(1);

                if (startOverride.TryGetValue(t, out var so)) s = so;
                if (endOverride.TryGetValue(t, out var eo)) e = eo;

                if (Math.Abs(s.X - e.X) < 1e-9 && Math.Abs(s.Y - e.Y) < 1e-9)
                    continue;

                try { t.BaselineCurve = Line.CreateBound(s, e); }
                catch { /* ignore */ }
            }
        }

        private static bool TryIntersectInfiniteLines2D(XYZ a0, XYZ a1, XYZ b0, XYZ b1, out XYZ intersection)
        {
            intersection = XYZ.Zero;

            double x1 = a0.X, y1 = a0.Y;
            double x2 = a1.X, y2 = a1.Y;
            double x3 = b0.X, y3 = b0.Y;
            double x4 = b1.X, y4 = b1.Y;

            double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(den) < 1e-12) return false;

            double det1 = x1 * y2 - y1 * x2;
            double det2 = x3 * y4 - y3 * x4;

            double px = (det1 * (x3 - x4) - (x1 - x2) * det2) / den;
            double py = (det1 * (y3 - y4) - (y1 - y2) * det2) / den;

            intersection = new XYZ(px, py, a0.Z);
            return true;
        }
    }
}
