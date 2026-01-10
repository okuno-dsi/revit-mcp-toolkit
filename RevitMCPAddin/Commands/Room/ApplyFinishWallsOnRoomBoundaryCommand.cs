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
            "includeBoundaryColumns=true also processes boundary segments whose element is a Column/StructuralColumn (default true).",
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
            // Boundary segments
            // ----------------------------
            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var boundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr, SpatialElementBoundaryLocation.Finish);
            var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = boundaryLocation };

            bool includeIslands = p.Value<bool?>("includeIslands") ?? false;
            double minSegmentLengthMm = p.Value<double?>("minSegmentLengthMm") ?? 1.0;
            if (minSegmentLengthMm < 0) minSegmentLengthMm = 0;

            IList<IList<BoundarySegment>>? loops = null;
            try { loops = room.GetBoundarySegments(options); } catch { loops = null; }

            if (loops == null || loops.Count == 0)
                return ResultUtil.Err("部屋の境界線を取得できませんでした（Room が閉じていない可能性があります）。", "ROOM_BOUNDARY_UNAVAILABLE");

            int loopCountToProcess = includeIslands ? loops.Count : Math.Min(1, loops.Count);

            // ----------------------------
            // Host wall filter (RC core)
            // ----------------------------
            bool onlyRcCore = p.Value<bool?>("onlyRcCore") ?? true;
            string coreMatContains = (p.Value<string>("coreMaterialNameContains") ?? "*コンクリート").Trim();
            var coreTokens = SplitTokens(coreMatContains);
            var wallTypeRcCache = new Dictionary<int, bool>();

            bool includeBoundaryColumns = p.Value<bool?>("includeBoundaryColumns") ?? true;

            var tasks = new List<SegTask>();
            var skipped = new List<object>();

            for (int li = 0; li < loopCountToProcess; li++)
            {
                var loop = loops[li];
                if (loop == null) continue;
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
                return ResultUtil.Ok(new
                {
                    roomId = room.Id.IntValue(),
                    createdWallIds = Array.Empty<int>(),
                    createdCount = 0,
                    skipped,
                    note = "No eligible boundary segments."
                });
            }

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

            var existingFinishWalls = new List<Autodesk.Revit.DB.Wall>();
            if (skipExisting)
            {
                var roomBbox = ComputeBoundaryBBoxFt(tasks, existingSearchMarginMm);
                try
                {
                    var allWalls = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Walls)
                        .WhereElementIsNotElementType()
                        .Cast<Autodesk.Revit.DB.Wall>();

                    foreach (var w in allWalls)
                    {
                        if (w == null) continue;
                        ElementId? tid = null;
                        try { tid = w.GetTypeId(); } catch { tid = null; }
                        if (tid == null || tid == ElementId.InvalidElementId) continue;
                        if (tid != finishWallType.Id) continue;

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

            // ----------------------------
            // Create walls
            // ----------------------------
            bool joinEnds = p.Value<bool?>("joinEnds") ?? true;
            string joinTypeStr = (p.Value<string>("joinType") ?? "miter").Trim();
            var joinType = ParseJoinType(joinTypeStr);

            bool setRoomBoundingFalse = !(p.Value<bool?>("roomBounding") ?? false);
            double probeDistMm = p.Value<double?>("probeDistMm") ?? 200.0;
            if (probeDistMm < 1.0) probeDistMm = 1.0;

            var createdWallIds = new List<int>();
            var perSegment = new List<object>();

            using (var tx = new Transaction(doc, "MCP: Apply Finish Walls (Room Boundary)"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                foreach (var t in tasks)
                {
                    try
                    {
                        if (skipExisting && HasExistingFinishWallOnSegment(room, t.BoundaryCurve, existingFinishWalls, tol, existingMinOverlapMm, sampleZFt))
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

                        var baseline = ComputeFinishWallBaselineInsideRoom(room, t.BoundaryCurve, finishWallType.Width * 0.5, sampleZFt, probeDistMm);
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
                includeBoundaryColumns,
                skipExisting,
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
    }
}
