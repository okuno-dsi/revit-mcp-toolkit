// RevitMCPAddin/Commands/MEPOps/MepCommon.cs (UnitHelper対応版)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    internal static class MepUnits
    {
        public static double MmToFt(double mm) => UnitHelper.MmToFt(mm);
        public static double FtToMm(double ft) => UnitHelper.FtToMm(ft);

        public static XYZ ReadPointMm(JObject pt) => UnitHelper.MmToXyz(
            pt.Value<double>("x"),
            pt.Value<double>("y"),
            pt.Value<double>("z"));
    }

    internal static class MepParam
    {
        // 値の設定は UnitHelper.TrySetParameterByExternalValue に一本化
        public static object TrySetParam(Document doc, Element elem, string paramName, JToken valueToken)
        {
            var p = elem?.LookupParameter(paramName);
            if (p == null) return new { ok = false, msg = $"Parameter not found: {paramName}" };

            var valueObj = valueToken?.ToObject<object>();

            using (var tx = new Transaction(doc, $"Set Param {paramName}"))
            {
                tx.Start();
                if (!UnitHelper.TrySetParameterByExternalValue(p, valueObj, out var err))
                {
                    tx.RollBack();
                    return new { ok = false, msg = err ?? "Failed to set parameter." };
                }
                tx.Commit();
                return new { ok = true };
            }
        }

        // 取得は MapParameter で SI/Project/Raw/Both も将来対応しやすい
        public static IEnumerable<object> DumpParams(Element e, Document doc, UnitsMode mode,
            bool includeDisplay = true, bool includeRaw = true, int siDigits = 3, bool includeUnit = true)
        => e.Parameters
             .Cast<Parameter>()
             .Where(pp => pp.StorageType != StorageType.None)
             .Select(pp => UnitHelper.MapParameter(pp, doc, mode, includeDisplay, includeRaw, siDigits, includeUnit))
             .ToList();
    }

    internal static class MepCurveInfo
    {
        public static object ShapeInfo(MEPCurve mc)
        {
            if (mc == null) return null;

            double? diamMm = GetParamAsSi(mmSpec: SpecTypeId.Length, mc, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                          ?? GetParamAsSi(mmSpec: SpecTypeId.Length, mc, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);

            double? widthMm = GetParamAsSi(SpecTypeId.Length, mc, BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            double? heightMm = GetParamAsSi(SpecTypeId.Length, mc, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);

            return new { diameterMm = diamMm, widthMm, heightMm };
        }

        public static double? LengthMm(Element e)
        {
            try
            {
                if (e?.Location is LocationCurve lc && lc.Curve != null)
                    return UnitHelper.FtToMm(lc.Curve.Length);
            }
            catch { }
            return null;
        }

        public static (XYZ a, XYZ b)? Endpoints(Element e)
        {
            if (e?.Location is LocationCurve lc && lc.Curve != null)
                return (lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1));
            return null;
        }

        private static double? GetParamAsSi(ForgeTypeId mmSpec, Element e, BuiltInParameter bip)
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p == null || p.StorageType != StorageType.Double) return null;
                // Spec を解決して SI(mm) へ
                var spec = UnitHelper.GetSpec(p) ?? mmSpec;
                return UnitHelper.ToExternal(p.AsDouble(), spec);
            }
            catch { return null; }
        }
    }
}
