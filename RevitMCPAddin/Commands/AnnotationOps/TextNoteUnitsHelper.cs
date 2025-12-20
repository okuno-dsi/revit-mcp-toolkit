// ================================================================
// File: Commands/AnnotationOps/TextNoteUnitsHelper.cs
// Purpose : Project Units aware conversions for TextNote commands
// Target  : .NET Framework 4.8 / C# 8.0 / Revit 2023+
// Notes   : Avoid C# 9.0 features (no pattern "or", no switch expressions).
//           Always use ForgeTypeId for unit variables/returns.
// ================================================================
#nullable enable
using System;
using System.Reflection;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    internal static class TextNoteUnitsHelper
    {
        /// <summary>
        /// Convert external numeric to internal units:
        ///  - if unitOpt is provided (mm/cm/m/in/ft/deg/rad), honor it
        ///  - else use the project's display unit for the given spec
        /// </summary>
        public static double ToInternalByProjectUnits(Document doc, ForgeTypeId spec, double external, string unitOpt)
        {
            if (!string.IsNullOrWhiteSpace(unitOpt))
            {
                ForgeTypeId unitId = MapUnitOpt(spec, unitOpt);
                return UnitUtils.ConvertToInternalUnits(external, unitId);
            }

            Units u = doc.GetUnits();
            FormatOptions fo = u.GetFormatOptions(spec);
            ForgeTypeId displayId = fo.GetUnitTypeId(); // ForgeTypeId (UnitTypeId.* の実体)
            return UnitUtils.ConvertToInternalUnits(external, displayId);
        }

        /// <summary>
        /// Map "mm|cm|m|in|ft|deg|rad" to ForgeTypeId considering spec (C#8 互換版)
        /// </summary>
        public static ForgeTypeId MapUnitOpt(ForgeTypeId spec, string unitOpt)
        {
            if (unitOpt == null) unitOpt = string.Empty;
            unitOpt = unitOpt.Trim().ToLowerInvariant();

            // Angle only
            if (spec == SpecTypeId.Angle)
            {
                if (unitOpt == "deg" || unitOpt == "degree" || unitOpt == "degrees")
                    return UnitTypeId.Degrees;   // ← ForgeTypeId 値
                if (unitOpt == "rad" || unitOpt == "radian" || unitOpt == "radians")
                    return UnitTypeId.Radians;
                return UnitTypeId.Degrees; // default
            }

            // Length & default
            switch (unitOpt)
            {
                case "mm": return UnitTypeId.Millimeters;
                case "cm": return UnitTypeId.Centimeters;
                case "m": return UnitTypeId.Meters;
                case "in": return UnitTypeId.Inches;
                case "ft": return UnitTypeId.Feet;
                default: return UnitTypeId.Millimeters; // safe default
            }
        }

        /// <summary>
        /// Best-effort SpecTypeId from Definition (Revit 2023: GetDataType or GetDataTypeId).
        /// </summary>
        public static ForgeTypeId TryGetSpecTypeId(Definition def)
        {
            if (def == null) return null;
            try
            {
                MethodInfo mi = def.GetType().GetMethod("GetDataType")
                              ?? def.GetType().GetMethod("GetDataTypeId");
                object obj = (mi != null) ? mi.Invoke(def, null) : null;
                return obj as ForgeTypeId;
            }
            catch
            {
                return null;
            }
        }
    }
}
