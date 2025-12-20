// ============================================================================
// File   : Commands/Area/AreaBoundaryMaterialCoreCenterCommands.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Purpose:
//   - area_boundary_create_by_material_corecenter
//   - area_boundary_adjust_by_material_corecenter
// Notes  :
//   - Align Area Boundary Lines to the centerline of a specified material layer
//     within a wall type CompoundStructure.
//   - Based on: Codex/Design/Revit_AreaBoundary_AlignToMaterialCoreCenter_DesignSpec_EN.md
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Area
{
    internal static class AreaBoundaryMaterialCoreCenterUtil
    {
        private static bool TryGetInt(JObject obj, IEnumerable<string> keys, out int value)
        {
            value = 0;
            if (obj == null) return false;
            foreach (var k in keys)
            {
                if (!obj.TryGetValue(k, out var tok) || tok == null) continue;
                try
                {
                    if (tok.Type == JTokenType.Integer) { value = tok.Value<int>(); return true; }
                    if (tok.Type == JTokenType.String && int.TryParse(tok.Value<string>(), out var v)) { value = v; return true; }
                }
                catch { }
            }
            return false;
        }

        private static bool TryGetString(JObject obj, IEnumerable<string> keys, out string value)
        {
            value = null;
            if (obj == null) return false;
            foreach (var k in keys)
            {
                if (!obj.TryGetValue(k, out var tok) || tok == null) continue;
                try
                {
                    var s = tok.Value<string>();
                    if (!string.IsNullOrWhiteSpace(s)) { value = s; return true; }
                }
                catch { }
            }
            return false;
        }

        private static IEnumerable<ElementId> ParseElementIds(JToken tok)
        {
            if (tok == null) yield break;

            if (tok.Type == JTokenType.Array)
            {
                foreach (var t in (JArray)tok)
                {
                    foreach (var id in ParseElementIds(t))
                        yield return id;
                }
                yield break;
            }

            if (tok.Type == JTokenType.Integer)
            {
                var v = tok.Value<int>();
                if (v != 0) yield return new ElementId(v);
                yield break;
            }

            if (tok.Type == JTokenType.String)
            {
                if (int.TryParse(tok.Value<string>(), out var v) && v != 0)
                    yield return new ElementId(v);
                yield break;
            }

            if (tok.Type == JTokenType.Object)
            {
                var jo = (JObject)tok;
                if (TryGetInt(jo, new[] { "elementId", "id", "wallId", "areaId" }, out var v) && v != 0)
                    yield return new ElementId(v);
            }
        }

        private static List<ElementId> DistinctIds(IEnumerable<ElementId> ids)
        {
            var set = new HashSet<int>();
            var list = new List<ElementId>();
            foreach (var id in ids ?? Enumerable.Empty<ElementId>())
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                if (set.Add(id.IntegerValue)) list.Add(id);
            }
            return list;
        }

        private static List<ElementId> ParseElementIdsFromKeys(JObject p, params string[] keys)
        {
            var list = new List<ElementId>();
            foreach (var k in keys ?? Array.Empty<string>())
            {
                if (!p.TryGetValue(k, out var tok2) || tok2 == null) continue;
                list.AddRange(ParseElementIds(tok2));
            }
            return list;
        }

        public static ViewPlan ResolveAreaPlanView(Document doc, UIApplication uiapp, JObject p, out Level level)
        {
            level = null;
            View view = null;

            if (TryGetInt(p, new[] { "viewId", "view_id" }, out var vid) && vid > 0)
                view = doc.GetElement(new ElementId(vid)) as View;

            if (view == null && TryGetString(p, new[] { "viewUniqueId", "view_unique_id" }, out var vuid))
                view = doc.GetElement(vuid) as View;

            if (view == null) view = doc.ActiveView;

            var vp = view as ViewPlan;
            if (vp == null || vp.ViewType != ViewType.AreaPlan)
                throw new InvalidOperationException("Area Plan ビュー(viewId/viewUniqueId)を指定するか、Area Plan をアクティブにしてください。");

            level = vp.GenLevel;
            if (level == null)
                throw new InvalidOperationException("Area Plan のレベルが解決できません。");

            return vp;
        }

        public static SketchPlane GetOrCreateSketchPlane(Document doc, ViewPlan vp, Level level)
        {
            var sp = vp?.SketchPlane;
            if (sp != null) return sp;

            var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
            sp = SketchPlane.Create(doc, plane);
            try { if (vp != null) vp.SketchPlane = sp; } catch { }
            return sp;
        }

        public static bool TryResolveMaterialId(Document doc, JObject p, out ElementId materialId, out string materialName, out string error)
        {
            materialId = ElementId.InvalidElementId;
            materialName = null;
            error = null;

            JObject matObj = null;
            if (p.TryGetValue("material", out var mt) && mt is JObject mj) matObj = mj;

            if (TryGetInt(matObj ?? p, new[] { "id", "materialId", "material_id" }, out var mid) && mid > 0)
            {
                var m = doc.GetElement(new ElementId(mid)) as Material;
                if (m == null) { error = $"Material not found: {mid}"; return false; }
                materialId = m.Id;
                materialName = m.Name;
                return true;
            }

            if (TryGetString(matObj ?? p, new[] { "name", "materialName", "material_name" }, out var mname))
            {
                var m = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(x => string.Equals(x.Name, mname, StringComparison.OrdinalIgnoreCase));
                if (m == null) { error = $"Material not found by name: {mname}"; return false; }
                materialId = m.Id;
                materialName = m.Name;
                return true;
            }

            error = "materialId/materialName（または material:{id|name}）が必要です。";
            return false;
        }

        public static List<ElementId> ResolveWallIds(UIDocument uidoc, JObject p)
        {
            var ids = new List<ElementId>();
            ids.AddRange(ParseElementIdsFromKeys(p,
                "wall_element_ids", "wallElementIds", "wallIds", "wall_ids",
                "elementIds", "items"));

            ids = DistinctIds(ids);
            if (ids.Count > 0) return ids;

            try
            {
                var sel = uidoc?.Selection?.GetElementIds();
                if (sel != null) ids.AddRange(sel);
            }
            catch { }

            return DistinctIds(ids);
        }

        public static void ResolveAreaAndWallIds(UIDocument uidoc, JObject p, out List<ElementId> areaIds, out List<ElementId> wallIds)
        {
            areaIds = DistinctIds(ParseElementIdsFromKeys(p, "area_element_ids", "areaElementIds", "areaIds", "area_ids"));
            wallIds = DistinctIds(ParseElementIdsFromKeys(p, "wall_element_ids", "wallElementIds", "wallIds", "wall_ids"));

            if (areaIds.Count > 0 || wallIds.Count > 0) return;

            try
            {
                var sel = uidoc?.Selection?.GetElementIds() ?? new List<ElementId>();
                foreach (var id in sel)
                {
                    var e = uidoc.Document.GetElement(id);
                    if (e is Autodesk.Revit.DB.Area) areaIds.Add(id);
                    else if (e is Autodesk.Revit.DB.Wall) wallIds.Add(id);
                }
            }
            catch { }

            areaIds = DistinctIds(areaIds);
            wallIds = DistinctIds(wallIds);
        }

        // ----------------------------
        // Geometry helpers
        // ----------------------------
        private static XYZ SafeNormalize(XYZ v)
        {
            try
            {
                if (v == null) return XYZ.BasisX;
                if (v.GetLength() < 1e-9) return XYZ.BasisX;
                return v.Normalize();
            }
            catch { return XYZ.BasisX; }
        }

        private static XYZ SafeNormalizeXY(XYZ v)
        {
            try
            {
                if (v == null) return XYZ.BasisX;
                var h = new XYZ(v.X, v.Y, 0);
                if (h.GetLength() < 1e-9) return XYZ.BasisX;
                return h.Normalize();
            }
            catch { return XYZ.BasisX; }
        }

        private static bool TryGetLargestPlanarSideFace(Document doc, Autodesk.Revit.DB.Wall wall, ShellLayerType side, out PlanarFace face, out double bestAreaFt2)
        {
            face = null;
            bestAreaFt2 = 0.0;
            try
            {
                var refs = HostObjectUtils.GetSideFaces(wall, side);
                if (refs == null || refs.Count == 0) return false;

                double best = double.MinValue;
                PlanarFace bestFace = null;
                foreach (var r in refs)
                {
                    try
                    {
                        var f = wall.GetGeometryObjectFromReference(r) as PlanarFace;
                        if (f == null) continue;
                        double a = 0.0;
                        try { a = f.Area; } catch { a = 0.0; }
                        if (a > best)
                        {
                            best = a;
                            bestFace = f;
                        }
                    }
                    catch { }
                }

                if (bestFace == null) return false;
                face = bestFace;
                bestAreaFt2 = best <= double.MinValue ? 0.0 : best;
                return true;
            }
            catch
            {
                face = null;
                bestAreaFt2 = 0.0;
                return false;
            }
        }

        private static XYZ TangentAtMid(Curve c)
        {
            try
            {
                if (c is Line ln) return SafeNormalize(ln.Direction);
                var d = c.ComputeDerivatives(0.5, true);
                return SafeNormalize(d.BasisX);
            }
            catch { return XYZ.BasisX; }
        }

        internal static Curve FlattenCurveToZ(Curve curve, double z, out string warning)
        {
            warning = null;
            if (curve == null) { warning = "curve is null"; return null; }

            try
            {
                if (curve is Line)
                {
                    var p0 = curve.GetEndPoint(0);
                    var p1 = curve.GetEndPoint(1);
                    return Line.CreateBound(new XYZ(p0.X, p0.Y, z), new XYZ(p1.X, p1.Y, z));
                }

                if (curve is Arc)
                {
                    var p0 = curve.GetEndPoint(0);
                    var p1 = curve.GetEndPoint(1);
                    var pm = curve.Evaluate(0.5, true);
                    var q0 = new XYZ(p0.X, p0.Y, z);
                    var q1 = new XYZ(p1.X, p1.Y, z);
                    var qm = new XYZ(pm.X, pm.Y, z);
                    return Arc.Create(q0, q1, qm);
                }

                var e0 = curve.GetEndPoint(0);
                var e1 = curve.GetEndPoint(1);
                if (Math.Abs(e0.Z - e1.Z) < 1e-6)
                {
                    var dz = z - e0.Z;
                    return curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, dz)));
                }
            }
            catch (Exception ex)
            {
                warning = ex.Message;
                return null;
            }

            warning = $"Unsupported curve type: {curve.GetType().Name}";
            return null;
        }

        private static int SafeFirstCore(CompoundStructure cs)
        {
            try { return cs.GetFirstCoreLayerIndex(); } catch { return -1; }
        }

        private static int SafeLastCore(CompoundStructure cs)
        {
            try { return cs.GetLastCoreLayerIndex(); } catch { return -1; }
        }

        private static MaterialFunctionAssignment SafeLayerFunction(IList<CompoundStructureLayer> layers, int i)
        {
            try { return layers[i].Function; }
            catch { return MaterialFunctionAssignment.None; }
        }

        private static int ChooseTargetLayerIndex(
            IList<CompoundStructureLayer> layers,
            List<int> matches,
            int firstCore,
            int lastCore,
            bool includeNonCore,
            out string ruleUsed)
        {
            ruleUsed = "thickest";
            if (matches == null || matches.Count == 0) return -1;

            Func<IEnumerable<int>, int> pickThickest = (idxs) =>
            {
                int best = -1;
                double bestW = double.MinValue;
                foreach (var i in idxs)
                {
                    double w = 0;
                    try { w = layers[i].Width; } catch { }
                    if (w > bestW + 1e-9) { bestW = w; best = i; continue; }
                    if (Math.Abs(w - bestW) <= 1e-9 && (best < 0 || i < best)) best = i;
                }
                return best;
            };

            bool hasCoreRange = firstCore >= 0 && lastCore >= 0 && firstCore <= lastCore;

            // Legacy (design spec): Structure -> Core -> Thickest (non-core layers can win)
            if (includeNonCore)
            {
                var structure = matches.Where(i => SafeLayerFunction(layers, i) == MaterialFunctionAssignment.Structure).ToList();
                if (structure.Count > 0)
                {
                    ruleUsed = "structure_thickest";
                    return pickThickest(structure);
                }

                if (hasCoreRange)
                {
                    var core = matches.Where(i => i >= firstCore && i <= lastCore).ToList();
                    if (core.Count > 0)
                    {
                        ruleUsed = "core_thickest";
                        return pickThickest(core);
                    }
                }

                ruleUsed = "thickest";
                return pickThickest(matches);
            }

            // Default (core-priority): if material exists in core range, use core only.
            // Rationale: when finish + core share the same material, averaging/choosing the finish
            // often yields the total-thickness center rather than the intended core centerline.
            if (hasCoreRange)
            {
                var coreMatches = matches.Where(i => i >= firstCore && i <= lastCore).ToList();
                if (coreMatches.Count > 0)
                {
                    var coreStructure = coreMatches.Where(i => SafeLayerFunction(layers, i) == MaterialFunctionAssignment.Structure).ToList();
                    if (coreStructure.Count > 0)
                    {
                        ruleUsed = "core_structure_thickest";
                        return pickThickest(coreStructure);
                    }
                    ruleUsed = "core_thickest";
                    return pickThickest(coreMatches);
                }
            }

            // No core match: fall back to structure/thickness.
            var structureFallback = matches.Where(i => SafeLayerFunction(layers, i) == MaterialFunctionAssignment.Structure).ToList();
            if (structureFallback.Count > 0)
            {
                ruleUsed = "structure_thickest";
                return pickThickest(structureFallback);
            }

            ruleUsed = "thickest";
            return pickThickest(matches);
        }

        private static int SafeWallLocationLineValue(Autodesk.Revit.DB.Wall wall)
        {
            try
            {
                var p = wall?.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
                if (p != null)
                {
                    try { return p.AsInteger(); } catch { return 0; }
                }
            }
            catch { }
            return 0;
        }

        private static string WallLocationLineName(int value)
        {
            switch (value)
            {
                case 0: return "WallCenterline";
                case 1: return "CoreCenterline";
                case 2: return "FinishFaceExterior";
                case 3: return "FinishFaceInterior";
                case 4: return "CoreFaceExterior";
                case 5: return "CoreFaceInterior";
                default: return value.ToString();
            }
        }

        private static double SumWidths(IList<double> widths, int startInclusive, int endInclusive)
        {
            if (widths == null || widths.Count == 0) return 0;
            startInclusive = Math.Max(0, startInclusive);
            endInclusive = Math.Min(widths.Count - 1, endInclusive);
            if (startInclusive > endInclusive) return 0;
            double sum = 0;
            for (int i = startInclusive; i <= endInclusive; i++) sum += widths[i];
            return sum;
        }

        internal static JObject CurveToJson(Curve c)
        {
            if (c == null) return new JObject { ["type"] = "null" };
            var o = new JObject();
            o["type"] = c.GetType().Name;
            try
            {
                o["startMm"] = AreaGeom.XyzToMm(c.GetEndPoint(0));
                o["endMm"] = AreaGeom.XyzToMm(c.GetEndPoint(1));
                o["midMm"] = AreaGeom.XyzToMm(c.Evaluate(0.5, true));
                o["lengthMm"] = Math.Round(UnitHelper.FtToMm(c.ApproximateLength), 3);
            }
            catch { }
            return o;
        }

        public static bool TryComputeTargetCurveFromWall(
            Document doc,
            Autodesk.Revit.DB.Wall wall,
            ElementId materialId,
            Level level,
            out Curve targetCurve,
            out JObject debug,
            out string warning)
        {
            return TryComputeTargetCurveFromWall(
                doc,
                wall,
                materialId,
                level,
                includeNonCore: false,
                includeLayerDetails: false,
                fallbackToCoreCenterlineWhenMaterialMissing: false,
                out targetCurve,
                out debug,
                out warning);
        }

        public static bool TryComputeTargetCurveFromWall(
            Document doc,
            Autodesk.Revit.DB.Wall wall,
            ElementId materialId,
            Level level,
            bool includeNonCore,
            bool includeLayerDetails,
            bool fallbackToCoreCenterlineWhenMaterialMissing,
            out Curve targetCurve,
            out JObject debug,
            out string warning)
        {
            targetCurve = null;
            debug = new JObject();
            warning = null;

            if (doc == null) { warning = "doc is null"; return false; }
            if (wall == null) { warning = "wall is null"; return false; }
            if (materialId == null || materialId == ElementId.InvalidElementId) { warning = "materialId is invalid"; return false; }
            if (level == null) { warning = "level is null"; return false; }

            debug["wallId"] = wall.Id.IntegerValue;
            debug["wallUniqueId"] = wall.UniqueId;
            debug["wallName"] = wall.Name ?? "";
            debug["layerSelection"] = new JObject
            {
                ["includeNonCore"] = includeNonCore,
                ["includeLayerDetails"] = includeLayerDetails,
                ["fallbackToCoreCenterlineWhenMaterialMissing"] = fallbackToCoreCenterlineWhenMaterialMissing
            };

            var lc = wall.Location as LocationCurve;
            if (lc == null || lc.Curve == null)
            {
                warning = $"Wall {wall.Id.IntegerValue} skipped: no LocationCurve.";
                return false;
            }

            var baseCurveRaw = lc.Curve;
            var baseCurve = FlattenCurveToZ(baseCurveRaw, level.Elevation, out var flatWarn);
            if (baseCurve == null)
            {
                warning = $"Wall {wall.Id.IntegerValue} skipped: curve flatten failed ({flatWarn}).";
                return false;
            }
            debug["baseCurve"] = CurveToJson(baseCurve);

            var wt = doc.GetElement(wall.GetTypeId()) as WallType;
            if (wt == null)
            {
                warning = $"Wall {wall.Id.IntegerValue} skipped: WallType not found.";
                return false;
            }
            debug["wallTypeId"] = wt.Id.IntegerValue;
            debug["wallTypeName"] = wt.Name ?? "";

            CompoundStructure cs = null;
            try { cs = wt.GetCompoundStructure(); } catch { cs = null; }
            if (cs == null)
            {
                warning = $"Wall {wall.Id.IntegerValue} skipped: no CompoundStructure (Curtain/Stacked wall etc.).";
                return false;
            }

            IList<CompoundStructureLayer> layers = null;
            try { layers = cs.GetLayers(); } catch { layers = null; }
            if (layers == null || layers.Count == 0)
            {
                warning = $"Wall {wall.Id.IntegerValue} skipped: CompoundStructure has no layers.";
                return false;
            }

            var matches = new List<int>();
            for (int i = 0; i < layers.Count; i++)
            {
                try
                {
                    var mid = layers[i].MaterialId;
                    if (mid != null && mid.IntegerValue == materialId.IntegerValue) matches.Add(i);
                }
                catch { }
            }
            bool materialMatched = matches.Count > 0;

            int firstCore = SafeFirstCore(cs);
            int lastCore = SafeLastCore(cs);
            if (firstCore < 0 || lastCore < 0 || firstCore > lastCore || lastCore >= layers.Count)
            {
                firstCore = -1;
                lastCore = -1;
            }

            var ruleUsed = "";
            int targetLayerIndex = -1;
            if (materialMatched)
            {
                targetLayerIndex = ChooseTargetLayerIndex(layers, matches, firstCore, lastCore, includeNonCore, out ruleUsed);
                if (targetLayerIndex < 0)
                {
                    warning = $"Wall {wall.Id.IntegerValue} skipped: target layer resolution failed.";
                    return false;
                }
            }
            else
            {
                if (!fallbackToCoreCenterlineWhenMaterialMissing)
                {
                    warning = $"Wall {wall.Id.IntegerValue} skipped: materialId {materialId.IntegerValue} not found in CompoundStructure layers.";
                    return false;
                }
            }

            debug["layerCount"] = layers.Count;
            debug["materialId"] = materialId.IntegerValue;
            debug["materialMatched"] = materialMatched;
            debug["targetLayerIndex"] = materialMatched ? (int?)targetLayerIndex : (int?)null;

            if (materialMatched)
                debug["targetLayerRule"] = ruleUsed;

            var coreRangeDbg = new JObject
            {
                ["firstCoreLayerIndex"] = firstCore,
                ["lastCoreLayerIndex"] = lastCore
            };
            debug["coreRange"] = coreRangeDbg;

            var widths = new List<double>();
            foreach (var l in layers)
            {
                try { widths.Add(l.Width); }
                catch { widths.Add(0); }
            }
            double total = widths.Sum();
            if (total <= 1e-9)
            {
                warning = $"Wall {wall.Id.IntegerValue} skipped: wall thickness is zero.";
                return false;
            }

            double coreStartExtType = 0;
            double coreEndExtType = total;
            if (firstCore >= 0 && lastCore >= 0)
            {
                coreStartExtType = SumWidths(widths, 0, firstCore - 1);
                coreEndExtType = SumWidths(widths, 0, lastCore);
            }

            double xTargetExtType = 0.0;
            if (materialMatched)
                xTargetExtType = SumWidths(widths, 0, targetLayerIndex - 1) + (widths[targetLayerIndex] * 0.5);

            bool wallFlipped = false;
            try { wallFlipped = wall.Flipped; } catch { wallFlipped = false; }

            // Thickness coordinate mapping (type exterior -> wall exterior):
            // In this Revit API environment, the wall's "Exterior" side corresponds to the type's exterior
            // layer ordering, even when the wall is flipped. Therefore we DO NOT mirror thickness coords.
            bool mirrorThickness = false;
            string mirrorSource = "no_mirror";

            // Cache planar side faces for geometry-based validation.
            PlanarFace extFace = null;
            double extAreaFt2 = 0.0;
            PlanarFace intFace = null;
            double intAreaFt2 = 0.0;
            try
            {
                TryGetLargestPlanarSideFace(doc, wall, ShellLayerType.Exterior, out extFace, out extAreaFt2);
                TryGetLargestPlanarSideFace(doc, wall, ShellLayerType.Interior, out intFace, out intAreaFt2);
            }
            catch { }

            // Exterior direction (XY). If we have an exterior planar face, align the direction to its normal.
            XYZ extDir = SafeNormalizeXY(wall.Orientation);
            try
            {
                if (extFace != null)
                {
                    var n = SafeNormalizeXY(extFace.FaceNormal);
                    var dot = extDir.DotProduct(n);
                    debug["orientationDotExteriorFaceNormal"] = Math.Round(dot, 6);
                    if (dot < 0) extDir = extDir.Negate();
                }
            }
            catch { }

            // Map core-range coordinates into "distance from wall exterior".
            double coreStartExtPhys = coreStartExtType;
            double coreEndExtPhys = coreEndExtType;

            coreStartExtPhys = Math.Max(0.0, Math.Min(total, coreStartExtPhys));
            coreEndExtPhys = Math.Max(0.0, Math.Min(total, coreEndExtPhys));
            if (coreStartExtPhys > coreEndExtPhys)
            {
                var tmp = coreStartExtPhys;
                coreStartExtPhys = coreEndExtPhys;
                coreEndExtPhys = tmp;
            }

            double xTargetExt = 0.0;
            if (materialMatched)
            {
                xTargetExt = mirrorThickness ? (total - xTargetExtType) : xTargetExtType;
                debug["targetMode"] = "material_layer_center";
            }
            else
            {
                bool hasCore = firstCore >= 0 && lastCore >= 0 && firstCore <= lastCore;
                xTargetExt = hasCore ? (coreStartExtPhys + coreEndExtPhys) * 0.5 : (total * 0.5);
                debug["targetMode"] = hasCore ? "core_centerline_fallback" : "wall_centerline_fallback";
                debug["targetLayerRule"] = hasCore ? "fallback_core_centerline" : "fallback_wall_centerline";

                // Informational warning (success path). Caller may choose to surface it.
                warning = $"Wall {wall.Id.IntegerValue}: materialId {materialId.IntegerValue} not found; used {(hasCore ? "core centerline" : "wall centerline")} fallback.";
            }

            xTargetExt = Math.Max(0.0, Math.Min(total, xTargetExt));

            try
            {
                coreRangeDbg["coreStartFromTypeExteriorMm"] = Math.Round(UnitHelper.FtToMm(coreStartExtType), 3);
                coreRangeDbg["coreEndFromTypeExteriorMm"] = Math.Round(UnitHelper.FtToMm(coreEndExtType), 3);
                coreRangeDbg["coreStartFromExteriorMm"] = Math.Round(UnitHelper.FtToMm(coreStartExtPhys), 3);
                coreRangeDbg["coreEndFromExteriorMm"] = Math.Round(UnitHelper.FtToMm(coreEndExtPhys), 3);
                coreRangeDbg["coreThicknessMm"] = Math.Round(UnitHelper.FtToMm(coreEndExtPhys - coreStartExtPhys), 3);
            }
            catch { }

            debug["wallFlipped"] = wallFlipped;
            debug["thicknessMirrored"] = mirrorThickness;
            debug["thicknessMirrorSource"] = mirrorSource;
            debug["wallThicknessMm"] = Math.Round(UnitHelper.FtToMm(total), 3);
            debug["xTargetExtTypeMm"] = materialMatched ? (double?)Math.Round(UnitHelper.FtToMm(xTargetExtType), 3) : (double?)null;
            debug["xTargetExtMm"] = Math.Round(UnitHelper.FtToMm(xTargetExt), 3);

            if (materialMatched)
            {
                try
                {
                    debug["targetLayerWidthMm"] = Math.Round(UnitHelper.FtToMm(widths[targetLayerIndex]), 3);
                    debug["targetLayerFunction"] = SafeLayerFunction(layers, targetLayerIndex).ToString();
                    ElementId targetMatId = ElementId.InvalidElementId;
                    try { targetMatId = layers[targetLayerIndex].MaterialId; } catch { targetMatId = ElementId.InvalidElementId; }
                    debug["targetLayerMaterialId"] = (targetMatId == null || targetMatId == ElementId.InvalidElementId) ? (int?)null : targetMatId.IntegerValue;
                    if (targetMatId != null && targetMatId != ElementId.InvalidElementId)
                    {
                        var m = doc.GetElement(targetMatId) as Material;
                        debug["targetLayerMaterialName"] = m?.Name ?? "";
                    }
                }
                catch { }
            }

            int locLine = SafeWallLocationLineValue(wall);
            double xRefExt;
            switch (locLine)
            {
                case 2: xRefExt = 0; break;
                case 3: xRefExt = total; break;
                case 0: xRefExt = total * 0.5; break;
                case 1: xRefExt = (coreStartExtPhys + coreEndExtPhys) * 0.5; break;
                case 4: xRefExt = coreStartExtPhys; break;
                case 5: xRefExt = coreEndExtPhys; break;
                default: xRefExt = total * 0.5; break;
            }

            if (includeLayerDetails)
            {
                var matNameCache = new Dictionary<int, string>();
                Func<ElementId, string> getMatName = (mid) =>
                {
                    try
                    {
                        if (mid == null || mid == ElementId.InvalidElementId) return null;
                        if (matNameCache.TryGetValue(mid.IntegerValue, out var nm)) return nm;
                        var m = doc.GetElement(mid) as Material;
                        nm = m?.Name;
                        matNameCache[mid.IntegerValue] = nm;
                        return nm;
                    }
                    catch { return null; }
                };

                var arr = new JArray();
                bool hasCore = firstCore >= 0 && lastCore >= 0 && firstCore <= lastCore;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    var mid = ElementId.InvalidElementId;
                    double w = 0;
                    MaterialFunctionAssignment fn = MaterialFunctionAssignment.None;
                    try { mid = layer.MaterialId; } catch { mid = ElementId.InvalidElementId; }
                    try { w = layer.Width; } catch { w = 0; }
                    try { fn = layer.Function; } catch { fn = MaterialFunctionAssignment.None; }

                    arr.Add(new JObject
                    {
                        ["index"] = i,
                        ["widthMm"] = Math.Round(UnitHelper.FtToMm(w), 3),
                        ["function"] = fn.ToString(),
                        ["materialId"] = (mid == null || mid == ElementId.InvalidElementId) ? (int?)null : mid.IntegerValue,
                        ["materialName"] = getMatName(mid) ?? "",
                        ["matchedMaterial"] = (mid != null && mid.IntegerValue == materialId.IntegerValue),
                        ["inCoreRange"] = hasCore && i >= firstCore && i <= lastCore,
                        ["isTargetLayer"] = i == targetLayerIndex
                    });
                }
                debug["layers"] = arr;
            }

            // --- Geometry-based reference validation (straight walls) ---
            // In some projects, the wall LocationCurve can remain at the original driving line
            // even if the type's core boundaries were edited later. This makes the UI "Core Centerline"
            // appear shifted relative to LocationCurve. We measure the actual distance from the exterior face
            // to the LocationCurve midpoint and, when valid, use it as the reference position.
            double xBaseExt = xRefExt;
            string xBaseSource = "param";
            double? baseFromExterior = null;
            double? baseFromInterior = null;

            try
            {
                var pBase = baseCurve.Evaluate(0.5, true);

                // Ensure faces are available (some walls may not have planar side faces).
                if (extFace == null)
                {
                    try { TryGetLargestPlanarSideFace(doc, wall, ShellLayerType.Exterior, out extFace, out extAreaFt2); } catch { }
                }

                if (extFace != null)
                {
                    // x measured from exterior face inward (positive)
                    baseFromExterior = -((pBase - extFace.Origin).DotProduct(extDir));
                    debug["exteriorFaceAreaM2"] = Math.Round(UnitHelper.InternalToSqm(extAreaFt2), 6);
                    debug["baseFromExteriorFaceMm"] = Math.Round(UnitHelper.FtToMm(baseFromExterior.Value), 3);

                    // Keep extDir consistent with the exterior face normal (in case we couldn't align earlier)
                    try
                    {
                        var n = SafeNormalizeXY(extFace.FaceNormal);
                        var dot = extDir.DotProduct(n);
                        if (dot < 0) extDir = extDir.Negate();
                    }
                    catch { }
                }

                if (intFace == null)
                {
                    try { TryGetLargestPlanarSideFace(doc, wall, ShellLayerType.Interior, out intFace, out intAreaFt2); } catch { }
                }

                if (intFace != null)
                {
                    baseFromInterior = ((pBase - intFace.Origin).DotProduct(extDir));
                    debug["interiorFaceAreaM2"] = Math.Round(UnitHelper.InternalToSqm(intAreaFt2), 6);
                    debug["baseFromInteriorFaceMm"] = Math.Round(UnitHelper.FtToMm(baseFromInterior.Value), 3);
                }

                bool extOk = baseFromExterior.HasValue && baseFromExterior.Value >= UnitHelper.MmToFt(-5) && baseFromExterior.Value <= total + UnitHelper.MmToFt(5);
                bool intOk = baseFromInterior.HasValue && baseFromInterior.Value >= UnitHelper.MmToFt(-5) && baseFromInterior.Value <= total + UnitHelper.MmToFt(5);

                if (extOk && intOk)
                {
                    var sum = baseFromExterior.Value + baseFromInterior.Value;
                    debug["baseToFacesSumMm"] = Math.Round(UnitHelper.FtToMm(sum), 3);
                    if (Math.Abs(sum - total) <= UnitHelper.MmToFt(5.0))
                    {
                        xBaseExt = Math.Max(0.0, Math.Min(total, baseFromExterior.Value));
                        xBaseSource = "geometry";
                    }
                    else
                    {
                        // If faces are not consistent with thickness (joins/openings), do not override.
                        debug["baseToFacesSumMismatchMm"] = Math.Round(UnitHelper.FtToMm(sum - total), 3);
                    }
                }
                else if (extOk)
                {
                    // Use exterior-only when interior cannot be obtained; still clamp.
                    xBaseExt = Math.Max(0.0, Math.Min(total, baseFromExterior.Value));
                    xBaseSource = "geometry_ext_only";
                }

                debug["xBaseExtMm"] = Math.Round(UnitHelper.FtToMm(xBaseExt), 3);
                debug["xBaseExtSource"] = xBaseSource;
                debug["xRefExtMismatchMm"] = Math.Round(UnitHelper.FtToMm(xBaseExt - xRefExt), 3);
            }
            catch (Exception exGeom)
            {
                debug["xBaseExtSource"] = "param";
                debug["geomRefNote"] = exGeom.Message;
            }

            // Physical delta (outward positive): move LocationCurve to target layer centerline.
            // x is measured from exterior face inward, so delta = xBase - xTarget.
            double deltaFt = xBaseExt - xTargetExt;

            debug["wallLocationLine"] = new JObject
            {
                ["value"] = locLine,
                ["name"] = WallLocationLineName(locLine)
            };
            debug["xRefExtMm"] = Math.Round(UnitHelper.FtToMm(xRefExt), 3);
            debug["xTargetExtMm"] = Math.Round(UnitHelper.FtToMm(xTargetExt), 3);
            debug["wallThicknessMm"] = Math.Round(UnitHelper.FtToMm(total), 3);
            debug["deltaMm"] = Math.Round(UnitHelper.FtToMm(deltaFt), 3);

            Curve offset = null;
            double appliedOffsetFt = deltaFt;

            if (baseCurve is Line)
            {
                var v = extDir.Multiply(deltaFt);
                offset = baseCurve.CreateTransformed(Transform.CreateTranslation(v));
                appliedOffsetFt = deltaFt;
            }
            else
            {
                var tan = TangentAtMid(baseCurve);
                var left = SafeNormalize(XYZ.BasisZ.CrossProduct(tan));
                var ext = extDir;
                var leftDotExt = left.DotProduct(ext);

                double distToUse = deltaFt;
                if (deltaFt > 0 && leftDotExt < 0) distToUse = -deltaFt;
                else if (deltaFt < 0 && leftDotExt > 0) distToUse = -deltaFt;

                try
                {
                    offset = baseCurve.CreateOffset(distToUse, XYZ.BasisZ);
                    appliedOffsetFt = distToUse;
                }
                catch
                {
                    try
                    {
                        var v = extDir.Multiply(deltaFt);
                        offset = baseCurve.CreateTransformed(Transform.CreateTranslation(v));
                        appliedOffsetFt = deltaFt;
                    }
                    catch { offset = null; }
                }
            }

            if (offset == null)
            {
                warning = $"Wall {wall.Id.IntegerValue} skipped: failed to compute offset curve.";
                return false;
            }

            var offsetFlat = FlattenCurveToZ(offset, level.Elevation, out var offWarn);
            if (offsetFlat == null)
            {
                warning = $"Wall {wall.Id.IntegerValue} skipped: offset curve flatten failed ({offWarn}).";
                return false;
            }

            debug["appliedOffsetMm"] = Math.Round(UnitHelper.FtToMm(appliedOffsetFt), 3);
            debug["targetCurve"] = CurveToJson(offsetFlat);

            targetCurve = offsetFlat;
            return true;
        }

        // ----------------------------
        // Adjust helpers
        // ----------------------------
        internal sealed class LoopSeg
        {
            public ElementId AreaId;
            public int LoopIndex;
            public int SegmentIndex;
            public Curve Curve;
            public ElementId BoundaryLineId; // OST_AreaSchemeLines if available, else Invalid
        }

        internal sealed class CandidateBoundary
        {
            public ElementId BoundaryLineId;
            public Curve Curve; // representative
            public Curve NewCurve; // assigned when matched
            public int MatchedWallId;
            public double MatchDistanceFt;
            public double ParallelScore;
        }

        internal static double ParallelScore(Curve a, Curve b)
        {
            var ta = TangentAtMid(a);
            var tb = TangentAtMid(b);
            try { return Math.Abs(SafeNormalize(ta).DotProduct(SafeNormalize(tb))); }
            catch { return 0; }
        }

        internal static double CurveDistance(Curve a, Curve b)
        {
            if (a == null || b == null) return double.MaxValue;
            try
            {
                var am = a.Evaluate(0.5, true);
                var bm = b.Evaluate(0.5, true);
                var d1 = a.Distance(bm);
                var d2 = b.Distance(am);
                return Math.Min(d1, d2);
            }
            catch { return double.MaxValue; }
        }

        internal static Curve ClipTargetCurveToCandidateExtent(Curve targetCurve, Curve candidateCurve, double levelZ, out string clipRule)
        {
            clipRule = "no_clip";
            if (targetCurve == null || candidateCurve == null) return targetCurve;

            try
            {
                // Most Area Boundary Lines and Wall location curves are Lines.
                // When a single Wall spans across multiple room segments, we must
                // keep the adjusted boundary segment within the matched candidate's extents.
                if (targetCurve is Line tl && candidateCurve is Line cl)
                {
                    var t0 = tl.GetEndPoint(0);
                    var t1 = tl.GetEndPoint(1);
                    var dir = SafeNormalize(t1 - t0);
                    double len = t0.DistanceTo(t1);
                    if (len < 1e-9) return targetCurve;

                    var c0 = cl.GetEndPoint(0);
                    var c1 = cl.GetEndPoint(1);

                    double s0 = dir.DotProduct(c0 - t0);
                    double s1 = dir.DotProduct(c1 - t0);

                    // Clamp to the original target segment extents to avoid runaway lines.
                    s0 = Math.Max(0.0, Math.Min(len, s0));
                    s1 = Math.Max(0.0, Math.Min(len, s1));

                    var p0 = new XYZ(t0.X + dir.X * s0, t0.Y + dir.Y * s0, levelZ);
                    var p1 = new XYZ(t0.X + dir.X * s1, t0.Y + dir.Y * s1, levelZ);
                    if (p0.DistanceTo(p1) < 1e-6) return targetCurve;

                    clipRule = "line_project_clamp";
                    return Line.CreateBound(p0, p1);
                }
            }
            catch
            {
                // Ignore and fall back to the original target curve.
            }

            return targetCurve;
        }

        private static bool TryIntersectUnboundedLinesXY(Line a, Line b, double z, out XYZ ip)
        {
            ip = null;
            try
            {
                var p = a.GetEndPoint(0);
                var r = a.GetEndPoint(1) - p;
                var q = b.GetEndPoint(0);
                var s = b.GetEndPoint(1) - q;

                double rxs = r.X * s.Y - r.Y * s.X;
                if (Math.Abs(rxs) < 1e-9) return false;

                double qpx = q.X - p.X;
                double qpy = q.Y - p.Y;
                double t = (qpx * s.Y - qpy * s.X) / rxs;

                ip = new XYZ(p.X + t * r.X, p.Y + t * r.Y, z);
                return true;
            }
            catch { return false; }
        }

        private static void FindClosestEndpointPair(Curve c1, Curve c2, out int idx1, out int idx2, out double dist)
        {
            idx1 = 1;
            idx2 = 0;
            dist = double.MaxValue;
            if (c1 == null || c2 == null) return;

            var a0 = c1.GetEndPoint(0);
            var a1 = c1.GetEndPoint(1);
            var b0 = c2.GetEndPoint(0);
            var b1 = c2.GetEndPoint(1);

            double d00 = a0.DistanceTo(b0);
            double d01 = a0.DistanceTo(b1);
            double d10 = a1.DistanceTo(b0);
            double d11 = a1.DistanceTo(b1);

            dist = d10; idx1 = 1; idx2 = 0;
            if (d00 < dist) { dist = d00; idx1 = 0; idx2 = 0; }
            if (d01 < dist) { dist = d01; idx1 = 0; idx2 = 1; }
            if (d11 < dist) { dist = d11; idx1 = 1; idx2 = 1; }
        }

        private static Line UpdateLineEndpoint(Line line, int endpointIndex, XYZ newPoint)
        {
            if (line == null || newPoint == null) return line;
            var p0 = line.GetEndPoint(0);
            var p1 = line.GetEndPoint(1);
            if (endpointIndex == 0) return Line.CreateBound(newPoint, p1);
            return Line.CreateBound(p0, newPoint);
        }

        internal static void ApplyCornerResolutionTrimExtend(
            IList<LoopSeg> loop,
            Dictionary<int, CandidateBoundary> candidatesById,
            double levelZ,
            double tolFt,
            List<string> warnings)
        {
            if (loop == null || loop.Count < 2) return;
            if (candidatesById == null) return;

            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                var s1 = loop[i];
                var s2 = loop[(i + 1) % n];

                var c1 = (s1.BoundaryLineId != null && s1.BoundaryLineId != ElementId.InvalidElementId &&
                          candidatesById.TryGetValue(s1.BoundaryLineId.IntegerValue, out var cand1) && cand1.NewCurve != null)
                    ? cand1.NewCurve
                    : s1.Curve;

                var c2 = (s2.BoundaryLineId != null && s2.BoundaryLineId != ElementId.InvalidElementId &&
                          candidatesById.TryGetValue(s2.BoundaryLineId.IntegerValue, out var cand2) && cand2.NewCurve != null)
                    ? cand2.NewCurve
                    : s2.Curve;

                if (c1 == null || c2 == null) continue;

                FindClosestEndpointPair(c1, c2, out var e1, out var e2, out var dist);
                if (dist <= tolFt) continue;

                CandidateBoundary m1 = null;
                CandidateBoundary m2 = null;
                bool has1 = s1.BoundaryLineId != null && s1.BoundaryLineId != ElementId.InvalidElementId && candidatesById.TryGetValue(s1.BoundaryLineId.IntegerValue, out m1);
                bool has2 = s2.BoundaryLineId != null && s2.BoundaryLineId != ElementId.InvalidElementId && candidatesById.TryGetValue(s2.BoundaryLineId.IntegerValue, out m2);
                bool mod1 = has1 && m1 != null && m1.NewCurve is Line;
                bool mod2 = has2 && m2 != null && m2.NewCurve is Line;

                var p1 = c1.GetEndPoint(e1);
                var p2 = c2.GetEndPoint(e2);
                var p1z = new XYZ(p1.X, p1.Y, levelZ);
                var p2z = new XYZ(p2.X, p2.Y, levelZ);

                if (!(c1 is Line l1) || !(c2 is Line l2))
                {
                    if (mod1 || mod2)
                        warnings?.Add($"Corner unresolved (non-line): areaId={s1.AreaId?.IntegerValue} loop={s1.LoopIndex} seg={s1.SegmentIndex}");
                    continue;
                }

                bool hasIp = TryIntersectUnboundedLinesXY(l1, l2, levelZ, out var ip);

                if (mod1 && mod2 && hasIp)
                {
                    m1.NewCurve = UpdateLineEndpoint((Line)m1.NewCurve, e1, ip);
                    m2.NewCurve = UpdateLineEndpoint((Line)m2.NewCurve, e2, ip);
                    continue;
                }

                if (mod1 && !mod2)
                {
                    m1.NewCurve = UpdateLineEndpoint((Line)m1.NewCurve, e1, p2z);
                    continue;
                }

                if (!mod1 && mod2)
                {
                    m2.NewCurve = UpdateLineEndpoint((Line)m2.NewCurve, e2, p1z);
                    continue;
                }

                if (mod1 && mod2 && !hasIp)
                {
                    warnings?.Add($"Corner unresolved (parallel): areaId={s1.AreaId?.IntegerValue} loop={s1.LoopIndex} seg={s1.SegmentIndex}");
                }
            }
        }
    }

    public class AreaBoundaryCreateByMaterialCoreCenterCommand : IRevitCommandHandler
    {
        public string CommandName => "area_boundary_create_by_material_corecenter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            bool refreshView = p.Value<bool?>("refreshView") ?? false;
            bool dryRun = p.Value<bool?>("dryRun") ?? p.Value<bool?>("dry_run") ?? false;

            var options = p["options"] as JObject;
            double tolMm =
                (options?.Value<double?>("tolerance_mm"))
                ?? (options?.Value<double?>("toleranceMm"))
                ?? p.Value<double?>("toleranceMm")
                ?? p.Value<double?>("tolerance_mm")
                ?? 5.0;
            double tolFt = UnitHelper.MmToFt(tolMm);

            bool skipExisting =
                (options?.Value<bool?>("skip_existing"))
                ?? (options?.Value<bool?>("skipExisting"))
                ?? p.Value<bool?>("skipExisting")
                ?? p.Value<bool?>("skip_existing")
                ?? true;

            bool includeDebug =
                (options?.Value<bool?>("include_debug"))
                ?? (options?.Value<bool?>("includeDebug"))
                ?? p.Value<bool?>("includeDebug")
                ?? p.Value<bool?>("include_debug")
                ?? true;

            bool includeLayerDetails =
                (options?.Value<bool?>("include_layer_details"))
                ?? (options?.Value<bool?>("includeLayerDetails"))
                ?? p.Value<bool?>("includeLayerDetails")
                ?? p.Value<bool?>("include_layer_details")
                ?? false;

            bool includeNonCore =
                (options?.Value<bool?>("include_noncore"))
                ?? (options?.Value<bool?>("includeNonCore"))
                ?? p.Value<bool?>("includeNonCore")
                ?? p.Value<bool?>("include_noncore")
                ?? false;

            bool fallbackToCoreCenterlineWhenMaterialMissing =
                (options?.Value<bool?>("fallback_to_core_centerline"))
                ?? (options?.Value<bool?>("fallbackToCoreCenterline"))
                ?? p.Value<bool?>("fallbackToCoreCenterline")
                ?? p.Value<bool?>("fallback_to_core_centerline")
                ?? false;

            try
            {
                Level level;
                var vp = AreaBoundaryMaterialCoreCenterUtil.ResolveAreaPlanView(doc, uiapp, p, out level);

                if (!AreaBoundaryMaterialCoreCenterUtil.TryResolveMaterialId(doc, p, out var materialId, out var materialName, out var matErr))
                    return new { ok = false, msg = matErr, units = UnitHelper.DefaultUnitsMeta() };

                var uidoc = uiapp.ActiveUIDocument;
                var wallIds = AreaBoundaryMaterialCoreCenterUtil.ResolveWallIds(uidoc, p);
                if (wallIds.Count == 0)
                    return new { ok = false, msg = "wall_element_ids（または選択した壁）が必要です。", units = UnitHelper.DefaultUnitsMeta() };

                var warnings = new List<string>();
                var perWall = new List<object>();
                var created = new List<int>();

                var existing = skipExisting ? AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList() : new List<CurveElement>();

                if (dryRun)
                {
                    foreach (var wid in wallIds)
                    {
                        var w = doc.GetElement(wid) as Autodesk.Revit.DB.Wall;
                        if (w == null) { warnings.Add($"Wall not found: {wid.IntegerValue}"); continue; }

                        if (!AreaBoundaryMaterialCoreCenterUtil.TryComputeTargetCurveFromWall(doc, w, materialId, level, includeNonCore, includeLayerDetails, fallbackToCoreCenterlineWhenMaterialMissing, out var curve, out var dbg, out var warn))
                        {
                            if (!string.IsNullOrWhiteSpace(warn)) warnings.Add(warn);
                            perWall.Add(new { wallId = wid.IntegerValue, ok = false, msg = warn, debug = includeDebug ? (object)dbg : null });
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(warn)) warnings.Add(warn);

                        bool dup = skipExisting && existing.Any(ce => AreaCommon.CurveEquals(ce.GeometryCurve, curve, tolFt));
                        perWall.Add(new
                        {
                            wallId = wid.IntegerValue,
                            ok = true,
                            dryRun = true,
                            duplicate = dup,
                            targetCurve = includeDebug ? (object)(dbg["targetCurve"] ?? null) : null,
                            debug = includeDebug ? (object)dbg : null
                        });
                    }

                    return new
                    {
                        ok = true,
                        dryRun = true,
                        viewId = vp.Id.IntegerValue,
                        levelId = level.Id.IntegerValue,
                        materialId = materialId.IntegerValue,
                        materialName,
                        requestedWalls = wallIds.Count,
                        createdBoundaryLineIds = created,
                        results = perWall,
                        warnings,
                        toleranceMm = tolMm,
                        units = UnitHelper.DefaultUnitsMeta()
                    };
                }

                using (var tx = new Transaction(doc, "Area Boundary Create by Material Core Center"))
                {
                    tx.Start();
                    var sp = AreaBoundaryMaterialCoreCenterUtil.GetOrCreateSketchPlane(doc, vp, level);

                    foreach (var wid in wallIds)
                    {
                        var w = doc.GetElement(wid) as Autodesk.Revit.DB.Wall;
                        if (w == null) { warnings.Add($"Wall not found: {wid.IntegerValue}"); continue; }

                        if (!AreaBoundaryMaterialCoreCenterUtil.TryComputeTargetCurveFromWall(doc, w, materialId, level, includeNonCore, includeLayerDetails, fallbackToCoreCenterlineWhenMaterialMissing, out var curve, out var dbg, out var warn))
                        {
                            if (!string.IsNullOrWhiteSpace(warn)) warnings.Add(warn);
                            perWall.Add(new { wallId = wid.IntegerValue, ok = false, msg = warn, debug = includeDebug ? (object)dbg : null });
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(warn)) warnings.Add(warn);

                        bool dup = skipExisting && existing.Any(ce => AreaCommon.CurveEquals(ce.GeometryCurve, curve, tolFt));
                        if (dup)
                        {
                            perWall.Add(new { wallId = wid.IntegerValue, ok = true, skipped = true, reason = "duplicate", debug = includeDebug ? (object)dbg : null });
                            continue;
                        }

                        var ce = doc.Create.NewAreaBoundaryLine(sp, curve, vp);
                        created.Add(ce.Id.IntegerValue);
                        existing.Add(ce);
                        perWall.Add(new { wallId = wid.IntegerValue, ok = true, createdBoundaryLineId = ce.Id.IntegerValue, debug = includeDebug ? (object)dbg : null });
                    }

                    tx.Commit();
                }

                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }

                return new
                {
                    ok = true,
                    viewId = vp.Id.IntegerValue,
                    levelId = level.Id.IntegerValue,
                    materialId = materialId.IntegerValue,
                    materialName,
                    requestedWalls = wallIds.Count,
                    created = created.Count,
                    createdBoundaryLineIds = created,
                    results = perWall,
                    warnings,
                    toleranceMm = tolMm,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message, units = UnitHelper.DefaultUnitsMeta() };
            }
        }
    }

    public class AreaBoundaryAdjustByMaterialCoreCenterCommand : IRevitCommandHandler
    {
        public string CommandName => "area_boundary_adjust_by_material_corecenter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            bool refreshView = p.Value<bool?>("refreshView") ?? false;
            bool dryRun = p.Value<bool?>("dryRun") ?? p.Value<bool?>("dry_run") ?? false;

            var options = p["options"] as JObject;
            double matchTolMm =
                (options?.Value<double?>("tolerance_mm"))
                ?? (options?.Value<double?>("toleranceMm"))
                ?? p.Value<double?>("toleranceMm")
                ?? p.Value<double?>("tolerance_mm")
                ?? 5.0;
            double matchTolFt = UnitHelper.MmToFt(matchTolMm);

            double? cornerTolMmInput =
                (options?.Value<double?>("corner_tolerance_mm"))
                ?? (options?.Value<double?>("cornerToleranceMm"))
                ?? p.Value<double?>("cornerToleranceMm")
                ?? p.Value<double?>("corner_tolerance_mm");
            double cornerTolMm = cornerTolMmInput ?? (matchTolMm > 10.0 ? 5.0 : matchTolMm);
            if (cornerTolMm < 0) cornerTolMm = 0;
            double cornerTolFt = UnitHelper.MmToFt(cornerTolMm);

            string matchStrategy =
                (options?.Value<string>("match_strategy"))
                ?? (options?.Value<string>("matchStrategy"))
                ?? p.Value<string>("matchStrategy")
                ?? p.Value<string>("match_strategy")
                ?? "nearest_parallel";

            string cornerResolution =
                (options?.Value<string>("corner_resolution"))
                ?? (options?.Value<string>("cornerResolution"))
                ?? p.Value<string>("cornerResolution")
                ?? p.Value<string>("corner_resolution")
                ?? "trim_extend";

            bool includeDebug =
                (options?.Value<bool?>("include_debug"))
                ?? (options?.Value<bool?>("includeDebug"))
                ?? p.Value<bool?>("includeDebug")
                ?? p.Value<bool?>("include_debug")
                ?? true;

            bool includeLayerDetails =
                (options?.Value<bool?>("include_layer_details"))
                ?? (options?.Value<bool?>("includeLayerDetails"))
                ?? p.Value<bool?>("includeLayerDetails")
                ?? p.Value<bool?>("include_layer_details")
                ?? false;

            double parallelThreshold =
                (options?.Value<double?>("parallel_threshold"))
                ?? (options?.Value<double?>("parallelThreshold"))
                ?? p.Value<double?>("parallelThreshold")
                ?? p.Value<double?>("parallel_threshold")
                ?? 0.98;

            bool allowViewWide =
                (options?.Value<bool?>("allow_view_wide"))
                ?? (options?.Value<bool?>("allowViewWide"))
                ?? p.Value<bool?>("allowViewWide")
                ?? p.Value<bool?>("allow_view_wide")
                ?? false;

            bool includeNonCore =
                (options?.Value<bool?>("include_noncore"))
                ?? (options?.Value<bool?>("includeNonCore"))
                ?? p.Value<bool?>("includeNonCore")
                ?? p.Value<bool?>("include_noncore")
                ?? false;

            bool fallbackToCoreCenterlineWhenMaterialMissing =
                (options?.Value<bool?>("fallback_to_core_centerline"))
                ?? (options?.Value<bool?>("fallbackToCoreCenterline"))
                ?? p.Value<bool?>("fallbackToCoreCenterline")
                ?? p.Value<bool?>("fallback_to_core_centerline")
                ?? false;

            try
            {
                Level level;
                var vp = AreaBoundaryMaterialCoreCenterUtil.ResolveAreaPlanView(doc, uiapp, p, out level);

                if (!AreaBoundaryMaterialCoreCenterUtil.TryResolveMaterialId(doc, p, out var materialId, out var materialName, out var matErr))
                    return new { ok = false, msg = matErr, units = UnitHelper.DefaultUnitsMeta() };

                var uidoc = uiapp.ActiveUIDocument;
                AreaBoundaryMaterialCoreCenterUtil.ResolveAreaAndWallIds(uidoc, p, out var areaIds, out var wallIds);
                if (wallIds.Count == 0)
                    return new { ok = false, msg = "wall_element_ids（または選択した壁）が必要です。", units = UnitHelper.DefaultUnitsMeta() };

                if (areaIds.Count == 0 && !allowViewWide)
                    return new { ok = false, msg = "area_element_ids（または選択したArea）が必要です。全ビュー対象にする場合は options.allow_view_wide=true を指定してください。", units = UnitHelper.DefaultUnitsMeta() };

                var warnings = new List<string>();
                var wallDebug = new List<object>();

                var loopSegs = new List<AreaBoundaryMaterialCoreCenterUtil.LoopSeg>();

                if (areaIds.Count > 0)
                {
                    var opts = new SpatialElementBoundaryOptions();
                    foreach (var aid in areaIds)
                    {
                        var area = doc.GetElement(aid) as Autodesk.Revit.DB.Area;
                        if (area == null) { warnings.Add($"Area not found: {aid.IntegerValue}"); continue; }

                        IList<IList<BoundarySegment>> loops = null;
                        try { loops = area.GetBoundarySegments(opts); } catch { loops = null; }
                        if (loops == null) { warnings.Add($"Area {aid.IntegerValue}: boundary segments not available."); continue; }

                        for (int li = 0; li < loops.Count; li++)
                        {
                            var loop = loops[li];
                            if (loop == null) continue;
                            for (int si = 0; si < loop.Count; si++)
                            {
                                var seg = loop[si];
                                if (seg == null) continue;
                                var c = seg.GetCurve();
                                if (c == null) continue;

                                var cFlat = AreaBoundaryMaterialCoreCenterUtil.FlattenCurveToZ(c, level.Elevation, out _);
                                if (cFlat == null) continue;

                                var bid = seg.ElementId;
                                if (bid != null && bid != ElementId.InvalidElementId)
                                {
                                    var e = doc.GetElement(bid);
                                    if (e != null && AreaCommon.IsAreaBoundaryLine(e))
                                    {
                                        loopSegs.Add(new AreaBoundaryMaterialCoreCenterUtil.LoopSeg
                                        {
                                            AreaId = aid,
                                            LoopIndex = li,
                                            SegmentIndex = si,
                                            Curve = cFlat,
                                            BoundaryLineId = bid
                                        });
                                        continue;
                                    }
                                }

                                loopSegs.Add(new AreaBoundaryMaterialCoreCenterUtil.LoopSeg
                                {
                                    AreaId = aid,
                                    LoopIndex = li,
                                    SegmentIndex = si,
                                    Curve = cFlat,
                                    BoundaryLineId = ElementId.InvalidElementId
                                });
                            }
                        }
                    }
                }
                else
                {
                    foreach (var ce in AreaCommon.GetAreaBoundaryLinesInView(doc, vp))
                    {
                        var cFlat = AreaBoundaryMaterialCoreCenterUtil.FlattenCurveToZ(ce.GeometryCurve, level.Elevation, out _);
                        if (cFlat == null) continue;
                        loopSegs.Add(new AreaBoundaryMaterialCoreCenterUtil.LoopSeg
                        {
                            AreaId = ElementId.InvalidElementId,
                            LoopIndex = 0,
                            SegmentIndex = 0,
                            Curve = cFlat,
                            BoundaryLineId = ce.Id
                        });
                    }
                }

                var candidatesById = new Dictionary<int, AreaBoundaryMaterialCoreCenterUtil.CandidateBoundary>();
                foreach (var s in loopSegs)
                {
                    if (s.BoundaryLineId == null || s.BoundaryLineId == ElementId.InvalidElementId) continue;
                    int key = s.BoundaryLineId.IntegerValue;
                    if (candidatesById.ContainsKey(key)) continue;
                    candidatesById[key] = new AreaBoundaryMaterialCoreCenterUtil.CandidateBoundary { BoundaryLineId = s.BoundaryLineId, Curve = s.Curve };
                }

                if (candidatesById.Count == 0)
                    return new { ok = false, msg = "対象となる Area Boundary Lines（OST_AreaSchemeLines）が見つかりません。Area の境界が壁等の要素由来のみの場合、先に境界線を作成してください。", units = UnitHelper.DefaultUnitsMeta() };

                var usedBoundaryIds = new HashSet<int>();
                int matched = 0;

                foreach (var wid in wallIds)
                {
                    var w = doc.GetElement(wid) as Autodesk.Revit.DB.Wall;
                    if (w == null) { warnings.Add($"Wall not found: {wid.IntegerValue}"); continue; }

                    if (!AreaBoundaryMaterialCoreCenterUtil.TryComputeTargetCurveFromWall(doc, w, materialId, level, includeNonCore, includeLayerDetails, fallbackToCoreCenterlineWhenMaterialMissing, out var targetCurve, out var dbg, out var warn))
                    {
                        if (!string.IsNullOrWhiteSpace(warn)) warnings.Add(warn);
                        wallDebug.Add(new { wallId = wid.IntegerValue, ok = false, msg = warn, debug = includeDebug ? (object)dbg : null });
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(warn)) warnings.Add(warn);

                    AreaBoundaryMaterialCoreCenterUtil.CandidateBoundary best = null;
                    double bestDist = double.MaxValue;
                    double bestPar = 0;

                    foreach (var kv in candidatesById)
                    {
                        if (usedBoundaryIds.Contains(kv.Key)) continue;
                        var cand = kv.Value;
                        if (cand == null || cand.Curve == null) continue;

                        double par = AreaBoundaryMaterialCoreCenterUtil.ParallelScore(cand.Curve, targetCurve);
                        if (par < parallelThreshold) continue;

                        double dist = AreaBoundaryMaterialCoreCenterUtil.CurveDistance(cand.Curve, targetCurve);
                        if (dist > matchTolFt) continue;

                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = cand;
                            bestPar = par;
                        }
                    }

                    if (best == null)
                    {
                        wallDebug.Add(new
                        {
                            wallId = wid.IntegerValue,
                            ok = true,
                            matched = false,
                            reason = $"no boundary line within tolerance (matchToleranceMm={matchTolMm}, parallelThreshold={parallelThreshold})",
                            debug = includeDebug ? (object)dbg : null
                        });
                        continue;
                    }

                    usedBoundaryIds.Add(best.BoundaryLineId.IntegerValue);

                    var clipped = AreaBoundaryMaterialCoreCenterUtil.ClipTargetCurveToCandidateExtent(targetCurve, best.Curve, level.Elevation, out var clipRule);
                    best.NewCurve = clipped;
                    best.MatchedWallId = wid.IntegerValue;
                    best.MatchDistanceFt = bestDist;
                    best.ParallelScore = bestPar;
                    matched++;

                    if (includeDebug)
                    {
                        try
                        {
                            dbg["clipRule"] = clipRule;
                            dbg["clippedTargetCurve"] = AreaBoundaryMaterialCoreCenterUtil.CurveToJson(clipped);
                        }
                        catch { }
                    }

                    wallDebug.Add(new
                    {
                        wallId = wid.IntegerValue,
                        ok = true,
                        matched = true,
                        boundaryLineId = best.BoundaryLineId.IntegerValue,
                        matchDistanceMm = Math.Round(UnitHelper.FtToMm(bestDist), 3),
                        parallelScore = Math.Round(bestPar, 4),
                        debug = includeDebug ? (object)dbg : null
                    });
                }

                if (matched == 0)
                {
                    return new
                    {
                        ok = true,
                        viewId = vp.Id.IntegerValue,
                        levelId = level.Id.IntegerValue,
                        materialId = materialId.IntegerValue,
                        materialName,
                        matchedBoundaryLines = 0,
                        adjustedBoundaryLineIdMap = Array.Empty<object>(),
                        wallResults = wallDebug,
                        warnings = warnings.Concat(new[] { "No boundary lines matched. Consider increasing tolerance_mm or selecting correct Areas/Walls." }).ToList(),
                        toleranceMm = matchTolMm,
                        matchToleranceMm = matchTolMm,
                        cornerToleranceMm = cornerTolMm,
                        units = UnitHelper.DefaultUnitsMeta()
                    };
                }

                if (string.Equals(cornerResolution, "trim_extend", StringComparison.OrdinalIgnoreCase) && areaIds.Count > 0)
                {
                    var grouped = loopSegs
                        .Where(s => s.AreaId != null && s.AreaId != ElementId.InvalidElementId)
                        .GroupBy(s => $"{s.AreaId.IntegerValue}:{s.LoopIndex}")
                        .ToList();
                    foreach (var g in grouped)
                    {
                        var ordered = g.OrderBy(s => s.SegmentIndex).ToList();
                        AreaBoundaryMaterialCoreCenterUtil.ApplyCornerResolutionTrimExtend(ordered, candidatesById, level.Elevation, cornerTolFt, warnings);
                    }
                }

                var map = new List<object>();

                if (dryRun)
                {
                    foreach (var kv in candidatesById.Values.Where(c => c.NewCurve != null))
                    {
                        map.Add(new
                        {
                            oldId = kv.BoundaryLineId.IntegerValue,
                            newId = (int?)null,
                            wallId = kv.MatchedWallId,
                            matchDistanceMm = Math.Round(UnitHelper.FtToMm(kv.MatchDistanceFt), 3),
                            parallelScore = Math.Round(kv.ParallelScore, 4),
                            newCurve = includeDebug ? (object)AreaBoundaryMaterialCoreCenterUtil.CurveToJson(kv.NewCurve) : null
                        });
                    }

                    return new
                    {
                        ok = true,
                        dryRun = true,
                        viewId = vp.Id.IntegerValue,
                        levelId = level.Id.IntegerValue,
                        materialId = materialId.IntegerValue,
                        materialName,
                        matchedBoundaryLines = matched,
                        adjustedBoundaryLineIdMap = map,
                        wallResults = wallDebug,
                        warnings,
                        matchStrategy,
                        cornerResolution,
                        toleranceMm = matchTolMm,
                        matchToleranceMm = matchTolMm,
                        cornerToleranceMm = cornerTolMm,
                        units = UnitHelper.DefaultUnitsMeta()
                    };
                }

                using (var tx = new Transaction(doc, "Area Boundary Adjust by Material Core Center"))
                {
                    tx.Start();
                    var sp = AreaBoundaryMaterialCoreCenterUtil.GetOrCreateSketchPlane(doc, vp, level);

                    foreach (var cand in candidatesById.Values.Where(c => c.NewCurve != null))
                    {
                        int oldId = cand.BoundaryLineId.IntegerValue;
                        int newId = 0;
                        try
                        {
                            doc.Delete(cand.BoundaryLineId);
                            var ce = doc.Create.NewAreaBoundaryLine(sp, cand.NewCurve, vp);
                            newId = ce.Id.IntegerValue;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Boundary {oldId} update failed: {ex.Message}");
                            newId = 0;
                        }

                        map.Add(new
                        {
                            oldId,
                            newId = newId == 0 ? (int?)null : newId,
                            wallId = cand.MatchedWallId,
                            matchDistanceMm = Math.Round(UnitHelper.FtToMm(cand.MatchDistanceFt), 3),
                            parallelScore = Math.Round(cand.ParallelScore, 4)
                        });
                    }

                    tx.Commit();
                }

                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }

                return new
                {
                    ok = true,
                    viewId = vp.Id.IntegerValue,
                    levelId = level.Id.IntegerValue,
                    materialId = materialId.IntegerValue,
                    materialName,
                    requestedAreas = areaIds.Count,
                    requestedWalls = wallIds.Count,
                    matchedBoundaryLines = matched,
                    adjustedBoundaryLineIdMap = map,
                    wallResults = wallDebug,
                    warnings,
                    matchStrategy,
                    cornerResolution,
                    toleranceMm = matchTolMm,
                    matchToleranceMm = matchTolMm,
                    cornerToleranceMm = cornerTolMm,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message, units = UnitHelper.DefaultUnitsMeta() };
            }
        }
    }
}
