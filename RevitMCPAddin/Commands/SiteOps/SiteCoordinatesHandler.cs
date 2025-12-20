#nullable enable
using System;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SiteOps
{
    /// <summary>
    /// set_project_base_point / set_survey_point / set_shared_coordinates_from_points / get_site_overview
    /// </summary>
    public sealed class SiteCoordinatesHandler : IRevitCommandHandler
    {
        public string CommandName => "set_project_base_point|set_survey_point|set_shared_coordinates_from_points|get_site_overview";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            try
            {
                switch (request.Method)
                {
                    case "set_project_base_point": return SetProjectBasePoint(doc, request);
                    case "set_survey_point": return SetSurveyPoint(doc, request);
                    case "set_shared_coordinates_from_points": return SetSharedCoordinates(doc, request);
                    case "get_site_overview": return GetSiteOverview(doc);
                }
                return new { ok = false, msg = "Unknown subcommand." };
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] coords error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        // --- Project Base Point -------------------------------------------------
        private object SetProjectBasePoint(Document doc, RequestCommand req)
        {
            var p = req.Params as JObject ?? new JObject();
            var xyz = p["xyzMm"]?.ToObject<P3>() ?? default;
            double? angleDeg = p.Value<double?>("angleToTrueNorthDeg");

            using (var t = new Transaction(doc, "Set Project Base Point"))
            {
                t.Start();
                var pbp = BasePoint.GetProjectBasePoint(doc);
                if (pbp == null) { t.RollBack(); return new { ok = false, msg = "Project Base Point not found." }; }

                // Position プロパティを書けない環境向け：個別パラメータで設定
                TrySetDoubleParam(pbp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM, UnitHelper.MmToFt(xyz.x));
                TrySetDoubleParam(pbp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM, UnitHelper.MmToFt(xyz.y));
                TrySetDoubleParam(pbp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM, UnitHelper.MmToFt(xyz.z));

                if (angleDeg.HasValue)
                {
                    var rad = angleDeg.Value * Math.PI / 180.0;
                    TrySetDoubleParam(pbp, BuiltInParameter.BASEPOINT_ANGLETON_PARAM, rad);
                }

                t.Commit();
                LoggerProxy.Info("[Site] Project Base Point updated.");
                return new { ok = true };
            }
        }

        // --- Survey Point -------------------------------------------------------
        private object SetSurveyPoint(Document doc, RequestCommand req)
        {
            var p = req.Params as JObject ?? new JObject();
            var xyz = p["xyzMm"]?.ToObject<P3>() ?? default;
            string siteName = p.Value<string?>("sharedSiteName") ?? string.Empty;

            using (var t = new Transaction(doc, "Set Survey Point"))
            {
                t.Start();
                var sp = BasePoint.GetSurveyPoint(doc);
                if (sp == null) { t.RollBack(); return new { ok = false, msg = "Survey Point not found." }; }

                TrySetDoubleParam(sp, BuiltInParameter.BASEPOINT_EASTWEST_PARAM, UnitHelper.MmToFt(xyz.x));
                TrySetDoubleParam(sp, BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM, UnitHelper.MmToFt(xyz.y));
                TrySetDoubleParam(sp, BuiltInParameter.BASEPOINT_ELEVATION_PARAM, UnitHelper.MmToFt(xyz.z));

                if (!string.IsNullOrWhiteSpace(siteName))
                {
                    var loc = doc.ActiveProjectLocation;
                    try { loc.Name = siteName; } catch { LoggerProxy.Warn("[Site] Rename site failed (ignored)."); }
                }

                t.Commit();
                LoggerProxy.Info("[Site] Survey Point updated.");
                return new { ok = true };
            }
        }

        // --- Shared Coordinates -------------------------------------------------
        private object SetSharedCoordinates(Document doc, RequestCommand req)
        {
            var p = req.Params as JObject ?? new JObject();
            var proj = p["projectPointMm"]?.ToObject<P3>() ?? default; // プロジェクト基準
            var surv = p["surveyPointMm"]?.ToObject<P3>() ?? default;  // 測量基準（目標）
            string siteName = p.Value<string?>("sharedSiteName") ?? string.Empty;
            double angleDeg = p.Value<double?>("angleToTrueNorthDeg") ?? 0.0;

            using (var t = new Transaction(doc, "Set Shared Coordinates"))
            {
                t.Start();

                // 差分（Survey 側に Project を合わせたい）
                double eastWest = UnitHelper.MmToFt(surv.x - proj.x); // X→E/W
                double northSouth = UnitHelper.MmToFt(surv.y - proj.y); // Y→N/S
                double elevation = UnitHelper.MmToFt(surv.z - proj.z); // Z→Elevation
                double angleRad = angleDeg * Math.PI / 180.0;

                var loc = doc.ActiveProjectLocation;
                // 注: SetProjectPosition は “指定座標での ProjectPosition（角度/EW/NS/標高）” を保存
                // ここでは原点 (0,0,0) を基準として登録
                loc.SetProjectPosition(XYZ.Zero, new ProjectPosition(northSouth, eastWest, elevation, angleRad));

                if (!string.IsNullOrWhiteSpace(siteName))
                {
                    try { loc.Name = siteName; } catch { LoggerProxy.Warn("[Site] Rename site failed (ignored)."); }
                }

                t.Commit();
                LoggerProxy.Info("[Site] Shared Coordinates updated via SetProjectPosition.");
                return new { ok = true, siteName = loc.Name };
            }
        }

        // --- Overview -----------------------------------------------------------
        private object GetSiteOverview(Document doc)
        {
            int topoCount = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.TopographySurface)).ToElements().Count;
            int padCount = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.BuildingPad)).ToElements().Count;

            var locs = new FilteredElementCollector(doc).OfClass(typeof(ProjectLocation)).ToElements();
            var active = doc.ActiveProjectLocation;

            var sites = new System.Collections.Generic.List<object>();
            foreach (ProjectLocation s in locs)
                sites.Add(new { name = s.Name, isCurrent = s.Id == active.Id });

            return new { ok = true, sites, topoCount, buildingPadCount = padCount };
        }

        // --- helpers ------------------------------------------------------------
        private static void TrySetDoubleParam(Element e, BuiltInParameter bip, double value)
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                    p.Set(value);
            }
            catch { /* ignore */ }
        }

        private struct P3 { public double x, y, z; }

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
