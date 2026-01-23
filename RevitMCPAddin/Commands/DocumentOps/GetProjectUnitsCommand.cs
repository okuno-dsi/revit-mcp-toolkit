// ================================================================
// File: Commands/DocumentOps/GetProjectUnitsCommand.cs
// Purpose:
//   Return project unit settings (FormatOptions) for key SpecTypeId entries.
//   Useful for confirming how UI "display values" should be interpreted.
//
// Canonical: doc.get_project_units
// Alias    : get_project_units
//
// Params:
//   - mode?: "common" | "all" (default: "common")
//   - specNames?: string[]    (optional; SpecTypeId property names, e.g. ["Length","Area"])
//   - includeLabels?: bool    (default: true)  -> best-effort labels via LabelUtils (reflection)
//   - includeExamples?: bool  (default: true)  -> exampleFromInternal_1 via UnitUtils.ConvertFromInternalUnits(1, unit)
//
// Target: .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DocumentOps
{
    [RpcCommand("doc.get_project_units",
        Aliases = new[] { "get_project_units" },
        Category = "DocumentOps",
        Tags = new[] { "Document", "Units" },
        Kind = "read",
        Importance = "normal",
        Risk = RiskLevel.Low,
        Summary = "Get project unit settings (FormatOptions) for common specs or requested SpecTypeId names.")]
    public sealed class GetProjectUnitsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_project_units";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return ResultUtil.Err("No active document.");

                var p = cmd.Params as JObject ?? new JObject();

                string mode = (p.Value<string>("mode") ?? string.Empty).Trim();
                bool includeAll = p.Value<bool?>("includeAll") ?? p.Value<bool?>("includeAllSpecs") ?? false;
                bool includeLabels = p.Value<bool?>("includeLabels") ?? true;
                bool includeExamples = p.Value<bool?>("includeExamples") ?? true;

                var specNames = new List<string>();
                try
                {
                    if (p["specNames"] is JArray arr)
                    {
                        foreach (var s in arr.Values<string>())
                        {
                            var ss = (s ?? string.Empty).Trim();
                            if (ss.Length > 0) specNames.Add(ss);
                        }
                    }
                }
                catch { /* ignore */ }

                if (string.IsNullOrWhiteSpace(mode))
                    mode = includeAll ? "all" : "common";
                mode = mode.Trim().ToLowerInvariant();
                if (mode != "all") mode = "common";

                var units = doc.GetUnits();

                // Build spec list
                var unresolved = new List<string>();
                var specEntries = new List<SpecEntry>();

                if (specNames.Count > 0)
                {
                    foreach (var n in specNames)
                    {
                        if (TryResolveSpecByName(n, out var specId, out var resolvedName))
                            specEntries.Add(new SpecEntry { Name = resolvedName, Spec = specId });
                        else
                            unresolved.Add(n);
                    }
                }
                else if (mode == "all")
                {
                    specEntries.AddRange(GetAllSpecEntries());
                }
                else
                {
                    // Common set (use reflection so this builds across Revit versions)
                    foreach (var n in new[] { "Length", "Area", "Volume", "Angle", "Slope" })
                    {
                        if (TryResolveSpecByName(n, out var specId, out var resolvedName))
                            specEntries.Add(new SpecEntry { Name = resolvedName, Spec = specId });
                    }
                }

                // Deduplicate by specTypeId
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                specEntries = specEntries
                    .Where(x => x.Spec != null && !string.IsNullOrWhiteSpace(x.Spec.TypeId))
                    .Where(x => seen.Add(x.Spec.TypeId))
                    .ToList();

                var items = new List<object>();
                var skipped = new List<object>();

                foreach (var e in specEntries)
                {
                    FormatOptions fo;
                    try
                    {
                        fo = units.GetFormatOptions(e.Spec);
                    }
                    catch (Exception ex)
                    {
                        skipped.Add(new { specName = e.Name, specTypeId = SafeTypeId(e.Spec), reason = ex.Message });
                        continue;
                    }

                    var unitTypeId = TryInvokeForgeTypeId(fo, "GetUnitTypeId");
                    var symbolTypeId = TryInvokeForgeTypeId(fo, "GetSymbolTypeId");

                    double? accuracy = TryGetDouble(fo, "Accuracy", "GetAccuracy");
                    string roundingMethod = TryGetEnumString(fo, "RoundingMethod", "GetRoundingMethod");
                    bool? useDigitGrouping = TryGetBool(fo, "UseDigitGrouping", "GetUseDigitGrouping");
                    bool? suppressTrailingZeros = TryGetBool(fo, "SuppressTrailingZeros", "GetSuppressTrailingZeros");

                    string specLabel = null;
                    string unitLabel = null;
                    string symbolLabel = null;
                    if (includeLabels)
                    {
                        specLabel = TryLabelUtils("GetLabelForSpec", e.Spec);
                        if (unitTypeId != null) unitLabel = TryLabelUtils("GetLabelForUnit", unitTypeId);
                        if (symbolTypeId != null) symbolLabel = TryLabelUtils("GetLabelForSymbol", symbolTypeId);
                    }

                    double? exampleFromInternal1 = null;
                    if (includeExamples && unitTypeId != null)
                    {
                        try { exampleFromInternal1 = UnitUtils.ConvertFromInternalUnits(1.0, unitTypeId); } catch { /* ignore */ }
                    }

                    items.Add(new
                    {
                        specName = e.Name,
                        specTypeId = SafeTypeId(e.Spec),
                        specLabel,
                        unitTypeId = SafeTypeId(unitTypeId),
                        unitLabel,
                        symbolTypeId = SafeTypeId(symbolTypeId),
                        symbolLabel,
                        accuracy,
                        roundingMethod,
                        useDigitGrouping,
                        suppressTrailingZeros,
                        exampleFromInternal_1 = exampleFromInternal1
                    });
                }

                var payload = new JObject
                {
                    ["ok"] = true,
                    ["projectName"] = SafeStr(() => doc.Title),
                    ["displayUnitSystem"] = SafeStr(() => doc.DisplayUnitSystem.ToString()),
                    ["mode"] = mode,
                    ["unresolvedSpecNames"] = new JArray(unresolved.ToArray()),
                    ["skipped"] = JArray.FromObject(skipped),
                    ["items"] = JArray.FromObject(items),
                    ["counts"] = JObject.FromObject(new
                    {
                        requested = specNames.Count,
                        returned = items.Count,
                        unresolved = unresolved.Count,
                        skipped = skipped.Count
                    }),
                    ["notes"] = new JArray(
                        "Revit internal units are fixed per spec (e.g., Length=ft, Area=ft^2, Volume=ft^3).",
                        "exampleFromInternal_1 is ConvertFromInternalUnits(1.0, unitTypeId) as a quick sanity check."
                    )
                };

                return payload;
            }
            catch (Exception ex)
            {
                return ResultUtil.Err(ex.Message);
            }
        }

        // -------------------------- helpers --------------------------

        private sealed class SpecEntry
        {
            public string Name { get; set; } = string.Empty;
            public ForgeTypeId Spec { get; set; }
        }

        private static string SafeStr(Func<string> f)
        {
            try { return f() ?? string.Empty; } catch { return string.Empty; }
        }

        private static string SafeTypeId(ForgeTypeId id)
        {
            try { return id != null ? (id.TypeId ?? string.Empty) : string.Empty; } catch { return string.Empty; }
        }

        private static IEnumerable<SpecEntry> GetAllSpecEntries()
        {
            var list = new List<SpecEntry>();
            try
            {
                foreach (var p in typeof(SpecTypeId).GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (p == null) continue;
                    if (!p.CanRead) continue;
                    if (p.PropertyType != typeof(ForgeTypeId)) continue;
                    ForgeTypeId v = null;
                    try { v = p.GetValue(null, null) as ForgeTypeId; } catch { v = null; }
                    if (v == null) continue;
                    list.Add(new SpecEntry { Name = p.Name ?? string.Empty, Spec = v });
                }
            }
            catch { /* ignore */ }
            return list;
        }

        private static bool TryResolveSpecByName(string input, out ForgeTypeId spec, out string resolvedName)
        {
            spec = null;
            resolvedName = string.Empty;
            var s = (input ?? string.Empty).Trim();
            if (s.Length == 0) return false;

            try
            {
                var prop = typeof(SpecTypeId)
                    .GetProperties(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(p => p != null
                        && p.CanRead
                        && p.PropertyType == typeof(ForgeTypeId)
                        && string.Equals(p.Name, s, StringComparison.OrdinalIgnoreCase));
                if (prop != null)
                {
                    var v = prop.GetValue(null, null) as ForgeTypeId;
                    if (v != null)
                    {
                        spec = v;
                        resolvedName = prop.Name ?? s;
                        return true;
                    }
                }
            }
            catch { /* ignore */ }

            // Best-effort: allow passing a ForgeTypeId string (if ctor exists in this Revit API)
            try
            {
                var t = typeof(ForgeTypeId);
                var ctor = t.GetConstructor(new[] { typeof(string) });
                if (ctor != null)
                {
                    var obj = ctor.Invoke(new object[] { s });
                    var ft = obj as ForgeTypeId;
                    if (ft != null && !string.IsNullOrWhiteSpace(ft.TypeId))
                    {
                        spec = ft;
                        resolvedName = s;
                        return true;
                    }
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private static ForgeTypeId TryInvokeForgeTypeId(object obj, string methodName)
        {
            if (obj == null) return null;
            if (string.IsNullOrWhiteSpace(methodName)) return null;
            try
            {
                var mi = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (mi == null) return null;
                var r = mi.Invoke(obj, null);
                return r as ForgeTypeId;
            }
            catch { return null; }
        }

        private static double? TryGetDouble(object obj, string propName, string methodName)
        {
            if (obj == null) return null;
            try
            {
                var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null && pi.PropertyType == typeof(double))
                {
                    var v = pi.GetValue(obj, null);
                    if (v is double d) return d;
                }
            }
            catch { /* ignore */ }
            try
            {
                var mi = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (mi != null && mi.ReturnType == typeof(double))
                {
                    var v = mi.Invoke(obj, null);
                    if (v is double d) return d;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static bool? TryGetBool(object obj, string propName, string methodName)
        {
            if (obj == null) return null;
            try
            {
                var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null && pi.PropertyType == typeof(bool))
                {
                    var v = pi.GetValue(obj, null);
                    if (v is bool b) return b;
                }
            }
            catch { /* ignore */ }
            try
            {
                var mi = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (mi != null && mi.ReturnType == typeof(bool))
                {
                    var v = mi.Invoke(obj, null);
                    if (v is bool b) return b;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static string TryGetEnumString(object obj, string propName, string methodName)
        {
            if (obj == null) return null;
            try
            {
                var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null)
                {
                    var v = pi.GetValue(obj, null);
                    if (v != null) return v.ToString();
                }
            }
            catch { /* ignore */ }
            try
            {
                var mi = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (mi != null)
                {
                    var v = mi.Invoke(obj, null);
                    if (v != null) return v.ToString();
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static string TryLabelUtils(string methodName, ForgeTypeId id)
        {
            if (id == null) return null;
            try
            {
                var mi = typeof(LabelUtils).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ForgeTypeId) }, null);
                if (mi == null) return null;
                var r = mi.Invoke(null, new object[] { id });
                return r as string;
            }
            catch { return null; }
        }
    }
}

