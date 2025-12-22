#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SiteOps
{
    /// <summary>
    /// create_site_subregion_from_boundary :
    ///   { "boundaryMm":[{"x":..,"y":..},...], "topographyId?":int, "closed?":true }
    /// delete_site_subregion :
    ///   { "elementId":int } or { "ids":[...] }
    /// </summary>
    public sealed class SiteSubRegionHandler : IRevitCommandHandler
    {
        public string CommandName => "create_site_subregion_from_boundary|delete_site_subregion";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            try
            {
                switch (request.Method)
                {
                    case "create_site_subregion_from_boundary":
                        return CreateSubRegion(doc, request.Params as JObject ?? new JObject());
                    case "delete_site_subregion":
                        return DeleteSubRegion(doc, request.Params as JObject ?? new JObject());
                }
                return new { ok = false, msg = "Unknown subcommand." };
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] subregion error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        private object CreateSubRegion(Document doc, JObject p)
        {
            var boundary = p["boundaryMm"]?.ToObject<List<Point2>>() ?? new List<Point2>();
            if (boundary.Count < 3) return new { ok = false, msg = "boundaryMm requires >= 3 points." };
            bool closed = p.Value<bool?>("closed") ?? true;

            int topoId = p.Value<int?>("topographyId") ?? 0;
            TopographySurface topo = null;
            if (topoId > 0) topo = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(topoId)) as TopographySurface;
            if (topo == null)
            {
                topo = new FilteredElementCollector(doc).OfClass(typeof(TopographySurface)).Cast<TopographySurface>().FirstOrDefault();
                if (topo == null) return new { ok = false, msg = "TopographySurface not found. Specify topographyId." };
            }

            // 2D polyline -> CurveLoop
            var poly = boundary.Select(v => UnitHelper.MmToXyz(v.x, v.y, 0)).ToList();
            if (closed && !poly.First().IsAlmostEqualTo(poly.Last()))
                poly.Add(poly.First());
            var curves = new List<Curve>();
            for (int i = 0; i < poly.Count - 1; i++)
                curves.Add(Line.CreateBound(poly[i], poly[i + 1]));
            var loop = CurveLoop.Create(curves);

            using (var t = new Transaction(doc, "Create Site SubRegion"))
            {
                t.Start();
                var region = TryCreateSiteSubRegion(doc, new List<CurveLoop> { loop }, topo.Id);
                if (region == null)
                {
                    t.RollBack();
                    return new { ok = false, msg = "SiteSubRegion.Create not available." };
                }
                t.Commit();
                LoggerProxy.Info($"[Site] SiteSubRegion created id={region.Id.IntValue()} on topo={topo.Id.IntValue()}");
                return new { ok = true, elementId = region.Id.IntValue(), topographyId = topo.Id.IntValue() };
            }
        }

        private object DeleteSubRegion(Document doc, JObject p)
        {
            var list = new List<int>();
            if (p.TryGetValue("elementId", out var one)) list.Add(one.Value<int>());
            if (p.TryGetValue("ids", out var arrToken))
                foreach (var jt in arrToken) list.Add(((JValue)jt).Value<int>());

            if (list.Count == 0) return new { ok = false, msg = "No id specified." };

            using (var t = new Transaction(doc, "Delete Site SubRegion"))
            {
                t.Start();
                int okCount = 0, ngCount = 0;
                foreach (var id in list)
                {
                    try { var d = doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(id)); okCount += d.Count; }
                    catch { ngCount++; }
                }
                t.Commit();
                return new { ok = true, deleted = okCount, failed = ngCount };
            }
        }

        // --- helpers ---
        private Element TryCreateSiteSubRegion(Document doc, IList<CurveLoop> loops, ElementId topoId)
        {
            var t = typeof(SiteSubRegion);

            // 1) Create(Document, IList<CurveLoop>, ElementId)
            try
            {
                var m = t.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null,
                                    new Type[] { typeof(Document), typeof(IList<CurveLoop>), typeof(ElementId) }, null);
                if (m != null)
                {
                    var el = m.Invoke(null, new object[] { doc, loops, topoId }) as Element;
                    if (el != null) return el;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("SiteSubRegion.Create(loops,topo) failed: " + ex.Message); }

            // 2) Create(Document, IList<CurveLoop>)
            try
            {
                var m = t.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null,
                                    new Type[] { typeof(Document), typeof(IList<CurveLoop>) }, null);
                if (m != null)
                {
                    var el = m.Invoke(null, new object[] { doc, loops }) as Element;
                    if (el != null) return el;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("SiteSubRegion.Create(loops) failed: " + ex.Message); }

            // 3) 他のオーバーロードは必要になれば追加
            return null;
        }

        private struct Point2 { public double x, y; }

        private static class LoggerProxy
        {
            static readonly Type T = typeof(RevitMCPAddin.Core.RevitLogger);
            static readonly System.Reflection.MethodInfo MInfo =
                T.GetMethod("Info") ?? T.GetMethod("LogInfo") ?? T.GetMethod("AppendLog");
            static readonly System.Reflection.MethodInfo MWarn =
                T.GetMethod("Warn") ?? T.GetMethod("LogWarn") ?? T.GetMethod("AppendLog");
            static readonly System.Reflection.MethodInfo MErr =
                T.GetMethod("Error") ?? T.GetMethod("LogError") ?? T.GetMethod("AppendLog");

            public static void Info(string msg) { if (MInfo != null) MInfo.Invoke(null, new object[] { msg }); else System.Diagnostics.Debug.WriteLine(msg); }
            public static void Warn(string msg) { if (MWarn != null) MWarn.Invoke(null, new object[] { msg }); else System.Diagnostics.Debug.WriteLine("WARN: " + msg); }
            public static void Error(string msg) { if (MErr != null) MErr.Invoke(null, new object[] { msg }); else System.Diagnostics.Debug.WriteLine("ERROR: " + msg); }
        }
    }
}


