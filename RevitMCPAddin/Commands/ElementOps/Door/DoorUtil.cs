// File: RevitMCPAddin/Commands/ElementOps/Door/DoorUtil.cs
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    internal static class DoorUtil
    {
        public static object UnitsIn() => new { Length = "mm", Area = "m2", Volume = "m3", Angle = "deg" };
        public static object UnitsInt() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };

        // --- UnitHelper 委譲（薄いラッパ）---
        public static double MmToFt(double mm) => UnitHelper.MmToFt(mm);
        public static double FtToMm(double ft) => UnitHelper.FtToMm(ft);

        public static object ConvertDoubleBySpec(double rawInternal, ForgeTypeId fdt)
        {
            try
            {
                var v = UnitHelper.ToExternal(rawInternal, fdt, siDigits: 6);
                if (v.HasValue)
                {
                    if (fdt.Equals(SpecTypeId.Length)) return Math.Round(v.Value, 3);
                    if (fdt.Equals(SpecTypeId.Area)) return Math.Round(v.Value, 6);
                    if (fdt.Equals(SpecTypeId.Volume)) return Math.Round(v.Value, 6);
                    if (fdt.Equals(SpecTypeId.Angle)) return Math.Round(v.Value, 6);
                    return Math.Round(v.Value, 6);
                }
            }
            catch { }
            return Math.Round(rawInternal, 3);
        }

        public static double ToInternalBySpec(double valueSi, ForgeTypeId fdt)
            => UnitHelper.ToInternal(valueSi, fdt);

        // --- 既存の解決系は据え置き ---
        public static Element ResolveElement(Document doc, JObject p)
        {
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            if (!string.IsNullOrWhiteSpace(uid)) return doc.GetElement(uid);
            return null;
        }

        public static FamilySymbol ResolveDoorType(Document doc, JObject p)
        {
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            FamilySymbol sym = null;
            if (typeId > 0)
            {
                sym = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FamilySymbol;
            }
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(s => string.Equals(s.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase));
                sym = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
            }
            return sym;
        }
    }
}

