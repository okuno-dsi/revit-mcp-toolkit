#nullable enable
// ================================================================
// File   : Core/RebarArrangementService.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Shared routines to inspect/update shape-driven rebar set layout rules.
// Policy : v1 supports shape-driven only (free-form is rejected).
// ================================================================
using System;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.Exceptions;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    internal static class RebarArrangementService
    {
        internal sealed class LayoutSpec
        {
            public string rule = "maximum_spacing"; // single|fixed_number|maximum_spacing|number_with_spacing|minimum_clear_spacing
            public double? spacingMm;
            public double? arrayLengthMm;
            public int? numberOfBarPositions;
            public bool? barsOnNormalSide;
            public bool? includeFirstBar;
            public bool? includeLastBar;
        }

        public static bool TryParseLayoutSpec(JObject? layoutObj, out LayoutSpec spec, out string errorCode, out string errorMsg)
        {
            spec = new LayoutSpec();
            errorCode = string.Empty;
            errorMsg = string.Empty;

            if (layoutObj == null)
            {
                errorCode = "INVALID_ARGS";
                errorMsg = "layout is required.";
                return false;
            }

            try
            {
                spec.rule = (layoutObj.Value<string>("rule") ?? "maximum_spacing").Trim().ToLowerInvariant();
                spec.spacingMm = layoutObj.Value<double?>("spacingMm");
                spec.arrayLengthMm = layoutObj.Value<double?>("arrayLengthMm");
                spec.numberOfBarPositions = layoutObj.Value<int?>("numberOfBarPositions");
                spec.barsOnNormalSide = layoutObj.Value<bool?>("barsOnNormalSide");
                spec.includeFirstBar = layoutObj.Value<bool?>("includeFirstBar");
                spec.includeLastBar = layoutObj.Value<bool?>("includeLastBar");
            }
            catch (Exception ex)
            {
                errorCode = "INVALID_ARGS";
                errorMsg = ex.Message;
                return false;
            }

            // Validation by rule
            var r = spec.rule;
            if (r == "single")
            {
                return true;
            }

            if (r == "fixed_number")
            {
                if (!spec.numberOfBarPositions.HasValue || spec.numberOfBarPositions.Value <= 0 || !spec.arrayLengthMm.HasValue || spec.arrayLengthMm.Value <= 0)
                {
                    errorCode = "INVALID_ARGS";
                    errorMsg = "fixed_number requires numberOfBarPositions (>0) and arrayLengthMm (>0).";
                    return false;
                }
                return true;
            }

            if (r == "maximum_spacing")
            {
                if (!spec.spacingMm.HasValue || spec.spacingMm.Value <= 0 || !spec.arrayLengthMm.HasValue || spec.arrayLengthMm.Value <= 0)
                {
                    errorCode = "INVALID_ARGS";
                    errorMsg = "maximum_spacing requires spacingMm (>0) and arrayLengthMm (>0).";
                    return false;
                }
                return true;
            }

            if (r == "number_with_spacing")
            {
                if (!spec.numberOfBarPositions.HasValue || spec.numberOfBarPositions.Value <= 0 || !spec.spacingMm.HasValue || spec.spacingMm.Value <= 0)
                {
                    errorCode = "INVALID_ARGS";
                    errorMsg = "number_with_spacing requires numberOfBarPositions (>0) and spacingMm (>0).";
                    return false;
                }
                return true;
            }

            if (r == "minimum_clear_spacing")
            {
                if (!spec.spacingMm.HasValue || spec.spacingMm.Value <= 0 || !spec.arrayLengthMm.HasValue || spec.arrayLengthMm.Value <= 0)
                {
                    errorCode = "INVALID_ARGS";
                    errorMsg = "minimum_clear_spacing requires spacingMm (>0) and arrayLengthMm (>0).";
                    return false;
                }
                return true;
            }

            errorCode = "INVALID_ARGS";
            errorMsg = "Unknown layout rule: '" + spec.rule + "'.";
            return false;
        }

        public static bool TryUpdateLayout(Autodesk.Revit.DB.Structure.Rebar rebar, LayoutSpec spec, out string errorCode, out string errorMsg)
        {
            errorCode = string.Empty;
            errorMsg = string.Empty;

            if (rebar == null)
            {
                errorCode = "NOT_FOUND";
                errorMsg = "Rebar element is null.";
                return false;
            }

            RebarShapeDrivenAccessor? acc = null;
            try { acc = rebar.GetShapeDrivenAccessor(); } catch { acc = null; }
            if (acc == null)
            {
                errorCode = "NOT_SHAPE_DRIVEN";
                errorMsg = "Rebar is not shape-driven (free-form). Layout update is not supported in v1.";
                return false;
            }

            bool barsOnNormalSide = spec.barsOnNormalSide ?? true;
            bool includeFirst = spec.includeFirstBar ?? true;
            bool includeLast = spec.includeLastBar ?? true;

            var rule = (spec.rule ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                if (rule == "single")
                {
                    acc.SetLayoutAsSingle();
                    return true;
                }

                if (rule == "fixed_number")
                {
                    int n = spec.numberOfBarPositions ?? 0;
                    double lenFt = UnitHelper.MmToFt(spec.arrayLengthMm ?? 0);
                    acc.SetLayoutAsFixedNumber(n, lenFt, barsOnNormalSide, includeFirst, includeLast);
                    return true;
                }

                if (rule == "maximum_spacing")
                {
                    double spFt = UnitHelper.MmToFt(spec.spacingMm ?? 0);
                    double lenFt = UnitHelper.MmToFt(spec.arrayLengthMm ?? 0);
                    acc.SetLayoutAsMaximumSpacing(spFt, lenFt, barsOnNormalSide, includeFirst, includeLast);
                    return true;
                }

                if (rule == "number_with_spacing")
                {
                    int n = spec.numberOfBarPositions ?? 0;
                    double spFt = UnitHelper.MmToFt(spec.spacingMm ?? 0);
                    acc.SetLayoutAsNumberWithSpacing(n, spFt, barsOnNormalSide, includeFirst, includeLast);
                    return true;
                }

                if (rule == "minimum_clear_spacing")
                {
                    double spFt = UnitHelper.MmToFt(spec.spacingMm ?? 0);
                    double lenFt = UnitHelper.MmToFt(spec.arrayLengthMm ?? 0);

                    var mi = acc.GetType().GetMethod("SetLayoutAsMinimumClearSpacing", new[]
                    {
                        typeof(double), typeof(double), typeof(bool), typeof(bool), typeof(bool)
                    });

                    if (mi == null)
                    {
                        errorCode = "INAPPLICABLE_DATA";
                        errorMsg = "minimum_clear_spacing is not supported by this Revit version/API.";
                        return false;
                    }

                    mi.Invoke(acc, new object[] { spFt, lenFt, barsOnNormalSide, includeFirst, includeLast });
                    return true;
                }

                errorCode = "INVALID_ARGS";
                errorMsg = "Unknown layout rule: '" + spec.rule + "'.";
                return false;
            }
            catch (DisabledDisciplineException ex)
            {
                errorCode = "DISCIPLINE_DISABLED";
                errorMsg = "Structural discipline is disabled. Enable Structural discipline in Revit. " + ex.Message;
                return false;
            }
            catch (InapplicableDataException ex)
            {
                errorCode = "INAPPLICABLE_DATA";
                errorMsg = "Rebar cannot accept this layout operation for its shape/type. " + ex.Message;
                return false;
            }
            catch (System.ArgumentOutOfRangeException ex)
            {
                errorCode = "INVALID_ARGS";
                errorMsg = "Layout arguments are out of allowed range. " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                errorCode = "REVIT_EXCEPTION";
                errorMsg = ex.ToString();
                return false;
            }
        }

        public static bool TryGetShapeDrivenAccessor(Autodesk.Revit.DB.Structure.Rebar rebar, out RebarShapeDrivenAccessor acc)
        {
            acc = null!;
            if (rebar == null) return false;
            try
            {
                var a = rebar.GetShapeDrivenAccessor();
                if (a == null) return false;
                acc = a;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
