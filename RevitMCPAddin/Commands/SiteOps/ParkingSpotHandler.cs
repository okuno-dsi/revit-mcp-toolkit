#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SiteOps
{
    /// <summary>
    /// place_parking_spot :
    ///   { "levelName":"GL", "locationMm":{"x":..,"y":..,"z":0},
    ///     "typeName?":"駐車_2.5m", "familyName?":"駐車" , "angleDeg?":0.0 }
    ///
    /// list_parking_spots : {}
    ///
    /// delete_parking_spot : { "elementId":int } or { "ids":[...] }
    /// </summary>
    public sealed class ParkingSpotHandler : IRevitCommandHandler
    {
        public string CommandName => "place_parking_spot|list_parking_spots|delete_parking_spot";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            try
            {
                switch (request.Method)
                {
                    case "place_parking_spot": return Place(doc, request.Params as JObject ?? new JObject());
                    case "list_parking_spots": return List(doc);
                    case "delete_parking_spot": return Delete(doc, request.Params as JObject ?? new JObject());
                }
                return new { ok = false, msg = "Unknown subcommand." };
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] parking error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        // --- place ---
        private object Place(Document doc, JObject p)
        {
            string levelName = p.Value<string>("levelName");
            var loc = p["locationMm"]?.ToObject<P3>() ?? default;
            string typeName = p.Value<string?>("typeName") ?? "";
            string familyName = p.Value<string?>("familyName") ?? "";
            double angleDeg = p.Value<double?>("angleDeg") ?? 0.0;

            if (string.IsNullOrWhiteSpace(levelName))
                return new { ok = false, msg = "levelName is required." };

            var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                          .Where(s => s.Category != null && s.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Parking);

            if (!string.IsNullOrWhiteSpace(typeName))
                symbols = symbols.Where(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
            else if (!string.IsNullOrWhiteSpace(familyName))
                symbols = symbols.Where(s => string.Equals(s.Family?.Name, familyName, StringComparison.OrdinalIgnoreCase));

            var symbol = symbols.FirstOrDefault();
            if (symbol == null)
                return new { ok = false, msg = "Parking FamilySymbol not found (typeName/familyName check)." };

            var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
            if (level == null) return new { ok = false, msg = $"Level not found: '{levelName}'" };

            var pnt = UnitHelper.MmToXyz(loc.x, loc.y, loc.z);

            using (var t = new Transaction(doc, "Place Parking Spot"))
            {
                t.Start();

                if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

                FamilyInstance fi = TryPlaceFamilyInstance(doc, symbol, pnt, level);
                if (fi == null)
                {
                    t.RollBack();
                    return new { ok = false, msg = "Failed to place Parking FamilyInstance." };
                }

                // 角度回転（Z軸）
                if (Math.Abs(angleDeg) > 1e-6)
                {
                    var axis = Line.CreateBound(pnt, pnt + XYZ.BasisZ);
                    try { ElementTransformUtils.RotateElement(doc, fi.Id, axis, angleDeg * Math.PI / 180.0); }
                    catch { /* ignore */ }
                }

                t.Commit();
                LoggerProxy.Info($"[Site] Parking placed id={fi.Id.IntegerValue} type='{symbol.Name}'");
                return new { ok = true, elementId = fi.Id.IntegerValue, type = symbol.Name, family = symbol.FamilyName, level = level.Name };
            }
        }

        // --- list ---
        private object List(Document doc)
        {
            var fis = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                      .Where(fi => fi.Category != null && fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Parking);

            var list = new List<object>();
            foreach (var fi in fis)
            {
                var lp = fi.Location as LocationPoint;
                var pt = (lp != null) ? lp.Point : null;
                list.Add(new
                {
                    id = fi.Id.IntegerValue,
                    type = fi.Symbol?.Name,
                    family = fi.Symbol?.Family?.Name,
                    level = doc.GetElement(fi.LevelId) is Level lv ? lv.Name : null,
                    locationMm = (pt != null) ? new { x = UnitHelper.FtToMm(pt.X), y = UnitHelper.FtToMm(pt.Y), z = UnitHelper.FtToMm(pt.Z) } : null
                });
            }
            return new { ok = true, count = list.Count, items = list };
        }

        // --- delete ---
        private object Delete(Document doc, JObject p)
        {
            var list = new List<int>();
            if (p.TryGetValue("elementId", out var one)) list.Add(one.Value<int>());
            if (p.TryGetValue("ids", out var arrToken))
                foreach (var jt in arrToken) list.Add(((JValue)jt).Value<int>());

            if (list.Count == 0) return new { ok = false, msg = "No id specified." };

            using (var t = new Transaction(doc, "Delete Parking Spot"))
            {
                t.Start();
                int okCount = 0, ngCount = 0;
                foreach (var id in list)
                {
                    try { var d = doc.Delete(new ElementId(id)); okCount += d.Count; }
                    catch { ngCount++; }
                }
                t.Commit();
                return new { ok = true, deleted = okCount, failed = ngCount };
            }
        }

        // --- helpers ---
        private static FamilyInstance TryPlaceFamilyInstance(Document doc, FamilySymbol sym, XYZ p, Level level)
        {
            var tDoc = typeof(Document);
            var tSym = typeof(FamilySymbol);
            var tLvl = typeof(Level);
            var tXYZ = typeof(XYZ);
            var tStructType = typeof(StructuralType);

            // 1) NewFamilyInstance(XYZ, FamilySymbol, Level, StructuralType)
            try
            {
                var m1 = tDoc.GetMethod("NewFamilyInstance", BindingFlags.Public | BindingFlags.Instance,
                                        null, new Type[] { tXYZ, tSym, tLvl, tStructType }, null);
                if (m1 != null)
                {
                    var r = m1.Invoke(doc, new object[] { p, sym, level, StructuralType.NonStructural }) as FamilyInstance;
                    if (r != null) return r;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("NewFamilyInstance(XYZ,Sym,Level,Struct) failed: " + ex.Message); }

            // 2) NewFamilyInstance(XYZ, FamilySymbol, StructuralType)
            try
            {
                var m2 = tDoc.GetMethod("NewFamilyInstance", BindingFlags.Public | BindingFlags.Instance,
                                        null, new Type[] { tXYZ, tSym, tStructType }, null);
                if (m2 != null)
                {
                    var r = m2.Invoke(doc, new object[] { p, sym, StructuralType.NonStructural }) as FamilyInstance;
                    if (r != null) return r;
                }
            }
            catch (Exception ex) { LoggerProxy.Warn("NewFamilyInstance(XYZ,Sym,Struct) failed: " + ex.Message); }

            return null;
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
