// ================================================================
// File: Commands/ConstraintOps/ConstraintCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: Constraint ops (lock/unlock/alignment/update via dimension)
// Notes  :
//  - RequestCommand のプロパティ名差異に対応するため Reflection 互換ヘルパを実装
//  - Element.GetReference() は存在しないため削除。Edge/LocationCurve.Reference のみ使用
//  - 失敗時は { ok:false, msg } を返す
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RevitMCPAddin.Commands.ConstraintOps
{
    public sealed class ConstraintCommands : IRevitCommandHandler
    {
        public string CommandName => "lock_constraint|unlock_constraint|set_alignment_constraint|update_dimension_value_if_temp_dim";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var method = GetMethodName(cmd);
            if (string.IsNullOrEmpty(method))
                return new { ok = false, msg = "RequestCommand: method/command/name not found" };

            var p = GetParams(cmd); // Dictionary<string,object>
            try
            {
                // Optional batch/time-slice wrapper: items[] + startIndex/batchSize/maxMillisPerTx
                var pj = JObject.FromObject(p);
                var itemsArr = pj["items"] as JArray;
                bool refreshView = pj.Value<bool?>("refreshView") ?? false;
                if (itemsArr != null && itemsArr.Count > 0)
                {
                    int startIndex = Math.Max(0, pj.Value<int?>("startIndex") ?? 0);
                    int batchSize = Math.Max(1, pj.Value<int?>("batchSize") ?? 50);
                    int maxMillis = Math.Max(0, pj.Value<int?>("maxMillisPerTx") ?? 100);
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    int processed = 0;
                    int nextIndex = startIndex;
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var item = itemsArr[i] as JObject ?? new JObject();
                        var di = item.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();

                        switch (method)
                        {
                            case "lock_constraint": HandleLock(uiapp, di); break;
                            case "unlock_constraint": HandleUnlock(uiapp, di); break;
                            case "set_alignment_constraint": HandleSetAlignment(uiapp, di); break;
                            case "update_dimension_value_if_temp_dim": HandleUpdateDim(uiapp, di); break;
                            default: return new { ok = false, msg = $"Unknown constraint method '{method}'" };
                        }

                        processed++;
                        nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }

                    if (refreshView)
                    {
                        try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { }
                    }

                    bool completed = nextIndex >= itemsArr.Count;
                    return new { ok = true, processed, completed, nextIndex = completed ? (int?)null : nextIndex };
                }

                switch (method)
                {
                    case "lock_constraint":
                        return HandleLock(uiapp, p);
                    case "unlock_constraint":
                        return HandleUnlock(uiapp, p);
                    case "set_alignment_constraint":
                        return HandleSetAlignment(uiapp, p);
                    case "update_dimension_value_if_temp_dim":
                        return HandleUpdateDim(uiapp, p);
                    default:
                        return new { ok = false, msg = $"Unknown constraint method '{method}'" };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        // -------- lock_constraint --------
        // p: { dimensionId? , viewId? , refA{elementId,hint?}, refB{...} }
        private object HandleLock(UIApplication uiapp, Dictionary<string, object> p)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            // 1) 既存 Dimension をロック
            if (p.ContainsKey("dimensionId"))
            {
                int dimId = Convert.ToInt32(p["dimensionId"]);
                var dim = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(dimId)) as Dimension;
                if (dim == null) return new { ok = false, msg = $"Dimension not found: {dimId}" };
                if (dim.Segments != null && dim.Segments.Size > 0)
                    return new { ok = false, msg = "Segmented dimension is not supported for locking." };

                using (var t = new Transaction(doc, "Lock Dimension"))
                {
                    t.Start();
                    dim.IsLocked = true;
                    t.Commit();
                }
                try
                {
                    var pj2 = JObject.FromObject(p);
                    if (pj2.Value<bool?>("refreshView") ?? false)
                        uiapp?.ActiveUIDocument?.RefreshActiveView();
                }
                catch { }
                return new { ok = true, locked = true, dimensionId = dimId };
            }

            // 2) 2参照から寸法作成→ロック
            int viewId = ToIntOrThrow(p, "viewId");
            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            if (view == null) return new { ok = false, msg = $"View not found: {viewId}" };

            var (ra, rb, line, err) = ResolveTwoRefsAndLine(doc, view, p);
            if (ra == null || rb == null || line == null)
                return new { ok = false, msg = err ?? "Failed to resolve two references in view." };

            var dimType = new FilteredElementCollector(doc)
                            .OfClass(typeof(DimensionType))
                            .Cast<DimensionType>()
                            .FirstOrDefault();
            if (dimType == null) return new { ok = false, msg = "No DimensionType found." };

            Dimension created;
            using (var t = new Transaction(doc, "Create & Lock Dimension"))
            {
                t.Start();
                var refs = new ReferenceArray();
                refs.Append(ra); refs.Append(rb);
                created = doc.Create.NewDimension(view, line, refs);
                created.DimensionType = dimType;
                created.IsLocked = true;
                t.Commit();
            }
            // Optional view refresh
            try
            {
                var pj = JObject.FromObject(p);
                if (pj.Value<bool?>("refreshView") ?? false)
                    uiapp?.ActiveUIDocument?.RefreshActiveView();
            }
            catch { }
            return new { ok = true, locked = true, createdDimensionId = created.Id.IntValue() };
        }

        // -------- unlock_constraint --------
        // p: { dimensionId }
        private object HandleUnlock(UIApplication uiapp, Dictionary<string, object> p)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int dimId = ToIntOrThrow(p, "dimensionId");
            var dim = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(dimId)) as Dimension;
            if (dim == null) return new { ok = false, msg = $"Dimension not found: {dimId}" };

            using (var t = new Transaction(doc, "Unlock Dimension"))
            {
                t.Start();
                if (dim.IsLocked) dim.IsLocked = false;
                t.Commit();
            }
            try
            {
                var pj = JObject.FromObject(p);
                if (pj.Value<bool?>("refreshView") ?? false)
                    uiapp?.ActiveUIDocument?.RefreshActiveView();
            }
            catch { }
            return new { ok = true, unlocked = true, dimensionId = dimId };
        }

        // -------- set_alignment_constraint --------
        // p: { viewId, refA{elementId,hint?}, refB{...} }
        private object HandleSetAlignment(UIApplication uiapp, Dictionary<string, object> p)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int viewId = ToIntOrThrow(p, "viewId");
            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            if (view == null) return new { ok = false, msg = $"View not found: {viewId}" };

            var (ra, rb, line, err) = ResolveTwoRefsAndLine(doc, view, p);
            if (ra == null || rb == null || line == null)
                return new { ok = false, msg = err ?? "Failed to resolve two references in view." };

            var dimType = new FilteredElementCollector(doc)
                            .OfClass(typeof(DimensionType))
                            .Cast<DimensionType>()
                            .FirstOrDefault();
            if (dimType == null) return new { ok = false, msg = "No DimensionType found." };

            Dimension d;
            using (var t = new Transaction(doc, "Align & Lock"))
            {
                t.Start();
                var refs = new ReferenceArray();
                refs.Append(ra); refs.Append(rb);
                d = doc.Create.NewDimension(view, line, refs);
                d.DimensionType = dimType;
                d.IsLocked = true;
                t.Commit();
            }
            try
            {
                var pj = JObject.FromObject(p);
                if (pj.Value<bool?>("refreshView") ?? false)
                    uiapp?.ActiveUIDocument?.RefreshActiveView();
            }
            catch { }
            return new { ok = true, alignedAndLocked = true, dimensionId = d.Id.IntValue() };
        }

        // -------- update_dimension_value_if_temp_dim --------
        // p: { dimensionId, targetMm }
        private object HandleUpdateDim(UIApplication uiapp, Dictionary<string, object> p)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            int dimId = ToIntOrThrow(p, "dimensionId");
            double targetMm = ToDoubleOrThrow(p, "targetMm");
            double targetFt = targetMm / 304.8; // mm→ft

            var dim = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(dimId)) as Dimension;
            if (dim == null) return new { ok = false, msg = $"Dimension not found: {dimId}" };
            if (dim.Segments != null && dim.Segments.Size > 0)
                return new { ok = false, msg = "Segmented dimension is not supported." };

            var line = dim.Curve as Line;
            if (line == null) return new { ok = false, msg = "Only linear dimension supported." };
            var dir = line.Direction.Normalize();

            var refs = ExtractTwoReferences(dim);
            if (refs == null) return new { ok = false, msg = "Failed to get two references from dimension." };

            var (eA, pA) = GetElementAndPointFromReference(doc, refs.Item1);
            var (eB, pB) = GetElementAndPointFromReference(doc, refs.Item2);
            if (eA == null || eB == null || pA == null || pB == null)
                return new { ok = false, msg = "Failed to resolve endpoints." };

            double current = Math.Abs(dir.DotProduct(pB - pA));
            double delta = targetFt - current;
            var ab = pB - pA;
            var sign = Math.Sign(dir.DotProduct(ab));
            var moveVec = dir.Multiply(sign * delta);

            var movable = PickMovable(eB, eA); // B→A の順で優先
            if (movable == null) return new { ok = false, msg = "Neither element is movable via Location." };

            using (var t = new Transaction(doc, "Adjust by Dimension"))
            {
                t.Start();
                if (movable.Location is LocationPoint lp) lp.Move(moveVec);
                else if (movable.Location is LocationCurve lc) lc.Move(moveVec);
                else return new { ok = false, msg = "Movable element has no supported Location." };
                t.Commit();
            }
            try
            {
                var pj = JObject.FromObject(p);
                if (pj.Value<bool?>("refreshView") ?? false)
                    uiapp?.ActiveUIDocument?.RefreshActiveView();
            }
            catch { }

            return new
            {
                ok = true,
                adjusted = true,
                dimensionId = dimId,
                targetMm,
                movedElementId = movable.Id.IntValue()
            };
        }

        // ---------- helpers ----------
        private static string? GetMethodName(object cmd)
        {
            foreach (var name in new[] { "method", "Method", "command", "Command", "name", "Name", "route", "Route" })
            {
                var pi = cmd.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null)
                {
                    var val = pi.GetValue(cmd) as string;
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
            return null;
        }

        private static Dictionary<string, object> GetParams(object cmd)
        {
            object? raw = null;
            foreach (var name in new[] { "params", "Params", "@params", "parameters", "Parameters", "args", "Args", "data", "Data" })
            {
                var pi = cmd.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null) { raw = pi.GetValue(cmd); break; }
            }

            if (raw is Dictionary<string, object> d) return d;
            if (raw is JObject jo) return jo.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            if (raw is null) return new Dictionary<string, object>();

            // できるだけ辞書化
            try
            {
                return JObject.FromObject(raw).ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static int ToIntOrThrow(Dictionary<string, object> p, string key)
        {
            if (!p.TryGetValue(key, out var v)) throw new ArgumentException($"Missing '{key}'");
            return Convert.ToInt32(v);
        }
        private static double ToDoubleOrThrow(Dictionary<string, object> p, string key)
        {
            if (!p.TryGetValue(key, out var v)) throw new ArgumentException($"Missing '{key}'");
            return Convert.ToDouble(v);
        }

        private static (Reference? ra, Reference? rb, Line? line, string? err) ResolveTwoRefsAndLine(Document doc, View view, Dictionary<string, object> p)
        {
            var ra = default(Reference);
            var rb = default(Reference);

            var pa = p.ContainsKey("refA") ? p["refA"] as JObject ?? JObject.FromObject(p["refA"]) : null;
            var pb = p.ContainsKey("refB") ? p["refB"] as JObject ?? JObject.FromObject(p["refB"]) : null;
            if (pa == null || pb == null) return (null, null, null, "Missing refA/refB");

            int aId = Convert.ToInt32(pa["elementId"]);
            int bId = Convert.ToInt32(pb["elementId"]);
            string hintA = (pa["hint"]?.ToString() ?? "").ToLowerInvariant();
            string hintB = (pb["hint"]?.ToString() ?? "").ToLowerInvariant();

            var ea = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(aId));
            var eb = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(bId));
            if (ea == null || eb == null) return (null, null, null, "Element not found for refA/refB");

            ra = TryGetReferenceFromElement(ea, hintA);
            rb = TryGetReferenceFromElement(eb, hintB);
            if (ra == null || rb == null)
                return (null, null, null, "Failed to get Reference. Try hint='Edge' or 'LocationCurve'.");

            var (okA, pA) = TryGetAnyPointOnReference(doc, ra);
            var (okB, pB) = TryGetAnyPointOnReference(doc, rb);
            if (!okA || !okB) return (null, null, null, "Failed to compute reference points.");

            var line = Line.CreateBound(pA, pB);
            return (ra, rb, line, null);
        }

        private static Reference? TryGetReferenceFromElement(Element e, string hint)
        {
            // 1) Edge 優先（hint=Edge のときは必ずEdgeを探す）
            var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            var geo = e.get_Geometry(opt);

            bool wantEdge = hint == "edge";
            bool wantLoc = hint == "locationcurve" || hint == "centerline";

            if (wantEdge && geo != null)
            {
                foreach (var obj in geo)
                {
                    if (obj is GeometryInstance gi)
                    {
                        foreach (var g in gi.GetInstanceGeometry())
                            if (g is Edge eg && eg.Reference != null) return eg.Reference;
                    }
                    if (obj is Solid s && s.Edges.Size > 0)
                    {
                        foreach (Edge eg in s.Edges) if (eg.Reference != null) return eg.Reference;
                    }
                }
                return null; // Edge指定だが取れない
            }

            // 2) LocationCurve の Reference（取れないタイプもある点に注意）
            if (wantLoc || !wantEdge)
            {
                if (e.Location is LocationCurve lc)
                {
                    var r = lc.Curve?.Reference;
                    if (r != null) return r;
                }
            }

            // 3) ヒントなしの場合は Edge を試し、ダメなら LocationCurve
            if (!wantEdge && !wantLoc && geo != null)
            {
                foreach (var obj in geo)
                {
                    if (obj is GeometryInstance gi)
                    {
                        foreach (var g in gi.GetInstanceGeometry())
                            if (g is Edge eg && eg.Reference != null) return eg.Reference;
                    }
                    if (obj is Solid s && s.Edges.Size > 0)
                    {
                        foreach (Edge eg in s.Edges) if (eg.Reference != null) return eg.Reference;
                    }
                }
                if (e.Location is LocationCurve lc2)
                {
                    var r2 = lc2.Curve?.Reference;
                    if (r2 != null) return r2;
                }
            }

            return null;
        }

        private static (bool ok, XYZ p) TryGetAnyPointOnReference(Document doc, Reference r)
        {
            try
            {
                var el = doc.GetElement(r);
                var go = el?.GetGeometryObjectFromReference(r);
                if (go is Edge edge)
                {
                    var crv = edge.AsCurve();
                    return (true, crv.Evaluate(0.5, true));
                }
                if (el?.Location is LocationCurve lc) return (true, lc.Curve.Evaluate(0.5, true));
                if (el?.Location is LocationPoint lp) return (true, lp.Point);
                var bb = el?.get_BoundingBox(null);
                if (bb != null) return (true, (bb.Min + bb.Max) * 0.5);
            }
            catch { }
            return (false, XYZ.Zero);
        }

        private static Tuple<Reference, Reference>? ExtractTwoReferences(Dimension d)
        {
            try
            {
                var ra = new ReferenceArray();
                foreach (Reference r in d.References) ra.Append(r);
                if (ra.Size != 2) return null;
                return Tuple.Create(ra.get_Item(0), ra.get_Item(1));
            }
            catch { return null; }
        }

        private static (Element? e, XYZ? p) GetElementAndPointFromReference(Document doc, Reference r)
        {
            var el = doc.GetElement(r);
            if (el == null) return (null, null);
            var go = el.GetGeometryObjectFromReference(r);
            if (go is Edge edge) return (el, edge.AsCurve().Evaluate(0.0, true));
            if (el.Location is LocationCurve lc) return (el, lc.Curve.Evaluate(0.0, true));
            if (el.Location is LocationPoint lp) return (el, lp.Point);
            var bb = el.get_BoundingBox(null);
            if (bb != null) return (el, (bb.Min + bb.Max) * 0.5);
            return (el, null);
        }

        private static Element? PickMovable(Element a, Element b)
        {
            // LocationPoint > LocationCurve を優先
            if (a.Location is LocationPoint || a.Location is LocationCurve) return a;
            if (b.Location is LocationPoint || b.Location is LocationCurve) return b;
            return null;
        }
    }
}


