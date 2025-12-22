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
    /// { "method":"place_building_pad",
    ///   "params":{ "levelName":"GL",
    ///              "boundaryMm":[{"x":...,"y":...},...],
    ///              "offsetMm":-300, "padTypeName":"Default" } }
    /// </summary>
    public sealed class PlaceBuildingPadHandler : IRevitCommandHandler
    {
        public string CommandName => "place_building_pad";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            try
            {
                var p = request.Params as JObject ?? new JObject();
                string levelName = p.Value<string>("levelName");
                var boundaryMm = p["boundaryMm"]?.ToObject<List<Point2>>() ?? new List<Point2>();
                double offsetMm = p.Value<double?>("offsetMm") ?? 0.0;
                string padTypeName = p.Value<string?>("padTypeName") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(levelName))
                    return new { ok = false, msg = "levelName is required." };
                if (boundaryMm.Count < 3)
                    return new { ok = false, msg = "boundaryMm requires >= 3 points." };

                var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(x => string.Equals(x.Name, levelName, StringComparison.OrdinalIgnoreCase));
                if (level == null) return new { ok = false, msg = $"Level not found: '{levelName}'" };

                // 2D polyline (closed)
                var poly = boundaryMm.Select(b => UnitHelper.MmToXyz(b.x, b.y, 0)).ToList();
                if (!poly.First().IsAlmostEqualTo(poly.Last()))
                    poly.Add(poly.First());
                var curves = new List<Curve>();
                for (int i = 0; i < poly.Count - 1; i++)
                    curves.Add(Line.CreateBound(poly[i], poly[i + 1]));

                using (var t = new Transaction(doc, "Place Building Pad"))
                {
                    t.Start();
                    // パッドタイプ
                    var padType = new FilteredElementCollector(doc).OfClass(typeof(BuildingPadType)).Cast<BuildingPadType>()
                                  .FirstOrDefault(x => string.IsNullOrEmpty(padTypeName)
                                                    || string.Equals(x.Name, padTypeName, StringComparison.OrdinalIgnoreCase));
                    if (padType == null)
                    {
                        t.RollBack();
                        return new { ok = false, msg = "No BuildingPadType available." };
                    }

                    var pad = TryCreateBuildingPad(doc, padType.Id, level.Id, curves);
                    if (pad == null)
                    {
                        t.RollBack();
                        return new { ok = false, msg = "Failed to create BuildingPad (no matching Create overload)." };
                    }

                    // --- オフセット設定（環境差を“表示名”で吸収） ------------------------
                    if (Math.Abs(offsetMm) > 1e-6)
                    {
                        var targetNames = new[]
                        {
                            "Offset", "Elevation", "Thickness",
                            "オフセット", "高さ", "標高", "厚さ"
                        };

                        bool setOk = false;
                        foreach (Parameter prm in pad.Parameters)
                        {
                            try
                            {
                                string n = prm.Definition?.Name ?? "";
                                if (string.IsNullOrEmpty(n)) continue;
                                if (!targetNames.Any(k => n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                                if (prm.StorageType != StorageType.Double || prm.IsReadOnly) continue;

                                setOk = prm.Set(UnitHelper.MmToFt(offsetMm));
                                if (setOk) break;
                            }
                            catch { /* try next */ }
                        }
                        if (!setOk) LoggerProxy.Warn("[Site] BuildingPad offset parameter not set (skipped).");
                    }
                    // -------------------------------------------------------------------

                    t.Commit();
                    LoggerProxy.Info($"[Site] BuildingPad placed id={pad.Id.IntValue()} on level='{level.Name}'");
                    return new { ok = true, elementId = pad.Id.IntValue() };
                }
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] place_building_pad error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        /// <summary>
        /// Revit のバージョン差に耐えるため、BuildingPad.Create の複数シグネチャを反射で試行。
        /// 試行順：
        ///  1) Create(Document, ElementId, ElementId, IList&lt;CurveLoop&gt;)
        ///  2) Create(Document, ElementId, ElementId, CurveArray)
        /// </summary>
        private static BuildingPad TryCreateBuildingPad(Document doc, ElementId typeId, ElementId levelId, IList<Curve> curves)
        {
            var tPad = typeof(BuildingPad);

            // 1) IList<CurveLoop>
            try
            {
                var loop = CurveLoop.Create(curves);
                var loops = new List<CurveLoop> { loop };
                var m1 = tPad.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(ElementId), typeof(System.Collections.Generic.IList<CurveLoop>) },
                    null);
                if (m1 != null)
                {
                    var pad = m1.Invoke(null, new object[] { doc, typeId, levelId, loops }) as BuildingPad;
                    if (pad != null) return pad;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("BuildingPad.Create(loops) failed: " + ex.Message); }

            // 2) CurveArray（旧式の互換）
            try
            {
                var arr = new CurveArray();
                foreach (var c in curves) arr.Append(c);
                var m2 = tPad.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(ElementId), typeof(CurveArray) },
                    null);
                if (m2 != null)
                {
                    var pad = m2.Invoke(null, new object[] { doc, typeId, levelId, arr }) as BuildingPad;
                    if (pad != null) return pad;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("BuildingPad.Create(curveArray) failed: " + ex.Message); }

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

