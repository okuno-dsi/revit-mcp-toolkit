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
    /// place_site_component :
    ///   { "category":"Planting|Entourage|Generic Models|Specialty Equipment|Parking",
    ///     "locationMm":{"x":..,"y":..,"z":0}, "levelName?":"GL",
    ///     "typeName?":"樹木_イチョウ_500", "familyName?":"樹木_イチョウ", "angleDeg?":0.0 }
    ///
    /// list_site_components :
    ///   { "categories?":["Planting","Entourage",...]} // 省略時は上記既定集合
    ///
    /// delete_site_component :
    ///   { "elementId":int } or { "ids":[...] }
    /// </summary>
    public sealed class SiteComponentHandler : IRevitCommandHandler
    {
        public string CommandName => "place_site_component|list_site_components|delete_site_component";

        public object Execute(UIApplication uiapp, RequestCommand request)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            try
            {
                switch (request.Method)
                {
                    case "place_site_component": return Place(doc, request.Params as JObject ?? new JObject());
                    case "list_site_components": return List(doc, request.Params as JObject ?? new JObject());
                    case "delete_site_component": return Delete(doc, request.Params as JObject ?? new JObject());
                }
                return new { ok = false, msg = "Unknown subcommand." };
            }
            catch (Exception ex)
            {
                LoggerProxy.Error($"[Site] site component error: {ex}");
                return new { ok = false, msg = ex.Message };
            }
        }

        // --- place ---
        private object Place(Document doc, JObject p)
        {
            string category = p.Value<string>("category") ?? "";
            var loc = p["locationMm"]?.ToObject<P3>() ?? default;
            string levelName = p.Value<string?>("levelName") ?? "";
            string typeName = p.Value<string?>("typeName") ?? "";
            string familyName = p.Value<string?>("familyName") ?? "";
            double angleDeg = p.Value<double?>("angleDeg") ?? 0.0;

            if (string.IsNullOrWhiteSpace(category))
                return new { ok = false, msg = "category is required." };

            // FamilySymbol の探索（カテゴリ＋タイプ名orファミリ名）
            var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>();
            symbols = symbols.Where(s => CategoryMatches(s.Category, category));

            if (!string.IsNullOrWhiteSpace(typeName))
                symbols = symbols.Where(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
            else if (!string.IsNullOrWhiteSpace(familyName))
                symbols = symbols.Where(s => string.Equals(s.Family?.Name, familyName, StringComparison.OrdinalIgnoreCase));

            var symbol = symbols.FirstOrDefault();
            if (symbol == null)
                return new { ok = false, msg = $"FamilySymbol not found (category='{category}', type='{typeName}', family='{familyName}')." };

            // Level
            Level level = null;
            if (!string.IsNullOrWhiteSpace(levelName))
            {
                level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
                if (level == null) return new { ok = false, msg = $"Level not found: '{levelName}'" };
            }
            else
            {
                // ActiveView → GenLevel → fallback: 最初のレベル
                level = (doc.ActiveView != null) ? doc.ActiveView.GenLevel : null;
                if (level == null)
                    level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
            }
            if (level == null) return new { ok = false, msg = "Level could not be resolved." };

            var pnt = UnitHelper.MmToXyz(loc.x, loc.y, loc.z);

            using (var t = new Transaction(doc, "Place Site Component"))
            {
                t.Start();

                if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }

                FamilyInstance fi = TryPlaceFamilyInstance(doc, symbol, pnt, level);
                if (fi == null)
                {
                    t.RollBack();
                    return new { ok = false, msg = "Failed to place FamilyInstance (no matching overload)." };
                }

                // 角度回転（Z軸）
                if (Math.Abs(angleDeg) > 1e-6)
                {
                    var axis = Line.CreateBound(pnt, pnt + XYZ.BasisZ);
                    try { ElementTransformUtils.RotateElement(doc, fi.Id, axis, angleDeg * Math.PI / 180.0); }
                    catch { /* ignore */ }
                }

                t.Commit();
                LoggerProxy.Info($"[Site] Site component placed id={fi.Id.IntegerValue} cat='{category}' type='{symbol.Name}'");
                return new { ok = true, elementId = fi.Id.IntegerValue, type = symbol.Name, family = symbol.FamilyName, level = level.Name };
            }
        }

        // --- list ---
        private object List(Document doc, JObject p)
        {
            var cats = p["categories"]?.ToObject<List<string>>() ??
                       new List<string> { "Planting", "Entourage", "Generic Models", "Specialty Equipment", "Site" };

            var fis = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                      .Where(fi => fi.Category != null && cats.Any(c => CategoryMatches(fi.Category, c)));

            var list = new List<object>();
            foreach (var fi in fis)
            {
                var lp = fi.Location as LocationPoint;
                var pt = (lp != null) ? lp.Point : null;
                list.Add(new
                {
                    id = fi.Id.IntegerValue,
                    category = fi.Category?.Name,
                    family = fi.Symbol?.Family?.Name,
                    type = fi.Symbol?.Name,
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

            using (var t = new Transaction(doc, "Delete Site Component"))
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
        private static bool CategoryMatches(Category cat, string name)
        {
            if (cat == null || string.IsNullOrWhiteSpace(name)) return false;
            // 名前一致（英日どちらでも）
            if (string.Equals(cat.Name, name, StringComparison.OrdinalIgnoreCase)) return true;

            // 一部代表カテゴリ名から BuiltInCategory へ近似マップ
            var map = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "Planting", BuiltInCategory.OST_Planting },
                { "Entourage", BuiltInCategory.OST_Entourage },
                { "Generic Models", BuiltInCategory.OST_GenericModel },
                { "Specialty Equipment", BuiltInCategory.OST_SpecialityEquipment },
                { "Parking", BuiltInCategory.OST_Parking },
                { "Site", BuiltInCategory.OST_Site }
            };
            if (map.TryGetValue(name, out var bic))
                return cat.Id.IntegerValue == (int)bic;

            return false;
        }

        private static FamilyInstance TryPlaceFamilyInstance(Document doc, FamilySymbol sym, XYZ p, Level level)
        {
            // Revit のバージョン差に備えて複数オーバーロードを反射で試行
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

            // 3) さらに必要なら View 指定のオーバーロード等も追加可能

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
