#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SiteOps
{
    // { "method":"create_toposurface_from_points", "params": { "pointsMm":[{x,y,z}], "siteName":"optional" } }
    public sealed class CreateToposurfaceHandler : IRevitCommandHandler
    {
        public string CommandName => "create_toposurface_from_points";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            try
            {
                var p = request.Params as JObject ?? new JObject();
                var points = p["pointsMm"]?.ToObject<List<Point3>>() ?? new List<Point3>();
                var siteName = p.Value<string?>("siteName");

                if (points.Count < 3)
                    return new { ok = false, msg = "At least 3 points are required." };

                var xyzs = points.Select(pt => UnitHelper.MmToXyz(pt.x, pt.y, pt.z)).ToList();

                using (var t = new Transaction(doc, "Create TopographySurface"))
                {
                    t.Start();
#pragma warning disable 0618 // TopographySurface is deprecated in Revit 2024+ (Toposolid). Keep for legacy projects.
                    var ts = TopographySurface.Create(doc, xyzs);
#pragma warning restore 0618
                    if (ts == null)
                    {
                        t.RollBack();
                        return new { ok = false, msg = "Failed to create TopographySurface." };
                    }
                    if (!string.IsNullOrWhiteSpace(siteName)) ts.Name = siteName!;
                    t.Commit();

                    LoggerProxy.Info($"[Site] Created TopographySurface id={ts.Id.IntValue()}, name='{ts.Name}'");
                    return new { ok = true, elementId = ts.Id.IntValue(), name = ts.Name };
                }
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] create_toposurface_from_points error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        private struct Point3 { public double x, y, z; }

        /// <summary>既存 RevitLogger のメソッド名に合わせて動的に呼ぶ安全ラッパ。</summary>
        private static class LoggerProxy
        {
            static readonly Type _loggerType = typeof(RevitMCPAddin.Core.RevitLogger);
            static readonly System.Reflection.MethodInfo _info =
                _loggerType.GetMethod("Info") ?? _loggerType.GetMethod("LogInfo") ?? _loggerType.GetMethod("AppendLog");
            static readonly System.Reflection.MethodInfo _warn =
                _loggerType.GetMethod("Warn") ?? _loggerType.GetMethod("LogWarn");
            static readonly System.Reflection.MethodInfo _error =
                _loggerType.GetMethod("Error") ?? _loggerType.GetMethod("LogError") ?? _loggerType.GetMethod("AppendLog");

            public static void Info(string msg)
            {
                if (_info != null) _info.Invoke(null, new object[] { msg });
                else System.Diagnostics.Debug.WriteLine(msg);
            }
            public static void Warn(string msg)
            {
                if (_warn != null) _warn.Invoke(null, new object[] { msg });
                else System.Diagnostics.Debug.WriteLine("WARN: " + msg);
            }
            public static void Error(string msg)
            {
                if (_error != null) _error.Invoke(null, new object[] { msg });
                else System.Diagnostics.Debug.WriteLine("ERROR: " + msg);
            }
        }
    }
}

