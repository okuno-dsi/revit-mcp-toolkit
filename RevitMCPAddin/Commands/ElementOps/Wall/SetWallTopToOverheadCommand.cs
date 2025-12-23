#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class SetWallTopToOverheadCommand : IRevitCommandHandler
    {
        public string CommandName => "set_wall_top_to_overhead";

        private static readonly double[] DefaultFractions = new[] { 0.1, 0.3, 0.5, 0.7, 0.9 };
        private static readonly int[] DefaultTargetCategoryIds = new[]
        {
            (int)BuiltInCategory.OST_Floors,
            (int)BuiltInCategory.OST_Roofs,
            (int)BuiltInCategory.OST_Ceilings,
            (int)BuiltInCategory.OST_StructuralFraming
        };

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null || uidoc == null) return new { ok = false, msg = "No active document." };

                var p = cmd.Params as JObject ?? new JObject();
                bool dryRun = p.Value<bool?>("dryRun") ?? p.Value<bool?>("preview") ?? false;
                bool apply = p.Value<bool?>("apply") ?? !dryRun;

                string mode = (p.Value<string>("mode") ?? "auto").Trim().ToLowerInvariant();
                if (mode != "auto" && mode != "attach" && mode != "raycast") mode = "auto";

                string startFrom = (p.Value<string>("startFrom") ?? "wallTop").Trim();
                bool startFromBase = startFrom.IndexOf("base", StringComparison.OrdinalIgnoreCase) >= 0;

                double startBelowTopMm = p.Value<double?>("startBelowTopMm") ?? 10.0;
                if (startBelowTopMm < 0) startBelowTopMm = 0;
                double startBelowTopFt = UnitHelper.MmToFt(startBelowTopMm);

                double maxDistanceMm = p.Value<double?>("maxDistanceMm") ?? 50000.0;
                if (maxDistanceMm <= 0) maxDistanceMm = 50000.0;
                double maxDistanceFt = UnitHelper.MmToFt(maxDistanceMm);

                string zAgg = (p.Value<string>("zAggregate") ?? "max").Trim().ToLowerInvariant();
                if (zAgg != "max" && zAgg != "min" && zAgg != "avg" && zAgg != "median") zAgg = "max";

                var fractions = ResolveFractions(p);
                var wallIds = ResolveWallIds(uidoc, doc, p);
                if (wallIds.Count == 0) return new { ok = false, msg = "No wall ids provided or selected." };

                var cats = ResolveTargetCategories(doc, p);
                if (cats.Resolved.Count == 0) return new { ok = false, msg = "No target categories resolved." };

                bool createdTempView;
                string? viewNote;
                int view3dIdUsed;
                var view3d = ResolveRaycastView3D(doc, p, allowCreateTemp: apply, createdTemp: out createdTempView, view3dIdUsed: out view3dIdUsed, note: out viewNote);
                if (view3d == null)
                    return new { ok = false, msg = "No usable 3D view found for raycasting. Specify view3dId or create a non-template 3D view.", note = viewNote };

                ElementFilter targetFilter = BuildCategoryFilter(cats.Resolved);
                var intersector = new ReferenceIntersector(targetFilter, FindReferenceTarget.Face, view3d);
                bool includeLinked = p.Value<bool?>("includeLinked") ?? false;
                try { intersector.FindReferencesInRevitLinks = includeLinked; } catch { /* best-effort */ }

                var results = new List<object>();
                var warnings = new List<object>();
                var affected = new List<int>();

                foreach (var wallId in wallIds)
                    results.Add(ProcessSingleWall(doc, wallId, intersector, maxDistanceFt, startFromBase, startBelowTopFt, fractions, mode, zAgg, apply, affected));

                bool keepTempView = p.Value<bool?>("keepTempView") ?? false;
                if (createdTempView && !keepTempView && apply)
                {
                    try
                    {
                        using (var tx = new Transaction(doc, "Delete MCP temp 3D view"))
                        {
                            tx.Start();
                            doc.Delete(view3d.Id);
                            tx.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(new { warning = "tempViewDeleteFailed", viewId = view3dIdUsed, msg = ex.Message });
                    }
                }

                return new
                {
                    ok = true,
                    totalCount = wallIds.Count,
                    apply = apply,
                    modeRequested = mode,
                    view3dId = view3dIdUsed,
                    view3dName = view3d.Name,
                    includeLinked = includeLinked,
                    sampleFractions = fractions,
                    startFrom = startFromBase ? "wallBase" : "wallTop",
                    startBelowTopMm = startBelowTopMm,
                    maxDistanceMm = maxDistanceMm,
                    zAggregate = zAgg,
                    targets = new { requested = cats.RequestedCount, resolved = cats.Resolved.Select(x => x.IntValue()).ToArray(), skipped = cats.Skipped },
                    affectedElementIds = affected.Distinct().ToArray(),
                    results = results,
                    warnings = warnings,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message, detail = ex.ToString() };
            }
        }

        private sealed class Hit
        {
            public double Fraction;
            public XYZ Origin = XYZ.Zero;
            public ElementId ElementId = ElementId.InvalidElementId;
            public XYZ HitPoint = XYZ.Zero;
            public double ProximityFt;
        }

        private static List<double> ResolveFractions(JObject p)
        {
            var list = new List<double>();
            if (p["sampleFractions"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    double v;
                    try { v = t.Value<double>(); }
                    catch { continue; }
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    if (v < 0) v = 0;
                    if (v > 1) v = 1;
                    list.Add(v);
                }
            }

            if (list.Count == 0)
            {
                int? sampleCount = p.Value<int?>("sampleCount");
                if (sampleCount.HasValue && sampleCount.Value > 0)
                {
                    int n = sampleCount.Value;
                    if (n == 1)
                    {
                        list.Add(0.5);
                    }
                    else
                    {
                        for (int i = 0; i < n; i++)
                        {
                            var v = (i + 1.0) / (n + 1.0); // exclude endpoints by default
                            list.Add(v);
                        }
                    }
                }
            }

            if (list.Count == 0)
                list.AddRange(DefaultFractions);

            var uniq = list.Where(x => x >= 0 && x <= 1).Distinct().OrderBy(x => x).ToList();
            if (uniq.Count == 0) uniq.Add(0.5);
            return uniq;
        }

        private static List<int> ResolveWallIds(UIDocument uidoc, Document doc, JObject p)
        {
            var ids = new List<int>();

            void AddOne(JToken? tok)
            {
                if (tok == null) return;
                try
                {
                    int v = tok.Value<int>();
                    if (v > 0) ids.Add(v);
                }
                catch { /* ignore */ }
            }

            AddOne(p["wallId"]);
            AddOne(p["elementId"]);

            if (p["wallIds"] is JArray arrW)
                foreach (var t in arrW) AddOne(t);
            if (p["elementIds"] is JArray arrE)
                foreach (var t in arrE) AddOne(t);

            if (ids.Count == 0)
            {
                try
                {
                    foreach (var eid in uidoc.Selection.GetElementIds())
                    {
                        var e = doc.GetElement(eid);
                        if (e is Autodesk.Revit.DB.Wall)
                            ids.Add(eid.IntValue());
                    }
                }
                catch { /* ignore */ }
            }

            return ids.Distinct().ToList();
        }

        private static (List<ElementId> Resolved, List<object> Skipped, int RequestedCount) ResolveTargetCategories(Document doc, JObject p)
        {
            var resolved = new List<ElementId>();
            var skipped = new List<object>();
            int requestedCount = 0;

            var allCats = doc.Settings.Categories.Cast<Category>().ToList();
            var byId = allCats.ToDictionary(c => c.Id.IntValue(), c => c);
            var byName = allCats.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            if (p.TryGetValue("categoryIds", out var catIdsToken) && catIdsToken is JArray idArr)
            {
                requestedCount += idArr.Count;
                foreach (var t in idArr)
                {
                    int id;
                    try { id = t.Value<int>(); }
                    catch { skipped.Add(new { categoryId = t.ToString(), reason = "Invalid categoryId" }); continue; }
                    if (byId.TryGetValue(id, out var cat))
                        resolved.Add(cat.Id);
                    else
                        skipped.Add(new { categoryId = id, reason = "CategoryId not found" });
                }
            }

            if (p.TryGetValue("categoryNames", out var catNamesToken) && catNamesToken is JArray nameArr)
            {
                requestedCount += nameArr.Count;
                foreach (var t in nameArr)
                {
                    string? name = null;
                    try { name = t.Value<string>(); } catch { }
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        skipped.Add(new { categoryName = t.ToString(), reason = "Invalid categoryName" });
                        continue;
                    }
                    if (byName.TryGetValue(name, out var cat))
                        resolved.Add(cat.Id);
                    else
                        skipped.Add(new { categoryName = name, reason = "Category name not found" });
                }
            }

            if (resolved.Count == 0 && requestedCount == 0)
            {
                requestedCount = DefaultTargetCategoryIds.Length;
                foreach (var id in DefaultTargetCategoryIds)
                {
                    if (byId.TryGetValue(id, out var cat))
                        resolved.Add(cat.Id);
                    else
                        skipped.Add(new { categoryId = id, reason = "Default categoryId not found in document" });
                }
            }

            resolved = resolved.GroupBy(x => x.IntValue()).Select(g => g.First()).ToList();
            return (resolved, skipped, requestedCount);
        }

        private static ElementFilter BuildCategoryFilter(List<ElementId> categoryIds)
        {
            var filters = new List<ElementFilter>();
            foreach (var cid in categoryIds ?? new List<ElementId>())
            {
                try { filters.Add(new ElementCategoryFilter(cid)); }
                catch { /* ignore invalid */ }
            }

            if (filters.Count == 0) return new ElementClassFilter(typeof(Element));
            if (filters.Count == 1) return filters[0];
            return new LogicalOrFilter(filters);
        }

        private static View3D? ResolveRaycastView3D(Document doc, JObject p, bool allowCreateTemp, out bool createdTemp, out int view3dIdUsed, out string? note)
        {
            createdTemp = false;
            view3dIdUsed = 0;
            note = null;

            int view3dId = p.Value<int?>("view3dId") ?? 0;
            if (view3dId > 0)
            {
                var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(view3dId)) as View3D;
                if (v != null && !v.IsTemplate && !v.IsPerspective)
                {
                    view3dIdUsed = v.Id.IntValue();
                    return v;
                }
                note = $"Specified view3dId is not a usable View3D: {view3dId}";
            }

            // Find an existing 3D view (prefer {3D})
            try
            {
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(v => !v.IsTemplate && !v.IsPerspective)
                    .ToList();

                var v3 = all.FirstOrDefault(v => string.Equals(v.Name, "{3D}", StringComparison.OrdinalIgnoreCase))
                         ?? all.FirstOrDefault();

                if (v3 != null)
                {
                    view3dIdUsed = v3.Id.IntValue();
                    return v3;
                }
            }
            catch (Exception ex)
            {
                note = "Find existing 3D view failed: " + ex.Message;
            }

            if (!allowCreateTemp)
            {
                note = (note ?? "") + " (temp view creation is disabled in dryRun)";
                return null;
            }

            // Create temp view (only if allowed)
            try
            {
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                if (vft == null)
                {
                    note = "No ViewFamilyType for 3D view.";
                    return null;
                }

                View3D? created = null;
                using (var tx = new Transaction(doc, "Create MCP temp 3D view"))
                {
                    tx.Start();
                    created = View3D.CreateIsometric(doc, vft.Id);
                    if (created != null)
                    {
                        try { created.Name = "MCP_TempRaycast3D"; } catch { /* ignore */ }
                    }
                    tx.Commit();
                }

                if (created != null)
                {
                    createdTemp = true;
                    view3dIdUsed = created.Id.IntValue();
                    return created;
                }
            }
            catch (Exception ex)
            {
                note = "Create temp 3D view failed: " + ex.Message;
            }

            return null;
        }

        private static object ProcessSingleWall(
            Document doc,
            int wallId,
            ReferenceIntersector intersector,
            double maxDistanceFt,
            bool startFromBase,
            double startBelowTopFt,
            List<double> fractions,
            string mode,
            string zAgg,
            bool apply,
            List<int> affected)
        {
            try
            {
                var wall = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wallId)) as Autodesk.Revit.DB.Wall;
                if (wall == null) return new { ok = false, wallId = wallId, msg = "Wall not found." };

                var loc = wall.Location as LocationCurve;
                if (loc == null || loc.Curve == null) return new { ok = false, wallId = wallId, msg = "Wall does not have a LocationCurve." };

                var bb = wall.get_BoundingBox(null);
                if (bb == null) return new { ok = false, wallId = wallId, msg = "Wall bounding box not available." };

                double zStartFt = startFromBase
                    ? (bb.Min.Z + UnitHelper.MmToFt(1.0))
                    : (bb.Max.Z - startBelowTopFt);

                double zMinFt = bb.Min.Z + UnitHelper.MmToFt(1.0);
                if (zStartFt < zMinFt) zStartFt = zMinFt;

                var curve = loc.Curve;

                var hits = new List<Hit>();
                var sampleRows = new List<object>();

                foreach (var f in fractions)
                {
                    XYZ pt;
                    try { pt = curve.Evaluate(f, true); }
                    catch { pt = curve.Evaluate(0.5, true); }

                    var origin = new XYZ(pt.X, pt.Y, zStartFt);
                    ReferenceWithContext? rwc = null;
                    try { rwc = intersector.FindNearest(origin, XYZ.BasisZ); }
                    catch { rwc = null; }

                    if (rwc == null || rwc.Proximity <= 1e-9 || rwc.Proximity > maxDistanceFt)
                    {
                        sampleRows.Add(new
                        {
                            fraction = f,
                            originMm = new { x = UnitHelper.FtToMm(origin.X), y = UnitHelper.FtToMm(origin.Y), z = UnitHelper.FtToMm(origin.Z) },
                            hit = (object?)null
                        });
                        continue;
                    }

                    var r = rwc.GetReference();
                    var hitElemId = (r != null) ? r.ElementId : ElementId.InvalidElementId;

                    XYZ? gp = null;
                    try { gp = r != null ? r.GlobalPoint : null; } catch { gp = null; }
                    if (gp == null)
                    {
                        try { gp = origin + XYZ.BasisZ.Multiply(rwc.Proximity); }
                        catch { gp = new XYZ(origin.X, origin.Y, origin.Z + rwc.Proximity); }
                    }

                    hits.Add(new Hit
                    {
                        Fraction = f,
                        Origin = origin,
                        ElementId = hitElemId,
                        HitPoint = gp,
                        ProximityFt = rwc.Proximity
                    });

                    sampleRows.Add(new
                    {
                        fraction = f,
                        originMm = new { x = UnitHelper.FtToMm(origin.X), y = UnitHelper.FtToMm(origin.Y), z = UnitHelper.FtToMm(origin.Z) },
                        hit = new
                        {
                            elementId = hitElemId.IntValue(),
                            proximityMm = UnitHelper.FtToMm(rwc.Proximity),
                            pointMm = new { x = UnitHelper.FtToMm(gp.X), y = UnitHelper.FtToMm(gp.Y), z = UnitHelper.FtToMm(gp.Z) }
                        }
                    });
                }

                var validHits = hits.Where(h => h.ElementId != null && h.ElementId != ElementId.InvalidElementId && h.ElementId.IntValue() > 0).ToList();
                if (validHits.Count == 0)
                {
                    return new
                    {
                        ok = true,
                        wallId = wallId,
                        methodUsed = "none",
                        msg = "No overhead element hit (raycast found nothing above start point).",
                        samples = sampleRows
                    };
                }

                var candidates = validHits
                    .GroupBy(h => h.ElementId.IntValue())
                    .Select(g =>
                    {
                        int eid = g.Key;
                        var elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                        int catId = 0;
                        string? catName = null;
                        string? typeName = null;
                        try
                        {
                            if (elem != null && elem.Category != null)
                            {
                                catId = elem.Category.Id.IntValue();
                                catName = elem.Category.Name;
                            }
                        }
                        catch { /* ignore */ }

                        try
                        {
                            if (elem != null)
                            {
                                var tid = elem.GetTypeId();
                                if (tid != null && tid != ElementId.InvalidElementId)
                                {
                                    var et = doc.GetElement(tid) as ElementType;
                                    if (et != null) typeName = et.Name;
                                }
                            }
                        }
                        catch { /* ignore */ }

                        int priority = CategoryPriority(catId);
                        double avgProxFt = g.Average(x => x.ProximityFt);
                        double minZ = g.Min(x => x.HitPoint.Z);
                        double maxZ = g.Max(x => x.HitPoint.Z);

                        return new
                        {
                            elementId = eid,
                            categoryId = catId,
                            categoryName = catName,
                            typeName = typeName,
                            hitCount = g.Count(),
                            priority = priority,
                            avgProximityMm = UnitHelper.FtToMm(avgProxFt),
                            minHitZmm = UnitHelper.FtToMm(minZ),
                            maxHitZmm = UnitHelper.FtToMm(maxZ)
                        };
                    })
                    .ToList();

                var primary = candidates
                    .OrderByDescending(c => c.hitCount)
                    .ThenByDescending(c => c.priority)
                    .ThenBy(c => c.avgProximityMm)
                    .First();

                int primaryId = primary.elementId;
                var primaryElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(primaryId));

                bool canAttach = primaryElem is Autodesk.Revit.DB.Floor || primaryElem is Autodesk.Revit.DB.RoofBase;
                bool attachRequested = (mode == "auto" || mode == "attach");
                bool raycastAllowed = (mode == "auto" || mode == "raycast");

                if (attachRequested && canAttach)
                {
                    if (!apply)
                    {
                        return new
                        {
                            ok = true,
                            wallId = wallId,
                            methodUsed = "attach",
                            dryRun = true,
                            overhead = primary,
                            candidates = candidates,
                            samples = sampleRows,
                            msg = "DryRun: would attach wall top to overhead element."
                        };
                    }

                    string? attachErr = null;
                    bool attachedOk;
                    using (var tx = new Transaction(doc, $"Attach Wall Top ({wallId})"))
                    {
                        tx.Start();
                        attachedOk = TryInvokeWallUtils("AttachWallTop", doc, wall, primaryElem, out attachErr);
                        if (attachedOk) tx.Commit();
                        else tx.RollBack();
                    }

                    if (attachedOk)
                    {
                        affected.Add(wallId);
                        return new
                        {
                            ok = true,
                            wallId = wallId,
                            methodUsed = "attach",
                            overhead = primary,
                            candidates = candidates,
                            samples = sampleRows,
                            msg = "Wall top attached to overhead element."
                        };
                    }

                    if (mode == "attach")
                    {
                        return new
                        {
                            ok = false,
                            wallId = wallId,
                            methodUsed = "attach",
                            overhead = primary,
                            candidates = candidates,
                            samples = sampleRows,
                            msg = "AttachWallTop failed.",
                            error = attachErr
                        };
                    }

                    // auto -> fall back to raycast
                }

                if (!raycastAllowed)
                {
                    return new
                    {
                        ok = true,
                        wallId = wallId,
                        methodUsed = "none",
                        overhead = primary,
                        candidates = candidates,
                        samples = sampleRows,
                        msg = "Raycast is disabled (mode=attach), and overhead is not attachable."
                    };
                }

                var zHits = validHits
                    .Where(h => h.ElementId.IntValue() == primaryId)
                    .Select(h => h.HitPoint.Z)
                    .ToList();
                double targetZFt = AggregateZ(zHits, zAgg);

                if (!apply)
                {
                    return new
                    {
                        ok = true,
                        wallId = wallId,
                        methodUsed = "raycast",
                        dryRun = true,
                        overhead = primary,
                        candidates = candidates,
                        samples = sampleRows,
                        computed = new { targetZmm = UnitHelper.FtToMm(targetZFt) },
                        msg = "DryRun: would set wall top to the computed Z."
                    };
                }

                object applied;
                using (var tx = new Transaction(doc, $"Set Wall Top ({wallId})"))
                {
                    tx.Start();
                    applied = ApplyWallTopByZ(doc, wall, targetZFt);
                    tx.Commit();
                }
                affected.Add(wallId);

                return new
                {
                    ok = true,
                    wallId = wallId,
                    methodUsed = "raycast",
                    overhead = primary,
                    candidates = candidates,
                    samples = sampleRows,
                    computed = new { targetZmm = UnitHelper.FtToMm(targetZFt) },
                    applied = applied,
                    msg = "Wall top updated by raycast result."
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, wallId = wallId, msg = ex.Message, detail = ex.ToString() };
            }
        }

        private static int CategoryPriority(int categoryId)
        {
            if (categoryId == (int)BuiltInCategory.OST_Floors) return 3;
            if (categoryId == (int)BuiltInCategory.OST_Roofs) return 3;
            if (categoryId == (int)BuiltInCategory.OST_Ceilings) return 2;
            if (categoryId == (int)BuiltInCategory.OST_StructuralFraming) return 1;
            return 0;
        }

        private static double AggregateZ(List<double> zs, string zAgg)
        {
            if (zs == null || zs.Count == 0) return 0;
            if (zs.Count == 1) return zs[0];
            switch (zAgg)
            {
                case "min": return zs.Min();
                case "avg": return zs.Average();
                case "median":
                    var s = zs.OrderBy(x => x).ToList();
                    int mid = s.Count / 2;
                    if (s.Count % 2 == 1) return s[mid];
                    return (s[mid - 1] + s[mid]) * 0.5;
                case "max":
                default:
                    return zs.Max();
            }
        }

        private static object ApplyWallTopByZ(Document doc, Autodesk.Revit.DB.Wall wall, double targetZFt)
        {
            var pTop = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
            var pTopOff = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
            var pUnconn = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);

            // If attached, top offset may be read-only; try detaching.
            if (pTopOff != null && pTopOff.IsReadOnly)
            {
                TryInvokeWallUtils("DetachWallTop", doc, wall, null, out _);
                pTopOff = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
            }

            // 1) top constrained
            var topLevelId = (pTop != null) ? pTop.AsElementId() : ElementId.InvalidElementId;
            var topLevel = (topLevelId != null && topLevelId != ElementId.InvalidElementId)
                ? doc.GetElement(topLevelId) as Level
                : null;
            if (topLevel != null && pTopOff != null && !pTopOff.IsReadOnly)
            {
                double topOffFt = targetZFt - topLevel.Elevation;
                pTopOff.Set(topOffFt);
                if (pUnconn != null && !pUnconn.IsReadOnly) pUnconn.Set(0.0);
                return new
                {
                    mode = "top-constrained",
                    topLevelId = topLevel.Id.IntValue(),
                    topLevelName = topLevel.Name,
                    topOffsetMm = UnitHelper.FtToMm(topOffFt)
                };
            }

            // 2) unconnected
            var pBase = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            var baseLevelId = pBase != null ? pBase.AsElementId() : ElementId.InvalidElementId;
            var baseLevel = (baseLevelId != null && baseLevelId != ElementId.InvalidElementId)
                ? doc.GetElement(baseLevelId) as Level
                : null;

            double baseOffFt = 0.0;
            try
            {
                var pBaseOff = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (pBaseOff != null) baseOffFt = pBaseOff.AsDouble();
            }
            catch { /* ignore */ }

            double baseElevFt = (baseLevel != null ? baseLevel.Elevation : 0.0) + baseOffFt;
            double heightFt = targetZFt - baseElevFt;
            if (heightFt < 0) heightFt = 0;

            if (pTop != null && !pTop.IsReadOnly) pTop.Set(ElementId.InvalidElementId);
            if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(0.0);
            if (pUnconn != null && !pUnconn.IsReadOnly) pUnconn.Set(heightFt);

            return new
            {
                mode = "unconnected",
                baseLevelId = baseLevel != null ? baseLevel.Id.IntValue() : (int?)null,
                baseLevelName = baseLevel != null ? baseLevel.Name : null,
                baseOffsetMm = UnitHelper.FtToMm(baseOffFt),
                unconnectedHeightMm = UnitHelper.FtToMm(heightFt)
            };
        }

        /// <summary>
        /// Reflection-based WallUtils invoker for cross-version compatibility.
        /// Supports signatures:
        /// - (Wall)
        /// - (Document, Wall)
        /// - (Wall, Element)
        /// - (Document, Wall, Element)
        /// - (Wall, ElementId)
        /// - (Document, Wall, ElementId)
        /// </summary>
        private static bool TryInvokeWallUtils(string methodName, Document doc, Autodesk.Revit.DB.Wall wall, Element? attached, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(methodName) || wall == null) { error = "Invalid args."; return false; }

            try
            {
                var methods = typeof(WallUtils).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    .ToList();

                if (methods.Count == 0)
                {
                    error = $"WallUtils.{methodName} not found.";
                    return false;
                }

                foreach (var mi in methods)
                {
                    var ps = mi.GetParameters();
                    try
                    {
                        if (ps.Length == 1 && ps[0].ParameterType == typeof(Autodesk.Revit.DB.Wall))
                        {
                            mi.Invoke(null, new object[] { wall });
                            return true;
                        }
                        if (ps.Length == 2 && ps[0].ParameterType == typeof(Document) && ps[1].ParameterType == typeof(Autodesk.Revit.DB.Wall))
                        {
                            mi.Invoke(null, new object[] { doc, wall });
                            return true;
                        }
                        if (ps.Length == 2 && ps[0].ParameterType == typeof(Autodesk.Revit.DB.Wall))
                        {
                            if (ps[1].ParameterType == typeof(Element))
                            {
                                mi.Invoke(null, new object[] { wall, attached });
                                return true;
                            }
                            if (ps[1].ParameterType == typeof(ElementId))
                            {
                                var id = attached != null ? attached.Id : ElementId.InvalidElementId;
                                mi.Invoke(null, new object[] { wall, id });
                                return true;
                            }
                        }
                        if (ps.Length == 3 && ps[0].ParameterType == typeof(Document) && ps[1].ParameterType == typeof(Autodesk.Revit.DB.Wall))
                        {
                            if (ps[2].ParameterType == typeof(Element))
                            {
                                mi.Invoke(null, new object[] { doc, wall, attached });
                                return true;
                            }
                            if (ps[2].ParameterType == typeof(ElementId))
                            {
                                var id = attached != null ? attached.Id : ElementId.InvalidElementId;
                                mi.Invoke(null, new object[] { doc, wall, id });
                                return true;
                            }
                        }
                    }
                    catch (TargetInvocationException tie)
                    {
                        var inner = tie.InnerException;
                        error = inner != null ? inner.Message : tie.Message;
                        // try other overloads
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        // try other overloads
                    }
                }

                error = error ?? $"WallUtils.{methodName} overload not matched.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
