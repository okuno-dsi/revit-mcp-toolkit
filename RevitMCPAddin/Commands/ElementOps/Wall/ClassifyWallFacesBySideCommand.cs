#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Dto;

namespace RevitMCPAddin.Commands.ElementOps
{
    /// <summary>
    /// classify_wall_faces_by_side
    /// 壁要素ごとに面を exterior / interior / top / bottom / end / other に分類します。
    /// </summary>
    public class ClassifyWallFacesBySideCommand : IRevitCommandHandler
    {
        public string CommandName => "classify_wall_faces_by_side";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new WallFaceClassificationResult { ok = false, errors = new List<WallFaceClassificationError> { new WallFaceClassificationError { ElementId = 0, Message = "アクティブドキュメントがありません。" } } };

            try
            {
                var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

                var req = new WallFaceClassificationRequest();

                if (p["elementIds"] is JArray arr)
                {
                    foreach (var t in arr)
                    {
                        if (t.Type == JTokenType.Integer)
                        {
                            int id = t.Value<int>();
                            if (id > 0) req.ElementIds.Add(id);
                        }
                    }
                }

                if (req.ElementIds.Count == 0)
                {
                    return new WallFaceClassificationResult
                    {
                        ok = false,
                        errors = new List<WallFaceClassificationError>
                        {
                            new WallFaceClassificationError { ElementId = 0, Message = "elementIds が指定されていません。" }
                        }
                    };
                }

                req.OffsetMm = p.Value<double?>("offsetMm") ?? 1000.0;
                req.RoomCheck = p.Value<bool?>("roomCheck") ?? true;
                req.MinAreaM2 = p.Value<double?>("minAreaM2") ?? 1.0;
                req.IncludeGeometryInfo = p.Value<bool?>("includeGeometryInfo") ?? false;
                req.IncludeStableReference = p.Value<bool?>("includeStableReference") ?? true;

                double offsetFt = UnitHelper.MmToFt(req.OffsetMm);

                // Rooms cache (for roomCheck)
                List<Autodesk.Revit.DB.Architecture.Room> rooms = null;
                if (req.RoomCheck)
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Autodesk.Revit.DB.Architecture.Room>()
                        .ToList();
                }

                var result = new WallFaceClassificationResult { ok = true };

                foreach (var id in req.ElementIds)
                {
                    var eid = new ElementId(id);
                    var wall = doc.GetElement(eid) as Autodesk.Revit.DB.Wall;
                    if (wall == null)
                    {
                        result.errors.Add(new WallFaceClassificationError { ElementId = id, Message = "Element is not a Wall." });
                        continue;
                    }

                    var wallInfo = new WallFaceClassificationForElement { ElementId = id };

                    try
                    {
                        ClassifyFacesForWall(doc, wall, wallInfo, rooms, offsetFt, req.MinAreaM2, req.RoomCheck, req.IncludeGeometryInfo, req.IncludeStableReference);
                        result.walls.Add(wallInfo);
                    }
                    catch (Exception exWall)
                    {
                        result.errors.Add(new WallFaceClassificationError { ElementId = id, Message = "Failed to classify faces: " + exWall.Message });
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new WallFaceClassificationResult
                {
                    ok = false,
                    errors = new List<WallFaceClassificationError>
                    {
                        new WallFaceClassificationError { ElementId = 0, Message = ex.Message }
                    }
                };
            }
        }

        private static void ClassifyFacesForWall(
            Document doc,
            Autodesk.Revit.DB.Wall wall,
            WallFaceClassificationForElement wallInfo,
            List<Autodesk.Revit.DB.Architecture.Room>? rooms,
            double offsetFt,
            double minAreaM2,
            bool roomCheck,
            bool includeGeometryInfo,
            bool includeStableReference)
        {
            var opt = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Medium,
                IncludeNonVisibleObjects = false
            };

            var geom = wall.get_Geometry(opt);
            if (geom == null)
                throw new InvalidOperationException("Failed to get geometry for wall.");

            // Precompute exterior/interior side face references via HostObjectUtils
            var extRefs = new HashSet<string>();
            var intRefs = new HashSet<string>();

            try
            {
                foreach (var r in HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior))
                {
                    try { extRefs.Add(r.ConvertToStableRepresentation(doc)); } catch { }
                }
            }
            catch { /* ignore */ }

            try
            {
                foreach (var r in HostObjectUtils.GetSideFaces(wall, ShellLayerType.Interior))
                {
                    try { intRefs.Add(r.ConvertToStableRepresentation(doc)); } catch { }
                }
            }
            catch { /* ignore */ }

            // Wall orientation (exterior direction)
            XYZ? wallOrient = null;
            try
            {
                wallOrient = wall.Orientation;
                if (wallOrient != null && wallOrient.IsZeroLength()) wallOrient = null;
                if (wallOrient != null) wallOrient = wallOrient.Normalize();
            }
            catch { wallOrient = null; }

            var faces = new List<PlanarFace>();
            foreach (var gObj in geom)
            {
                var solid = gObj as Solid;
                if (solid == null || solid.Faces == null || solid.Faces.Size == 0)
                    continue;

                foreach (Autodesk.Revit.DB.Face f in solid.Faces)
                {
                    if (f is PlanarFace pf)
                    {
                        faces.Add(pf);
                    }
                }
            }

            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                var pf = faces[faceIndex];
                if (pf == null) continue;

                // Basic geometric info
                var uv = new UV(0.5, 0.5);
                XYZ p;
                XYZ n;
                try
                {
                    p = pf.Evaluate(uv);
                    n = pf.ComputeNormal(uv);
                    if (n != null && !n.IsZeroLength()) n = n.Normalize();
                }
                catch
                {
                    continue;
                }

                if (n == null) continue;

                bool isVertical = Math.Abs(n.Z) < 0.707; // ~45deg
                string role = "other";

                double areaM2 = 0;
                try { areaM2 = UnitHelper.InternalToSqm(pf.Area); } catch { }

                string? stableRef = null;
                if (includeStableReference)
                {
                    try
                    {
                        if (pf.Reference != null)
                            stableRef = pf.Reference.ConvertToStableRepresentation(doc);
                    }
                    catch { stableRef = null; }
                }

                if (!isVertical)
                {
                    // Horizontal
                    if (n.Z > 0.001) role = "top";
                    else if (n.Z < -0.001) role = "bottom";
                    else role = "other";
                }
                else
                {
                    // Vertical
                    if (areaM2 < minAreaM2)
                    {
                        role = "end";
                    }
                    else
                    {
                        // Side candidate
                        string guessedByHost = null;
                        if (!string.IsNullOrEmpty(stableRef))
                        {
                            if (extRefs.Contains(stableRef)) guessedByHost = "exterior";
                            else if (intRefs.Contains(stableRef)) guessedByHost = "interior";
                        }

                        string sideByOrientation = "other";
                        if (wallOrient != null)
                        {
                            double dot = n.DotProduct(wallOrient);
                            const double eps = 0.1;
                            if (dot > eps) sideByOrientation = "exterior";
                            else if (dot < -eps) sideByOrientation = "interior";
                        }

                        string sideByRoom = "other";
                        if (roomCheck && rooms != null && rooms.Count > 0)
                        {
                            try
                            {
                                var pOut = p + n * offsetFt;
                                var pIn = p - n * offsetFt;
                                bool inOut = rooms.Any(r => r.IsPointInRoom(pOut));
                                bool inIn = rooms.Any(r => r.IsPointInRoom(pIn));
                                if (!inOut && inIn) sideByRoom = "interior";
                                else if (inOut && !inIn) sideByRoom = "exterior";
                            }
                            catch { sideByRoom = "other"; }
                        }

                        // Resolve final role preference: room > host > orientation > other
                        if (sideByRoom == "exterior" || sideByRoom == "interior")
                        {
                            role = sideByRoom;
                        }
                        else if (guessedByHost == "exterior" || guessedByHost == "interior")
                        {
                            role = guessedByHost;
                        }
                        else if (sideByOrientation == "exterior" || sideByOrientation == "interior")
                        {
                            role = sideByOrientation;
                        }
                        else
                        {
                            role = "other";
                        }
                    }
                }

                var dto = new WallFaceInfoDto
                {
                    FaceIndex = faceIndex,
                    Role = role,
                    IsVertical = isVertical
                };

                if (includeGeometryInfo)
                {
                    dto.AreaM2 = Math.Round(areaM2, 4);
                    dto.Normal = new WallFaceNormalDto { X = n.X, Y = n.Y, Z = n.Z };
                }

                if (includeStableReference)
                {
                    dto.StableReference = stableRef;
                }

                wallInfo.Faces.Add(dto);
            }
        }
    }
}

