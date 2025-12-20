#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SiteOps
{
    /// <summary>
    /// update_property_line : { "propertyLineId":int, "pointsMm":[{"x":..,"y":..},...], "closed":true }
    ///   既存を削除→新規作成（IDは変わる）。戻り値: { ok, oldId, newId }
    /// delete_property_line : { "propertyLineId":int } もしくは { "ids":[int,int,...] }
    /// </summary>
    public sealed class PropertyLineEditDeleteHandler : IRevitCommandHandler
    {
        public string CommandName => "update_property_line|delete_property_line";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            try
            {
                if (request.Method == "update_property_line")
                    return UpdatePropertyLine(doc, request.Params as JObject ?? new JObject());
                if (request.Method == "delete_property_line")
                    return DeletePropertyLine(doc, request.Params as JObject ?? new JObject());

                return new { ok = false, msg = "Unknown subcommand." };
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] property line edit/delete error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        private object UpdatePropertyLine(Document doc, JObject p)
        {
            int oldId = p.Value<int>("propertyLineId");
            var pts = p["pointsMm"]?.ToObject<List<Point2>>() ?? new List<Point2>();
            bool closed = p.Value<bool?>("closed") ?? true;

            if (oldId <= 0) return new { ok = false, msg = "propertyLineId is required." };
            if (pts.Count < 2) return new { ok = false, msg = "Need >= 2 points." };

            var pl = doc.GetElement(new ElementId(oldId));
            if (pl == null) return new { ok = false, msg = $"PropertyLine not found: {oldId}" };

            // 2D polyline → 曲線列
            var poly = pts.Select(v => UnitHelper.MmToXyz(v.x, v.y, 0)).ToList();
            if (closed && !poly.First().IsAlmostEqualTo(poly.Last()))
                poly.Add(poly.First());
            var curves = new List<Curve>();
            for (int i = 0; i < poly.Count - 1; i++)
                curves.Add(Line.CreateBound(poly[i], poly[i + 1]));

            using (var t = new Transaction(doc, "Update Property Line"))
            {
                t.Start();

                // 旧を削除
                try { doc.Delete(new ElementId(oldId)); }
                catch (Exception exDel)
                {
                    t.RollBack();
                    return new { ok = false, msg = "Delete old property line failed: " + exDel.Message };
                }

                // 新規作成（同一位置に置き直し）
                var created = TryCreatePropertyLine(doc, curves);
                if (created == null)
                {
                    t.RollBack();
                    return new { ok = false, msg = "PropertyLine.Create not available." };
                }

                t.Commit();
                LoggerProxy.Info($"[Site] PropertyLine updated old={oldId} -> new={created.Id.IntegerValue}");
                return new { ok = true, oldId, newId = created.Id.IntegerValue };
            }
        }

        private object DeletePropertyLine(Document doc, JObject p)
        {
            var list = new List<int>();
            if (p.TryGetValue("propertyLineId", out var one)) list.Add(one.Value<int>());
            if (p.TryGetValue("ids", out var arrToken))
                foreach (var jt in arrToken) list.Add(((JValue)jt).Value<int>());

            if (list.Count == 0) return new { ok = false, msg = "No id specified." };

            using (var t = new Transaction(doc, "Delete Property Line"))
            {
                t.Start();
                int okCount = 0, ngCount = 0;
                foreach (var id in list)
                {
                    try { var deleted = doc.Delete(new ElementId(id)); okCount += deleted.Count; }
                    catch { ngCount++; }
                }
                t.Commit();
                return new { ok = true, deleted = okCount, failed = ngCount };
            }
        }

        // --- helpers ---
        private Element TryCreatePropertyLine(Document doc, IList<Curve> curves)
        {
            var type = typeof(PropertyLine);
            if (type == null) return null;

            // 1) IList<CurveLoop>
            try
            {
                var loop = CurveLoop.Create(curves);
                var loops = new List<CurveLoop> { loop };
                var m = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null,
                                       new Type[] { typeof(Document), typeof(IList<CurveLoop>) }, null);
                if (m != null)
                {
                    var el = m.Invoke(null, new object[] { doc, loops }) as Element;
                    if (el != null) return el;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("PropertyLine.Create(loops) failed: " + ex.Message); }

            // 2) IList<Curve>
            try
            {
                var m = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null,
                                       new Type[] { typeof(Document), typeof(IList<Curve>) }, null);
                if (m != null)
                {
                    var el = m.Invoke(null, new object[] { doc, curves }) as Element;
                    if (el != null) return el;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("PropertyLine.Create(curves) failed: " + ex.Message); }

            // 3) CurveArray
            try
            {
                var arr = new CurveArray();
                foreach (var c in curves) arr.Append(c);
                var m = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null,
                                       new Type[] { typeof(Document), typeof(CurveArray) }, null);
                if (m != null)
                {
                    var el = m.Invoke(null, new object[] { doc, arr }) as Element;
                    if (el != null) return el;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("PropertyLine.Create(curveArray) failed: " + ex.Message); }

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
