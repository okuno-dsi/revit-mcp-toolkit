// ================================================================
// File: Commands/ElementOps/ApplyTransformDeltaCommand.cs
// Purpose: Apply translation / rotation-only delta to a single element
//          (no shape change). Designed for Rhino↔Revit workflow.
// JSON-RPC: method = "apply_transform_delta"
// Params: {
//   uniqueId?: string, elementId?: int,
//   delta: {
//     translate?: { x:number, y:number, z:number, units?: "feet"|"mm" },
//     rotateZDeg?: number,
//     rotate?: { axis:[x,y,z], angleRad:number }   // optional full-axis rotation
//   },
//   guard?: { snapshotStamp?: string, geomHash?: string } // reserved (optional)
// }
// Result: { ok:bool, applied:string[], warnings?:string[], msg?:string }
// Errors: NOT_FOUND | NO_LOCATION | INPLACE_UNSUPPORTED | SCALE_SHEAR_FORBIDDEN
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    public class ApplyTransformDeltaCommand : IRevitCommandHandler
    {
        public string CommandName => "apply_transform_delta";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "Active document not found." };

            var p = cmd.Params as JObject ?? new JObject();
            Element elem = null;

            if (p.TryGetValue("uniqueId", out var jUid) && !string.IsNullOrWhiteSpace(jUid?.ToString()))
                elem = doc.GetElement(jUid.ToString());
            else if (p.TryGetValue("elementId", out var jId) && int.TryParse(jId.ToString(), out var idInt))
                elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idInt));

            if (elem == null) return new { ok = false, code = "NOT_FOUND", msg = "Element not found." };

            // In-Place families: non-supported (運用ルール)
            if (elem is FamilyInstance fi && fi.Symbol?.Family?.IsInPlace == true)
                return new { ok = false, code = "INPLACE_UNSUPPORTED", msg = "In-Place families are not supported." };

            var delta = p["delta"] as JObject;
            if (delta == null) return new { ok = false, msg = "delta is required." };

            // Parse translation
            XYZ moveFt = XYZ.Zero;
            if (delta["translate"] is JObject jt)
            {
                double x = jt.Value<double?>("x") ?? 0.0;
                double y = jt.Value<double?>("y") ?? 0.0;
                double z = jt.Value<double?>("z") ?? 0.0;
                string units = (jt.Value<string>("units") ?? "feet").Trim().ToLowerInvariant();
                if (units == "mm") { x /= 304.8; y /= 304.8; z /= 304.8; }
                moveFt = new XYZ(x, y, z);
            }

            // Parse rotation
            Line axis = null;
            double angleRad = 0.0;
            if (delta.TryGetValue("rotateZDeg", out var jdeg))
            {
                var deg = jdeg.Value<double>();
                angleRad = deg * Math.PI / 180.0;
                // Default axis: world Z through element pivot
                var pivot = GetElementPivot(elem);
                axis = Line.CreateUnbound(pivot, XYZ.BasisZ);
            }
            if (delta["rotate"] is JObject jr)
            {
                var arr = jr["axis"] as JArray;
                var angleR = jr.Value<double?>("angleRad") ?? 0.0;
                if (arr != null && arr.Count == 3)
                {
                    var ax = new XYZ(arr.Value<double>(0), arr.Value<double>(1), arr.Value<double>(2));
                    if (ax.GetLength() > 1e-10)
                    {
                        ax = ax.Normalize();
                        var pivot = GetElementPivot(elem);
                        axis = Line.CreateUnbound(pivot, ax);
                        angleRad = angleR;
                    }
                }
            }

            var applied = new System.Collections.Generic.List<string>();

            using (var t = new Transaction(doc, "Apply Transform Delta"))
            {
                t.Start();

                // Move
                if (!moveFt.IsZeroLength())
                {
                    if (!TryMoveElement(doc, elem, moveFt, out string moveErr))
                    {
                        t.RollBack();
                        return new { ok = false, code = "NO_LOCATION", msg = moveErr ?? "Element has no Location and cannot be moved." };
                    }
                    applied.Add("move");
                }

                // Rotate
                if (axis != null && Math.Abs(angleRad) > 1e-12)
                {
                    try
                    {
                        ElementTransformUtils.RotateElement(doc, elem.Id, axis, angleRad);
                        applied.Add("rotate");
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        return new { ok = false, msg = "Rotate failed: " + ex.Message };
                    }
                }

                t.Commit();
            }

            return new
            {
                ok = true,
                applied = applied.ToArray(),
                msg = applied.Count == 0 ? "no-op" : string.Join(", ", applied)
            };
        }

        private static bool TryMoveElement(Document doc, Element elem, XYZ offset, out string err)
        {
            err = null;

            // Prefer ElementTransformUtils common move
            try
            {
                ElementTransformUtils.MoveElement(doc, elem.Id, offset);
                return true;
            }
            catch
            {
                // fallback to Location-based
            }

            var loc = elem.Location;
            if (loc is LocationPoint lp)
            {
                try { lp.Point = lp.Point + offset; return true; }
                catch (Exception ex) { err = ex.Message; return false; }
            }
            else if (loc is LocationCurve lc)
            {
                try { lc.Move(offset); return true; }
                catch (Exception ex) { err = ex.Message; return false; }
            }

            err = "Element has no Location and cannot be moved.";
            return false;
        }

        private static XYZ GetElementPivot(Element elem)
        {
            var loc = elem.Location;
            if (loc is LocationPoint lp) return lp.Point;
            if (loc is LocationCurve lc)
            {
                try
                {
                    var bb = elem.get_BoundingBox(null);
                    if (bb != null) return (bb.Min + bb.Max) * 0.5;
                }
                catch { }
                return lc.Curve.Evaluate(0.5, true);
            }

            try
            {
                var bb = elem.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) * 0.5;
            }
            catch { }
            return XYZ.Zero;
        }
    }
}

