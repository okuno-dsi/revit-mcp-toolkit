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
    /// { "method":"create_property_line_from_points",
    ///   "params":{ "pointsMm":[{"x":...,"y":...},...], "closed":true } }
    /// 2D XY の点から PropertyLine を作成。
    /// バージョン差に備え、Create のオーバーロードはリフレクションで複数試行。
    /// </summary>
    public sealed class PropertyLineHandler : IRevitCommandHandler
    {
        public string CommandName => "create_property_line_from_points";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            try
            {
                var p = request.Params as JObject ?? new JObject();
                var points = p["pointsMm"]?.ToObject<List<Point2>>() ?? new List<Point2>();
                bool closed = p.Value<bool?>("closed") ?? true;
                if (points.Count < 2) return new { ok = false, msg = "Need >= 2 points." };

                var poly = points.Select(v => UnitHelper.MmToXyz(v.x, v.y, 0)).ToList();
                if (closed && !poly.First().IsAlmostEqualTo(poly.Last()))
                    poly.Add(poly.First());
                var curves = new List<Curve>();
                for (int i = 0; i < poly.Count - 1; i++)
                    curves.Add(Line.CreateBound(poly[i], poly[i + 1]));

                using (var t = new Transaction(doc, "Create Property Line"))
                {
                    t.Start();
                    var created = TryCreatePropertyLine(doc, curves);
                    if (created == null)
                    {
                        t.RollBack();
                        return new { ok = false, msg = "PropertyLine.Create is not available in this environment." };
                    }
                    t.Commit();
                    LoggerProxy.Info($"[Site] PropertyLine created id={created.Id.IntValue()}");
                    return new { ok = true, elementId = created.Id.IntValue() };
                }
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] property line error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        private Element TryCreatePropertyLine(Document doc, IList<Curve> curves)
        {
            // API 仕様差に備え、いくつかの Create シグネチャを試行（IList<CurveLoop> / IList<Curve> / CurveArray）
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

