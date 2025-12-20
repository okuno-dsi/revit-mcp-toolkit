#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture; // Room
using Autodesk.Revit.DB.Mechanical;   // Space
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Face
{
    /// <summary>
    /// 指定要素のフェイス上にある分割面（リージョン）一覧。
    /// 既定は軽量: includeGeometry=false / includeMesh=false
    /// Room/Space 所属は複数代表点（既定5点）の多数決的判定。
    /// </summary>
    public class GetFaceRegionsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_face_regions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            try
            {
                var p = (JObject)cmd.Params;

                // ---- 入力 ----
                Element elem = null;
                int elementIdIn = p.Value<int?>("elementId") ?? 0;
                string uniqueIdIn = p.Value<string>("uniqueId");
                if (elementIdIn > 0) elem = doc.GetElement(new ElementId(elementIdIn));
                else if (!string.IsNullOrWhiteSpace(uniqueIdIn)) elem = doc.GetElement(uniqueIdIn);
                if (elem == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId）。");

                if (!p.TryGetValue("faceIndex", out var faceIdxTok))
                    return ResultUtil.Err("faceIndex が必要です。");
                int faceIndex = faceIdxTok.Value<int>();

                // 軽量既定
                bool includeGeometry = p.Value<bool?>("includeGeometry") ?? false;
                bool includeMesh = p.Value<bool?>("includeMesh") ?? false;

                // Room/Space 判定（複数点プローブ）
                bool includeRoom = p.Value<bool?>("includeRoom") ?? true;
                bool includeSpace = p.Value<bool?>("includeSpace") ?? false;
                bool returnProbeHits = p.Value<bool?>("returnProbeHits") ?? false;
                int probeCount = Math.Max(1, p.Value<int?>("probeCount") ?? 5);
                string probeStrategy = p.Value<string>("probeStrategy") ?? "cross"; // cross | grid3x3
                double probeOffsetMm = p.Value<double?>("probeOffsetMm") ?? 5.0;

                // 幾何（返却時にのみ利用）
                double chordMm = p.Value<double?>("tessellateChordMm") ?? 100.0;
                double simplifyTolMm = p.Value<double?>("simplifyToleranceMm") ?? 20.0;
                int maxPointsPerLoop = Math.Max(50, p.Value<int?>("maxPointsPerLoop") ?? 500);
                // paging
                int legacyOffset = Math.Max(0, p.Value<int?>("regionOffset") ?? 0);
                int legacyLimit = Math.Max(1, p.Value<int?>("regionLimit") ?? 50);
                var shape = p["_shape"] as JObject;
                bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
                var page = shape?["page"] as JObject;
                int regionLimit = Math.Max(1, page?.Value<int?>("limit") ?? legacyLimit);
                int regionOffset = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacyOffset);
                bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

                // ---- 対象フェイス ----
                IList<Autodesk.Revit.DB.Face> faces = PaintHelper.GetPaintableFaces(elem) ?? new List<Autodesk.Revit.DB.Face>();
                if (faces.Count == 0)
                {
                    return ResultUtil.Ok(new
                    {
                        elementId = elem.Id.IntegerValue,
                        uniqueId = elem.UniqueId,
                        faceIndex,
                        regionCount = 0,
                        regions = Array.Empty<object>(),
                        inputUnits = UnitHelper.DefaultUnitsMeta(),
                        internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                    });
                }
                if (faceIndex < 0 || faceIndex >= faces.Count)
                    return ResultUtil.Err($"faceIndex {faceIndex} は範囲外です（0..{faces.Count - 1}）。");

                var face = faces[faceIndex];
                var subFaces = face.GetRegions() ?? new List<Autodesk.Revit.DB.Face>();
                int totalRegions = subFaces.Count;
                var slice = subFaces.Skip(regionOffset).Take(regionLimit).ToList();

                if (summaryOnly)
                {
                    return ResultUtil.Ok(new
                    {
                        elementId = elem.Id.IntegerValue,
                        uniqueId = elem.UniqueId,
                        faceIndex,
                        regionCount = totalRegions
                    });
                }

                if (idsOnly)
                {
                    var indices = Enumerable.Range(regionOffset, slice.Count).ToList();
                    return ResultUtil.Ok(new
                    {
                        elementId = elem.Id.IntegerValue,
                        uniqueId = elem.UniqueId,
                        faceIndex,
                        regionCount = totalRegions,
                        regionIndices = indices
                    });
                }

                // Room/Space 全件収集（※フェーズ絞りは使わない）
                var rooms = includeRoom ? CollectRooms(doc) : null;
                var spaces = includeSpace ? CollectSpaces(doc) : null;

                var regionsOut = new List<object>(slice.Count);
                for (int i = 0; i < slice.Count; i++)
                {
                    var rf = slice[i];
                    regionsOut.Add(BuildRegionInfo(
                        doc, elem, rf, regionOffset + i,
                        includeGeometry, includeMesh, chordMm, simplifyTolMm, maxPointsPerLoop,
                        includeRoom, includeSpace, rooms, spaces,
                        probeCount, probeStrategy, probeOffsetMm, returnProbeHits
                    ));
                }

                var payload = new
                {
                    elementId = elem.Id.IntegerValue,
                    uniqueId = elem.UniqueId,
                    faceIndex,
                    regionCount = totalRegions,
                    regions = regionsOut,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };

                RevitLogger.Info($"get_face_regions: elem={elem.Id.IntegerValue} face={faceIndex} regions={regionsOut.Count}/{totalRegions} probes={probeCount} strategy={probeStrategy}");
                return ResultUtil.Ok(payload);
            }
            catch (Exception ex)
            {
                RevitLogger.Error($"get_face_regions failed: {ex}");
                return ResultUtil.Err($"GetRegions 失敗: {ex.Message}");
            }
        }

        private static object BuildRegionInfo(
            Document doc, Element hostElem, Autodesk.Revit.DB.Face rf, int regionIndex,
            bool includeGeometry, bool includeMesh, double chordMm, double simplifyTolMm, int maxPointsPerLoop,
            bool includeRoom, bool includeSpace, List<Autodesk.Revit.DB.Architecture.Room> rooms, List<Autodesk.Revit.DB.Mechanical.Space> spaces,
            int probeCount, string probeStrategy, double probeOffsetMm, bool returnProbeHits)
        {
            // Material
            bool isPainted = false;
            ElementId matId = ElementId.InvalidElementId;
            try { isPainted = doc.IsPainted(hostElem.Id, rf); if (isPainted) matId = doc.GetPaintedMaterial(hostElem.Id, rf); } catch { }
            if (matId == ElementId.InvalidElementId) { try { matId = rf.MaterialElementId; } catch { } }

            string matName = "", matClass = "", hex = ""; int r = 0, g = 0, b = 0;
            var mat = matId != ElementId.InvalidElementId ? doc.GetElement(matId) as Autodesk.Revit.DB.Material : null;
            if (mat != null)
            {
                matName = mat.Name ?? "";
                matClass = mat.MaterialClass ?? "";
                try { r = mat.Color.Red; g = mat.Color.Green; b = mat.Color.Blue; hex = $"#{r:X2}{g:X2}{b:X2}"; } catch { }
            }

            // Area
            double areaFt2 = 0.0, areaM2 = 0.0;
            try { areaFt2 = rf.Area; areaM2 = UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters); } catch { }

            // StableRep
            string stableRep = "";
            try { stableRep = rf.Reference?.ConvertToStableRepresentation(doc) ?? ""; } catch { }

            // spatial（複数点）
            JObject spatialObj = null;
            if (includeRoom || includeSpace)
            {
                spatialObj = BuildSpatialInfo(rf, rooms, spaces, probeCount, probeStrategy, probeOffsetMm, returnProbeHits);
            }

            // geometry/mesh（既定は返さない）
            JArray boundaryLoops = null;
            object meshObj = null;

            if (includeGeometry)
            {
                try
                {
                    var chordFt = UnitUtils.ConvertToInternalUnits(chordMm, UnitTypeId.Millimeters);
                    var tolFt = simplifyTolMm > 0 ? UnitUtils.ConvertToInternalUnits(simplifyTolMm, UnitTypeId.Millimeters) : 0.0;
                    boundaryLoops = new JArray();
                    foreach (var loop in rf.GetEdgesAsCurveLoops())
                    {
                        var pts = TessellateWithLimit(loop, chordFt, tolFt, maxPointsPerLoop);
                        var arr = new JArray();
                        foreach (var p in pts) arr.Add(JToken.FromObject(UnitHelper.XyzToMm(p)));
                        boundaryLoops.Add(new JObject { ["points"] = arr, ["count"] = arr.Count });
                    }
                }
                catch (Exception ex)
                {
                    RevitLogger.Info($"boundary loop tessellation note: {ex.Message}");
                }
            }

            if (includeMesh)
            {
                try
                {
                    var mesh = rf.Triangulate();
                    var verts = new JArray();
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                        verts.Add(JToken.FromObject(UnitHelper.XyzToMm(mesh.Vertices[i])));
                    var tris = new JArray();
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        var t = mesh.get_Triangle(i);
                        tris.Add(new JArray(t.get_Index(0), t.get_Index(1), t.get_Index(2)));
                    }
                    meshObj = new { vertexCount = mesh.Vertices.Count, triangleCount = mesh.NumTriangles, vertices = verts, triangles = tris };
                }
                catch (Exception ex)
                {
                    RevitLogger.Info($"mesh triangulate note: {ex.Message}");
                }
            }

            return new
            {
                index = regionIndex,
                stableRep,
                isPainted,
                material = new
                {
                    id = matId?.IntegerValue ?? -1,
                    name = matName,
                    className = matClass,
                    color = new { hex, r, g, b }
                },
                area = new { internalValue = areaFt2, m2 = areaM2 },
                spatial = spatialObj,
                boundaryLoops,
                mesh = meshObj
            };
        }

        // ---------- Spatial (multi-probe) ----------
        private static JObject BuildSpatialInfo(Autodesk.Revit.DB.Face rf, List<Autodesk.Revit.DB.Architecture.Room> rooms, List<Autodesk.Revit.DB.Mechanical.Space> spaces, int probeCount, string probeStrategy, double probeOffsetMm, bool returnProbeHits)
        {
            var result = new JObject();
            var probes = ComputeProbePointsOnFace(rf, probeCount, probeStrategy, probeOffsetMm);

            int used = 0;
            var roomHits = new Dictionary<int, Autodesk.Revit.DB.Architecture.Room>();
            var spaceHits = new Dictionary<int, Autodesk.Revit.DB.Mechanical.Space>();

            foreach (var q in probes)
            {
                if (q == null) continue;
                used++;

                if (rooms != null)
                {
                    foreach (var rm in rooms)
                    {
                        try { if (rm.IsPointInRoom(q)) { roomHits[rm.Id.IntegerValue] = rm; break; } } catch { }
                    }
                }
                if (spaces != null)
                {
                    foreach (var sp in spaces)
                    {
                        try { if (sp.IsPointInSpace(q)) { spaceHits[sp.Id.IntegerValue] = sp; break; } } catch { }
                    }
                }
            }

            result["samples"] = used;
            var primaryRoom = roomHits.Values.FirstOrDefault();
            var primarySpace = spaceHits.Values.FirstOrDefault();

            result["primaryRoom"] = primaryRoom != null ? JToken.FromObject(new
            {
                id = primaryRoom.Id.IntegerValue,
                uniqueId = primaryRoom.UniqueId,
                number = SafeStr(() => primaryRoom.Number),
                name = SafeStr(() => primaryRoom.Name)
            }) : null;

            result["primarySpace"] = primarySpace != null ? JToken.FromObject(new
            {
                id = primarySpace.Id.IntegerValue,
                uniqueId = primarySpace.UniqueId,
                number = SafeStr(() => primarySpace.Number),
                name = SafeStr(() => primarySpace.Name)
            }) : null;

            if (returnProbeHits)
            {
                if (roomHits.Count > 0)
                    result["roomsHit"] = new JArray(roomHits.Values.Select(rm => new JObject
                    {
                        ["id"] = rm.Id.IntegerValue,
                        ["uniqueId"] = rm.UniqueId,
                        ["number"] = SafeStr(() => rm.Number),
                        ["name"] = SafeStr(() => rm.Name)
                    }));
                if (spaceHits.Count > 0)
                    result["spacesHit"] = new JArray(spaceHits.Values.Select(sp => new JObject
                    {
                        ["id"] = sp.Id.IntegerValue,
                        ["uniqueId"] = sp.UniqueId,
                        ["number"] = SafeStr(() => sp.Number),
                        ["name"] = SafeStr(() => sp.Name)
                    }));
            }
            return result;
        }

        private static List<XYZ?> ComputeProbePointsOnFace(Autodesk.Revit.DB.Face rf, int probeCount, string strategy, double probeOffsetMm)
        {
            var results = new List<XYZ?>(probeCount);
            BoundingBoxUV bb;
            try { bb = rf.GetBoundingBox(); }
            catch { return results; }

            var uMid = (bb.Min.U + bb.Max.U) * 0.5;
            var vMid = (bb.Min.V + bb.Max.V) * 0.5;
            var du = (bb.Max.U - bb.Min.U) / 4.0;
            var dv = (bb.Max.V - bb.Min.V) / 4.0;

            IEnumerable<UV> seeds = strategy == "grid3x3"
                ? new[]
                {
                    new UV(uMid, vMid),
                    new UV(uMid-du, vMid), new UV(uMid+du, vMid),
                    new UV(uMid, vMid-dv), new UV(uMid, vMid+dv),
                    new UV(uMid-du, vMid-dv), new UV(uMid+du, vMid-dv),
                    new UV(uMid-du, vMid+dv), new UV(uMid+du, vMid+dv)
                }
                : new[]
                {
                    new UV(uMid, vMid),
                    new UV(uMid-du, vMid), new UV(uMid+du, vMid),
                    new UV(uMid, vMid-dv), new UV(uMid, vMid+dv)
                };

            var list = seeds.Take(Math.Max(1, probeCount)).ToList();
            double off = UnitUtils.ConvertToInternalUnits(probeOffsetMm, UnitTypeId.Millimeters);

            foreach (var uv in list)
            {
                try
                {
                    bool inside = true;
                    try { inside = rf.IsInside(uv); } catch { }
                    if (!inside) { results.Add(null); continue; }

                    var p = rf.Evaluate(uv);
                    var n = rf.ComputeNormal(uv);
                    results.Add(p + n.Normalize() * off);
                }
                catch { results.Add(null); }
            }
            return results;
        }

        // ---------- Collectors（フェーズ絞りなし全件） ----------
        private static List<Autodesk.Revit.DB.Architecture.Room> CollectRooms(Document doc)
        {
            var col = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType();
            var list = new List<Autodesk.Revit.DB.Architecture.Room>();
            foreach (var e in col) if (e is Autodesk.Revit.DB.Architecture.Room r) list.Add(r);
            return list;
        }

        private static List<Autodesk.Revit.DB.Mechanical.Space> CollectSpaces(Document doc)
        {
            var col = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_MEPSpaces).WhereElementIsNotElementType();
            var list = new List<Autodesk.Revit.DB.Mechanical.Space>();
            foreach (var e in col) if (e is Autodesk.Revit.DB.Mechanical.Space s) list.Add(s);
            return list;
        }

        // ---------- Geometry helpers ----------
        private static IList<XYZ> TessellateWithLimit(CurveLoop loop, double chordFt, double tolFt, int maxPointsPerLoop)
        {
            var pts = new List<XYZ>(256);
            foreach (var c in loop) pts.AddRange(SafeTessellate(c, chordFt));
            if (tolFt > 0 && pts.Count > 2) pts = SimplifyPolylineDp(pts, tolFt);
            if (pts.Count > maxPointsPerLoop && maxPointsPerLoop > 2)
            {
                double step = (double)pts.Count / (maxPointsPerLoop - 1);
                var reduced = new List<XYZ>(maxPointsPerLoop);
                for (int i = 0; i < maxPointsPerLoop; i++)
                {
                    int idx = (i == maxPointsPerLoop - 1) ? pts.Count - 1 : (int)Math.Round(i * step);
                    reduced.Add(pts[idx]);
                }
                pts = reduced;
            }
            return pts;
        }

        private static IList<XYZ> SafeTessellate(Curve c, double maxChordInternal)
        {
            var basePts = c.Tessellate();
            if (basePts == null || basePts.Count < 2) return basePts;

            var refined = new List<XYZ>(basePts.Count);
            refined.Add(basePts[0]);
            for (int i = 1; i < basePts.Count; i++)
            {
                var a = refined[refined.Count - 1];
                var b = basePts[i];
                var d = a.DistanceTo(b);
                if (d > maxChordInternal && maxChordInternal > 1e-9)
                {
                    int div = Math.Max(1, (int)Math.Ceiling(d / maxChordInternal));
                    for (int k = 1; k < div; k++)
                    {
                        double t = (double)k / div;
                        refined.Add(new XYZ(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t));
                    }
                }
                refined.Add(b);
            }
            return refined;
        }

        private static List<XYZ> SimplifyPolylineDp(IList<XYZ> pts, double tol)
        {
            if (pts.Count < 3) return new List<XYZ>(pts);
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            Dp(pts, 0, pts.Count - 1, tol, keep);
            var res = new List<XYZ>(pts.Count);
            for (int i = 0; i < pts.Count; i++) if (keep[i]) res.Add(pts[i]);
            return res;

            static void Dp(IList<XYZ> p, int i, int j, double tol, bool[] keep)
            {
                double maxD = -1; int idx = -1;
                var a = p[i]; var b = p[j];
                for (int k = i + 1; k < j; k++)
                {
                    double d = PointToSegmentDistance(p[k], a, b);
                    if (d > maxD) { maxD = d; idx = k; }
                }
                if (maxD > tol && idx > 0)
                {
                    keep[idx] = true;
                    Dp(p, i, idx, tol, keep);
                    Dp(p, idx, j, tol, keep);
                }
            }

            static double PointToSegmentDistance(XYZ p, XYZ a, XYZ b)
            {
                var ab = (b - a);
                double denom = Math.Max(1e-12, ab.DotProduct(ab));
                double t = ((p - a).DotProduct(ab)) / denom;
                t = Math.Max(0, Math.Min(1, t));
                var q = a + t * ab;
                return p.DistanceTo(q);
            }
        }

        // 文字列用セーフ関数（C#8対応）
        private static string SafeStr(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
    }
}
