// File: RevitMCPAddin/Commands/ElementOps/CurtainWall/CurtainWallUtil.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    internal static class CurtainWallUtil
    {
        public static object UnitsGeom() => new { Length = "mm" };
        public static object UnitsSched() => new { PanelArea = "m2", MullionLength = "m" };

        // --- UnitHelper へ委譲 ---
        public static double MmToFt(double mm) => UnitHelper.MmToFt(mm);
        public static double FtToMm(double ft) => UnitHelper.FtToMm(ft);
        public static double Ft2ToM2(double ft2) => UnitHelper.InternalToSqm(ft2);
        public static double FtToM(double ft) => UnitHelper.FtToMm(ft) / 1000.0;

        public static Element ResolveElement(Document doc, JObject p)
        {
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            if (!string.IsNullOrWhiteSpace(uid)) return doc.GetElement(uid);
            return null;
        }

        public static Autodesk.Revit.DB.Wall ResolveCurtainWall(Document doc, JObject p)
        {
            var el = ResolveElement(doc, p) as Autodesk.Revit.DB.Wall;
            if (el == null || el.CurtainGrid == null) return null;
            return el;
        }

        public static FamilySymbol ResolvePanelType(Document doc, JObject p)
        {
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            FamilySymbol sym = null;
            if (typeId > 0) sym = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FamilySymbol;
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(s => string.Equals(s.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase));
                sym = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
            }
            return sym;
        }

        public static MullionType ResolveMullionType(Document doc, JObject p)
        {
            int typeId = p.Value<int?>("mullionTypeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            MullionType t = null;
            if (typeId > 0) t = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as MullionType;
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                t = new FilteredElementCollector(doc).OfClass(typeof(MullionType)).Cast<MullionType>()
                    .FirstOrDefault(x => string.Equals(x.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
            }
            return t;
        }

        public static CurtainGridLine ResolveGridLine(Autodesk.Revit.DB.Wall wall, Document doc, JObject p)
        {
            int glid = p.Value<int?>("gridLineId") ?? 0;
            if (glid > 0) return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(glid)) as CurtainGridLine;

            string ori = p.Value<string>("orientation"); // "U" | "V"
            int idx = p.Value<int?>("gridLineIndex") ?? -1;
            if (string.IsNullOrWhiteSpace(ori) || idx < 0) return null;

            var grid = wall.CurtainGrid;
            if (grid == null) return null;
            var ids = ori.Equals("U", StringComparison.OrdinalIgnoreCase)
                ? grid.GetUGridLineIds().ToList()
                : grid.GetVGridLineIds().ToList();

            if (idx >= 0 && idx < ids.Count)
                return doc.GetElement(ids[idx]) as CurtainGridLine;

            return null;
        }
    }
}

