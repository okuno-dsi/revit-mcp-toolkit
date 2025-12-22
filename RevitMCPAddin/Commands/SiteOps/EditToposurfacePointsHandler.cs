#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SiteOps
{
    /// <summary>
    /// append_toposurface_points / replace_toposurface_points を同一ハンドラで処理
    /// Params:
    ///   { "topoId": int, "pointsMm": [ {x,y,z}, ... ] }
    /// </summary>
    public sealed class EditToposurfacePointsHandler : IRevitCommandHandler
    {
        public string CommandName => "append_toposurface_points|replace_toposurface_points";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            try
            {
                var p = request.Params as JObject ?? new JObject();
                if (!p.TryGetValue("topoId", out var topoToken))
                    return new { ok = false, msg = "topoId is required." };

                int topoId = topoToken.Value<int>();
                var pts = p["pointsMm"]?.ToObject<List<Point3>>() ?? new List<Point3>();
                if (pts.Count == 0) return new { ok = false, msg = "pointsMm is empty." };

                var topo = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(topoId)) as TopographySurface;
                if (topo == null) return new { ok = false, msg = $"TopographySurface not found: {topoId}" };

                var xyzs = pts.Select(q => UnitHelper.MmToXyz(q.x, q.y, q.z)).ToList();
                bool replace = string.Equals(request.Method, "replace_toposurface_points", StringComparison.OrdinalIgnoreCase);

                using (var t = new Transaction(doc, replace ? "Replace Topography Points" : "Append Topography Points"))
                {
                    t.Start();
                    if (replace)
                    {
                        try
                        {
                            var existing = topo.GetPoints();
                            if (existing != null && existing.Count > 0)
#pragma warning disable 0618 // TopographySurface point edit APIs are deprecated in Revit 2024+ (Toposolid).
                                topo.DeletePoints(existing);
#pragma warning restore 0618
                        }
                        catch (Exception exDel)
                        {
                            LoggerProxy.Warn("[Site] DeletePoints failed (ignore): " + exDel.Message);
                        }

#pragma warning disable 0618 // TopographySurface point edit APIs are deprecated in Revit 2024+ (Toposolid).
                        topo.AddPoints(xyzs);
#pragma warning restore 0618
                        t.Commit();
                        LoggerProxy.Info($"[Site] Replaced topo points: id={topo.Id.IntValue()}, count={xyzs.Count}");
                        return new { ok = true, count = xyzs.Count };
                    }
                    else
                    {
#pragma warning disable 0618 // TopographySurface point edit APIs are deprecated in Revit 2024+ (Toposolid).
                        topo.AddPoints(xyzs);
#pragma warning restore 0618
                        t.Commit();
                        LoggerProxy.Info($"[Site] Appended topo points: id={topo.Id.IntValue()}, countAdded={xyzs.Count}");
                        return new { ok = true, countAdded = xyzs.Count };
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] edit topo points error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        private struct Point3 { public double x, y, z; }

        /// <summary>RevitLoggerに依存しない安全ラッパ（存在するメソッドがあれば呼ぶ）。</summary>
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


