// RevitMCPAddin/Commands/ElementOps/Wall/CreateFlushWallsCommand.cs
// Create new walls flush (face-aligned) to existing walls.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Walls;
using RevitMCPAddin.Models;
using ArchRoom = Autodesk.Revit.DB.Architecture.Room;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    [RpcCommand("element.create_flush_walls",
        Aliases = new[] { "create_flush_walls" },
        Category = "ElementOps/Wall",
        Tags = new[] { "ElementOps", "Wall" },
        Risk = RiskLevel.Medium,
        Summary = "Create new walls flush (face-aligned) to existing walls, on a chosen side and plane reference.",
        Requires = new[] { "newWallTypeNameOrId" },
        Constraints = new[]
        {
            "If sourceWallIds is omitted/empty, current selection is used (walls only).",
            "roomId + includeBoundaryColumns=true can add walls along column boundary segments (room side).",
            "sideMode: ByGlobalDirection|ByExterior|ByInterior (default ByGlobalDirection).",
            "globalDirection is only used when sideMode=ByGlobalDirection. Example: [0,-1,0] means -Y side.",
            "sourcePlane/newPlane: FinishFace|CoreFace|WallCenterline|CoreCenterline (default FinishFace)."
        },
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"element.create_flush_walls\", \"params\":{ \"sourceWallIds\":[123456], \"newWallTypeNameOrId\":\"(内壁)W5\", \"sideMode\":\"ByGlobalDirection\", \"globalDirection\":[0,-1,0], \"sourcePlane\":\"FinishFace\", \"newPlane\":\"FinishFace\", \"newExteriorMode\":\"MatchSourceExterior\", \"miterJoints\":true, \"copyVerticalConstraints\":true } }")]
    public sealed class CreateFlushWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "create_flush_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = cmd?.Params as JObject ?? new JObject();

                var req = p.ToObject<CreateFlushWallsRequest>() ?? new CreateFlushWallsRequest();

                // Back-compat aliases for new wall type key.
                if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                {
                    req.NewWallTypeNameOrId =
                        (p.Value<string>("newWallTypeNameOrId") ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                        req.NewWallTypeNameOrId = (p.Value<string>("newWallTypeName") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                        req.NewWallTypeNameOrId = (p.Value<string>("wallTypeName") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                        req.NewWallTypeNameOrId = (p.Value<string>("wallTypeId") ?? string.Empty).Trim();
                }

                // Selection fallback
                if (req.SourceWallIds == null) req.SourceWallIds = new List<int>();
                if (req.SourceWallIds.Count == 0)
                {
                    try
                    {
                        var selIds = uiapp.ActiveUIDocument.Selection.GetElementIds();
                        foreach (var id in selIds)
                        {
                            var w = doc.GetElement(id) as Autodesk.Revit.DB.Wall;
                            if (w == null) continue;
                            req.SourceWallIds.Add(id.IntValue());
                        }
                    }
                    catch { /* ignore */ }
                }

                if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                    return new { ok = false, code = "INVALID_PARAMS", msg = "newWallTypeNameOrId が必要です。" };

                // Optional: capture column boundary baselines (room-side) when roomId is provided.
                var columnBaselines = new List<Curve>();
                var columnWarnings = new List<string>();

                int roomId = p.Value<int?>("roomId") ?? p.Value<int?>("elementId") ?? 0;
                bool includeBoundaryColumns = p.Value<bool?>("includeBoundaryColumns") ?? false;
                bool autoDetectColumns = p.Value<bool?>("autoDetectColumnsInRoom") ?? false;
                double searchMarginMm = p.Value<double?>("searchMarginMm") ?? 1000.0;
                string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
                bool includeIslands = p.Value<bool?>("includeIslands") ?? false;
                double probeDistMm = p.Value<double?>("probeDistMm") ?? 200.0;

                if (probeDistMm < 1.0) probeDistMm = 1.0;

                ArchRoom? room = null;
                var columnIds = new List<ElementId>();
                if (includeBoundaryColumns && roomId > 0)
                {
                    room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(roomId)) as ArchRoom;
                    if (room == null)
                    {
                        columnWarnings.Add("Room not found for column boundary capture: " + roomId);
                    }
                    else
                    {
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

                        if (autoDetectColumns)
                        {
                            try
                            {
                                var autoCols = AutoDetectColumnsInRoom(doc, room, searchMarginMm);
                                foreach (var eid in autoCols)
                                {
                                    if (!columnIds.Contains(eid)) columnIds.Add(eid);
                                }
                            }
                            catch (Exception ex)
                            {
                                columnWarnings.Add("Auto-detect columns failed: " + ex.Message);
                            }
                        }
                    }
                }

                bool hasSourceWalls = req.SourceWallIds != null && req.SourceWallIds.Count > 0;

                var newWallType = FindWallType(doc, req.NewWallTypeNameOrId);
                if (newWallType == null)
                {
                    if (hasSourceWalls)
                    {
                        var respTypeFail = WallFlushPlacement.Execute(doc, req);
                        return new
                        {
                            ok = respTypeFail.Ok,
                            msg = respTypeFail.Message,
                            createdWallIds = respTypeFail.CreatedWallIds,
                            warnings = respTypeFail.Warnings
                        };
                    }

                    return new
                    {
                        ok = false,
                        msg = "WallType not found: " + (req.NewWallTypeNameOrId ?? string.Empty),
                        createdWallIds = Array.Empty<int>(),
                        warnings = Array.Empty<string>()
                    };
                }

                var createdWallIds = new List<int>();
                var warnings = new List<string>();

                double columnBaseOffsetFt = 0.0;
                double? columnRoomHeightFt = null;
                if (room != null && includeBoundaryColumns)
                {
                    columnBaseOffsetFt = GetRoomBaseOffsetFt(room);
                    columnRoomHeightFt = TryGetRoomHeightFt(room);
                    if (!columnRoomHeightFt.HasValue || columnRoomHeightFt.Value <= 1e-9)
                    {
                        warnings.Add("Room height is unavailable; column boundary walls were skipped.");
                    }
                    else
                    {
                        var halfThicknessFt = newWallType.Width * 0.5;
                        if (halfThicknessFt <= 1e-9)
                        {
                            warnings.Add("WallType width is zero; column boundary walls were skipped.");
                        }
                        else
                        {
                            var boundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr);
                            var sampleZFt = ComputeRoomSampleZ(doc, room, columnBaseOffsetFt, columnRoomHeightFt.Value);
                            var toggledColumnIds = new List<int>();

                            // Compute baselines while columns are temporarily room-bounding.
                            columnBaselines = CollectColumnBoundaryBaselines(
                                doc,
                                room,
                                boundaryLocation,
                                includeIslands,
                                columnIds,
                                toggledColumnIds,
                                columnWarnings,
                                halfThicknessFt,
                                sampleZFt,
                                probeDistMm);
                        }
                    }
                }

                if (!hasSourceWalls && columnBaselines.Count == 0)
                    return new { ok = false, code = "INVALID_PARAMS", msg = "sourceWallIds が空です（または選択に Wall がありません）。" };

                CreateFlushWallsResponse? resp = null;
                if (hasSourceWalls)
                {
                    resp = WallFlushPlacement.Execute(doc, req);
                    createdWallIds.AddRange(resp.CreatedWallIds);
                    warnings.AddRange(resp.Warnings);
                }

                if (columnWarnings.Count > 0)
                    warnings.AddRange(columnWarnings);

                if (room != null && columnBaselines.Count > 0 && columnRoomHeightFt.HasValue && columnRoomHeightFt.Value > 1e-9)
                {
                    var columnCreated = CreateWallsAlongColumnBoundary(
                        doc,
                        room,
                        columnBaselines,
                        newWallType,
                        columnBaseOffsetFt,
                        columnRoomHeightFt.Value,
                        warnings);
                    createdWallIds.AddRange(columnCreated);
                }

                bool ok = createdWallIds.Count > 0;
                string msg;
                if (ok)
                {
                    msg = "Created " + createdWallIds.Count + " wall(s).";
                }
                else if (resp != null && !string.IsNullOrWhiteSpace(resp.Message))
                {
                    msg = resp.Message;
                }
                else
                {
                    msg = "No walls were created (see warnings).";
                }

                return new
                {
                    ok,
                    msg,
                    createdWallIds,
                    warnings
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "INTERNAL_ERROR", msg = ex.Message };
            }
        }

        private static WallType? FindWallType(Document? doc, string nameOrId)
        {
            if (doc == null) return null;
            if (string.IsNullOrWhiteSpace(nameOrId)) return null;
            var s = nameOrId.Trim();

            if (int.TryParse(s, out var idInt) && idInt > 0)
                return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idInt)) as WallType;

            var types = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().ToList();
            var hit = types.Find(t => string.Equals(t.Name, s, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;

            return types.Find(t => (t.Name ?? string.Empty).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static List<Curve> CollectColumnBoundaryBaselines(
            Document? doc,
            ArchRoom? room,
            SpatialElementBoundaryLocation boundaryLocation,
            bool includeIslands,
            List<ElementId> columnIds,
            List<int> toggledColumnIds,
            List<string> warnings,
            double halfThicknessFt,
            double sampleZFt,
            double probeDistMm)
        {
            var baselines = new List<Curve>();
            if (doc == null || room == null) return baselines;

            using (var tg = new TransactionGroup(doc, "Temp RoomBounding for Column Boundary"))
            {
                tg.Start();

                if (columnIds != null && columnIds.Count > 0)
                {
                    using (var t = new Transaction(doc, "Enable Room Bounding for columns"))
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
                                    if (pRoomBound.AsInteger() == 0)
                                        toggledColumnIds.Add(id.IntValue());
                                    pRoomBound.Set(1);
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

                var opt = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = boundaryLocation
                };

                IList<IList<BoundarySegment>>? loops = null;
                try { loops = room.GetBoundarySegments(opt); } catch { loops = null; }

                if (loops != null)
                {
                    int loopIndex = 0;
                    foreach (var loop in loops)
                    {
                        if (!includeIslands && loopIndex > 0) break;
                        foreach (var seg in loop)
                        {
                            Curve? c = null;
                            try { c = seg.GetCurve(); } catch { c = null; }
                            if (c == null) continue;

                            Element? be = null;
                            try
                            {
                                var eid = seg.ElementId;
                                if (eid != null && eid != ElementId.InvalidElementId)
                                    be = doc.GetElement(eid);
                            }
                            catch { be = null; }

                            if (IsColumnBoundaryElement(be))
                            {
                                var baseline = ComputeFinishWallBaselineInsideRoom(room, c, halfThicknessFt, sampleZFt, probeDistMm);
                                if (baseline != null)
                                {
                                    baselines.Add(baseline);
                                }
                                else
                                {
                                    warnings.Add("Column boundary: baseline failed.");
                                }
                            }
                        }
                        loopIndex++;
                    }
                }

                tg.RollBack();
            }

            if (baselines.Count == 0 && (columnIds != null && columnIds.Count > 0))
                warnings.Add("No column boundary segments were found for the specified room.");

            return baselines;
        }

        private static bool IsColumnBoundaryElement(Element e)
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

        private static IList<ElementId> AutoDetectColumnsInRoom(Document? doc, ArchRoom? room, double searchMarginMm)
        {
            var result = new List<ElementId>();
            if (doc == null || room == null) return result;

            var roomBb = room.get_BoundingBox(null);
            if (roomBb == null) return result;

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
                var bb = fi.get_BoundingBox(null);
                if (bb == null) continue;

                if (IntersectsRoomApprox(room, bb, roomBb))
                    result.Add(fi.Id);
            }

            return result;
        }

        private static bool IntersectsRoomApprox(ArchRoom? room, BoundingBoxXYZ? colBb, BoundingBoxXYZ? roomBb)
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
                catch { /* ignore */ }
            }

            return false;
        }

        private static double GetRoomBaseOffsetFt(ArchRoom? room)
        {
            if (room == null) return 0.0;
            try
            {
                var pBaseOff = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
                if (pBaseOff != null && pBaseOff.StorageType == StorageType.Double)
                    return pBaseOff.AsDouble();
            }
            catch { /* ignore */ }
            return 0.0;
        }

        private static double? TryGetRoomHeightFt(ArchRoom? room)
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

        private static double ComputeRoomSampleZ(Document doc, ArchRoom room, double baseOffsetFt, double heightFt)
        {
            double z = 0.0;
            try
            {
                var level = doc.GetElement(room.LevelId) as Level;
                var levelZ = level != null ? level.Elevation : 0.0;
                var dz = Math.Min(UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters),
                    Math.Max(UnitUtils.ConvertToInternalUnits(50.0, UnitTypeId.Millimeters), heightFt * 0.25));
                z = levelZ + baseOffsetFt + dz;
            }
            catch
            {
                z = baseOffsetFt + Math.Min(UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters), heightFt * 0.25);
            }
            return z;
        }

        private static Curve? ComputeFinishWallBaselineInsideRoom(
            ArchRoom? room,
            Curve? boundaryCurve,
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
                    double d = UnitUtils.ConvertToInternalUnits(probeDistMm, UnitTypeId.Millimeters);

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

        private static List<int> CreateWallsAlongColumnBoundary(
            Document? doc,
            ArchRoom? room,
            List<Curve>? baselines,
            WallType? wallType,
            double baseOffsetFt,
            double roomHeightFt,
            List<string> warnings)
        {
            var created = new List<int>();
            if (doc == null || room == null || wallType == null || baselines == null || baselines.Count == 0)
                return created;

            using (var tx = new Transaction(doc, "Create Flush Walls (Column Boundary)"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                foreach (var baseline in baselines)
                {
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        try
                        {
                            var newWall = Autodesk.Revit.DB.Wall.Create(
                                doc,
                                baseline,
                                wallType.Id,
                                room.LevelId,
                                roomHeightFt,
                                baseOffsetFt,
                                false,
                                false);

                            if (newWall != null) created.Add(newWall.Id.IntValue());
                            st.Commit();
                        }
                        catch (Exception ex)
                        {
                            warnings.Add("Column boundary create failed: " + ex.Message);
                            try { st.RollBack(); } catch { }
                        }
                    }
                }

                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                    warnings.Add("Column boundary transaction did not commit: " + status);
            }

            return created;
        }
    }
}
