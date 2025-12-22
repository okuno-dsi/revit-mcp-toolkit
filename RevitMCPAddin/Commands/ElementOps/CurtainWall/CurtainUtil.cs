// File: RevitMCPAddin/Commands/ElementOps/CurtainWall/CurtainUtil.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    internal static class CurtainUtil
    {
        public static object UnitsIn() => new { Length = "mm" };
        public static object UnitsInt() => new { Length = "ft" };
        public static object ScheduleUnits() => new { PanelArea = "m2", MullionLength = "m" };

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
            if (el == null) return null;
            if (el.CurtainGrid == null) return null;
            return el;
        }

        public static double TryGetParamDouble(Element e, BuiltInParameter bip, double defaultInternal = 0)

        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch { }
            return defaultInternal;
        }
    }
}


