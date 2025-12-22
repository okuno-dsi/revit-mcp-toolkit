// ================================================================
// File: Commands/LightingOps/LightingCommon.cs
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.LightingOps
{
    internal static class LightingCommon
    {
        public static IEnumerable<FamilyInstance> CollectFixtures(Document doc, int? viewId = null, int? levelId = null)
        {
            var baseCol = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType();

            IEnumerable<Element> elems = baseCol;

            if (viewId.HasValue)
            {
                var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
                if (v != null)
                {
                    var inView = new FilteredElementCollector(doc, v.Id)
                        .OfCategory(BuiltInCategory.OST_LightingFixtures)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    var set = new HashSet<int>(inView.Select(e => e.Id.IntValue()));
                    elems = baseCol.Where(e => set.Contains(e.Id.IntValue()));
                }
            }

            var fixtures = elems.Cast<FamilyInstance>();
            if (levelId.HasValue)
            {
                fixtures = fixtures.Where(f => f.LevelId != ElementId.InvalidElementId &&
                                               f.LevelId.IntValue() == levelId.Value);
            }
            return fixtures;
        }

        public static double GetDoubleParam(Element e, params string[] names)
        {
            foreach (var n in names)
            {
                var p = e.LookupParameter(n);
                if (p == null) continue;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out var d)) return d;
            }
            return 0.0;
        }

        public static XYZ? GetLocationPoint(Element e)
        {
            if (e.Location is LocationPoint lp) return lp.Point;
            var bb = e.get_BoundingBox(null);
            return bb != null ? (bb.Min + bb.Max) * 0.5 : null;
        }

        // ---- 単位変換（自前定数換算：UnitHelper依存なし） ----
        private const double FT_TO_M = 0.3048;
        private const double FT2_TO_M2 = 0.09290304;

        public static double FtToM(double ft) => ft * FT_TO_M;
        public static double Ft2ToM2(double ft2) => ft2 * FT2_TO_M2;

        public static SpatialElement? GetSpatialElement(Document doc, ElementId id)
        {
            return doc.GetElement(id) as SpatialElement;
        }

        public static bool IsInside(SpatialElement se, XYZ p)
        {
            if (se is Autodesk.Revit.DB.Architecture.Room r) return r.IsPointInRoom(p);
            if (se is Autodesk.Revit.DB.Mechanical.Space s) return s.IsPointInSpace(p);
            return false;
        }

        public static double GetAreaM2(SpatialElement se)
        {
            return Ft2ToM2(se.Area);
        }

        public static double GetWatt(FamilyInstance f)
        {
            return GetDoubleParam(f, "Wattage", "W", "消費電力", "電力");
        }

        public static double GetLumensOrEstimate(FamilyInstance f, double lmPerW)
        {
            double lm = GetDoubleParam(f, "Initial Luminous Flux", "Lumens", "光束");
            if (lm > 0) return lm;
            var w = GetWatt(f);
            return Math.Max(0, w) * lmPerW;
        }
    }
}


