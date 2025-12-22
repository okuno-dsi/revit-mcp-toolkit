// RevitMCPAddin/Commands/ElementOps/GetWallFacesAndFinishCommands.cs
// 4コマンドを UnitHelper 化（面積= m2 への変換は UnitHelper.InternalToSqm）＆ units 付加
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    public class GetWallFacesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_faces";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            if (!WallLookupUtil.TryGetWall(doc, cmd, out var wall, out int wallId, out string uniqueId, out string err))
                return new { ok = false, msg = err };

            var faces = FaceHostHelper.GetPlanarFaces(wall) ?? new List<PlanarFace>();
            var list = new List<object>(faces.Count);

            for (int idx = 0; idx < faces.Count; idx++)
            {
                var pf = faces[idx];
                if (pf == null) continue;

                string stableRef = "";
                try { if (pf.Reference != null) stableRef = pf.Reference.ConvertToStableRepresentation(doc); } catch { }

                double areaM2 = 0;
                try { areaM2 = Math.Round(UnitHelper.InternalToSqm(pf.Area), 4); } catch { }

                list.Add(new { faceIndex = idx, reference = stableRef, area = areaM2 });
            }

            return new
            {
                ok = true,
                wallId,
                elementId = wallId,
                uniqueId,
                faces = list,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }

    public class GetFaceFinishDataCommand : IRevitCommandHandler
    {
        public string CommandName => "get_face_finish_data";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            if (!WallLookupUtil.TryGetWall(doc, cmd, out var wall, out int wallId, out string uniqueId, out var err))
                return new { ok = false, msg = err };

            int faceIndex = cmd.Params.Value<int?>("faceIndex") ?? -1;

            var faces = FaceHostHelper.GetPlanarFaces(wall) ?? new List<PlanarFace>();
            if (faceIndex < 0 || faceIndex >= faces.Count)
                return new { ok = false, msg = "Invalid faceIndex." };

            var pf = faces[faceIndex];
            if (pf == null) return new { ok = false, msg = "PlanarFace not found." };

            double areaM2 = 0;
            try { areaM2 = Math.Round(UnitHelper.InternalToSqm(pf.Area), 4); } catch { }

            string matName = "<Unknown>";
            int materialId = 0;
            if (pf.MaterialElementId != ElementId.InvalidElementId)
            {
                materialId = pf.MaterialElementId.IntValue();
                matName = (doc.GetElement(pf.MaterialElementId) as Autodesk.Revit.DB.Material)?.Name ?? "<Unknown>";
            }

            return new
            {
                ok = true,
                wallId,
                elementId = wallId,
                uniqueId,
                faceIndex,
                finish = new { materialId, materialName = matName, area = areaM2 },
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }

    public class GetFacePaintDataCommand : IRevitCommandHandler
    {
        public string CommandName => "get_face_paint_data";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            if (!WallLookupUtil.TryGetWall(doc, cmd, out var wall, out int wallId, out string uniqueId, out var err))
                return new { ok = false, msg = err };

            int faceIndex = cmd.Params.Value<int?>("faceIndex") ?? -1;

            var faces = FaceHostHelper.GetPlanarFaces(wall) ?? new List<PlanarFace>();
            if (faceIndex < 0 || faceIndex >= faces.Count)
                return new { ok = false, msg = "Invalid faceIndex." };

            var pf = faces[faceIndex];
            if (pf == null) return new { ok = false, msg = "PlanarFace not found." };

            var paints = new List<object>();
            try
            {
                if (doc.IsPainted(wall.Id, pf))
                {
                    var pmId = doc.GetPaintedMaterial(wall.Id, pf);
                    if (pmId != ElementId.InvalidElementId)
                    {
                        var paintMat = doc.GetElement(pmId) as Autodesk.Revit.DB.Material;
                        double paintAreaM2 = 0;
                        try { paintAreaM2 = Math.Round(UnitHelper.InternalToSqm(pf.Area), 4); } catch { }
                        paints.Add(new
                        {
                            materialId = pmId.IntValue(),
                            materialName = (paintMat?.Name ?? "<Unknown>"),
                            area = paintAreaM2
                        });
                    }
                }
            }
            catch { /* ignore */ }

            return new
            {
                ok = true,
                wallId,
                elementId = wallId,
                uniqueId,
                faceIndex,
                paints,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }

    public class GetWallFinishSummaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_finish_summary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。", summary = new List<object>() };

            if (!WallLookupUtil.TryGetWall(doc, cmd, out var wall, out int wallId, out string uniqueId, out var err))
                return new { ok = false, msg = err, summary = new List<object>() };

            var allFaces = FaceHostHelper.GetPlanarFaces(wall) ?? new List<PlanarFace>();
            var stats = new Dictionary<string, (double orig, double paint, bool hasRegions, bool isRegion, HashSet<int> indices)>();

            XYZ wallDir = null;
            if (wall.Location is LocationCurve lc)
                wallDir = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize();

            for (int faceIndex = 0; faceIndex < allFaces.Count; faceIndex++)
            {
                var pf = allFaces[faceIndex];
                if (pf == null) continue;

                var n = pf.FaceNormal;
                if (Math.Abs(n.Z) > 1e-6) continue;
                if (wallDir != null && Math.Abs(n.DotProduct(wallDir)) > 1e-6) continue;

                var regions = pf.HasRegions ? pf.GetRegions().OfType<PlanarFace>().ToList()
                                            : new List<PlanarFace> { pf };

                string parentMat = "<Unknown>";
                if (pf.MaterialElementId != ElementId.InvalidElementId)
                    parentMat = (doc.GetElement(pf.MaterialElementId) as Autodesk.Revit.DB.Material)?.Name ?? "<Unknown>";

                bool parentHasRegions = pf.HasRegions;
                if (parentHasRegions)
                {
                    if (!stats.ContainsKey(parentMat)) stats[parentMat] = (0, 0, true, false, new HashSet<int>());
                    var t = stats[parentMat]; t.hasRegions = true; stats[parentMat] = t;
                }

                for (int i = 0; i < regions.Count; i++)
                {
                    var r = regions[i];
                    if (r == null) continue;

                    double areaOrigM2 = 0;
                    try { areaOrigM2 = UnitHelper.InternalToSqm(r.Area); } catch { }
                    if (areaOrigM2 <= 0) continue;

                    string matName = "<Unknown>";
                    if (r.MaterialElementId != ElementId.InvalidElementId)
                        matName = (doc.GetElement(r.MaterialElementId) as Autodesk.Revit.DB.Material)?.Name ?? "<Unknown>";

                    bool isRegion = parentHasRegions && i > 0;
                    if (!stats.ContainsKey(matName)) stats[matName] = (0, 0, false, false, new HashSet<int>());
                    var ent = stats[matName];
                    ent.orig += areaOrigM2;
                    ent.hasRegions = ent.hasRegions || (parentHasRegions && i == 0);
                    ent.isRegion = ent.isRegion || isRegion;
                    ent.indices.Add(faceIndex);
                    stats[matName] = ent;

                    try
                    {
                        if (doc.IsPainted(wall.Id, r))
                        {
                            var pmId = doc.GetPaintedMaterial(wall.Id, r);
                            if (pmId != ElementId.InvalidElementId)
                            {
                                double areaPaintM2 = 0;
                                try { areaPaintM2 = UnitHelper.InternalToSqm(r.Area); } catch { }
                                var paintName = (doc.GetElement(pmId) as Autodesk.Revit.DB.Material)?.Name ?? "<Unknown>";
                                if (!stats.ContainsKey(paintName)) stats[paintName] = (0, 0, false, false, new HashSet<int>());
                                var pt = stats[paintName];
                                pt.paint += areaPaintM2;
                                pt.indices.Add(faceIndex);
                                stats[paintName] = pt;
                            }
                        }
                    }
                    catch { }
                }
            }

            var summary = stats.Select(kv => new
            {
                material = kv.Key,
                originalArea = Math.Round(kv.Value.orig, 4),
                paintedArea = Math.Round(kv.Value.paint, 4),
                netArea = Math.Round(kv.Value.orig - kv.Value.paint, 4),
                hasRegions = kv.Value.hasRegions,
                isRegion = kv.Value.isRegion,
                faceIndices = kv.Value.indices.OrderBy(i => i).ToList()
            }).ToList();

            return new
            {
                ok = true,
                wallId,
                elementId = wallId,
                uniqueId,
                summary,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }
}

