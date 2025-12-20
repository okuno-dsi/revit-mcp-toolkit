// File: RevitMCPAddin/Commands/RevisionCloud/CreateRevisionCircleCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System.Reflection;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    /// <summary>
    /// Create a circular revision cloud by center/radius (mm) in a view.
    /// Params: { viewId:int, revisionId:int, center:{x:double,y:double,z?:double}, radiusMm:double, segments?:int }
    /// </summary>
    public class CreateRevisionCircleCommand : IRevitCommandHandler
    {
        public string CommandName => "create_revision_circle";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            int viewIdInt = p.Value<int?>("viewId") ?? 0;
            int revIdInt = p.Value<int?>("revisionId") ?? 0;
            var view = doc.GetElement(new ElementId(viewIdInt)) as View;
            if (view == null) return new { ok = false, msg = $"View not found: {viewIdInt}" };
            var revElem = doc.GetElement(new ElementId(revIdInt)) as Autodesk.Revit.DB.Revision;
            if (revElem == null) return new { ok = false, msg = $"Revision not found: {revIdInt}" };

            var centerTok = p["center"] as JObject;
            if (centerTok == null) return new { ok = false, msg = "center{x,y,z?} is required (mm)." };
            double cxMm = centerTok.Value<double>("x");
            double cyMm = centerTok.Value<double>("y");
            double czMm = centerTok.Value<double?>("z") ?? 0.0;
            double radiusMm = p.Value<double?>("radiusMm") ?? 0.0;
            if (radiusMm <= 0) return new { ok = false, msg = "radiusMm must be positive." };
            int segments = Math.Max(8, p.Value<int?>("segments") ?? 24);

            var c = new XYZ(ConvertToInternalUnits(cxMm, UnitTypeId.Millimeters),
                             ConvertToInternalUnits(cyMm, UnitTypeId.Millimeters),
                             ConvertToInternalUnits(czMm, UnitTypeId.Millimeters));
            double r = ConvertToInternalUnits(radiusMm, UnitTypeId.Millimeters);

            var arcs = BuildCircleArcs(c, r, segments);
            EnsureClockwise(ref arcs);

            Autodesk.Revit.DB.RevisionCloud rc = null;
            using (var tx = new Transaction(doc, "Create Revision Circle"))
            {
                tx.Start();
                // Resolve overloads by reflection: (doc, view, ElementId, IList<Curve>) or (doc, view, IList<Curve>, ElementId) or (doc, view, IList<Curve>)
                var t = typeof(Autodesk.Revit.DB.RevisionCloud);
                var args1 = new object[] { doc, view, new ElementId(revIdInt), arcs };
                var args2 = new object[] { doc, view, arcs, new ElementId(revIdInt) };
                var args3 = new object[] { doc, view, arcs };
                MethodInfo m;
                m = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(ElementId), typeof(IList<Curve>) });
                if (m != null) rc = (Autodesk.Revit.DB.RevisionCloud)m.Invoke(null, args1);
                else
                {
                    m = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<Curve>), typeof(ElementId) });
                    if (m != null) rc = (Autodesk.Revit.DB.RevisionCloud)m.Invoke(null, args2);
                    else
                    {
                        m = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<Curve>) });
                        if (m == null) throw new InvalidOperationException("No suitable RevisionCloud.Create overload found.");
                        rc = (Autodesk.Revit.DB.RevisionCloud)m.Invoke(null, args3);
                        try
                        {
                            var par = rc?.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION);
                            if (par != null && !par.IsReadOnly) par.Set(new ElementId(revIdInt));
                        }
                        catch { /* ignore */ }
                    }
                }
                tx.Commit();
            }

            return new { ok = true, elementId = rc?.Id.IntegerValue ?? 0 };
        }

        private static IList<Curve> BuildCircleArcs(XYZ center, double radius, int segments)
        {
            var curves = new List<Curve>(segments);
            double step = 2.0 * Math.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                double a0 = i * step;
                double a1 = (i + 1) * step;
                var p0 = new XYZ(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0), center.Z);
                var p1 = new XYZ(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1), center.Z);
                double amid = 0.5 * (a0 + a1);
                var pm = new XYZ(center.X + radius * Math.Cos(amid), center.Y + radius * Math.Sin(amid), center.Z);
                curves.Add(Arc.Create(p0, p1, pm));
            }
            return curves;
        }

        private static void EnsureClockwise(ref IList<Curve> curves)
        {
            // Compute signed area from start points; positive = CCW
            var pts = curves.Select(c => c.GetEndPoint(0)).ToList();
            double area = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % pts.Count];
                area += (p0.X * p1.Y - p1.X * p0.Y);
            }
            if (area > 0) // CCW => reverse
            {
                var rev = new List<Curve>(curves.Count);
                for (int i = curves.Count - 1; i >= 0; i--) rev.Add(curves[i].CreateReversed());
                curves = rev;
            }
        }
    }
}
