#nullable enable
// ================================================================
// File   : Core/RebarAutoModelService.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Auto rebar planning + creation for existing hosts.
// Notes  :
//  - v1: Columns => main bars + ties; Framing => main bars + stirrups.
//  - Geometry is bbox-based (approx). Intended as automation scaffolding,
//    not as a structural design engine.
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core.Rebar;

namespace RevitMCPAddin.Core
{
    internal static class RebarAutoModelService
    {
        private const int PlanVersion = 1;
        private const string DefaultTagComments = "RevitMcp:AutoRebar";

        private sealed class AutoOptions
        {
            public string tagComments = DefaultTagComments;
            public string mainBarTypeName = string.Empty;
            public string tieBarTypeName = string.Empty;
            public bool includeMainBars = true;
            public bool includeTies = true;       // columns
            public bool includeStirrups = true;   // framing
            public bool beamUseTypeParams = true; // framing: use mapped beam attribute keys when available

            // Cover policy (safety):
            // - If per-face cover (up/down/left/right) cannot be resolved from instance parameters,
            //   or any raw cover is below coverMinMm, the tool can require user confirmation.
            public bool coverConfirmEnabled = true;
            public bool coverConfirmProceed = false; // user/agent explicit acknowledgement
            public double coverMinMm = 40.0;
            public bool coverClampToMin = true;

            // Optional explicit parameter-name hints for per-face cover (model-dependent).
            public string[] coverUpParamNames = null;
            public string[] coverDownParamNames = null;
            public string[] coverLeftParamNames = null;
            public string[] coverRightParamNames = null;

            // Beam main bars: user-specified counts (overrides mapping/default when provided)
            public int? beamMainTopCount;
            public int? beamMainBottomCount;

            // Beam main bars: when mapping provides 2nd/3rd layer counts, they are placed by stacking layers inward.
            // This is a geometric heuristic (not a full detailing engine). Center-to-center pitch uses:
            //   (barDiameter + beamMainBarLayerClearMm)
            public double beamMainBarLayerClearMm = 30.0;

            // Beam main bars: axis extension/trim (mm). Positive extends beyond bbox; negative shortens.
            public double beamMainBarStartExtensionMm;
            public double beamMainBarEndExtensionMm;

            // Beam main bars: embed into support columns (best-effort).
            // Example: ratio=0.75 means "extend the main bar into the column by 0.75 * column width along the beam axis".
            public bool beamMainBarEmbedIntoSupportColumns = true;
            public double beamMainBarEmbedIntoSupportColumnRatio = 0.75;
            public double beamSupportSearchRangeMm = 1500.0;
            public double beamSupportFaceToleranceMm = 250.0;

            // Beam main bars: optional 90deg bends at ends (mm, 0 disables).
            public double beamMainBarStartBendLengthMm;
            public double beamMainBarEndBendLengthMm;
            public string beamMainBarStartBendDir = "none"; // up|down|none (relative to local up axis)
            public string beamMainBarEndBendDir = "none";   // up|down|none

            // Column main bars: optional 90deg bends at ends (mm, 0 disables).
            public double columnMainBarStartBendLengthMm;
            public double columnMainBarEndBendLengthMm;
            public string columnMainBarStartBendDir = "none"; // up|down|none (relative to world Z)
            public string columnMainBarEndBendDir = "none";   // up|down|none

            // Column main bars: extension/trim at ends (mm). Positive extends beyond bbox; negative shortens.
            public double columnMainBarBottomExtensionMm;
            public double columnMainBarTopExtensionMm;

            // Column main bars: perimeter bars per face (>=2). 2 means corners only.
            // Example: 5 => 16 bars total for a rectangular column (5*2 + 5*2 - 4).
            public int columnMainBarsPerFace = 2;

            // Beam stirrups: which corner is the first segment start.
            public string beamStirrupStartCorner = "top_left"; // bottom_left|bottom_right|top_right|top_left

            // Beam stirrups: optional hook at both ends (best-effort).
            // When enabled, the stirrup curve becomes an open polyline and hooks are applied by Revit.
            public bool beamStirrupUseHooks;
            public double beamStirrupHookAngleDeg; // e.g. 135
            public string beamStirrupHookTypeName = string.Empty; // optional exact name (preferred)
            public string beamStirrupHookOrientationStart = "left";
            public string beamStirrupHookOrientationEnd = "right";
            public double beamStirrupHookStartRotationDeg; // default 0
            public double beamStirrupHookEndRotationDeg = 179.9; // default 179.9

            // Beam stirrups: start/end offsets from the physical end faces (mm).
            public double beamStirrupStartOffsetMm;
            public double beamStirrupEndOffsetMm;

            // Column ties: optional hook at both ends (best-effort).
            public bool columnTieUseHooks;
            public double columnTieHookAngleDeg; // e.g. 135
            public string columnTieHookTypeName = string.Empty;
            public string columnTieHookOrientationStart = "left";
            public string columnTieHookOrientationEnd = "right";
            public double columnTieHookStartRotationDeg; // default 0
            public double columnTieHookEndRotationDeg = 179.9; // default 179.9

            // Column ties: start/end offsets from the physical end faces (mm) along the column axis.
            public double columnTieBottomOffsetMm;
            public double columnTieTopOffsetMm;

            // Column ties: joint-focused non-uniform pattern around "beam top" (best-effort).
            // When enabled, the default column tie layout is replaced by 2 shape-driven sets:
            // - above: N bars @ pitch from the reference plane upward (includes ref bar at 0)
            // - below: N bars @ pitch below the reference plane (starts at -pitch, no ref bar)
            public bool columnTieJointPatternEnabled;
            public int columnTieJointAboveCount = 3;
            public double columnTieJointAbovePitchMm = 100.0;
            public int columnTieJointBelowCount = 2;
            public double columnTieJointBelowPitchMm = 150.0;
            public double columnTieJointBeamSearchRangeMm = 1500.0;
            public double columnTieJointBeamXYToleranceMm = 250.0;

            // Column ties: fully custom pattern (recommended). If set, it overrides the legacy joint options.
            // This can also be provided via mapping key `Column.Attr.Tie.PatternJson`.
            public JObject columnTiePattern = null;

            // If true (default), and hook types are not explicitly specified in options,
            // the tool tries to read hook settings from existing tagged rebars in the same host.
            public bool hookAutoDetectFromExistingTaggedRebar = true;

            public bool includeMappingDebug;
            public bool preferMappingArrayLength;
            public JObject layoutOverride = null;

            public static AutoOptions Parse(JObject obj)
            {
                var o = new AutoOptions();
                if (obj == null) return o;
                try
                {
                    o.tagComments = (obj.Value<string>("tagComments") ?? DefaultTagComments).Trim();
                    if (string.IsNullOrWhiteSpace(o.tagComments)) o.tagComments = DefaultTagComments;

                    o.mainBarTypeName = (obj.Value<string>("mainBarTypeName") ?? string.Empty).Trim();
                    o.tieBarTypeName = (obj.Value<string>("tieBarTypeName") ?? string.Empty).Trim();

                    o.includeMainBars = obj.Value<bool?>("includeMainBars") ?? true;
                    o.includeTies = obj.Value<bool?>("includeTies") ?? true;
                    o.includeStirrups = obj.Value<bool?>("includeStirrups") ?? true;
                    o.beamUseTypeParams = obj.Value<bool?>("beamUseTypeParams") ?? true;

                    o.beamMainTopCount = obj.Value<int?>("beamMainTopCount");
                    o.beamMainBottomCount = obj.Value<int?>("beamMainBottomCount");
                    o.beamMainBarLayerClearMm = obj.Value<double?>("beamMainBarLayerClearMm") ?? 30.0;
                    if (o.beamMainBarLayerClearMm < 0.0) o.beamMainBarLayerClearMm = 0.0;

                    o.beamMainBarStartExtensionMm = obj.Value<double?>("beamMainBarStartExtensionMm") ?? 0.0;
                    o.beamMainBarEndExtensionMm = obj.Value<double?>("beamMainBarEndExtensionMm") ?? 0.0;

                    o.beamMainBarEmbedIntoSupportColumns = obj.Value<bool?>("beamMainBarEmbedIntoSupportColumns") ?? true;
                    o.beamMainBarEmbedIntoSupportColumnRatio = obj.Value<double?>("beamMainBarEmbedIntoSupportColumnRatio") ?? 0.75;
                    o.beamSupportSearchRangeMm = obj.Value<double?>("beamSupportSearchRangeMm") ?? 1500.0;
                    o.beamSupportFaceToleranceMm = obj.Value<double?>("beamSupportFaceToleranceMm") ?? 250.0;

                    o.beamMainBarStartBendLengthMm = obj.Value<double?>("beamMainBarStartBendLengthMm") ?? 0.0;
                    o.beamMainBarEndBendLengthMm = obj.Value<double?>("beamMainBarEndBendLengthMm") ?? 0.0;

                    o.beamMainBarStartBendDir = (obj.Value<string>("beamMainBarStartBendDir") ?? "none").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.beamMainBarStartBendDir)) o.beamMainBarStartBendDir = "none";
                    o.beamMainBarEndBendDir = (obj.Value<string>("beamMainBarEndBendDir") ?? "none").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.beamMainBarEndBendDir)) o.beamMainBarEndBendDir = "none";

                    o.columnMainBarBottomExtensionMm = obj.Value<double?>("columnMainBarBottomExtensionMm") ?? 0.0;
                    o.columnMainBarTopExtensionMm = obj.Value<double?>("columnMainBarTopExtensionMm") ?? 0.0;
                    o.columnMainBarStartBendLengthMm = obj.Value<double?>("columnMainBarStartBendLengthMm") ?? 0.0;
                    o.columnMainBarEndBendLengthMm = obj.Value<double?>("columnMainBarEndBendLengthMm") ?? 0.0;
                    o.columnMainBarStartBendDir = (obj.Value<string>("columnMainBarStartBendDir") ?? "none").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.columnMainBarStartBendDir)) o.columnMainBarStartBendDir = "none";
                    o.columnMainBarEndBendDir = (obj.Value<string>("columnMainBarEndBendDir") ?? "none").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.columnMainBarEndBendDir)) o.columnMainBarEndBendDir = "none";
                    o.columnMainBarsPerFace = Math.Max(2, obj.Value<int?>("columnMainBarsPerFace") ?? o.columnMainBarsPerFace);

                    o.beamStirrupStartCorner = (obj.Value<string>("beamStirrupStartCorner") ?? "top_left").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.beamStirrupStartCorner)) o.beamStirrupStartCorner = "top_left";

                    // Default ON: shear reinforcement (stirrups/ties) usually requires hooks.
                    o.beamStirrupUseHooks = obj.Value<bool?>("beamStirrupUseHooks") ?? true;
                    o.beamStirrupHookAngleDeg = obj.Value<double?>("beamStirrupHookAngleDeg") ?? 0.0;
                    o.beamStirrupHookTypeName = (obj.Value<string>("beamStirrupHookTypeName") ?? string.Empty).Trim();
                    o.beamStirrupHookOrientationStart = (obj.Value<string>("beamStirrupHookOrientationStart") ?? "left").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.beamStirrupHookOrientationStart)) o.beamStirrupHookOrientationStart = "left";
                    o.beamStirrupHookOrientationEnd = (obj.Value<string>("beamStirrupHookOrientationEnd") ?? "right").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.beamStirrupHookOrientationEnd)) o.beamStirrupHookOrientationEnd = "right";
                    o.beamStirrupHookStartRotationDeg = obj.Value<double?>("beamStirrupHookStartRotationDeg") ?? 0.0;
                    o.beamStirrupHookEndRotationDeg = obj.Value<double?>("beamStirrupHookEndRotationDeg") ?? 179.9;

                    o.beamStirrupStartOffsetMm = obj.Value<double?>("beamStirrupStartOffsetMm") ?? 0.0;
                    o.beamStirrupEndOffsetMm = obj.Value<double?>("beamStirrupEndOffsetMm") ?? 0.0;

                    // Default ON: shear reinforcement (stirrups/ties) usually requires hooks.
                    o.columnTieUseHooks = obj.Value<bool?>("columnTieUseHooks") ?? true;
                    o.columnTieHookAngleDeg = obj.Value<double?>("columnTieHookAngleDeg") ?? 0.0;
                    o.columnTieHookTypeName = (obj.Value<string>("columnTieHookTypeName") ?? string.Empty).Trim();
                    o.columnTieHookOrientationStart = (obj.Value<string>("columnTieHookOrientationStart") ?? "left").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.columnTieHookOrientationStart)) o.columnTieHookOrientationStart = "left";
                    o.columnTieHookOrientationEnd = (obj.Value<string>("columnTieHookOrientationEnd") ?? "right").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.columnTieHookOrientationEnd)) o.columnTieHookOrientationEnd = "right";
                    o.columnTieHookStartRotationDeg = obj.Value<double?>("columnTieHookStartRotationDeg") ?? 0.0;
                    o.columnTieHookEndRotationDeg = obj.Value<double?>("columnTieHookEndRotationDeg") ?? 179.9;
                    o.columnTieBottomOffsetMm = obj.Value<double?>("columnTieBottomOffsetMm") ?? 0.0;
                    o.columnTieTopOffsetMm = obj.Value<double?>("columnTieTopOffsetMm") ?? 0.0;

                    o.columnTieJointPatternEnabled = obj.Value<bool?>("columnTieJointPatternEnabled") ?? false;
                    o.columnTieJointAboveCount = Math.Max(0, obj.Value<int?>("columnTieJointAboveCount") ?? o.columnTieJointAboveCount);
                    o.columnTieJointAbovePitchMm = obj.Value<double?>("columnTieJointAbovePitchMm") ?? o.columnTieJointAbovePitchMm;
                    o.columnTieJointBelowCount = Math.Max(0, obj.Value<int?>("columnTieJointBelowCount") ?? o.columnTieJointBelowCount);
                    o.columnTieJointBelowPitchMm = obj.Value<double?>("columnTieJointBelowPitchMm") ?? o.columnTieJointBelowPitchMm;
                    o.columnTieJointBeamSearchRangeMm = obj.Value<double?>("columnTieJointBeamSearchRangeMm") ?? o.columnTieJointBeamSearchRangeMm;
                    o.columnTieJointBeamXYToleranceMm = obj.Value<double?>("columnTieJointBeamXYToleranceMm") ?? o.columnTieJointBeamXYToleranceMm;

                    var colPat = obj["columnTiePattern"] as JObject;
                    if (colPat != null) o.columnTiePattern = colPat;

                    o.hookAutoDetectFromExistingTaggedRebar = obj.Value<bool?>("hookAutoDetectFromExistingTaggedRebar") ?? true;

                    // Cover policy
                    o.coverConfirmEnabled = obj.Value<bool?>("coverConfirmEnabled") ?? true;
                    o.coverConfirmProceed = obj.Value<bool?>("coverConfirmProceed") ?? false;
                    o.coverMinMm = obj.Value<double?>("coverMinMm") ?? 40.0;
                    o.coverClampToMin = obj.Value<bool?>("coverClampToMin") ?? true;

                    // Optional per-face cover parameter names: { up:[], down:[], left:[], right:[] }
                    try
                    {
                        var coverNames = obj["coverParamNames"] as JObject;
                        if (coverNames != null)
                        {
                            o.coverUpParamNames = ReadStringArray(coverNames["up"]);
                            o.coverDownParamNames = ReadStringArray(coverNames["down"]);
                            o.coverLeftParamNames = ReadStringArray(coverNames["left"]);
                            o.coverRightParamNames = ReadStringArray(coverNames["right"]);
                        }
                    }
                    catch { /* ignore */ }

                    o.includeMappingDebug = obj.Value<bool?>("includeMappingDebug") ?? false;
                    o.preferMappingArrayLength = obj.Value<bool?>("preferMappingArrayLength") ?? false;

                    var lo = obj["layout"] as JObject;
                    if (lo != null) o.layoutOverride = lo;
                }
                catch { /* ignore */ }
                return o;
            }

            private static string[] ReadStringArray(JToken tok)
            {
                try
                {
                    if (tok == null) return null;
                    if (tok.Type == JTokenType.String)
                    {
                        var s = (tok.Value<string>() ?? string.Empty).Trim();
                        if (s.Length == 0) return null;
                        return new[] { s };
                    }
                    var arr = tok as JArray;
                    if (arr == null || arr.Count == 0) return null;
                    var list = new List<string>();
                    foreach (var t in arr)
                    {
                        var s = (t != null ? (t.Value<string>() ?? string.Empty) : string.Empty).Trim();
                        if (s.Length == 0) continue;
                        list.Add(s);
                    }
                    return list.Count > 0 ? list.ToArray() : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        private sealed class LocalBox
        {
            public double minX, minY, minZ;
            public double maxX, maxY, maxZ;
        }

        private sealed class ColumnMappedRebarSpec
        {
            public int? mainBarsPerFace;
            public string tiePatternJson = string.Empty;
        }

        private sealed class ColumnTiePatternSpec
        {
            public string referenceKind = "beam_top"; // beam_top|beam_bottom|column_top|column_bottom
            public double referenceOffsetMm;
            public double beamSearchRangeMm = 1500.0;
            public double beamXYToleranceMm = 250.0;
            public List<ColumnTiePatternSegment> segments = new List<ColumnTiePatternSegment>();
        }

        private sealed class ColumnTiePatternSegment
        {
            public string name = string.Empty;
            public string direction = "up"; // up|down
            public double startOffsetMm;
            public int count;
            public double pitchMm;
        }

        private sealed class HookDefaults
        {
            public int sourceRebarId;
            public string startHookTypeName = string.Empty;
            public string endHookTypeName = string.Empty;
            public string startOrientation = string.Empty; // left/right (RebarHookOrientation)
            public string endOrientation = string.Empty;   // left/right (RebarHookOrientation)
            public double? startRotationRad = null;        // "始端でのフックの回転" etc. (Angle; internal radians)
            public double? endRotationRad = null;
        }

        internal sealed class CreatedRebarInfo
        {
            public int elementId;
            public string role = string.Empty;
            public string style = string.Empty;
            public string barTypeName = string.Empty;
        }

        internal static List<CreatedRebarInfo> ExecuteActionsInTransaction(
            Document doc,
            Element host,
            JArray actionsArr,
            out JArray layoutWarnings)
        {
            layoutWarnings = new JArray();
            var created = new List<CreatedRebarInfo>();

            if (doc == null) throw new System.InvalidOperationException("doc is null.");
            if (host == null) throw new System.InvalidOperationException("host is null.");
            if (actionsArr == null || actionsArr.Count == 0) return created;

            foreach (var aTok in actionsArr.OfType<JObject>())
            {
                var role = (aTok.Value<string>("role") ?? string.Empty).Trim();
                var styleStr = (aTok.Value<string>("style") ?? string.Empty).Trim();
                var barTypeName = (aTok.Value<string>("barTypeName") ?? string.Empty).Trim();
                var tagComments = (aTok.Value<string>("tagComments") ?? DefaultTagComments).Trim();
                if (string.IsNullOrWhiteSpace(tagComments)) tagComments = DefaultTagComments;

                if (string.IsNullOrWhiteSpace(styleStr) || string.IsNullOrWhiteSpace(barTypeName))
                    continue;

                if (!TryFindBarTypeByName(doc, barTypeName, out var barType))
                    throw new System.InvalidOperationException("Bar type not found: " + barTypeName);

                var curvesArr = aTok["curves"] as JArray;
                if (curvesArr == null || curvesArr.Count == 0)
                    continue;

                var normalObj = aTok["normal"] as JObject;
                var normal = normalObj != null ? TryParseVectorMm(normalObj) : XYZ.BasisZ;
                if (normal.GetLength() < 1e-9) normal = XYZ.BasisZ;
                normal = normal.Normalize();

                var curves = new List<Curve>();
                foreach (var cTok in curvesArr.OfType<JObject>())
                {
                    var kind = (cTok.Value<string>("kind") ?? string.Empty).Trim().ToLowerInvariant();
                    if (kind == "line")
                    {
                        var s = cTok["start"] as JObject;
                        var e = cTok["end"] as JObject;
                        if (s == null || e == null) continue;
                        var p0 = TryParsePointMm(s);
                        var p1 = TryParsePointMm(e);
                        curves.Add(Line.CreateBound(p0, p1));
                    }
                }
                if (curves.Count == 0) continue;

                    var style = styleStr.Equals("StirrupTie", StringComparison.OrdinalIgnoreCase) ? RebarStyle.StirrupTie : RebarStyle.Standard;

                Autodesk.Revit.DB.Structure.Rebar rebar = null;
                bool hookRequested = false;
                bool hookResolved = false;
                RebarHookType startHook = null;
                RebarHookType endHook = null;
                RebarHookOrientation startOrient = RebarHookOrientation.Left;
                RebarHookOrientation endOrient = RebarHookOrientation.Right;
                double? hookStartRotationRad = null;
                double? hookEndRotationRad = null;
                try
                {
                    // If hook is requested, resolve it regardless of the rebar style (Standard/StirrupTie).
                    var hookObj = aTok["hook"] as JObject;
                    if (hookObj != null)
                    {
                        bool enabled = hookObj.Value<bool?>("enabled") ?? true;
                        hookRequested = enabled;
                        try { hookStartRotationRad = hookObj.Value<double?>("startRotationRad"); } catch { /* ignore */ }
                        try { hookEndRotationRad = hookObj.Value<double?>("endRotationRad"); } catch { /* ignore */ }
                    }

                    hookResolved = TryResolveHookSpec(doc, aTok, style, out startHook, out endHook, out startOrient, out endOrient, out var hookWarn);
                    if (!string.IsNullOrWhiteSpace(hookWarn))
                        layoutWarnings.Add(hookWarn);

                    // For closed loop shapes (typical ties/stirrups), passing hook types to CreateFromCurves can cause
                    // "hook does not match rebar shape" failures. Create the closed loop first, then set hook parameters.
                    bool isClosed = false;
                    try
                    {
                        if (curves.Count >= 4)
                        {
                            var p0 = curves[0].GetEndPoint(0);
                            var p1 = curves[curves.Count - 1].GetEndPoint(1);
                            isClosed = p0.DistanceTo(p1) < 1e-6;
                        }
                    }
                    catch { isClosed = false; }

                    if (hookRequested && isClosed)
                    {
                        rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                            doc, style, barType, null, null, host, normal, curves,
                            RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    }
                    else
                    {
                        rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                            doc, style, barType, startHook, endHook, host, normal, curves,
                            startOrient, endOrient, true, true);
                    }
                }
                catch
                {
                    // Best-effort fallbacks:
                    // 1) Try without hooks.
                    try
                    {
                        rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                            doc, style, barType, null, null, host, normal, curves,
                            RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    }
                    catch
                    {
                        // 2) Try any hook type (some rebar styles reject some hook styles).
                        if (style == RebarStyle.StirrupTie)
                        {
                            var hook = TryGetAnyHookType(doc, style);
                            if (hook != null)
                            {
                                rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                    doc, style, barType, hook, hook, host, normal, curves,
                                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                            }
                            else throw;
                        }
                        else throw;
                    }
                }

                if (rebar == null) continue;

                // Ensure hooks are applied on the instance parameters (JP/EN UI names).
                // Some Revit versions/styles can ignore the hook arguments of CreateFromCurves.
                if (hookRequested && hookResolved)
                {
                    try
                    {
                        // Apply in small steps to reduce failure risk:
                        //  - set hook types
                        //  - regenerate
                        //  - set hook rotations
                        //  - regenerate
                        try { doc.Regenerate(); } catch { /* ignore */ }

                        using (var st1 = new SubTransaction(doc))
                        {
                            st1.Start();
                            try
                            {
                                ApplyHookTypesByName(rebar, startHook, endHook);
                                st1.Commit();
                            }
                            catch
                            {
                                try { st1.RollBack(); } catch { /* ignore */ }
                            }
                        }

                        try { doc.Regenerate(); } catch { /* ignore */ }

                        using (var st2 = new SubTransaction(doc))
                        {
                            st2.Start();
                            try
                            {
                                ApplyHookRotationsByName(rebar, hookStartRotationRad, hookEndRotationRad);
                                st2.Commit();
                            }
                            catch
                            {
                                try { st2.RollBack(); } catch { /* ignore */ }
                            }
                        }

                        try { doc.Regenerate(); } catch { /* ignore */ }
                    }
                    catch { /* ignore */ }
                }

                int rid = 0;
                try { rid = rebar.Id.IntValue(); } catch { rid = 0; }
                if (rid > 0)
                {
                    created.Add(new CreatedRebarInfo
                    {
                        elementId = rid,
                        role = role,
                        style = styleStr,
                        barTypeName = barTypeName
                    });
                }

                try
                {
                    var pC = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (pC != null && !pC.IsReadOnly) pC.Set(tagComments);
                }
                catch { /* ignore */ }

                var layoutObj = aTok["layout"] as JObject;
                if (layoutObj != null)
                {
                    if (RebarArrangementService.TryParseLayoutSpec(layoutObj, out var spec, out var parseCode, out var parseMsg))
                    {
                        if (!RebarArrangementService.TryUpdateLayout(rebar, spec, out var updateCode, out var updateMsg))
                        {
                            layoutWarnings.Add(new JObject
                            {
                                ["stage"] = "update",
                                ["code"] = updateCode,
                                ["msg"] = updateMsg,
                                ["rebarId"] = rid,
                                ["role"] = role
                            });
                        }
                    }
                    else
                    {
                        layoutWarnings.Add(new JObject
                        {
                            ["stage"] = "parse",
                            ["code"] = parseCode,
                            ["msg"] = parseMsg,
                            ["rebarId"] = rid,
                            ["role"] = role
                        });
                    }
                }
            }

            return created;
        }

        private sealed class BeamMappedRebarSpec
        {
            public int? mainDiaMm; // optional (for deriving bar type name like D{dia})
            public bool hasMainCounts;
            public int? topCount;
            public int? bottomCount;
            public int? topCount2;
            public int? topCount3;
            public int? bottomCount2;
            public int? bottomCount3;
            public bool hasLayerCounts;

            public int? stirrupDiaMm; // optional
            public string stirrupSymbol = "D"; // optional for deriving bar type name like {symbol}{dia}
            public bool hasPitchParams;
            public double pitchMidMm;
            public double pitchStartMm;
            public double pitchEndMm;
            public double pitchEffectiveMm;
        }

        private static bool TryGetBeamMappedRebarSpec(JObject values, out BeamMappedRebarSpec spec, out JObject debug)
        {
            spec = new BeamMappedRebarSpec();
            debug = null;
            if (values == null) return false;

            try
            {
                spec.mainDiaMm = values.Value<int?>("Beam.Attr.MainBar.DiameterMm");

                var top = values.Value<int?>("Beam.Attr.MainBar.TopCount");
                var bottom = values.Value<int?>("Beam.Attr.MainBar.BottomCount");
                spec.hasMainCounts = top.HasValue || bottom.HasValue;
                if (top.HasValue) spec.topCount = Math.Max(0, top.Value);
                if (bottom.HasValue) spec.bottomCount = Math.Max(0, bottom.Value);

                var top2 = values.Value<int?>("Beam.Attr.MainBar.TopCount2");
                var top3 = values.Value<int?>("Beam.Attr.MainBar.TopCount3");
                var bottom2 = values.Value<int?>("Beam.Attr.MainBar.BottomCount2");
                var bottom3 = values.Value<int?>("Beam.Attr.MainBar.BottomCount3");
                spec.hasLayerCounts = top2.HasValue || top3.HasValue || bottom2.HasValue || bottom3.HasValue;
                if (top2.HasValue) spec.topCount2 = Math.Max(0, top2.Value);
                if (top3.HasValue) spec.topCount3 = Math.Max(0, top3.Value);
                if (bottom2.HasValue) spec.bottomCount2 = Math.Max(0, bottom2.Value);
                if (bottom3.HasValue) spec.bottomCount3 = Math.Max(0, bottom3.Value);

                spec.stirrupDiaMm = values.Value<int?>("Beam.Attr.Stirrup.DiameterMm");

                var sym = (values.Value<string>("Beam.Attr.Stirrup.Symbol") ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(sym)) spec.stirrupSymbol = sym;

                bool hasMid = values["Beam.Attr.Stirrup.PitchMidMm"] != null;
                bool hasStart = values["Beam.Attr.Stirrup.PitchStartMm"] != null;
                bool hasEnd = values["Beam.Attr.Stirrup.PitchEndMm"] != null;
                spec.hasPitchParams = hasMid || hasStart || hasEnd;

                if (hasMid) spec.pitchMidMm = values.Value<double?>("Beam.Attr.Stirrup.PitchMidMm") ?? 0.0;
                if (hasStart) spec.pitchStartMm = values.Value<double?>("Beam.Attr.Stirrup.PitchStartMm") ?? 0.0;
                if (hasEnd) spec.pitchEndMm = values.Value<double?>("Beam.Attr.Stirrup.PitchEndMm") ?? 0.0;

                spec.pitchEffectiveMm = 0.0;
                if (hasMid && spec.pitchMidMm > 0.0) spec.pitchEffectiveMm = spec.pitchMidMm;
                else if (hasStart && spec.pitchStartMm > 0.0) spec.pitchEffectiveMm = spec.pitchStartMm;
                else if (hasEnd && spec.pitchEndMm > 0.0) spec.pitchEffectiveMm = spec.pitchEndMm;

                debug = new JObject
                {
                    ["mainBars"] = new JObject
                    {
                        ["diaMm"] = spec.mainDiaMm,
                        ["topCount"] = spec.topCount,
                        ["bottomCount"] = spec.bottomCount,
                        ["topCount2"] = spec.topCount2,
                        ["topCount3"] = spec.topCount3,
                        ["bottomCount2"] = spec.bottomCount2,
                        ["bottomCount3"] = spec.bottomCount3,
                        ["countsFound"] = spec.hasMainCounts
                    },
                    ["stirrups"] = new JObject
                    {
                        ["symbol"] = spec.stirrupSymbol,
                        ["diaMm"] = spec.stirrupDiaMm,
                        ["pitch_mid_mm"] = spec.pitchMidMm,
                        ["pitch_start_mm"] = spec.pitchStartMm,
                        ["pitch_end_mm"] = spec.pitchEndMm,
                        ["pitch_effective_mm"] = spec.pitchEffectiveMm,
                        ["pitchFound"] = spec.hasPitchParams
                    }
                };

                bool hasAny =
                    (spec.mainDiaMm.HasValue && spec.mainDiaMm.Value > 0)
                    || spec.hasMainCounts
                    || spec.hasLayerCounts
                    || (spec.stirrupDiaMm.HasValue && spec.stirrupDiaMm.Value > 0)
                    || spec.hasPitchParams;

                return hasAny;
            }
            catch
            {
                debug = null;
                return false;
            }
        }

        private static bool TryGetColumnMappedRebarSpec(JObject values, out ColumnMappedRebarSpec spec, out JObject debug)
        {
            spec = new ColumnMappedRebarSpec();
            debug = null;
            if (values == null) return false;

            try
            {
                var pf = values.Value<int?>("Column.Attr.MainBar.BarsPerFace");
                if (pf.HasValue) spec.mainBarsPerFace = Math.Max(2, pf.Value);

                var pat = (values.Value<string>("Column.Attr.Tie.PatternJson") ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(pat)) spec.tiePatternJson = pat;

                debug = new JObject
                {
                    ["mainBarsPerFace"] = spec.mainBarsPerFace,
                    ["tiePatternJson"] = string.IsNullOrWhiteSpace(spec.tiePatternJson) ? null : "(present)"
                };

                return spec.mainBarsPerFace.HasValue || !string.IsNullOrWhiteSpace(spec.tiePatternJson);
            }
            catch
            {
                debug = null;
                return false;
            }
        }

        private static bool TryParseColumnTiePatternFromJsonString(string json, out ColumnTiePatternSpec spec, out string errorMsg)
        {
            spec = new ColumnTiePatternSpec();
            errorMsg = string.Empty;
            if (string.IsNullOrWhiteSpace(json)) { errorMsg = "pattern json is empty"; return false; }

            try
            {
                var obj = JObject.Parse(json);
                return TryParseColumnTiePatternFromJObject(obj, out spec, out errorMsg);
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
        }

        private static bool TryParseColumnTiePatternFromJObject(JObject obj, out ColumnTiePatternSpec spec, out string errorMsg)
        {
            spec = new ColumnTiePatternSpec();
            errorMsg = string.Empty;
            if (obj == null) { errorMsg = "pattern object is null"; return false; }

            try
            {
                var refObj = obj["reference"] as JObject;
                if (refObj != null)
                {
                    spec.referenceKind = (refObj.Value<string>("kind") ?? spec.referenceKind).Trim().ToLowerInvariant();
                    spec.referenceOffsetMm = refObj.Value<double?>("offsetMm") ?? 0.0;
                    spec.beamSearchRangeMm = refObj.Value<double?>("beamSearchRangeMm") ?? spec.beamSearchRangeMm;
                    spec.beamXYToleranceMm = refObj.Value<double?>("beamXYToleranceMm") ?? spec.beamXYToleranceMm;
                }
                else
                {
                    spec.referenceKind = (obj.Value<string>("referenceKind") ?? spec.referenceKind).Trim().ToLowerInvariant();
                    spec.referenceOffsetMm = obj.Value<double?>("referenceOffsetMm") ?? 0.0;
                    spec.beamSearchRangeMm = obj.Value<double?>("beamSearchRangeMm") ?? spec.beamSearchRangeMm;
                    spec.beamXYToleranceMm = obj.Value<double?>("beamXYToleranceMm") ?? spec.beamXYToleranceMm;
                }

                var segs = obj["segments"] as JArray;
                if (segs != null)
                {
                    foreach (var t in segs.OfType<JObject>())
                    {
                        var s = new ColumnTiePatternSegment();
                        s.name = (t.Value<string>("name") ?? string.Empty).Trim();
                        s.direction = (t.Value<string>("direction") ?? "up").Trim().ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(s.direction)) s.direction = "up";
                        s.startOffsetMm = t.Value<double?>("startOffsetMm") ?? 0.0;
                        s.count = Math.Max(0, t.Value<int?>("count") ?? 0);
                        s.pitchMm = t.Value<double?>("pitchMm") ?? 0.0;
                        if (s.count <= 0) continue;
                        spec.segments.Add(s);
                    }
                }

                if (spec.segments.Count == 0)
                {
                    errorMsg = "No valid segments.";
                    return false;
                }

                var rk = (spec.referenceKind ?? string.Empty).Trim().ToLowerInvariant();
                if (rk != "beam_top" && rk != "beam_bottom" && rk != "column_top" && rk != "column_bottom")
                {
                    errorMsg = "Unknown referenceKind: '" + spec.referenceKind + "'.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
        }

        private static bool TryGetBeamTopBottomZ(Document doc, Element beam, out double topZ, out double bottomZ, out JObject debug)
        {
            topZ = 0.0;
            bottomZ = 0.0;
            debug = null;
            if (doc == null || beam == null) return false;

            double locZ = 0.0;
            if (!TryGetLocationCurveAverageZ(beam, out locZ))
                return false;

            double heightMm = 0.0;
            string heightSource = "mapping";
            try
            {
                var r = RebarMappingService.ResolveForElement(doc, beam, null, new[] { "Host.Section.Height" }, includeDebug: false);
                var v = r != null ? (r["values"] as JObject) : null;
                var hm = v != null ? v.Value<double?>("Host.Section.Height") : null;
                if (hm.HasValue && hm.Value > 1.0)
                {
                    heightMm = hm.Value;
                    heightSource = "mapping";
                }
            }
            catch
            {
                heightMm = 0.0;
            }

            BoundingBoxXYZ bb = null;
            try { bb = beam.get_BoundingBox(null); } catch { bb = null; }

            if (!(heightMm > 1.0))
            {
                if (bb != null && bb.Min != null && bb.Max != null)
                {
                    topZ = bb.Max.Z;
                    bottomZ = bb.Min.Z;
                    debug = new JObject
                    {
                        ["mode"] = "bbox_fallback",
                        ["locZmm"] = UnitHelper.FtToMm(locZ),
                        ["bboxTopZmm"] = UnitHelper.FtToMm(topZ),
                        ["bboxBottomZmm"] = UnitHelper.FtToMm(bottomZ)
                    };
                    return topZ > bottomZ;
                }
                return false;
            }

            double hFt = UnitHelper.MmToFt(heightMm);
            double tol = UnitHelper.MmToFt(20.0);
            string mode = "locZ_center";

            if (bb != null && bb.Min != null && bb.Max != null)
            {
                if (Math.Abs(bb.Max.Z - locZ) <= tol) { topZ = locZ; bottomZ = locZ - hFt; mode = "locZ_is_top"; }
                else if (Math.Abs(bb.Min.Z - locZ) <= tol) { bottomZ = locZ; topZ = locZ + hFt; mode = "locZ_is_bottom"; }
                else { topZ = locZ + 0.5 * hFt; bottomZ = locZ - 0.5 * hFt; mode = "locZ_is_center"; }
            }
            else
            {
                topZ = locZ + 0.5 * hFt;
                bottomZ = locZ - 0.5 * hFt;
                mode = "locZ_is_center_no_bbox";
            }

            debug = new JObject
            {
                ["mode"] = mode,
                ["heightMm"] = heightMm,
                ["heightSource"] = heightSource,
                ["locZmm"] = UnitHelper.FtToMm(locZ),
                ["topZmm"] = UnitHelper.FtToMm(topZ),
                ["bottomZmm"] = UnitHelper.FtToMm(bottomZ),
                ["bboxTopZmm"] = bb != null && bb.Max != null ? UnitHelper.FtToMm(bb.Max.Z) : (double?)null,
                ["bboxBottomZmm"] = bb != null && bb.Min != null ? UnitHelper.FtToMm(bb.Min.Z) : (double?)null
            };
            return topZ > bottomZ;
        }

        private static bool TryGetLocationCurveAverageZ(Element e, out double locZ)
        {
            locZ = 0.0;
            if (e == null) return false;
            try
            {
                var lc = e.Location as LocationCurve;
                var c = lc != null ? lc.Curve : null;
                if (c == null) return false;
                var p0 = c.GetEndPoint(0);
                var p1 = c.GetEndPoint(1);
                locZ = 0.5 * (p0.Z + p1.Z);
                return true;
            }
            catch
            {
                locZ = 0.0;
                return false;
            }
        }

        private static bool TryGetColumnTieReferenceAxisCoordFromNearestBeam(
            Document doc,
            Element columnHost,
            Transform columnTr,
            int axisIndex,
            double axisStartFt,
            double axisEndFt,
            bool useBeamTop,
            double beamSearchRangeFt,
            double beamXYToleranceFt,
            out double referenceAxisCoordFt,
            out JObject debug)
        {
            referenceAxisCoordFt = 0.0;
            debug = null;
            if (doc == null || columnHost == null) return false;
            if (columnTr == null) columnTr = Transform.Identity;
            if (!(axisEndFt > axisStartFt)) return false;

            BoundingBoxXYZ colBb = null;
            try { colBb = columnHost.get_BoundingBox(null); } catch { colBb = null; }
            if (colBb == null || colBb.Min == null || colBb.Max == null) return false;

            double cx = 0.5 * (colBb.Min.X + colBb.Max.X);
            double cy = 0.5 * (colBb.Min.Y + colBb.Max.Y);

            IList<Element> beams = null;
            try
            {
                var min = new XYZ(colBb.Min.X - beamSearchRangeFt, colBb.Min.Y - beamSearchRangeFt, colBb.Min.Z - beamSearchRangeFt);
                var max = new XYZ(colBb.Max.X + beamSearchRangeFt, colBb.Max.Y + beamSearchRangeFt, colBb.Max.Z + beamSearchRangeFt);
                var outline = new Outline(min, max);
                var bbFilter = new BoundingBoxIntersectsFilter(outline);

                beams = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbFilter)
                    .ToElements();
            }
            catch { beams = null; }
            if (beams == null || beams.Count == 0) return false;

            Element best = null;
            double bestRefZ = useBeamTop ? double.NegativeInfinity : double.PositiveInfinity;
            JObject bestBeamDbg = null;

            foreach (var e in beams)
            {
                if (e == null) continue;
                BoundingBoxXYZ bb = null;
                try { bb = e.get_BoundingBox(null); } catch { bb = null; }
                if (bb == null || bb.Min == null || bb.Max == null) continue;

                // XY overlap check against the column bbox (with tolerance)
                if (bb.Max.X < colBb.Min.X - beamXYToleranceFt) continue;
                if (bb.Min.X > colBb.Max.X + beamXYToleranceFt) continue;
                if (bb.Max.Y < colBb.Min.Y - beamXYToleranceFt) continue;
                if (bb.Min.Y > colBb.Max.Y + beamXYToleranceFt) continue;

                if (!TryGetBeamTopBottomZ(doc, e, out var topZ, out var bottomZ, out var beamDbg) || !(topZ > bottomZ))
                    continue;

                double refZ = useBeamTop ? topZ : bottomZ;

                // Reference should be within the column vertical range (roughly).
                if (refZ < colBb.Min.Z - beamXYToleranceFt) continue;
                if (refZ > colBb.Max.Z + beamXYToleranceFt) continue;

                if (useBeamTop)
                {
                    if (refZ > bestRefZ)
                    {
                        bestRefZ = refZ;
                        best = e;
                        bestBeamDbg = beamDbg;
                    }
                }
                else
                {
                    if (refZ < bestRefZ)
                    {
                        bestRefZ = refZ;
                        best = e;
                        bestBeamDbg = beamDbg;
                    }
                }
            }

            if (best == null) return false;

            var inv = columnTr.Inverse;
            XYZ localRef;
            try { localRef = inv.OfPoint(new XYZ(cx, cy, bestRefZ)); }
            catch { localRef = inv.OfPoint(new XYZ(cx, cy, bestRefZ)); }

            double refAxis = axisIndex == 0 ? localRef.X : (axisIndex == 1 ? localRef.Y : localRef.Z);
            double refClamped = refAxis;
            if (refClamped < axisStartFt) refClamped = axisStartFt;
            if (refClamped > axisEndFt) refClamped = axisEndFt;

            referenceAxisCoordFt = refClamped;
            debug = new JObject
            {
                ["source"] = useBeamTop ? "nearest_beam_top" : "nearest_beam_bottom",
                ["beamId"] = best.Id.IntValue(),
                ["beamRefZmm"] = UnitHelper.FtToMm(bestRefZ),
                ["axisIndex"] = axisIndex,
                ["refAxisMm_raw"] = UnitHelper.FtToMm(refAxis),
                ["refAxisMm_clamped"] = UnitHelper.FtToMm(refClamped),
                ["axisStartMm"] = UnitHelper.FtToMm(axisStartFt),
                ["axisEndMm"] = UnitHelper.FtToMm(axisEndFt),
                ["beamDebug"] = bestBeamDbg
            };
            return true;
        }

        private static Parameter TryLookupParamByNames(Element e, params string[] names)
        {
            if (e == null || names == null || names.Length == 0) return null;
            foreach (var n in names)
            {
                var name = (n ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                try
                {
                    var p = e.LookupParameter(name);
                    if (p != null) return p;
                }
                catch { /* ignore */ }
            }
            return null;
        }

        private static void ApplyHookTypesByName(
            Autodesk.Revit.DB.Structure.Rebar rebar,
            RebarHookType startHook,
            RebarHookType endHook)
        {
            if (rebar == null) return;
            if (startHook != null) TrySetHookTypeParam(rebar, startHook, "始端のフック", "Start Hook");
            if (endHook != null) TrySetHookTypeParam(rebar, endHook, "終端のフック", "End Hook");
        }

        private static void ApplyHookRotationsByName(
            Autodesk.Revit.DB.Structure.Rebar rebar,
            double? startRotationRad,
            double? endRotationRad)
        {
            if (rebar == null) return;
            // In JP UI, the angle-style parameters are typically named "始端でのフックの回転" / "終端でのフックの回転".
            // Those are Angle specs (internal radians).
            TrySetAngleParamIfWritable(rebar, startRotationRad ?? 0.0, "始端でのフックの回転", "始端のフックの回転", "Start Hook Rotation");
            TrySetAngleParamIfWritable(rebar, endRotationRad ?? 0.0, "終端でのフックの回転", "終端のフックの回転", "End Hook Rotation");
        }

        private static void TrySetHookTypeParam(Element e, RebarHookType hookType, params string[] paramNames)
        {
            if (e == null || hookType == null) return;
            var p = TryLookupParamByNames(e, paramNames);
            if (p == null || p.IsReadOnly) return;
            try
            {
                if (p.StorageType == StorageType.ElementId)
                {
                    p.Set(hookType.Id);
                    return;
                }
            }
            catch { /* ignore */ }
            try
            {
                if (p.StorageType == StorageType.String)
                {
                    p.Set((hookType.Name ?? string.Empty).Trim());
                    return;
                }
            }
            catch { /* ignore */ }
        }

        private static void TrySetHookOrientationParam(Element e, RebarHookOrientation orient, params string[] paramNames)
        {
            if (e == null) return;
            var p = TryLookupParamByNames(e, paramNames);
            if (p == null || p.IsReadOnly) return;

            try
            {
                if (p.StorageType == StorageType.Integer)
                {
                    p.Set(orient == RebarHookOrientation.Right ? 1 : 0);
                    return;
                }
            }
            catch { /* ignore */ }

            try
            {
                if (p.StorageType == StorageType.String)
                {
                    // Prefer JP tokens; if rejected, fall back to EN tokens.
                    var jp = orient == RebarHookOrientation.Right ? "右" : "左";
                    p.Set(jp);
                    return;
                }
            }
            catch
            {
                try
                {
                    if (p.StorageType == StorageType.String)
                    {
                        var en = orient == RebarHookOrientation.Right ? "Right" : "Left";
                        p.Set(en);
                    }
                }
                catch { /* ignore */ }
            }
        }

        private static void TrySetAngleParamIfWritable(Element e, double angleRad, params string[] paramNames)
        {
            if (e == null) return;
            var p = TryLookupParamByNames(e, paramNames);
            if (p == null || p.IsReadOnly) return;
            try
            {
                if (p.StorageType == StorageType.Double)
                {
                    p.Set(angleRad);
                }
            }
            catch { /* ignore */ }
        }

        private static bool TryReadHookTypeNameFromParam(Document doc, Element e, params string[] paramNames)
        {
            // Convenience overload for bool-return check only.
            return TryReadHookTypeNameFromParam(doc, e, out _, paramNames);
        }

        private static bool TryReadHookTypeNameFromParam(Document doc, Element e, out string hookTypeName, params string[] paramNames)
        {
            hookTypeName = string.Empty;
            if (doc == null || e == null) return false;

            var p = TryLookupParamByNames(e, paramNames);
            if (p == null) return false;

            try
            {
                if (p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (id == null || id == ElementId.InvalidElementId) return false;
                    var ht = doc.GetElement(id) as RebarHookType;
                    if (ht != null && !string.IsNullOrWhiteSpace(ht.Name))
                    {
                        hookTypeName = ht.Name.Trim();
                        return hookTypeName.Length > 0;
                    }
                    return false;
                }

                var s = p.AsString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    hookTypeName = s.Trim();
                    return hookTypeName.Length > 0;
                }

                var vs = p.AsValueString();
                if (!string.IsNullOrWhiteSpace(vs))
                {
                    hookTypeName = vs.Trim();
                    return hookTypeName.Length > 0;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool TryReadLeftRightFromParam(Element e, out string orientation, params string[] paramNames)
        {
            orientation = string.Empty;
            if (e == null) return false;

            var p = TryLookupParamByNames(e, paramNames);
            if (p == null) return false;

            try
            {
                var s = (p.AsValueString() ?? p.AsString() ?? string.Empty).Trim();
                if (s.Length > 0)
                {
                    if (s.IndexOf("左", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        orientation = "left";
                        return true;
                    }
                    if (s.IndexOf("右", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        orientation = "right";
                        return true;
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                if (p.StorageType == StorageType.Integer)
                {
                    int v = p.AsInteger();
                    if (v == 0) { orientation = "left"; return true; }
                    if (v == 1) { orientation = "right"; return true; }
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool TryReadAngleRadFromParam(Element e, out double angleRad, params string[] paramNames)
        {
            angleRad = 0.0;
            if (e == null) return false;

            var p = TryLookupParamByNames(e, paramNames);
            if (p == null) return false;

            try
            {
                if (p.StorageType == StorageType.Double)
                {
                    angleRad = p.AsDouble();
                    return true;
                }
            }
            catch { /* ignore */ }

            // Best-effort parse from display string like "0°" or "90 deg"
            try
            {
                var s = (p.AsValueString() ?? p.AsString() ?? string.Empty).Trim();
                if (s.Length == 0) return false;
                s = s.Replace("°", "");
                s = s.Replace("deg", "");
                s = s.Replace("DEG", "");
                s = s.Trim();
                double deg;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out deg)
                    || double.TryParse(s, out deg))
                {
                    angleRad = deg * Math.PI / 180.0;
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool TryGetHookDefaultsFromExistingTaggedRebarInHost(
            Document doc,
            Element host,
            string tagComments,
            string preferBarTypeName,
            out HookDefaults defaults,
            out JObject debug)
        {
            defaults = new HookDefaults();
            debug = null;

            if (doc == null || host == null) return false;
            var needle = (tagComments ?? string.Empty).Trim();
            if (needle.Length == 0) return false;

            bool validHost = false;
            try { validHost = RebarHostData.IsValidHost(host); } catch { validHost = false; }
            if (!validHost) return false;

            RebarHostData rhd = null;
            try { rhd = RebarHostData.GetRebarHostData(host); } catch { rhd = null; }
            if (rhd == null) return false;

            System.Collections.IEnumerable items = null;
            try { items = rhd.GetRebarsInHost() as System.Collections.IEnumerable; } catch { items = null; }
            if (items == null) return false;

            int bestScore = int.MinValue;
            HookDefaults best = null;

            foreach (var it in items)
            {
                if (it == null) continue;
                Autodesk.Revit.DB.Structure.Rebar rebar = null;
                try
                {
                    rebar = it as Autodesk.Revit.DB.Structure.Rebar;
                    if (rebar == null)
                    {
                        var id = it as ElementId;
                        if (id != null) rebar = doc.GetElement(id) as Autodesk.Revit.DB.Structure.Rebar;
                    }
                }
                catch { rebar = null; }
                if (rebar == null) continue;

                string comments = string.Empty;
                try
                {
                    var pC = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    comments = pC != null ? (pC.AsString() ?? string.Empty) : string.Empty;
                }
                catch { comments = string.Empty; }
                if (comments.Length == 0) continue;
                if (comments.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;

                // Extract hook params by UI-visible names (JP/EN). These are Rebar instance parameters.
                string startHook = string.Empty;
                string endHook = string.Empty;
                TryReadHookTypeNameFromParam(doc, rebar, out startHook, "始端のフック", "Start Hook");
                TryReadHookTypeNameFromParam(doc, rebar, out endHook, "終端のフック", "End Hook");
                if (string.IsNullOrWhiteSpace(startHook) && string.IsNullOrWhiteSpace(endHook)) continue;

                string startRot = string.Empty;
                string endRot = string.Empty;
                TryReadLeftRightFromParam(rebar, out startRot, "始端のフックの回転", "Start Hook Rotation", "Start Hook Orientation");
                TryReadLeftRightFromParam(rebar, out endRot, "終端のフックの回転", "End Hook Rotation", "End Hook Orientation");

                double? startRotRad = null;
                double? endRotRad = null;
                try
                {
                    if (TryReadAngleRadFromParam(rebar, out var a1, "始端でのフックの回転", "始端のフックの回転", "Start Hook Rotation"))
                        startRotRad = a1;
                    if (TryReadAngleRadFromParam(rebar, out var a2, "終端でのフックの回転", "終端のフックの回転", "End Hook Rotation"))
                        endRotRad = a2;
                }
                catch { /* ignore */ }

                int rid = 0;
                try { rid = rebar.Id.IntValue(); } catch { rid = 0; }

                int score = 0;
                if (!string.IsNullOrWhiteSpace(startHook)) score += 10;
                if (!string.IsNullOrWhiteSpace(endHook)) score += 10;
                if (startRotRad.HasValue) score += 5;
                if (endRotRad.HasValue) score += 5;

                try
                {
                    var bt = doc.GetElement(rebar.GetTypeId()) as RebarBarType;
                    var bn = bt != null ? (bt.Name ?? string.Empty) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(preferBarTypeName) && bn.Equals(preferBarTypeName, StringComparison.OrdinalIgnoreCase))
                        score += 100;
                }
                catch { /* ignore */ }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new HookDefaults
                    {
                        sourceRebarId = rid,
                        startHookTypeName = (startHook ?? string.Empty).Trim(),
                        endHookTypeName = (endHook ?? string.Empty).Trim(),
                        startOrientation = (startRot ?? string.Empty).Trim().ToLowerInvariant(),
                        endOrientation = (endRot ?? string.Empty).Trim().ToLowerInvariant(),
                        startRotationRad = startRotRad,
                        endRotationRad = endRotRad
                    };
                }
            }

            if (best == null) return false;

            // If only one end is set, use it for both ends.
            if (string.IsNullOrWhiteSpace(best.startHookTypeName) && !string.IsNullOrWhiteSpace(best.endHookTypeName))
                best.startHookTypeName = best.endHookTypeName;
            if (string.IsNullOrWhiteSpace(best.endHookTypeName) && !string.IsNullOrWhiteSpace(best.startHookTypeName))
                best.endHookTypeName = best.startHookTypeName;

            if (string.IsNullOrWhiteSpace(best.startOrientation)) best.startOrientation = "left";
            if (string.IsNullOrWhiteSpace(best.endOrientation)) best.endOrientation = "right";

            defaults = best;
            debug = new JObject
            {
                ["sourceRebarId"] = best.sourceRebarId,
                ["startHookTypeName"] = best.startHookTypeName,
                ["endHookTypeName"] = best.endHookTypeName,
                ["startOrientation"] = best.startOrientation,
                ["endOrientation"] = best.endOrientation,
                ["startRotationRad"] = best.startRotationRad,
                ["endRotationRad"] = best.endRotationRad
            };
            return true;
        }

        private static bool TryGetColumnTiePatternReferenceAxisCoord(
            Document doc,
            Element columnHost,
            Transform columnTr,
            int axisIndex,
            double axisStartFt,
            double axisEndFt,
            ColumnTiePatternSpec pat,
            out double referenceAxisCoordFt,
            out JObject debug)
        {
            referenceAxisCoordFt = 0.0;
            debug = null;
            if (doc == null || columnHost == null || pat == null) return false;

            string rk = (pat.referenceKind ?? string.Empty).Trim().ToLowerInvariant();
            bool ok = false;
            JObject dbg = null;
            double refFt = 0.0;

            if (rk == "column_bottom")
            {
                refFt = axisStartFt;
                ok = true;
                dbg = new JObject { ["source"] = "column_bottom" };
            }
            else if (rk == "column_top")
            {
                refFt = axisEndFt;
                ok = true;
                dbg = new JObject { ["source"] = "column_top" };
            }
            else if (rk == "beam_top" || rk == "beam_bottom")
            {
                bool useTop = rk == "beam_top";
                ok = TryGetColumnTieReferenceAxisCoordFromNearestBeam(
                    doc, columnHost, columnTr, axisIndex, axisStartFt, axisEndFt,
                    useTop,
                    UnitHelper.MmToFt(pat.beamSearchRangeMm),
                    UnitHelper.MmToFt(pat.beamXYToleranceMm),
                    out refFt, out dbg);
            }

            if (!ok) return false;

            double offFt = UnitHelper.MmToFt(pat.referenceOffsetMm);
            double refOff = refFt + offFt;
            if (refOff < axisStartFt) refOff = axisStartFt;
            if (refOff > axisEndFt) refOff = axisEndFt;

            referenceAxisCoordFt = refOff;
            debug = new JObject
            {
                ["referenceKind"] = rk,
                ["referenceOffsetMm"] = pat.referenceOffsetMm,
                ["referenceAxisMm"] = UnitHelper.FtToMm(refOff),
                ["referenceDebug"] = dbg
            };
            return true;
        }

        public static bool TryCollectHostIds(UIApplication uiapp, JObject p, out List<int> hostIds, out string errorCode, out string errorMsg)
        {
            hostIds = new List<int>();
            errorCode = string.Empty;
            errorMsg = string.Empty;

            var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
            if (uidoc == null)
            {
                errorCode = "NO_UIDOC";
                errorMsg = "ActiveUIDocument is not available.";
                return false;
            }

            try
            {
                var arr = p["hostElementIds"] as JArray;
                if (arr != null)
                {
                    foreach (var t in arr)
                    {
                        if (t == null || t.Type != JTokenType.Integer) continue;
                        int v = t.Value<int>();
                        if (v > 0) hostIds.Add(v);
                    }
                }
            }
            catch { /* ignore */ }

            bool useSelectionIfEmpty = p.Value<bool?>("useSelectionIfEmpty") ?? true;
            if (hostIds.Count == 0 && useSelectionIfEmpty)
            {
                try
                {
                    foreach (var id in uidoc.Selection.GetElementIds())
                    {
                        try
                        {
                            int v = id.IntValue();
                            if (v > 0) hostIds.Add(v);
                        }
                        catch { /* ignore */ }
                    }
                }
                catch { /* ignore */ }
            }

            hostIds = hostIds.Distinct().ToList();
            if (hostIds.Count == 0)
            {
                errorCode = "INVALID_ARGS";
                errorMsg = "hostElementIds が空で、選択も空です。";
                return false;
            }

            return true;
        }

        public static JObject BuildPlan(UIApplication uiapp, Document doc, JObject p)
        {
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            if (!TryCollectHostIds(uiapp, p, out var hostIds, out var code, out var msg))
                return ResultUtil.Err(msg, string.IsNullOrWhiteSpace(code) ? "INVALID_ARGS" : code);

            var opts = AutoOptions.Parse(p["options"] as JObject);
            var profile = (p.Value<string>("profile") ?? string.Empty).Trim();
            if (profile.Length == 0) profile = null;

            bool deleteExistingTaggedInHosts = p.Value<bool?>("deleteExistingTaggedInHosts") ?? false;
            if (!deleteExistingTaggedInHosts)
            {
                try
                {
                    var o = p["options"] as JObject;
                    if (o != null) deleteExistingTaggedInHosts = o.Value<bool?>("deleteExistingTaggedInHosts") ?? false;
                }
                catch { /* ignore */ }
            }

             var mappingKeysBase = new[]
             {
                 "Common.MainBar.BarType",
                 "Common.TieBar.BarType",
                 "Common.Arrangement.Rule",
                 "Common.Arrangement.Spacing",
                 "Common.Arrangement.ArrayLength",
                 "Common.Arrangement.NumberOfBarPositions",
                 "Common.Arrangement.IncludeFirstBar",
                 "Common.Arrangement.IncludeLastBar",
                 "Common.Arrangement.BarsOnNormalSide",
                 "Host.Cover.Top",
                 "Host.Cover.Bottom",
                 "Host.Cover.Other",
                 // Column attribute keys (optional; prefer attributes over hardcoded options)
                 "Column.Attr.MainBar.BarsPerFace",
                 "Column.Attr.Tie.PatternJson"
             };

            var mappingKeysBeamAttr = new[]
            {
                "Beam.Attr.MainBar.DiameterMm",
                "Beam.Attr.MainBar.TopCount",
                "Beam.Attr.MainBar.TopCount2",
                "Beam.Attr.MainBar.TopCount3",
                "Beam.Attr.MainBar.BottomCount",
                "Beam.Attr.MainBar.BottomCount2",
                "Beam.Attr.MainBar.BottomCount3",
                "Beam.Attr.Stirrup.Symbol",
                "Beam.Attr.Stirrup.DiameterMm",
                "Beam.Attr.Stirrup.PitchMidMm",
                "Beam.Attr.Stirrup.PitchStartMm",
                "Beam.Attr.Stirrup.PitchEndMm"
            };

            JObject mappingStatus;
            try { mappingStatus = JObject.FromObject(RebarMappingService.GetStatus()); }
            catch { mappingStatus = new JObject { ["ok"] = false, ["code"] = "UNKNOWN", ["msg"] = "mapping status unavailable" }; }

            var hostsArr = new JArray();
            bool coverConfirmRequiredAny = false;

            foreach (var hid in hostIds)
            {
                var hostObj = new JObject
                {
                    ["hostElementId"] = hid
                };

                Element host = null;
                try { host = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(hid)); } catch { host = null; }
                if (host == null)
                {
                    hostObj["ok"] = false;
                    hostObj["code"] = "NOT_FOUND";
                    hostObj["msg"] = "Host element not found.";
                    hostsArr.Add(hostObj);
                    continue;
                }

                string categoryBic = string.Empty;
                try
                {
                    int catId = host.Category != null && host.Category.Id != null ? host.Category.Id.IntValue() : 0;
                    if (catId != 0)
                    {
                        try { categoryBic = ((BuiltInCategory)catId).ToString(); } catch { categoryBic = string.Empty; }
                    }
                    hostObj["categoryName"] = host.Category != null ? (host.Category.Name ?? string.Empty) : string.Empty;
                    hostObj["categoryBic"] = categoryBic.Length > 0 ? categoryBic : null;
                }
                catch { /* ignore */ }

                bool isColumn = categoryBic.Equals("OST_StructuralColumns", StringComparison.OrdinalIgnoreCase);
                bool isFraming = categoryBic.Equals("OST_StructuralFraming", StringComparison.OrdinalIgnoreCase);
                if (!isColumn && !isFraming)
                {
                    hostObj["ok"] = false;
                    hostObj["code"] = "UNSUPPORTED_CATEGORY";
                    hostObj["msg"] = "Only Structural Columns / Structural Framing are supported in v1.";
                    hostsArr.Add(hostObj);
                    continue;
                }

                bool validHost = false;
                try { validHost = RebarHostData.IsValidHost(host); } catch { validHost = false; }
                hostObj["isValidRebarHost"] = validHost;

                JObject resolved;
                IEnumerable<string> keyList = mappingKeysBase;
                try
                {
                    if (isFraming && opts.beamUseTypeParams)
                    {
                        bool includeBeamKeys = false;
                        try
                        {
                            if (RebarMappingService.TryGetIndex(out var idx))
                            {
                                RebarMappingService.RebarMappingProfile? prof = null;
                                if (!string.IsNullOrWhiteSpace(profile) && idx.TryGetProfile(profile, out var requested))
                                    prof = requested;
                                if (prof == null)
                                    prof = idx.MatchProfileForElement(doc, host) ?? idx.GetDefaultProfile();

                                if (prof != null)
                                {
                                    // Include only if the matched profile defines at least one beam-attribute key.
                                    if (prof.TryGetEntry("Beam.Attr.MainBar.DiameterMm", out var _)
                                        || prof.TryGetEntry("Beam.Attr.MainBar.TopCount", out var _2)
                                        || prof.TryGetEntry("Beam.Attr.Stirrup.DiameterMm", out var _3)
                                        || prof.TryGetEntry("Beam.Attr.Stirrup.PitchMidMm", out var _4))
                                    {
                                        includeBeamKeys = true;
                                    }
                                }
                            }
                        }
                        catch { includeBeamKeys = false; }

                        if (includeBeamKeys)
                            keyList = mappingKeysBase.Concat(mappingKeysBeamAttr);
                    }
                }
                catch { /* ignore */ }

                try { resolved = RebarMappingService.ResolveForElement(doc, host, profile, keyList, opts.includeMappingDebug); }
                catch (Exception ex) { resolved = new JObject { ["ok"] = false, ["code"] = "MAPPING_EXCEPTION", ["msg"] = ex.Message }; }
                hostObj["mapping"] = resolved;

                var values = resolved["values"] as JObject;

                string mainBarTypeName = (opts.mainBarTypeName ?? string.Empty).Trim();
                if (mainBarTypeName.Length == 0 && values != null) mainBarTypeName = (values.Value<string>("Common.MainBar.BarType") ?? string.Empty).Trim();

                string tieBarTypeName = (opts.tieBarTypeName ?? string.Empty).Trim();
                if (tieBarTypeName.Length == 0 && values != null) tieBarTypeName = (values.Value<string>("Common.TieBar.BarType") ?? string.Empty).Trim();

                // Covers:
                // Prefer the host instance's cover settings when available (Revit instance parameters),
                // then allow mapping-table override, finally fall back to a safe default.
                // This avoids "cover not reflected" when the mapping profile does not include cover entries.
                double coverTopMm = 40.0;
                double coverBottomMm = 40.0;
                double coverOtherMm = 40.0;
                string coverTopSource = "default";
                string coverBottomSource = "default";
                string coverOtherSource = "default";

                try
                {
                    double mm;
                    if (TryGetHostCoverMm(doc, host, BuiltInParameter.CLEAR_COVER_TOP, out mm))
                    {
                        coverTopMm = mm;
                        coverTopSource = "hostInstance";
                    }
                    if (TryGetHostCoverMm(doc, host, BuiltInParameter.CLEAR_COVER_BOTTOM, out mm))
                    {
                        coverBottomMm = mm;
                        coverBottomSource = "hostInstance";
                    }
                    if (TryGetHostCoverMm(doc, host, BuiltInParameter.CLEAR_COVER_OTHER, out mm))
                    {
                        coverOtherMm = mm;
                        coverOtherSource = "hostInstance";
                    }
                }
                catch { /* ignore */ }

                try
                {
                    if (values != null)
                    {
                        var v = values.Value<double?>("Host.Cover.Top");
                        if (v.HasValue) { coverTopMm = v.Value; coverTopSource = "mapping"; }
                        v = values.Value<double?>("Host.Cover.Bottom");
                        if (v.HasValue) { coverBottomMm = v.Value; coverBottomSource = "mapping"; }
                        v = values.Value<double?>("Host.Cover.Other");
                        if (v.HasValue) { coverOtherMm = v.Value; coverOtherSource = "mapping"; }
                    }
                }
                catch { /* ignore */ }

                hostObj["coversMm"] = new JObject
                {
                    ["top"] = coverTopMm,
                    ["bottom"] = coverBottomMm,
                    ["other"] = coverOtherMm,
                    ["sources"] = new JObject
                    {
                        ["top"] = coverTopSource,
                        ["bottom"] = coverBottomSource,
                        ["other"] = coverOtherSource
                    }
                };

                // Some families/projects store cover as explicit instance parameters (e.g. "かぶり厚-上/下/左/右").
                // Use them for cross-section placement when present (best-effort).
                double faceUpMm = coverTopMm;
                double faceDownMm = coverBottomMm;
                double faceLeftMm = coverOtherMm;
                double faceRightMm = coverOtherMm;
                string faceUpSource = "coversMm.top";
                string faceDownSource = "coversMm.bottom";
                string faceLeftSource = "coversMm.other";
                string faceRightSource = "coversMm.other";

                try
                {
                    double mm;
                    string hit;
                    var upNames = (opts.coverUpParamNames != null && opts.coverUpParamNames.Length > 0)
                        ? opts.coverUpParamNames
                        : new[] { "かぶり厚-上", "Rebar Cover - Top", "CoverTop", "Cover_Top" };
                    if (TryGetHostCoverParamMm(host, upNames, out mm, out hit) && mm >= 0.0)
                    {
                        faceUpMm = mm;
                        faceUpSource = "instanceParam:" + hit;
                    }
                    var downNames = (opts.coverDownParamNames != null && opts.coverDownParamNames.Length > 0)
                        ? opts.coverDownParamNames
                        : new[] { "かぶり厚-下", "Rebar Cover - Bottom", "CoverBottom", "Cover_Bottom" };
                    if (TryGetHostCoverParamMm(host, downNames, out mm, out hit) && mm >= 0.0)
                    {
                        faceDownMm = mm;
                        faceDownSource = "instanceParam:" + hit;
                    }
                    var leftNames = (opts.coverLeftParamNames != null && opts.coverLeftParamNames.Length > 0)
                        ? opts.coverLeftParamNames
                        : new[] { "かぶり厚-左", "Rebar Cover - Left", "CoverLeft", "Cover_Left" };
                    if (TryGetHostCoverParamMm(host, leftNames, out mm, out hit) && mm >= 0.0)
                    {
                        faceLeftMm = mm;
                        faceLeftSource = "instanceParam:" + hit;
                    }
                    var rightNames = (opts.coverRightParamNames != null && opts.coverRightParamNames.Length > 0)
                        ? opts.coverRightParamNames
                        : new[] { "かぶり厚-右", "Rebar Cover - Right", "CoverRight", "Cover_Right" };
                    if (TryGetHostCoverParamMm(host, rightNames, out mm, out hit) && mm >= 0.0)
                    {
                        faceRightMm = mm;
                        faceRightSource = "instanceParam:" + hit;
                    }
                }
                catch { /* ignore */ }

                double minCoverMm = 40.0;
                try { minCoverMm = Math.Max(0.0, opts.coverMinMm); } catch { minCoverMm = 40.0; }

                // Record raw (before clamp) for audit/debug.
                hostObj["coverFacesMmRaw"] = new JObject
                {
                    ["up"] = faceUpMm,
                    ["down"] = faceDownMm,
                    ["left"] = faceLeftMm,
                    ["right"] = faceRightMm,
                    ["sources"] = new JObject
                    {
                        ["up"] = faceUpSource,
                        ["down"] = faceDownSource,
                        ["left"] = faceLeftSource,
                        ["right"] = faceRightSource
                    }
                };

                bool anyBelowMin = (faceUpMm < minCoverMm) || (faceDownMm < minCoverMm) || (faceLeftMm < minCoverMm) || (faceRightMm < minCoverMm);
                bool anyFaceParamMissing = !faceUpSource.StartsWith("instanceParam:", StringComparison.OrdinalIgnoreCase)
                                           || !faceDownSource.StartsWith("instanceParam:", StringComparison.OrdinalIgnoreCase)
                                           || !faceLeftSource.StartsWith("instanceParam:", StringComparison.OrdinalIgnoreCase)
                                           || !faceRightSource.StartsWith("instanceParam:", StringComparison.OrdinalIgnoreCase);

                bool didClamp = false;
                if (opts.coverClampToMin && minCoverMm > 0.0)
                {
                    if (faceUpMm < minCoverMm) { faceUpMm = minCoverMm; didClamp = true; }
                    if (faceDownMm < minCoverMm) { faceDownMm = minCoverMm; didClamp = true; }
                    if (faceLeftMm < minCoverMm) { faceLeftMm = minCoverMm; didClamp = true; }
                    if (faceRightMm < minCoverMm) { faceRightMm = minCoverMm; didClamp = true; }
                }

                JArray coverCandidateParamNames = null;
                bool hasCoverCandidateParams = false;
                try
                {
                    coverCandidateParamNames = CollectCandidateCoverParamNames(host);
                    hasCoverCandidateParams = coverCandidateParamNames != null && coverCandidateParamNames.Count > 0;
                }
                catch
                {
                    coverCandidateParamNames = null;
                    hasCoverCandidateParams = false;
                }

                hostObj["coverPolicy"] = new JObject
                {
                    ["minCoverMm"] = minCoverMm,
                    ["clampToMin"] = opts.coverClampToMin,
                    ["didClamp"] = didClamp,
                    ["confirmEnabled"] = opts.coverConfirmEnabled,
                    ["confirmProceed"] = opts.coverConfirmProceed,
                    ["anyFaceParamMissing"] = anyFaceParamMissing,
                    ["anyBelowMin"] = anyBelowMin,
                    ["hasCoverCandidateParams"] = hasCoverCandidateParams
                };

                bool needsCoverConfirmation = opts.coverConfirmEnabled
                                            && !opts.coverConfirmProceed
                                            && (anyBelowMin || (hasCoverCandidateParams && anyFaceParamMissing));

                if (needsCoverConfirmation)
                {
                    coverConfirmRequiredAny = true;
                    hostObj["ok"] = false;
                    hostObj["code"] = "COVER_CONFIRMATION_REQUIRED";
                    hostObj["msg"] = "被り厚さの読み取りが確定できません（または最小値未満）。どのパラメータを読むか／最小値(minCoverMm)へ丸めてよいかを確認してください。";
                    hostObj["coverCandidateParamNames"] = coverCandidateParamNames ?? new JArray();
                    hostObj["coverFacesMmProposed"] = new JObject
                    {
                        ["up"] = faceUpMm,
                        ["down"] = faceDownMm,
                        ["left"] = faceLeftMm,
                        ["right"] = faceRightMm,
                        ["sources"] = new JObject
                        {
                            ["up"] = faceUpSource,
                            ["down"] = faceDownSource,
                            ["left"] = faceLeftSource,
                            ["right"] = faceRightSource
                        }
                    };
                    hostObj["nextActions"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "confirm_cover_policy",
                            ["prompt"] = "配筋用の被り厚さの読み取りルールを確定してください。",
                            ["minCoverMm"] = minCoverMm,
                            ["suggestedOptions"] = new JObject
                            {
                                ["coverConfirmProceed"] = true,
                                ["coverMinMm"] = minCoverMm,
                                ["coverClampToMin"] = true
                            }
                        }
                    };
                    hostsArr.Add(hostObj);
                    continue;
                }

                hostObj["coverFacesMm"] = new JObject
                {
                    ["up"] = faceUpMm,
                    ["down"] = faceDownMm,
                    ["left"] = faceLeftMm,
                    ["right"] = faceRightMm,
                    ["sources"] = new JObject
                    {
                        ["up"] = faceUpSource,
                        ["down"] = faceDownSource,
                        ["left"] = faceLeftSource,
                        ["right"] = faceRightSource
                    }
                };

                // Beam attribute spec (via mapping table) – best-effort
                BeamMappedRebarSpec beamSpec = null;
                try
                {
                    if (isFraming && opts.beamUseTypeParams && values != null)
                    {
                        if (TryGetBeamMappedRebarSpec(values, out var bs, out var bsDbg))
                        {
                            beamSpec = bs;
                            if (bsDbg != null)
                            {
                                hostObj["beamTypeRebar"] = bsDbg;   // backward-compatible key
                                hostObj["beamMappedRebar"] = bsDbg; // preferred key
                            }
                        }
                    }
                }
                catch { /* ignore */ }

                // Column attribute spec (via mapping table) - recommended (BIM-driven)
                ColumnMappedRebarSpec colSpec = null;
                try
                {
                    if (isColumn && values != null)
                    {
                        ColumnMappedRebarSpec cs;
                        JObject csDbg;
                        if (TryGetColumnMappedRebarSpec(values, out cs, out csDbg))
                        {
                            colSpec = cs;
                            if (csDbg != null) hostObj["columnMappedRebar"] = csDbg;
                        }
                    }
                }
                catch { /* ignore */ }

                int columnMainBarsPerFaceEffective = Math.Max(2, opts.columnMainBarsPerFace);
                string columnMainBarsPerFaceSource = "options";
                try
                {
                    if (isColumn && colSpec != null && colSpec.mainBarsPerFace.HasValue && colSpec.mainBarsPerFace.Value >= 2)
                    {
                        columnMainBarsPerFaceEffective = colSpec.mainBarsPerFace.Value;
                        columnMainBarsPerFaceSource = "mapping";
                    }
                }
                catch { /* ignore */ }
                if (isColumn)
                {
                    hostObj["columnMainBarsPerFace"] = new JObject
                    {
                        ["value"] = columnMainBarsPerFaceEffective,
                        ["source"] = columnMainBarsPerFaceSource
                    };
                }

                // If beamSpec provides bar types and user did not override, prefer it.
                bool mainExplicit = !string.IsNullOrWhiteSpace(opts.mainBarTypeName);
                bool tieExplicit = !string.IsNullOrWhiteSpace(opts.tieBarTypeName);
                if (beamSpec != null)
                {
                    try
                    {
                        if (!mainExplicit && beamSpec.mainDiaMm.HasValue && beamSpec.mainDiaMm.Value > 0)
                        {
                            if (TryFindBarTypeByDiameterMm(doc, beamSpec.mainDiaMm.Value, out var bt))
                            {
                                mainBarTypeName = (bt.Name ?? string.Empty).Trim();
                            }
                            else
                            {
                                var cand = "D" + beamSpec.mainDiaMm.Value;
                                if (TryFindBarTypeByName(doc, cand, out _)) mainBarTypeName = cand;
                            }
                        }

                        if (!tieExplicit && beamSpec.stirrupDiaMm.HasValue && beamSpec.stirrupDiaMm.Value > 0)
                        {
                            if (TryFindBarTypeByDiameterMm(doc, beamSpec.stirrupDiaMm.Value, out var bt))
                            {
                                tieBarTypeName = (bt.Name ?? string.Empty).Trim();
                            }
                            else
                            {
                                var sym = string.IsNullOrWhiteSpace(beamSpec.stirrupSymbol) ? "D" : beamSpec.stirrupSymbol.Trim();
                                var cand = sym + beamSpec.stirrupDiaMm.Value;
                                if (TryFindBarTypeByName(doc, cand, out _)) tieBarTypeName = cand;
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                if (opts.includeMainBars && string.IsNullOrWhiteSpace(mainBarTypeName))
                {
                    hostObj["ok"] = false;
                    hostObj["code"] = "MISSING_MAIN_BAR_TYPE";
                    hostObj["msg"] = "Main bar type name is empty (mapping + override).";
                    hostsArr.Add(hostObj);
                    continue;
                }
                if ((opts.includeTies || opts.includeStirrups) && string.IsNullOrWhiteSpace(tieBarTypeName))
                {
                    hostObj["ok"] = false;
                    hostObj["code"] = "MISSING_TIE_BAR_TYPE";
                    hostObj["msg"] = "Tie/Stirrup bar type name is empty (mapping + override).";
                    hostsArr.Add(hostObj);
                    continue;
                }

                Autodesk.Revit.DB.Structure.RebarBarType mainBarType = null;
                Autodesk.Revit.DB.Structure.RebarBarType tieBarType = null;

                if (opts.includeMainBars)
                {
                    var reqName = mainBarTypeName;
                    var resolvedBy = "name";
                    int diaMm = 0;

                    if (!TryFindBarTypeByName(doc, mainBarTypeName, out mainBarType))
                    {
                        resolvedBy = "diameter";
                        if (TryParseDiameterMmFromBarTypeText(mainBarTypeName, out diaMm)
                            && TryFindBarTypeByDiameterMm(doc, diaMm, out mainBarType))
                        {
                            mainBarTypeName = (mainBarType.Name ?? string.Empty).Trim();
                        }
                        else
                        {
                            hostObj["ok"] = false;
                            hostObj["code"] = "BAR_TYPE_NOT_FOUND";
                            hostObj["msg"] = "Main bar type not found: " + mainBarTypeName;
                            hostsArr.Add(hostObj);
                            continue;
                        }
                    }

                    hostObj["mainBarTypeResolve"] = new JObject
                    {
                        ["requested"] = reqName,
                        ["resolved"] = mainBarTypeName,
                        ["resolvedBy"] = resolvedBy,
                        ["diameterMm"] = diaMm > 0 ? (JToken)diaMm : JValue.CreateNull()
                    };
                }
                if (opts.includeTies || opts.includeStirrups)
                {
                    var reqName = tieBarTypeName;
                    var resolvedBy = "name";
                    int diaMm = 0;

                    if (!TryFindBarTypeByName(doc, tieBarTypeName, out tieBarType))
                    {
                        resolvedBy = "diameter";
                        if (TryParseDiameterMmFromBarTypeText(tieBarTypeName, out diaMm)
                            && TryFindBarTypeByDiameterMm(doc, diaMm, out tieBarType))
                        {
                            tieBarTypeName = (tieBarType.Name ?? string.Empty).Trim();
                        }
                        else
                        {
                            hostObj["ok"] = false;
                            hostObj["code"] = "BAR_TYPE_NOT_FOUND";
                            hostObj["msg"] = "Tie/Stirrup bar type not found: " + tieBarTypeName;
                            hostsArr.Add(hostObj);
                            continue;
                        }
                    }

                    hostObj["tieBarTypeResolve"] = new JObject
                    {
                        ["requested"] = reqName,
                        ["resolved"] = tieBarTypeName,
                        ["resolvedBy"] = resolvedBy,
                        ["diameterMm"] = diaMm > 0 ? (JToken)diaMm : JValue.CreateNull()
                    };
                }

                if (!TryGetHostTransform(host, out var tr) || tr == null) tr = Transform.Identity;
                if (!TryGetLocalBox(host, tr, out var localBox, out var bboxMsg))
                {
                    hostObj["ok"] = false;
                    hostObj["code"] = "NO_BBOX";
                    hostObj["msg"] = bboxMsg;
                    hostsArr.Add(hostObj);
                    continue;
                }

                // Axis selection
                var inv = tr.Inverse;
                var axisGuessWorld = GuessHostAxisDirection(host, tr);
                var axisGuessLocal = inv.OfVector(axisGuessWorld);
                int axisIndex = AxisIndexFromLocalVector(axisGuessLocal);

                var crossIdx = new List<int> { 0, 1, 2 };
                crossIdx.Remove(axisIndex);
                int crossA = crossIdx[0];
                int crossB = crossIdx[1];

                int upIndex = crossA;
                int sideIndex = crossB;
                try
                {
                    var bA = GetBasisVectorByIndex(tr, crossA).Normalize();
                    var bB = GetBasisVectorByIndex(tr, crossB).Normalize();
                    double da = Math.Abs(bA.DotProduct(XYZ.BasisZ));
                    double db = Math.Abs(bB.DotProduct(XYZ.BasisZ));
                    if (db > da) { upIndex = crossB; sideIndex = crossA; }
                }
                catch { /* ignore */ }

                hostObj["axisIndex"] = axisIndex;
                hostObj["upIndex"] = upIndex;
                hostObj["sideIndex"] = sideIndex;

                var actions = new JArray();
                hostObj["actions"] = actions;

                double coverTopFt = UnitHelper.MmToFt(coverTopMm);
                double coverBottomFt = UnitHelper.MmToFt(coverBottomMm);
                double coverOtherFt = UnitHelper.MmToFt(coverOtherMm);
                double faceUpFt = UnitHelper.MmToFt(faceUpMm);
                double faceDownFt = UnitHelper.MmToFt(faceDownMm);
                double faceLeftFt = UnitHelper.MmToFt(faceLeftMm);
                double faceRightFt = UnitHelper.MmToFt(faceRightMm);

                var axisBasis = GetBasisVectorByIndex(tr, axisIndex);
                if (axisBasis.GetLength() < 1e-9) axisBasis = XYZ.BasisZ;

                // Main bars (4 corners)
                 if (opts.includeMainBars)
                 {
                     double r = mainBarType.BarModelDiameter / 2.0;
 
                     double baseMin = GetMinByIndex(localBox, axisIndex);
                     double baseMax = GetMaxByIndex(localBox, axisIndex);
                     if (isFraming)
                     {
                        // Keep the raw bbox range for debugging.
                        try
                        {
                            hostObj["beamAxisRangeBboxMm_raw"] = new JObject
                            {
                                ["min"] = UnitHelper.FtToMm(baseMin),
                                ["max"] = UnitHelper.FtToMm(baseMax),
                                ["length"] = UnitHelper.FtToMm(baseMax - baseMin)
                            };
                        }
                        catch { /* ignore */ }

                        // IMPORTANT: For beams, LocationCurve endpoints may be at the support centerline,
                        // while the physical geometry is cut back to the support face. Using bbox extents
                        // keeps rebar start/end aligned to the actual concrete faces (typical RC detailing).
                        hostObj["beamAxisSource"] = "bbox";

                        try
                        {
                            if (TryGetBeamAxisRangeFromLocationCurve(host, tr, axisIndex, out var lcMin, out var lcMax))
                            {
                                hostObj["beamAxisRangeLocationCurveMm"] = new JObject
                                {
                                    ["min"] = UnitHelper.FtToMm(lcMin),
                                    ["max"] = UnitHelper.FtToMm(lcMax),
                                    ["length"] = UnitHelper.FtToMm(lcMax - lcMin)
                                };
                            }
                        }
                        catch { /* ignore */ }

                        // Prefer solid-geometry-derived extents when available (captures cutbacks at supports).
                        try
                        {
                            if (TryGetHostAxisRangeFromSolidGeometry(host, tr, axisIndex, out var gMin, out var gMax, out var gMsg) && (gMax > gMin))
                            {
                                baseMin = gMin;
                                baseMax = gMax;
                                hostObj["beamAxisRangeFrom"] = "solidGeometry";
                                hostObj["beamAxisRangeSolidMm"] = new JObject
                                {
                                    ["min"] = UnitHelper.FtToMm(gMin),
                                    ["max"] = UnitHelper.FtToMm(gMax),
                                    ["length"] = UnitHelper.FtToMm(gMax - gMin)
                                };
                            }
                            else
                            {
                                hostObj["beamAxisRangeFrom"] = "bbox";
                                if (!string.IsNullOrWhiteSpace(gMsg)) hostObj["beamAxisRangeSolidError"] = gMsg;
                            }
                        }
                        catch { /* ignore */ }

                        // Final (used) axis range
                        try
                        {
                            hostObj["beamAxisRangeUsedMm"] = new JObject
                            {
                                ["min"] = UnitHelper.FtToMm(baseMin),
                                ["max"] = UnitHelper.FtToMm(baseMax),
                                ["length"] = UnitHelper.FtToMm(baseMax - baseMin)
                            };
                        }
                        catch { /* ignore */ }
                     }
 
                    double axisStart = baseMin + (isColumn ? (coverBottomFt + r) : (coverOtherFt + r));
                    double axisEnd = baseMax - (isColumn ? (coverTopFt + r) : (coverOtherFt + r));

                    // Beam: embed main bars into support columns by ratio of column width (along beam axis).
                    if (isFraming)
                    {
                        // Along-beam cover does not make sense for typical RC detailing; align to physical end faces.
                        axisStart = baseMin;
                        axisEnd = baseMax;
                        try
                        {
                            double ratio = opts.beamMainBarEmbedIntoSupportColumnRatio;
                            if (opts.beamMainBarEmbedIntoSupportColumns && ratio > 0.0)
                            {
                                if (TryGetBeamSupportColumnWidthsAlongAxis(doc, host, tr, localBox, axisIndex, sideIndex, upIndex, baseMin, baseMax,
                                        UnitHelper.MmToFt(opts.beamSupportSearchRangeMm),
                                        UnitHelper.MmToFt(opts.beamSupportFaceToleranceMm),
                                        out var startColWidthFt, out var endColWidthFt, out var dbg))
                                {
                                    double embedStartFt = startColWidthFt > 0.0 ? (startColWidthFt * ratio) : 0.0;
                                    double embedEndFt = endColWidthFt > 0.0 ? (endColWidthFt * ratio) : 0.0;
                                    axisStart -= embedStartFt;
                                    axisEnd += embedEndFt;
                                    hostObj["beamMainBarEmbed"] = new JObject
                                    {
                                        ["enabled"] = true,
                                        ["ratio"] = ratio,
                                        ["startColumnWidthMm"] = startColWidthFt > 0.0 ? UnitHelper.FtToMm(startColWidthFt) : (double?)null,
                                        ["endColumnWidthMm"] = endColWidthFt > 0.0 ? UnitHelper.FtToMm(endColWidthFt) : (double?)null,
                                        ["startEmbedMm"] = embedStartFt > 0.0 ? UnitHelper.FtToMm(embedStartFt) : (double?)null,
                                        ["endEmbedMm"] = embedEndFt > 0.0 ? UnitHelper.FtToMm(embedEndFt) : (double?)null,
                                        ["debug"] = dbg
                                    };
                                }
                                else
                                {
                                    hostObj["beamMainBarEmbed"] = new JObject
                                    {
                                        ["enabled"] = true,
                                        ["ratio"] = ratio,
                                        ["note"] = "Support columns not found; embed skipped."
                                    };
                                }
                            }
                            else
                            {
                                hostObj["beamMainBarEmbed"] = new JObject
                                {
                                    ["enabled"] = false,
                                    ["ratio"] = ratio
                                };
                            }
                        }
                        catch { /* ignore */ }
                    }
                    else if (isColumn)
                    {
                        // Column main bars: allow end extensions (mm).
                        try
                        {
                            double extBot = UnitHelper.MmToFt(opts.columnMainBarBottomExtensionMm);
                            double extTop = UnitHelper.MmToFt(opts.columnMainBarTopExtensionMm);
                            axisStart -= extBot;
                            axisEnd += extTop;
                            hostObj["columnMainBarAxisExtensionMm"] = new JObject
                            {
                                ["bottom"] = opts.columnMainBarBottomExtensionMm,
                                ["top"] = opts.columnMainBarTopExtensionMm
                            };
                        }
                        catch { /* ignore */ }
                    }

                    // Beam: optional extension/trim (positive extends beyond bbox; negative shortens)
                    if (isFraming)
                    {
                        try
                        {
                            double extStartFt = UnitHelper.MmToFt(opts.beamMainBarStartExtensionMm);
                            double extEndFt = UnitHelper.MmToFt(opts.beamMainBarEndExtensionMm);
                            axisStart -= extStartFt;
                            axisEnd += extEndFt;
                            hostObj["beamMainBarAxisExtensionMm"] = new JObject
                            {
                                ["start"] = opts.beamMainBarStartExtensionMm,
                                ["end"] = opts.beamMainBarEndExtensionMm
                            };
                        }
                        catch { /* ignore */ }
                    }

                    if (!(axisStart < axisEnd))
                    {
                        hostObj["ok"] = false;
                        hostObj["code"] = "NO_AXIS_LENGTH";
                        hostObj["msg"] = "Computed main-bar axis range is invalid (covers too large?).";
                        hostsArr.Add(hostObj);
                        continue;
                    }

                    if (isFraming)
                    {
                        int sIdx = sideIndex;
                        int uIdx = upIndex;
                        double sMin = GetMinByIndex(localBox, sIdx);
                        double sMax = GetMaxByIndex(localBox, sIdx);
                        double uMin = GetMinByIndex(localBox, uIdx);
                        double uMax = GetMaxByIndex(localBox, uIdx);
                        double left = sMin + faceLeftFt + r;
                        double right = sMax - faceRightFt - r;
                        double bottom = uMin + faceDownFt + r;
                        double top = uMax - faceUpFt - r;

                        var upVec = GetBasisVectorByIndex(tr, uIdx);
                        if (upVec.GetLength() < 1e-9) upVec = XYZ.BasisZ;
                        upVec = upVec.Normalize();

                        var axisDir = axisBasis.Normalize();
                        var planeNrm = axisDir.CrossProduct(upVec);
                        if (planeNrm.GetLength() < 1e-9)
                            planeNrm = GetBasisVectorByIndex(tr, sIdx);
                        if (planeNrm.GetLength() < 1e-9)
                            planeNrm = XYZ.BasisX;
                        planeNrm = planeNrm.Normalize();

                        if (!(left < right && bottom < top))
                        {
                            hostObj["ok"] = false;
                            hostObj["code"] = "INVALID_MAIN_GEOMETRY";
                            hostObj["msg"] = "Computed main-bar rectangle is invalid (covers too large?).";
                            hostsArr.Add(hostObj);
                            continue;
                        }

                        // Beam main-bar counts: user overrides -> mapping -> default.
                        // Supports up to 3 layers per side (TopCount, TopCount2, TopCount3, ...).
                        int topCount = 2;
                        int topCount2 = 0;
                        int topCount3 = 0;
                        int bottomCount = 2;
                        int bottomCount2 = 0;
                        int bottomCount3 = 0;
                        string countSource = "default";
                        if (opts.beamMainTopCount.HasValue || opts.beamMainBottomCount.HasValue)
                        {
                            // options override => treat as single-layer counts (v1 behavior).
                            topCount = Math.Max(0, opts.beamMainTopCount ?? topCount);
                            bottomCount = Math.Max(0, opts.beamMainBottomCount ?? bottomCount);
                            topCount2 = 0;
                            topCount3 = 0;
                            bottomCount2 = 0;
                            bottomCount3 = 0;
                            countSource = "options";
                        }
                        else if (beamSpec != null && (beamSpec.hasMainCounts || beamSpec.hasLayerCounts))
                        {
                            if (beamSpec.topCount.HasValue) topCount = Math.Max(0, beamSpec.topCount.Value);
                            if (beamSpec.bottomCount.HasValue) bottomCount = Math.Max(0, beamSpec.bottomCount.Value);
                            if (beamSpec.topCount2.HasValue) topCount2 = Math.Max(0, beamSpec.topCount2.Value);
                            if (beamSpec.topCount3.HasValue) topCount3 = Math.Max(0, beamSpec.topCount3.Value);
                            if (beamSpec.bottomCount2.HasValue) bottomCount2 = Math.Max(0, beamSpec.bottomCount2.Value);
                            if (beamSpec.bottomCount3.HasValue) bottomCount3 = Math.Max(0, beamSpec.bottomCount3.Value);
                            countSource = (topCount2 > 0 || topCount3 > 0 || bottomCount2 > 0 || bottomCount3 > 0) ? "mapping_layers" : "mapping";
                        }

                        hostObj["beamMainCounts"] = new JObject
                        {
                            ["source"] = countSource,
                            ["top"] = topCount,
                            ["top2"] = topCount2,
                            ["top3"] = topCount3,
                            ["bottom"] = bottomCount,
                            ["bottom2"] = bottomCount2,
                            ["bottom3"] = bottomCount3,
                            ["total"] = topCount + topCount2 + topCount3 + bottomCount + bottomCount2 + bottomCount3
                        };

                        int idx = 0;
                        void AddBarsAtU(double u, int count)
                        {
                            if (count <= 0) return;

                            double startBendFt = 0.0;
                            double endBendFt = 0.0;
                            double startDir = 0.0;
                            double endDir = 0.0;
                            try
                            {
                                startBendFt = UnitHelper.MmToFt(Math.Max(0.0, isColumn ? opts.columnMainBarStartBendLengthMm : opts.beamMainBarStartBendLengthMm));
                                endBendFt = UnitHelper.MmToFt(Math.Max(0.0, isColumn ? opts.columnMainBarEndBendLengthMm : opts.beamMainBarEndBendLengthMm));

                                var sDir = isColumn ? opts.columnMainBarStartBendDir : opts.beamMainBarStartBendDir;
                                var eDir = isColumn ? opts.columnMainBarEndBendDir : opts.beamMainBarEndBendDir;
                                if (sDir == "up") startDir = 1.0;
                                else if (sDir == "down") startDir = -1.0;
                                if (eDir == "up") endDir = 1.0;
                                else if (eDir == "down") endDir = -1.0;
                            }
                            catch { /* ignore */ }

                            for (int i = 0; i < count; i++)
                            {
                                double t = (count == 1) ? 0.5 : ((double)i / (count - 1));
                                double s = left + (right - left) * t;

                                var p0 = MakeLocalPoint(axisIndex, axisStart, sIdx, s, uIdx, u);
                                var p1 = MakeLocalPoint(axisIndex, axisEnd, sIdx, s, uIdx, u);
                                var w0 = tr.OfPoint(p0);
                                var w1 = tr.OfPoint(p1);

                                var segs = new List<Curve>();
                                if (startBendFt > 1e-9 && Math.Abs(startDir) > 0.5)
                                {
                                    var tip = w0.Add(upVec.Multiply(startDir * startBendFt));
                                    segs.Add(Line.CreateBound(tip, w0));
                                }
                                segs.Add(Line.CreateBound(w0, w1));
                                if (endBendFt > 1e-9 && Math.Abs(endDir) > 0.5)
                                {
                                    var tip = w1.Add(upVec.Multiply(endDir * endBendFt));
                                    segs.Add(Line.CreateBound(w1, tip));
                                }

                                var jCurves = new JArray();
                                foreach (var c in segs)
                                    jCurves.Add(GeometryJsonHelper.CurveToJson(c));

                                actions.Add(new JObject
                                {
                                    ["role"] = "beam_main_bar",
                                    ["index"] = idx++,
                                    ["style"] = "Standard",
                                    ["barTypeName"] = mainBarTypeName,
                                    ["curves"] = jCurves,
                                    ["normal"] = GeometryJsonHelper.VectorToJson(planeNrm),
                                    ["tagComments"] = opts.tagComments
                                });
                            }
                        }

                        // Layer pitch (center-to-center). Prefer clearance table; fallback: barDia + clear.
                        double barDiaMm = 0.0;
                        try { barDiaMm = UnitHelper.FtToMm(mainBarType.BarModelDiameter); } catch { barDiaMm = 0.0; }
                        int barDiaKey = 0;
                        try { barDiaKey = (int)Math.Round(barDiaMm); } catch { barDiaKey = 0; }

                        double requiredCcMm = 0.0;
                        string requiredCcSource = "fallback_dia_plus_clear";
                        try
                        {
                            if (barDiaKey > 0 && RevitMCPAddin.Core.Rebar.RebarBarClearanceTableService.TryGetCenterToCenterMm(barDiaKey, out var cc))
                            {
                                requiredCcMm = cc;
                                requiredCcSource = "table";
                            }
                        }
                        catch { /* ignore */ }
                        if (!(requiredCcMm > 0.0))
                        {
                            requiredCcMm = Math.Max(0.0, barDiaMm) + Math.Max(0.0, opts.beamMainBarLayerClearMm);
                            requiredCcSource = "fallback_dia_plus_clear";
                        }

                        double layerPitchMm = requiredCcMm;
                        double layerPitchFt = layerPitchMm > 0.0 ? UnitHelper.MmToFt(layerPitchMm) : 0.0;
                        hostObj["beamMainLayerPitchMm"] = new JObject
                        {
                            ["barDiaMm"] = barDiaMm,
                            ["barDiaKeyMm"] = barDiaKey,
                            ["requiredCcMm"] = requiredCcMm,
                            ["requiredCcSource"] = requiredCcSource,
                            ["fallbackClearMm"] = opts.beamMainBarLayerClearMm,
                            ["pitchMm"] = layerPitchMm
                        };

                        void AddLayerSafe(string side, int layerIndex, double uCoord, int count)
                        {
                            if (count <= 0) return;
                            if (uCoord < bottom || uCoord > top)
                            {
                                var skips = hostObj["beamMainLayerSkips"] as JArray;
                                if (skips == null) { skips = new JArray(); hostObj["beamMainLayerSkips"] = skips; }
                                skips.Add(new JObject
                                {
                                    ["side"] = side,
                                    ["layerIndex"] = layerIndex,
                                    ["count"] = count,
                                    ["reason"] = "uCoord outside usable [bottom,top] range"
                                });
                                return;
                            }
                            AddBarsAtU(uCoord, count);
                        }

                        // Spacing checks (plan-time, based on the same local box / covers used for placement).
                        try
                        {
                            double RequiredCcMmForMain()
                            {
                                if (requiredCcMm > 0.0) return requiredCcMm;
                                return Math.Max(0.0, barDiaMm) + Math.Max(0.0, opts.beamMainBarLayerClearMm);
                            }

                            double WithinLayerSpacingMm(int count)
                            {
                                if (count <= 1) return 0.0;
                                double spanFt = right - left;
                                if (spanFt <= 1e-9) return 0.0;
                                return UnitHelper.FtToMm(spanFt / (count - 1));
                            }

                            var req = RequiredCcMmForMain();
                            var checks = new JObject
                            {
                                ["requiredCcMm"] = req,
                                ["requiredCcSource"] = requiredCcSource
                            };

                            var layers = new JArray();
                            void AddLayerCheck(string side, int layerIndex, int count)
                            {
                                if (count <= 0) return;
                                var sp = WithinLayerSpacingMm(count);
                                layers.Add(new JObject
                                {
                                    ["side"] = side,
                                    ["layerIndex"] = layerIndex,
                                    ["count"] = count,
                                    ["withinLayerSpacingMm"] = sp > 0.0 ? Math.Round(sp, 3) : 0.0,
                                    ["ok"] = (count <= 1) ? true : (sp + 1e-6 >= req)
                                });
                            }

                            AddLayerCheck("top", 1, topCount);
                            AddLayerCheck("top", 2, topCount2);
                            AddLayerCheck("top", 3, topCount3);
                            AddLayerCheck("bottom", 1, bottomCount);
                            AddLayerCheck("bottom", 2, bottomCount2);
                            AddLayerCheck("bottom", 3, bottomCount3);

                            checks["layers"] = layers;

                            // Between-layer pitch check (only when multiple layers exist on a side).
                            bool multiTop = (topCount2 > 0 || topCount3 > 0);
                            bool multiBottom = (bottomCount2 > 0 || bottomCount3 > 0);
                            checks["betweenLayersPitchMm"] = new JObject
                            {
                                ["pitchMm"] = Math.Round(layerPitchMm, 3),
                                ["ok"] = !(req > 0.0) ? true : (layerPitchMm + 1e-6 >= req),
                                ["multiTop"] = multiTop,
                                ["multiBottom"] = multiBottom
                            };

                            hostObj["beamMainBarClearanceCheck"] = checks;
                        }
                        catch { /* ignore */ }

                        // Top layers (downwards)
                        AddLayerSafe("top", 1, top, topCount);
                        if (layerPitchFt > 1e-9)
                        {
                            AddLayerSafe("top", 2, top - layerPitchFt, topCount2);
                            AddLayerSafe("top", 3, top - (layerPitchFt * 2.0), topCount3);
                        }
                        else
                        {
                            if (topCount2 > 0 || topCount3 > 0)
                            {
                                var skips = hostObj["beamMainLayerSkips"] as JArray;
                                if (skips == null) { skips = new JArray(); hostObj["beamMainLayerSkips"] = skips; }
                                if (topCount2 > 0) skips.Add(new JObject { ["side"] = "top", ["layerIndex"] = 2, ["count"] = topCount2, ["reason"] = "layerPitchMm<=0" });
                                if (topCount3 > 0) skips.Add(new JObject { ["side"] = "top", ["layerIndex"] = 3, ["count"] = topCount3, ["reason"] = "layerPitchMm<=0" });
                            }
                        }

                        // Bottom layers (upwards)
                        AddLayerSafe("bottom", 1, bottom, bottomCount);
                        if (layerPitchFt > 1e-9)
                        {
                            AddLayerSafe("bottom", 2, bottom + layerPitchFt, bottomCount2);
                            AddLayerSafe("bottom", 3, bottom + (layerPitchFt * 2.0), bottomCount3);
                        }
                        else
                        {
                            if (bottomCount2 > 0 || bottomCount3 > 0)
                            {
                                var skips = hostObj["beamMainLayerSkips"] as JArray;
                                if (skips == null) { skips = new JArray(); hostObj["beamMainLayerSkips"] = skips; }
                                if (bottomCount2 > 0) skips.Add(new JObject { ["side"] = "bottom", ["layerIndex"] = 2, ["count"] = bottomCount2, ["reason"] = "layerPitchMm<=0" });
                                if (bottomCount3 > 0) skips.Add(new JObject { ["side"] = "bottom", ["layerIndex"] = 3, ["count"] = bottomCount3, ["reason"] = "layerPitchMm<=0" });
                            }
                        }
                    }
                    else
                    {
                        double aMin = GetMinByIndex(localBox, crossA);
                        double aMax = GetMaxByIndex(localBox, crossA);
                        double bMin = GetMinByIndex(localBox, crossB);
                        double bMax = GetMaxByIndex(localBox, crossB);
                        double ax1 = aMin + faceLeftFt + r;
                        double ax2 = aMax - faceRightFt - r;
                        double bx1 = bMin + faceDownFt + r;
                        double bx2 = bMax - faceUpFt - r;

                        var nrm = GetBasisVectorByIndex(tr, crossA);
                        if (nrm.GetLength() < 1e-9) nrm = XYZ.BasisX;

                        int perFace = 2;
                        try { perFace = Math.Max(2, columnMainBarsPerFaceEffective); } catch { perFace = 2; }

                        double[] Linspace(double start, double end, int count)
                        {
                            if (count <= 1) return new[] { start };
                            var arr = new double[count];
                            double step = (end - start) / (count - 1);
                            for (int i = 0; i < count; i++) arr[i] = start + step * i;
                            return arr;
                        }

                        var aPositions = Linspace(ax1, ax2, perFace);
                        var bPositions = Linspace(bx1, bx2, perFace);

                        // Perimeter points per face; dedupe corners (rectangular columns).
                        var pts = new List<Tuple<double, double>>();
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        string Key(double a, double b)
                        {
                            // Local ft coords are small; this tolerance is purely for corner de-duplication.
                            long ka = (long)Math.Round(a * 1_000_000.0);
                            long kb = (long)Math.Round(b * 1_000_000.0);
                            return ka.ToString() + "_" + kb.ToString();
                        }

                        foreach (var b in bPositions)
                        {
                            var k1 = Key(ax1, b);
                            if (seen.Add(k1)) pts.Add(Tuple.Create(ax1, b));
                            var k2 = Key(ax2, b);
                            if (seen.Add(k2)) pts.Add(Tuple.Create(ax2, b));
                        }
                        foreach (var a in aPositions)
                        {
                            var k1 = Key(a, bx1);
                            if (seen.Add(k1)) pts.Add(Tuple.Create(a, bx1));
                            var k2 = Key(a, bx2);
                            if (seen.Add(k2)) pts.Add(Tuple.Create(a, bx2));
                        }

                        int idx = 0;
                        foreach (var pt in pts)
                        {
                            var p0 = MakeLocalPoint(axisIndex, axisStart, crossA, pt.Item1, crossB, pt.Item2);
                            var p1 = MakeLocalPoint(axisIndex, axisEnd, crossA, pt.Item1, crossB, pt.Item2);
                            var w0 = tr.OfPoint(p0);
                            var w1 = tr.OfPoint(p1);
                            actions.Add(new JObject
                            {
                                ["role"] = "column_main_bar",
                                ["index"] = idx++,
                                ["style"] = "Standard",
                                ["barTypeName"] = mainBarTypeName,
                                ["curves"] = new JArray { GeometryJsonHelper.CurveToJson(Line.CreateBound(w0, w1)) },
                                ["normal"] = GeometryJsonHelper.VectorToJson(nrm.Normalize()),
                                ["tagComments"] = opts.tagComments
                            });
                        }
                    }
                }

                // Ties/Stirrups set
                bool wantTies = isColumn ? opts.includeTies : opts.includeStirrups;
                if (isFraming && wantTies && beamSpec != null && beamSpec.hasPitchParams && !(beamSpec.pitchEffectiveMm > 0.0))
                {
                    // When the beam type defines pitch params but they are all 0, interpret it as "no stirrups".
                    wantTies = false;
                    hostObj["beamStirrups"] = new JObject
                    {
                        ["source"] = "mapping",
                        ["skipped"] = true,
                        ["reason"] = "pitchEffectiveMm<=0 (mapping pitch keys present but no valid pitch)"
                    };
                }
                if (wantTies)
                {
                    double r = tieBarType.BarModelDiameter / 2.0;
 
                     double baseMin = GetMinByIndex(localBox, axisIndex);
                     double baseMax = GetMaxByIndex(localBox, axisIndex);
                     if (isFraming)
                     {
                        try
                        {
                            hostObj["beamAxisRangeBboxMm_raw_stirrups"] = new JObject
                            {
                                ["min"] = UnitHelper.FtToMm(baseMin),
                                ["max"] = UnitHelper.FtToMm(baseMax),
                                ["length"] = UnitHelper.FtToMm(baseMax - baseMin)
                            };
                        }
                        catch { /* ignore */ }

                        hostObj["beamAxisSourceStirrups"] = "bbox";

                        try
                        {
                            if (TryGetBeamAxisRangeFromLocationCurve(host, tr, axisIndex, out var lcMin, out var lcMax))
                            {
                                hostObj["beamAxisRangeLocationCurveMm_stirrups"] = new JObject
                                {
                                    ["min"] = UnitHelper.FtToMm(lcMin),
                                    ["max"] = UnitHelper.FtToMm(lcMax),
                                    ["length"] = UnitHelper.FtToMm(lcMax - lcMin)
                                };
                            }
                        }
                        catch { /* ignore */ }

                        try
                        {
                            if (TryGetHostAxisRangeFromSolidGeometry(host, tr, axisIndex, out var gMin, out var gMax, out var gMsg) && (gMax > gMin))
                            {
                                baseMin = gMin;
                                baseMax = gMax;
                                hostObj["beamAxisRangeFrom_stirrups"] = "solidGeometry";
                                hostObj["beamAxisRangeSolidMm_stirrups"] = new JObject
                                {
                                    ["min"] = UnitHelper.FtToMm(gMin),
                                    ["max"] = UnitHelper.FtToMm(gMax),
                                    ["length"] = UnitHelper.FtToMm(gMax - gMin)
                                };
                            }
                            else
                            {
                                hostObj["beamAxisRangeFrom_stirrups"] = "bbox";
                                if (!string.IsNullOrWhiteSpace(gMsg)) hostObj["beamAxisRangeSolidError_stirrups"] = gMsg;
                            }
                        }
                        catch { /* ignore */ }

                        try
                        {
                            hostObj["beamAxisRangeUsedMm_stirrups"] = new JObject
                            {
                                ["min"] = UnitHelper.FtToMm(baseMin),
                                ["max"] = UnitHelper.FtToMm(baseMax),
                                ["length"] = UnitHelper.FtToMm(baseMax - baseMin)
                            };
                        }
                        catch { /* ignore */ }
 
                         // Best-effort: move start/end closer to connected column faces (support faces),
                         // so the first stirrup is placed at the joint face (typical RC detailing).
                         try
                         {
                            if (TryAdjustBeamAxisRangeToJoinedColumnFaces(doc, host, tr, axisIndex, baseMin, baseMax, out var adjMin, out var adjMax, out var dbg) && (adjMax > adjMin))
                            {
                                baseMin = adjMin;
                                baseMax = adjMax;
                                hostObj["beamStirrupSupportFaces"] = dbg;
                            }
                        }
                        catch { /* ignore */ }
                    }

                    double axisStart = baseMin + (isColumn ? (coverBottomFt + r) : (coverOtherFt + r));
                    double axisEnd = baseMax - (isColumn ? (coverTopFt + r) : (coverOtherFt + r));
                    if (isFraming)
                    {
                        // Along-beam cover does not apply; stirrups should start at the joint face (physical end).
                        axisStart = baseMin;
                        axisEnd = baseMax;
                    }
                    // Start/end offsets for ties/stirrups (mm)
                    try
                    {
                        if (isFraming)
                        {
                            axisStart += UnitHelper.MmToFt(opts.beamStirrupStartOffsetMm);
                            axisEnd -= UnitHelper.MmToFt(opts.beamStirrupEndOffsetMm);
                            hostObj["beamStirrupAxisOffsetsMm"] = new JObject
                            {
                                ["start"] = opts.beamStirrupStartOffsetMm,
                                ["end"] = opts.beamStirrupEndOffsetMm
                            };
                        }
                        else if (isColumn)
                        {
                            axisStart += UnitHelper.MmToFt(opts.columnTieBottomOffsetMm);
                            axisEnd -= UnitHelper.MmToFt(opts.columnTieTopOffsetMm);
                            hostObj["columnTieAxisOffsetsMm"] = new JObject
                            {
                                ["bottom"] = opts.columnTieBottomOffsetMm,
                                ["top"] = opts.columnTieTopOffsetMm
                            };
                        }
                    }
                    catch { /* ignore */ }
                    double arrayLenFt = axisEnd - axisStart;
                    if (!(arrayLenFt > 0.0))
                    {
                        hostObj["ok"] = false;
                        hostObj["code"] = "NO_AXIS_LENGTH";
                        hostObj["msg"] = "Computed tie/stirrup axis length is invalid.";
                        hostsArr.Add(hostObj);
                        continue;
                    }

                    int sIdx = isFraming ? sideIndex : crossA;
                    int uIdx = isFraming ? upIndex : crossB;
                    double sMin = GetMinByIndex(localBox, sIdx);
                    double sMax = GetMaxByIndex(localBox, sIdx);
                    double uMin = GetMinByIndex(localBox, uIdx);
                    double uMax = GetMaxByIndex(localBox, uIdx);

                    double left = sMin + faceLeftFt + r;
                    double right = sMax - faceRightFt - r;
                    double bottom = uMin + faceDownFt + r;
                    double top = uMax - faceUpFt - r;

                    if (!(left < right && bottom < top))
                    {
                        hostObj["ok"] = false;
                        hostObj["code"] = "INVALID_TIE_GEOMETRY";
                        hostObj["msg"] = "Computed tie/stirrup rectangle is invalid (covers too large?).";
                        hostsArr.Add(hostObj);
                        continue;
                    }

                    // Hook defaults: if not explicitly provided, try to read from existing tagged rebars in the same host
                    // (e.g. user edited "始端のフック/終端のフック/回転" on the existing hoop set).
                    double hookAngleDeg = 0.0;
                    string hookStartTypeName = string.Empty;
                    string hookEndTypeName = string.Empty;
                    string hookStartOrient = "left";
                    string hookEndOrient = "right";
                    string hookSource = "none";
                    double? hookStartRotationRad = null;
                    double? hookEndRotationRad = null;

                    try
                    {
                        if (isFraming)
                        {
                            hookAngleDeg = opts.beamStirrupHookAngleDeg;
                            hookStartTypeName = (opts.beamStirrupHookTypeName ?? string.Empty).Trim();
                            hookEndTypeName = hookStartTypeName;
                            hookStartOrient = (opts.beamStirrupHookOrientationStart ?? "left").Trim().ToLowerInvariant();
                            hookEndOrient = (opts.beamStirrupHookOrientationEnd ?? "right").Trim().ToLowerInvariant();
                            hookSource = "options";
                        }
                        else if (isColumn)
                        {
                            hookAngleDeg = opts.columnTieHookAngleDeg;
                            hookStartTypeName = (opts.columnTieHookTypeName ?? string.Empty).Trim();
                            hookEndTypeName = hookStartTypeName;
                            hookStartOrient = (opts.columnTieHookOrientationStart ?? "left").Trim().ToLowerInvariant();
                            hookEndOrient = (opts.columnTieHookOrientationEnd ?? "right").Trim().ToLowerInvariant();
                            hookSource = "options";
                        }
                    }
                    catch { /* ignore */ }

                    // Auto-detect from existing tagged rebars only when hook is not explicitly specified.
                    bool hookExplicit = (hookAngleDeg > 1.0) || !string.IsNullOrWhiteSpace(hookStartTypeName) || !string.IsNullOrWhiteSpace(hookEndTypeName);
                    if (!hookExplicit && opts.hookAutoDetectFromExistingTaggedRebar)
                    {
                        try
                        {
                            if (TryGetHookDefaultsFromExistingTaggedRebarInHost(doc, host, opts.tagComments, tieBarTypeName, out var hd, out var hdDbg))
                            {
                                hookStartTypeName = (hd.startHookTypeName ?? string.Empty).Trim();
                                hookEndTypeName = (hd.endHookTypeName ?? string.Empty).Trim();
                                hookStartOrient = (hd.startOrientation ?? "left").Trim().ToLowerInvariant();
                                hookEndOrient = (hd.endOrientation ?? "right").Trim().ToLowerInvariant();
                                hookStartRotationRad = hd.startRotationRad;
                                hookEndRotationRad = hd.endRotationRad;
                                hookSource = "existingTaggedRebarParams";
                                if (hdDbg != null) hostObj["hookDefaultsFromExistingTaggedRebar"] = hdDbg;
                            }
                        }
                        catch { /* ignore */ }
                    }

                    // Decide whether to create hooks.
                    // - Explicit config (angle/type) => hooks
                    // - User option flag => hooks (even if no explicit angle/type)
                    // - Auto-detected defaults => hooks
                    bool optionWantsHooks = isFraming ? opts.beamStirrupUseHooks : opts.columnTieUseHooks;
                    bool detectedDefaults = hookSource == "existingTaggedRebarParams";
                    bool wantHooks = hookExplicit || optionWantsHooks || detectedDefaults;

                    // If hooks are enabled but no explicit hook type/angle is available, default to 135deg.
                    if (wantHooks && !(hookAngleDeg > 1.0)
                        && string.IsNullOrWhiteSpace(hookStartTypeName)
                        && string.IsNullOrWhiteSpace(hookEndTypeName))
                    {
                        hookAngleDeg = 135.0;
                        if (hookSource == "none") hookSource = "default(135deg)";
                    }

                    // If hooks are enabled, resolve the actual hook type names now (best-effort) so that:
                    //  - we can avoid creating an open polyline when the hook type is missing
                    //  - action.hook can carry explicit type names (stable across runs)
                    if (wantHooks)
                    {
                        try
                        {
                            RebarHookType htStart = null;
                            RebarHookType htEnd = null;

                            if (!string.IsNullOrWhiteSpace(hookStartTypeName))
                                htStart = TryFindHookTypeByExactName(doc, hookStartTypeName, RebarStyle.StirrupTie);
                            if (!string.IsNullOrWhiteSpace(hookEndTypeName))
                                htEnd = TryFindHookTypeByExactName(doc, hookEndTypeName, RebarStyle.StirrupTie);

                            // If type names are present but incompatible/missing, fall back to an angle-based search.
                            if ((htStart == null || htEnd == null) && !(hookAngleDeg > 1.0))
                            {
                                double inferred;
                                var angleText = (hookStartTypeName ?? string.Empty) + " " + (hookEndTypeName ?? string.Empty);
                                if (TryInferHookAngleDegFromText(angleText, out inferred) && inferred > 1.0)
                                    hookAngleDeg = inferred;
                                else
                                    hookAngleDeg = 135.0;
                                if (hookSource == "none") hookSource = "fallback(angleDeg=" + hookAngleDeg + ")";
                            }

                            if ((htStart == null || htEnd == null) && hookAngleDeg > 1.0)
                            {
                                var ht = TryFindHookTypeByAngleDeg(doc, hookAngleDeg, RebarStyle.StirrupTie);
                                if (htStart == null) htStart = ht;
                                if (htEnd == null) htEnd = ht;
                            }

                            if (htStart == null || htEnd == null)
                            {
                                // Disable hooks to avoid failures / dialogs and keep a valid closed loop.
                                wantHooks = false;
                                hookSource = "disabled(no RebarHookType found)";
                                try { hostObj["tieHookWarning"] = "Hook requested but no matching RebarHookType was found; creating closed loop without hooks."; } catch { /* ignore */ }
                            }
                            else
                            {
                                // Fill explicit names if omitted (angle-based default).
                                if (string.IsNullOrWhiteSpace(hookStartTypeName)) hookStartTypeName = (htStart.Name ?? string.Empty).Trim();
                                if (string.IsNullOrWhiteSpace(hookEndTypeName)) hookEndTypeName = (htEnd.Name ?? string.Empty).Trim();
                            }
                        }
                        catch
                        {
                            // If hook resolution fails unexpectedly, fall back to no hooks for safety.
                            wantHooks = false;
                            hookSource = "disabled(resolution error)";
                        }
                    }

                    // Default hook rotations:
                    // - start: 0deg
                    // - end  : 180deg (user workflow for "closed stirrup + 135deg hooks")
                    if (wantHooks)
                    {
                        try
                        {
                            if (isFraming)
                            {
                                if (!hookStartRotationRad.HasValue) hookStartRotationRad = CoerceHookRotationDeg(opts.beamStirrupHookStartRotationDeg) * Math.PI / 180.0;
                                if (!hookEndRotationRad.HasValue) hookEndRotationRad = CoerceHookRotationDeg(opts.beamStirrupHookEndRotationDeg) * Math.PI / 180.0;
                            }
                            else
                            {
                                if (!hookStartRotationRad.HasValue) hookStartRotationRad = CoerceHookRotationDeg(opts.columnTieHookStartRotationDeg) * Math.PI / 180.0;
                                if (!hookEndRotationRad.HasValue) hookEndRotationRad = CoerceHookRotationDeg(opts.columnTieHookEndRotationDeg) * Math.PI / 180.0;
                            }
                        }
                        catch { /* ignore */ }
                    }

                    try
                    {
                        hostObj["tieHook"] = new JObject
                        {
                            ["enabled"] = wantHooks,
                            ["source"] = hookSource,
                            ["angleDeg"] = hookAngleDeg,
                            ["startTypeName"] = hookStartTypeName,
                            ["endTypeName"] = hookEndTypeName,
                            ["startOrientation"] = hookStartOrient,
                            ["endOrientation"] = hookEndOrient,
                            ["startRotationRad"] = hookStartRotationRad,
                            ["endRotationRad"] = hookEndRotationRad
                        };
                    }
                    catch { /* ignore */ }

                    JArray BuildTieCurvesAtAxisCoord(double axisCoordFt)
                    {
                        var p1 = MakeLocalPoint(axisIndex, axisCoordFt, sIdx, left, uIdx, bottom);
                        var p2 = MakeLocalPoint(axisIndex, axisCoordFt, sIdx, right, uIdx, bottom);
                        var p3 = MakeLocalPoint(axisIndex, axisCoordFt, sIdx, right, uIdx, top);
                        var p4 = MakeLocalPoint(axisIndex, axisCoordFt, sIdx, left, uIdx, top);
                        var w1 = tr.OfPoint(p1);
                        var w2 = tr.OfPoint(p2);
                        var w3 = tr.OfPoint(p3);
                        var w4 = tr.OfPoint(p4);

                        // Beam stirrups: allow choosing which corner is the "start" (rotates the loop order only).
                        XYZ c1 = w1, c2 = w2, c3 = w3, c4 = w4;
                        if (isFraming)
                        {
                            try
                            {
                                var corner = (opts.beamStirrupStartCorner ?? "top_left").Trim().ToLowerInvariant();
                                if (corner == "top") corner = "top_left";
                                if (corner == "bottom") corner = "bottom_left";

                                if (corner == "bottom_right") { c1 = w2; c2 = w3; c3 = w4; c4 = w1; }
                                else if (corner == "top_right") { c1 = w3; c2 = w4; c3 = w1; c4 = w2; }
                                else if (corner == "top_left") { c1 = w4; c2 = w1; c3 = w2; c4 = w3; }
                                else { c1 = w1; c2 = w2; c3 = w3; c4 = w4; } // bottom_left/default

                                hostObj["beamStirrupStartCorner"] = corner;
                            }
                            catch { /* ignore */ }
                        }

                        var curves = new JArray();
                        curves.Add(GeometryJsonHelper.CurveToJson(Line.CreateBound(c1, c2)));
                        curves.Add(GeometryJsonHelper.CurveToJson(Line.CreateBound(c2, c3)));
                        curves.Add(GeometryJsonHelper.CurveToJson(Line.CreateBound(c3, c4)));
                        // Always create a closed loop for ties/stirrups.
                        // Hooks (if any) are applied by setting instance parameters after creation.
                        curves.Add(GeometryJsonHelper.CurveToJson(Line.CreateBound(c4, c1)));
                        return curves;
                    }

                    string actionStyle = "StirrupTie";

                    bool skipDefaultTiesAction = false;

                    // Column ties: custom pattern (BIM-driven) via mapping / options (recommended).
                    // Fallback: legacy joint pattern options.
                    if (isColumn)
                    {
                        ColumnTiePatternSpec pat = null;
                        string patSource = null;
                        string patErr = null;

                        try
                        {
                            // 1) Mapping JSON pattern (preferred)
                            if (colSpec != null && !string.IsNullOrWhiteSpace(colSpec.tiePatternJson))
                            {
                                if (TryParseColumnTiePatternFromJsonString(colSpec.tiePatternJson, out var p2, out var e2))
                                {
                                    pat = p2;
                                    patSource = "mapping";
                                }
                                else
                                {
                                    patErr = "mapping pattern parse failed: " + e2;
                                }
                            }

                            // 2) options.columnTiePattern (override)
                            if (pat == null && opts.columnTiePattern != null)
                            {
                                if (TryParseColumnTiePatternFromJObject(opts.columnTiePattern, out var p3, out var e3))
                                {
                                    pat = p3;
                                    patSource = "options";
                                }
                                else
                                {
                                    patErr = "options pattern parse failed: " + e3;
                                }
                            }

                            // 3) legacy joint pattern options (compat)
                            if (pat == null && opts.columnTieJointPatternEnabled)
                            {
                                var p4 = new ColumnTiePatternSpec
                                {
                                    referenceKind = "beam_top",
                                    referenceOffsetMm = 0.0,
                                    beamSearchRangeMm = opts.columnTieJointBeamSearchRangeMm,
                                    beamXYToleranceMm = opts.columnTieJointBeamXYToleranceMm
                                };
                                p4.segments.Add(new ColumnTiePatternSegment
                                {
                                    name = "above",
                                    direction = "up",
                                    startOffsetMm = 0.0,
                                    count = Math.Max(0, opts.columnTieJointAboveCount),
                                    pitchMm = opts.columnTieJointAbovePitchMm
                                });
                                p4.segments.Add(new ColumnTiePatternSegment
                                {
                                    name = "below",
                                    direction = "down",
                                    startOffsetMm = Math.Max(0.0, opts.columnTieJointBelowPitchMm),
                                    count = Math.Max(0, opts.columnTieJointBelowCount),
                                    pitchMm = opts.columnTieJointBelowPitchMm
                                });
                                pat = p4;
                                patSource = "legacyOptions";
                            }
                        }
                        catch (Exception ex)
                        {
                            pat = null;
                            patErr = ex.Message;
                        }

                        if (pat != null)
                        {
                            try
                            {
                                if (TryGetColumnTiePatternReferenceAxisCoord(doc, host, tr, axisIndex, axisStart, axisEnd, pat, out var refAxisFt, out var refDbg))
                                {
                                    var used = new JArray();

                                    int segIndex = 0;
                                    foreach (var seg in pat.segments)
                                    {
                                        if (seg == null) continue;
                                        int n = Math.Max(0, seg.count);
                                        if (n <= 0) continue;

                                        double pitchMm = seg.pitchMm;
                                        double pitchFt = UnitHelper.MmToFt(Math.Max(0.0, pitchMm));

                                        int dir = (seg.direction ?? string.Empty).Trim().ToLowerInvariant() == "down" ? -1 : 1;

                                        double baseCoord = refAxisFt + dir * UnitHelper.MmToFt(seg.startOffsetMm);
                                        if (baseCoord < axisStart || baseCoord > axisEnd) { segIndex++; continue; }

                                        if (n > 1 && pitchFt <= 1e-9) { segIndex++; continue; }

                                        // Clamp by available length.
                                        if (n > 1 && pitchFt > 1e-9)
                                        {
                                            int maxBars = 0;
                                            if (dir > 0)
                                                maxBars = (int)Math.Floor(Math.Max(0.0, (axisEnd - baseCoord)) / pitchFt) + 1;
                                            else
                                                maxBars = (int)Math.Floor(Math.Max(0.0, (baseCoord - axisStart)) / pitchFt) + 1;

                                            if (maxBars <= 0) { segIndex++; continue; }
                                            n = Math.Min(n, maxBars);
                                        }

                                        JObject layout = null;
                                        if (n == 1)
                                        {
                                            layout = new JObject { ["rule"] = "single" };
                                        }
                                        else
                                        {
                                            layout = new JObject
                                            {
                                                ["rule"] = "number_with_spacing",
                                                ["numberOfBarPositions"] = n,
                                                ["spacingMm"] = pitchMm,
                                                ["barsOnNormalSide"] = true
                                            };
                                        }

                                        var nrmVec = axisBasis.Normalize().Multiply(dir);
                                        var role = "column_ties_pattern_" + (string.IsNullOrWhiteSpace(seg.name) ? ("seg" + segIndex) : seg.name);

                                        actions.Add(new JObject
                                        {
                                            ["role"] = role,
                                            ["style"] = actionStyle,
                                            ["barTypeName"] = tieBarTypeName,
                                            ["curves"] = BuildTieCurvesAtAxisCoord(baseCoord),
                                            ["normal"] = GeometryJsonHelper.VectorToJson(nrmVec),
                                            ["layout"] = layout,
                                            ["tagComments"] = opts.tagComments,
                                            ["hook"] = wantHooks ? new JObject
                                            {
                                                ["enabled"] = true,
                                                ["angleDeg"] = hookAngleDeg,
                                                ["typeName"] = (!string.IsNullOrWhiteSpace(hookStartTypeName) && hookStartTypeName.Equals(hookEndTypeName, StringComparison.OrdinalIgnoreCase)) ? hookStartTypeName : null,
                                                ["startTypeName"] = string.IsNullOrWhiteSpace(hookStartTypeName) ? null : hookStartTypeName,
                                                ["endTypeName"] = string.IsNullOrWhiteSpace(hookEndTypeName) ? null : hookEndTypeName,
                                                ["startOrientation"] = hookStartOrient,
                                                ["endOrientation"] = hookEndOrient,
                                                ["startRotationRad"] = hookStartRotationRad,
                                                ["endRotationRad"] = hookEndRotationRad
                                            } : null
                                        });

                                        used.Add(new JObject
                                        {
                                            ["name"] = string.IsNullOrWhiteSpace(seg.name) ? null : seg.name,
                                            ["direction"] = dir > 0 ? "up" : "down",
                                            ["startOffsetMm"] = seg.startOffsetMm,
                                            ["count"] = n,
                                            ["pitchMm"] = seg.pitchMm,
                                            ["baseAxisMm"] = UnitHelper.FtToMm(baseCoord)
                                        });
                                        segIndex++;
                                    }

                                    if (used.Count > 0)
                                    {
                                        hostObj["columnTiePattern"] = new JObject
                                        {
                                            ["enabled"] = true,
                                            ["source"] = patSource,
                                            ["reference"] = refDbg,
                                            ["segmentsUsed"] = used
                                        };
                                        skipDefaultTiesAction = true;
                                    }
                                    else
                                    {
                                        hostObj["columnTiePattern"] = new JObject
                                        {
                                            ["enabled"] = true,
                                            ["source"] = patSource,
                                            ["skipped"] = true,
                                            ["reason"] = "No usable segments after clamping; falling back to default layout.",
                                            ["reference"] = refDbg
                                        };
                                    }
                                }
                                else
                                {
                                    hostObj["columnTiePattern"] = new JObject
                                    {
                                        ["enabled"] = true,
                                        ["source"] = patSource,
                                        ["skipped"] = true,
                                        ["reason"] = "Reference not resolved; falling back to default layout."
                                    };
                                }
                            }
                            catch (Exception ex)
                            {
                                hostObj["columnTiePattern"] = new JObject
                                {
                                    ["enabled"] = true,
                                    ["source"] = patSource,
                                    ["skipped"] = true,
                                    ["reason"] = "Exception; falling back to default layout.",
                                    ["error"] = ex.Message
                                };
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(patErr))
                        {
                            hostObj["columnTiePattern"] = new JObject
                            {
                                ["enabled"] = true,
                                ["skipped"] = true,
                                ["reason"] = patErr
                            };
                        }
                    }

                    if (!skipDefaultTiesAction)
                    {
                        var curves = BuildTieCurvesAtAxisCoord(axisStart);

                        var layoutObj = opts.layoutOverride != null ? (JObject)opts.layoutOverride.DeepClone() : BuildLayoutFromMapping(values, opts.preferMappingArrayLength, UnitHelper.FtToMm(arrayLenFt));
                        try
                        {
                            if (layoutObj["arrayLengthMm"] == null || layoutObj.Value<double?>("arrayLengthMm") == null)
                                layoutObj["arrayLengthMm"] = UnitHelper.FtToMm(arrayLenFt);
                            if (isFraming)
                            {
                                // Start at the host joint face (first stirrup at start) by default.
                                if (layoutObj["includeFirstBar"] == null) layoutObj["includeFirstBar"] = true;
                                if (layoutObj["includeLastBar"] == null) layoutObj["includeLastBar"] = true;
                            }
                        }
                        catch { /* ignore */ }

                        // Beam: if pitch is provided by mapping keys, prefer it unless layout override is provided.
                        if (isFraming && beamSpec != null && beamSpec.hasPitchParams && beamSpec.pitchEffectiveMm > 0.0 && opts.layoutOverride == null)
                        {
                            try
                            {
                                layoutObj["rule"] = "maximum_spacing";
                                layoutObj["spacingMm"] = beamSpec.pitchEffectiveMm;
                                if (layoutObj["includeFirstBar"] == null) layoutObj["includeFirstBar"] = true;
                                if (layoutObj["includeLastBar"] == null) layoutObj["includeLastBar"] = true;
                                if (layoutObj["barsOnNormalSide"] == null) layoutObj["barsOnNormalSide"] = true;

                                hostObj["beamStirrups"] = new JObject
                                {
                                    ["source"] = "mapping",
                                    ["spacingMm"] = beamSpec.pitchEffectiveMm
                                };
                            }
                            catch { /* ignore */ }
                        }

                        actions.Add(new JObject
                        {
                            ["role"] = isColumn ? "column_ties" : "beam_stirrups",
                            ["style"] = actionStyle,
                            ["barTypeName"] = tieBarTypeName,
                            ["curves"] = curves,
                            ["normal"] = GeometryJsonHelper.VectorToJson(axisBasis.Normalize()),
                            ["layout"] = layoutObj,
                            ["tagComments"] = opts.tagComments,
                            ["hook"] = wantHooks ? new JObject
                            {
                                ["enabled"] = true,
                                ["angleDeg"] = hookAngleDeg,
                                ["typeName"] = (!string.IsNullOrWhiteSpace(hookStartTypeName) && hookStartTypeName.Equals(hookEndTypeName, StringComparison.OrdinalIgnoreCase)) ? hookStartTypeName : null,
                                ["startTypeName"] = string.IsNullOrWhiteSpace(hookStartTypeName) ? null : hookStartTypeName,
                                ["endTypeName"] = string.IsNullOrWhiteSpace(hookEndTypeName) ? null : hookEndTypeName,
                                ["startOrientation"] = hookStartOrient,
                                ["endOrientation"] = hookEndOrient,
                                ["startRotationRad"] = hookStartRotationRad,
                                ["endRotationRad"] = hookEndRotationRad
                            } : null
                        });
                    }
                }

                hostObj["ok"] = true;
                hostsArr.Add(hostObj);
            }

            var planOut = new JObject
            {
                ["ok"] = true,
                ["planVersion"] = PlanVersion,
                ["mappingStatus"] = mappingStatus,
                ["tagComments"] = opts.tagComments,
                ["deleteExistingTaggedInHosts"] = deleteExistingTaggedInHosts,
                ["hosts"] = hostsArr
            };

            if (coverConfirmRequiredAny && opts.coverConfirmEnabled && !opts.coverConfirmProceed)
            {
                planOut["ok"] = false;
                planOut["code"] = "COVER_CONFIRMATION_REQUIRED";
                planOut["msg"] = "被り厚さの読み取りが確定できないホストがあります。hosts[].coverCandidateParamNames / coverFacesMmRaw を確認し、options.coverConfirmProceed=true で続行するか、options.coverParamNames で読むパラメータ名を指定して再実行してください。";
                planOut["nextActions"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "confirm_cover_policy",
                        ["prompt"] = "配筋用の被り厚さ（上/下/左/右）をどのパラメータから読むか、最小値(minCoverMm)へ丸めてよいかを確定してください。",
                        ["exampleRerunParams"] = new JObject
                        {
                            ["options"] = new JObject
                            {
                                ["coverConfirmProceed"] = true,
                                ["coverMinMm"] = opts.coverMinMm,
                                ["coverClampToMin"] = opts.coverClampToMin
                            }
                        }
                    }
                };
            }

            return planOut;
        }

        public static JObject ApplyPlan(Document doc, JObject plan, bool dryRun)
        {
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");
            if (plan == null) return ResultUtil.Err("plan is required.", "INVALID_ARGS");

            var hostsArr = plan["hosts"] as JArray;
            if (hostsArr == null) return ResultUtil.Err("plan.hosts is required.", "INVALID_ARGS");

            // If the plan requires confirmation (or is explicitly marked not ok), do not apply.
            try
            {
                var ok = plan.Value<bool?>("ok");
                if (ok.HasValue && !ok.Value) return plan;
            }
            catch { /* ignore */ }

            if (dryRun)
            {
                return ResultUtil.Ok(new { ok = true, dryRun = true, plan });
            }

            var createdAll = new List<int>();
            var hostResults = new JArray();

            bool deleteExistingTaggedInHosts = plan.Value<bool?>("deleteExistingTaggedInHosts") ?? false;
            string tagComments = (plan.Value<string>("tagComments") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(tagComments)) tagComments = DefaultTagComments;

            using (var tg = new TransactionGroup(doc, "RevitMcp - Auto Rebar Apply Plan"))
            {
                tg.Start();

                foreach (var hTok in hostsArr.OfType<JObject>())
                {
                    int hostId = hTok.Value<int?>("hostElementId") ?? 0;
                    var r = new JObject { ["hostElementId"] = hostId };

                    if (!(hTok.Value<bool?>("ok") ?? false))
                    {
                        r["ok"] = false;
                        r["code"] = hTok.Value<string>("code") ?? "PLAN_HOST_INVALID";
                        r["msg"] = hTok.Value<string>("msg") ?? "Host plan item is not ok.";
                        hostResults.Add(r);
                        continue;
                    }

                    Element host = null;
                    try { host = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(hostId)); } catch { host = null; }
                    if (host == null)
                    {
                        r["ok"] = false;
                        r["code"] = "NOT_FOUND";
                        r["msg"] = "Host element not found.";
                        hostResults.Add(r);
                        continue;
                    }

                    bool validHost = false;
                    try { validHost = RebarHostData.IsValidHost(host); } catch { validHost = false; }
                    if (!validHost)
                    {
                        r["ok"] = false;
                        r["code"] = "NOT_VALID_REBAR_HOST";
                        r["msg"] = "Host is not a valid rebar host (RebarHostData.IsValidHost=false).";
                        hostResults.Add(r);
                        continue;
                    }

                    var actionsArr = hTok["actions"] as JArray;
                    if (actionsArr == null || actionsArr.Count == 0)
                    {
                        r["ok"] = true;
                        r["createdRebarIds"] = new JArray();
                        r["msg"] = "No actions.";
                        hostResults.Add(r);
                        continue;
                    }

                    var created = new List<int>();
                    var layoutWarnings = new JArray();

                    using (var tx = new Transaction(doc, "RevitMcp - Auto Rebar Host " + hostId))
                    {
                        tx.Start();

                        try
                        {
                            if (deleteExistingTaggedInHosts)
                            {
                                var toDelete = RebarDeleteService.CollectTaggedRebarIdsInHost(doc, host, tagComments);
                                var deleted = RebarDeleteService.DeleteElementsByIds(doc, toDelete);
                                if (deleted != null && deleted.Count > 0)
                                    r["deletedRebarIds"] = new JArray(deleted.Distinct().OrderBy(x => x));
                            }

                            var createdInfos = ExecuteActionsInTransaction(doc, host, actionsArr, out layoutWarnings);
                            foreach (var ci in createdInfos)
                            {
                                if (ci == null) continue;
                                if (ci.elementId > 0) created.Add(ci.elementId);
                            }

                            tx.Commit();
                            r["ok"] = true;
                            r["createdRebarIds"] = new JArray(created.Distinct().OrderBy(x => x));
                            r["msg"] = "OK";
                            if (layoutWarnings.Count > 0) r["layoutWarnings"] = layoutWarnings;
                            hostResults.Add(r);
                            createdAll.AddRange(created);
                        }
                        catch (DisabledDisciplineException ex)
                        {
                            try { tx.RollBack(); } catch { /* ignore */ }
                            r["ok"] = false;
                            r["code"] = "DISCIPLINE_DISABLED";
                            r["msg"] = ex.Message;
                            hostResults.Add(r);
                        }
                        catch (InapplicableDataException ex)
                        {
                            try { tx.RollBack(); } catch { /* ignore */ }
                            r["ok"] = false;
                            r["code"] = "INAPPLICABLE_DATA";
                            r["msg"] = ex.Message;
                            hostResults.Add(r);
                        }
                        catch (Exception ex)
                        {
                            try { tx.RollBack(); } catch { /* ignore */ }
                            r["ok"] = false;
                            r["code"] = "REVIT_EXCEPTION";
                            r["msg"] = ex.Message;
                            hostResults.Add(r);
                        }
                    }
                }

                tg.Assimilate();
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                createdRebarIds = createdAll.Distinct().OrderBy(x => x).ToArray(),
                hosts = hostResults,
                msg = "Auto rebar applied."
            });
        }

        // ------------------------- helpers -------------------------

        private static bool TryGetHostTransform(Element host, out Transform tr)
        {
            tr = Transform.Identity;
            try
            {
                var fi = host as FamilyInstance;
                if (fi != null)
                {
                    tr = fi.GetTransform();
                    if (tr == null) tr = Transform.Identity;
                    return true;
                }
            }
            catch { /* ignore */ }

            tr = Transform.Identity;
            return true;
        }

        private static bool TryGetLocalBox(Element host, Transform tr, out LocalBox box, out string msg)
        {
            box = new LocalBox();
            msg = string.Empty;

            BoundingBoxXYZ bb = null;
            try { bb = host.get_BoundingBox(null); } catch { bb = null; }
            if (bb == null) { msg = "BoundingBoxXYZ is null."; return false; }

            var min = bb.Min;
            var max = bb.Max;
            if (min == null || max == null) { msg = "BoundingBoxXYZ.Min/Max is null."; return false; }

            var corners = new[]
            {
                new XYZ(min.X, min.Y, min.Z),
                new XYZ(max.X, min.Y, min.Z),
                new XYZ(min.X, max.Y, min.Z),
                new XYZ(max.X, max.Y, min.Z),
                new XYZ(min.X, min.Y, max.Z),
                new XYZ(max.X, min.Y, max.Z),
                new XYZ(min.X, max.Y, max.Z),
                new XYZ(max.X, max.Y, max.Z)
            };

            var inv = tr.Inverse;
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            foreach (var p in corners)
            {
                XYZ q;
                try { q = inv.OfPoint(p); } catch { q = p; }
                if (q.X < minX) minX = q.X;
                if (q.Y < minY) minY = q.Y;
                if (q.Z < minZ) minZ = q.Z;
                if (q.X > maxX) maxX = q.X;
                if (q.Y > maxY) maxY = q.Y;
                if (q.Z > maxZ) maxZ = q.Z;
            }

            if (double.IsInfinity(minX) || double.IsInfinity(maxX)) { msg = "Invalid bbox extents."; return false; }

            box.minX = minX; box.minY = minY; box.minZ = minZ;
            box.maxX = maxX; box.maxY = maxY; box.maxZ = maxZ;
            return true;
        }

         private static bool TryGetBeamAxisRangeFromLocationCurve(Element host, Transform tr, int axisIndex, out double axisMin, out double axisMax)
         {
            axisMin = 0.0;
            axisMax = 0.0;
            if (host == null) return false;
            if (tr == null) tr = Transform.Identity;

            try
            {
                var lc = host.Location as LocationCurve;
                if (lc == null) return false;
                var c = lc.Curve;
                if (c == null) return false;

                XYZ p0 = null;
                XYZ p1 = null;
                try
                {
                    p0 = c.GetEndPoint(0);
                    p1 = c.GetEndPoint(1);
                }
                catch { return false; }

                if (p0 == null || p1 == null) return false;

                var inv = tr.Inverse;
                var lp0 = inv.OfPoint(p0);
                var lp1 = inv.OfPoint(p1);

                double a0 = axisIndex == 0 ? lp0.X : (axisIndex == 1 ? lp0.Y : lp0.Z);
                double a1 = axisIndex == 0 ? lp1.X : (axisIndex == 1 ? lp1.Y : lp1.Z);

                axisMin = Math.Min(a0, a1);
                axisMax = Math.Max(a0, a1);
                return axisMax > axisMin;
            }
            catch
            {
                axisMin = 0.0;
                axisMax = 0.0;
                return false;
            }
         }

        private static bool TryGetHostAxisRangeFromSolidGeometry(
            Element host,
            Transform tr,
            int axisIndex,
            out double axisMin,
            out double axisMax,
            out string msg)
        {
            axisMin = 0.0;
            axisMax = 0.0;
            msg = string.Empty;
            if (host == null) { msg = "host is null."; return false; }
            if (tr == null) tr = Transform.Identity;

            GeometryElement ge = null;
            try
            {
                var opt = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = true,
                    DetailLevel = ViewDetailLevel.Fine
                };
                ge = host.get_Geometry(opt);
            }
            catch (Exception ex)
            {
                msg = "get_Geometry failed: " + ex.Message;
                ge = null;
            }
            if (ge == null) { if (string.IsNullOrWhiteSpace(msg)) msg = "GeometryElement is null."; return false; }

            var inv = tr.Inverse;
            double minA = double.PositiveInfinity;
            double maxA = double.NegativeInfinity;
            int usedVertexCount = 0;

            void Acc(XYZ p)
            {
                if (p == null) return;
                XYZ q;
                try { q = inv.OfPoint(p); } catch { q = p; }
                double a = axisIndex == 0 ? q.X : (axisIndex == 1 ? q.Y : q.Z);
                if (double.IsNaN(a) || double.IsInfinity(a)) return;
                if (a < minA) minA = a;
                if (a > maxA) maxA = a;
                usedVertexCount++;
            }

            void Walk(GeometryElement e)
            {
                if (e == null) return;
                foreach (var obj in e)
                {
                    if (obj == null) continue;

                    var gi = obj as GeometryInstance;
                    if (gi != null)
                    {
                        try
                        {
                            var inst = gi.GetInstanceGeometry();
                            Walk(inst);
                            continue;
                        }
                        catch { /* ignore */ }
                    }

                    var solid = obj as Solid;
                    if (solid != null)
                    {
                        if (solid.Faces == null || solid.Faces.Size == 0) continue;
                        foreach (Face f in solid.Faces)
                        {
                            if (f == null) continue;
                            Mesh m = null;
                            try { m = f.Triangulate(); } catch { m = null; }
                            if (m == null) continue;
                            try
                            {
                                var verts = m.Vertices;
                                if (verts != null)
                                {
                                    foreach (var v in verts) Acc(v);
                                }
                            }
                            catch { /* ignore */ }
                        }
                        continue;
                    }

                    var mesh = obj as Mesh;
                    if (mesh != null)
                    {
                        try
                        {
                            var verts = mesh.Vertices;
                            if (verts != null)
                            {
                                foreach (var v in verts) Acc(v);
                            }
                        }
                        catch { /* ignore */ }
                        continue;
                    }
                }
            }

            Walk(ge);

            if (usedVertexCount <= 0 || double.IsInfinity(minA) || double.IsInfinity(maxA) || !(maxA > minA))
            {
                if (string.IsNullOrWhiteSpace(msg)) msg = "No valid solid vertices for axis range.";
                return false;
            }

            axisMin = minA;
            axisMax = maxA;
            return true;
        }

        private static RebarHookOrientation ParseHookOrientation(string s, RebarHookOrientation fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            if (s == "left") return RebarHookOrientation.Left;
            if (s == "right") return RebarHookOrientation.Right;
            return fallback;
        }

        private static string TryGetHookStyleName(RebarHookType ht)
        {
            if (ht == null) return null;
            try
            {
                // Revit API versions differ; use reflection to stay compatible.
                var p = ht.GetType().GetProperty("HookStyle");
                if (p == null) return null;
                var v = p.GetValue(ht, null);
                return v != null ? v.ToString() : null;
            }
            catch { return null; }
        }

        private static bool NameContainsAny(string text, params string[] tokens)
        {
            if (string.IsNullOrEmpty(text) || tokens == null || tokens.Length == 0) return false;
            foreach (var t in tokens)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static int GetHookTypeStylePriority(RebarHookType ht, RebarStyle desired)
        {
            if (ht == null) return 0;
            var name = ht.Name ?? string.Empty;

            // 1) Prefer explicit HookStyle when available.
            var hs = TryGetHookStyleName(ht);
            if (!string.IsNullOrWhiteSpace(hs))
            {
                var key = hs.Trim().ToLowerInvariant();
                bool isTie = key.Contains("stirrup") || key.Contains("tie");
                if (desired == RebarStyle.StirrupTie) return isTie ? 100 : 0;
                if (desired == RebarStyle.Standard) return isTie ? 10 : 90;
                return 50;
            }

            // 2) Fallback: name heuristics (JP/EN).
            bool looksTie = NameContainsAny(name, "スターラップ", "タイ", "stirrup", "tie");
            if (desired == RebarStyle.StirrupTie) return looksTie ? 80 : 20;
            if (desired == RebarStyle.Standard) return looksTie ? 20 : 70;
            return 50;
        }

        private static bool TryInferHookAngleDegFromText(string text, out double angleDeg)
        {
            angleDeg = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            // Extract first plausible 2-3 digit angle (e.g. "135", "135度").
            try
            {
                for (int i = 0; i < text.Length; i++)
                {
                    if (!char.IsDigit(text[i])) continue;
                    int val = 0;
                    int digits = 0;
                    int j = i;
                    while (j < text.Length && char.IsDigit(text[j]) && digits < 4)
                    {
                        val = (val * 10) + (text[j] - '0');
                        j++;
                        digits++;
                    }
                    if (digits >= 2 && val >= 30 && val <= 179)
                    {
                        angleDeg = (double)val;
                        return true;
                    }
                    i = j;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        private static double CoerceHookRotationDeg(double deg)
        {
            // Revit sometimes normalizes exactly 180° to 0° for some hook rotation parameters.
            // Use a small offset so UI can still display ~180° while the value remains distinct.
            try
            {
                double d = deg % 360.0;
                if (d < 0) d += 360.0;
                if (Math.Abs(d - 180.0) < 1e-9) return 179.9;
            }
            catch { /* ignore */ }
            return deg;
        }

        private static bool TryResolveHookSpec(
            Document doc,
            JObject actionObj,
            RebarStyle? styleHint,
            out RebarHookType startHook,
            out RebarHookType endHook,
            out RebarHookOrientation startOrient,
            out RebarHookOrientation endOrient,
            out string warning)
        {
            startHook = null;
            endHook = null;
            startOrient = RebarHookOrientation.Left;
            endOrient = RebarHookOrientation.Right;
            warning = string.Empty;
            if (doc == null || actionObj == null) return false;

            var hookObj = actionObj["hook"] as JObject;
            if (hookObj == null) return false;

            bool enabled = hookObj.Value<bool?>("enabled") ?? true;
            if (!enabled) return false;

            startOrient = ParseHookOrientation(hookObj.Value<string>("startOrientation"), RebarHookOrientation.Left);
            endOrient = ParseHookOrientation(hookObj.Value<string>("endOrientation"), RebarHookOrientation.Right);

            var typeName = (hookObj.Value<string>("typeName") ?? string.Empty).Trim();
            var typeNameStart = (hookObj.Value<string>("startTypeName") ?? string.Empty).Trim();
            var typeNameEnd = (hookObj.Value<string>("endTypeName") ?? string.Empty).Trim();
            double angleDeg = hookObj.Value<double?>("angleDeg") ?? 0.0;

            // If start/end are not provided, fall back to legacy typeName.
            if (string.IsNullOrWhiteSpace(typeNameStart)) typeNameStart = typeName;
            if (string.IsNullOrWhiteSpace(typeNameEnd)) typeNameEnd = typeName;

            RebarHookType htStart = null;
            RebarHookType htEnd = null;
            var desiredStyle = styleHint ?? RebarStyle.Standard;

            // If angle is omitted but the name suggests it (e.g. "135度"), infer it.
            if (!(angleDeg > 1.0))
            {
                double inferred;
                var angleText = (typeNameStart ?? string.Empty) + " " + (typeNameEnd ?? string.Empty);
                if (TryInferHookAngleDegFromText(angleText, out inferred) && inferred > 1.0)
                    angleDeg = inferred;
            }
            if (!string.IsNullOrWhiteSpace(typeNameStart))
            {
                htStart = TryFindHookTypeByExactName(doc, typeNameStart, desiredStyle);
            }
            if (!string.IsNullOrWhiteSpace(typeNameEnd))
            {
                htEnd = TryFindHookTypeByExactName(doc, typeNameEnd, desiredStyle);
            }
            if ((htStart == null || htEnd == null) && angleDeg > 1.0)
            {
                var ht = TryFindHookTypeByAngleDeg(doc, angleDeg, desiredStyle);
                if (htStart == null) htStart = ht;
                if (htEnd == null) htEnd = ht;
            }
            if ((htStart == null || htEnd == null) && angleDeg > 1.0)
            {
                // last resort: name contains digits (e.g. "135", "135度")
                var token = ((int)Math.Round(angleDeg)).ToString();
                var ht = TryFindHookTypeByNameContains(doc, token, desiredStyle);
                if (htStart == null) htStart = ht;
                if (htEnd == null) htEnd = ht;
            }
            if ((htStart == null || htEnd == null) && angleDeg > 1.0)
            {
                var ht = TryFindHookTypeByNameContains(doc, "度", desiredStyle);
                if (htStart == null) htStart = ht;
                if (htEnd == null) htEnd = ht;
            }

            if (htStart == null || htEnd == null)
            {
                warning = "Hook requested but RebarHookType not found (angleDeg=" + angleDeg
                    + ", startTypeName='" + typeNameStart + "', endTypeName='" + typeNameEnd + "').";
                return false;
            }

            startHook = htStart;
            endHook = htEnd;
            return true;
        }

        private static RebarHookType TryFindHookTypeByExactName(Document doc, string name)
        {
            return TryFindHookTypeByExactName(doc, name, null);
        }

        private static RebarHookType TryFindHookTypeByExactName(Document doc, string name, RebarStyle? styleHint)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                var desiredStyle = styleHint ?? RebarStyle.Standard;
                RebarHookType best = null;
                int bestScore = -1;
                foreach (var ht in new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>())
                {
                    if (ht == null || ht.Name == null) continue;
                    if (!ht.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    var score = GetHookTypeStylePriority(ht, desiredStyle);
                    if (score <= 0) continue;
                    if (score > bestScore) { best = ht; bestScore = score; }
                }
                return best;
            }
            catch { return null; }
        }

        private static RebarHookType TryFindHookTypeByNameContains(Document doc, string token)
        {
            return TryFindHookTypeByNameContains(doc, token, null);
        }

        private static RebarHookType TryFindHookTypeByNameContains(Document doc, string token, RebarStyle? styleHint)
        {
            if (doc == null || string.IsNullOrWhiteSpace(token)) return null;
            token = token.Trim();
            try
            {
                var desiredStyle = styleHint ?? RebarStyle.Standard;
                RebarHookType best = null;
                int bestScore = -1;
                foreach (var ht in new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>())
                {
                    if (ht == null || ht.Name == null) continue;
                    if (ht.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var score = GetHookTypeStylePriority(ht, desiredStyle);
                    if (score <= 0) continue;
                    if (score > bestScore) { best = ht; bestScore = score; }
                }
                return best;
            }
            catch { return null; }
        }

        private static RebarHookType TryFindHookTypeByAngleDeg(Document doc, double angleDeg)
        {
            return TryFindHookTypeByAngleDeg(doc, angleDeg, null);
        }

        private static RebarHookType TryFindHookTypeByAngleDeg(Document doc, double angleDeg, RebarStyle? styleHint)
        {
            if (doc == null || !(angleDeg > 1.0)) return null;
            double target = angleDeg * Math.PI / 180.0;
            double tol = 1.0 * Math.PI / 180.0;
            try
            {
                var desiredStyle = styleHint ?? RebarStyle.Standard;
                RebarHookType best = null;
                int bestScore = -1;
                foreach (var ht in new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>())
                {
                    if (ht == null) continue;
                    try
                    {
                        if (Math.Abs(ht.HookAngle - target) > tol) continue;

                        var score = GetHookTypeStylePriority(ht, desiredStyle);
                        if (score <= 0) continue;

                        // Prefer a clear naming match when multiple exist.
                        var n = (ht.Name ?? string.Empty);
                        if (desiredStyle == RebarStyle.StirrupTie)
                        {
                            if (NameContainsAny(n, "スターラップ", "タイ", "stirrup", "tie")) score += 10;
                        }
                        else if (desiredStyle == RebarStyle.Standard)
                        {
                            if (NameContainsAny(n, "標準", "standard")) score += 10;
                        }

                        if (score > bestScore) { best = ht; bestScore = score; }
                    }
                    catch { /* ignore */ }
                }
                return best;
            }
            catch { /* ignore */ }
            return null;
        }

        private static bool TryGetBeamSupportColumnWidthsAlongAxis(
            Document doc,
            Element beam,
            Transform beamTr,
            LocalBox beamLocalBox,
            int axisIndex,
            int sideIndex,
            int upIndex,
            double beamMinAxis,
            double beamMaxAxis,
            double searchRangeFt,
            double faceTolFt,
            out double startColumnWidthFt,
            out double endColumnWidthFt,
            out JObject debug)
        {
            startColumnWidthFt = 0.0;
            endColumnWidthFt = 0.0;
            debug = null;
            if (doc == null || beam == null) return false;
            if (beamTr == null) beamTr = Transform.Identity;
            if (beamLocalBox == null) return false;

            double sMid = (GetMinByIndex(beamLocalBox, sideIndex) + GetMaxByIndex(beamLocalBox, sideIndex)) / 2.0;
            double uMid = (GetMinByIndex(beamLocalBox, upIndex) + GetMaxByIndex(beamLocalBox, upIndex)) / 2.0;

            XYZ startWorld = null;
            XYZ endWorld = null;
            try
            {
                startWorld = beamTr.OfPoint(MakeLocalPoint(axisIndex, beamMinAxis, sideIndex, sMid, upIndex, uMid));
                endWorld = beamTr.OfPoint(MakeLocalPoint(axisIndex, beamMaxAxis, sideIndex, sMid, upIndex, uMid));
            }
            catch { /* ignore */ }
            if (startWorld == null || endWorld == null) return false;

            var startCandidates = CollectColumnLikeIdsNearPoint(doc, startWorld, searchRangeFt).ToList();
            var endCandidates = CollectColumnLikeIdsNearPoint(doc, endWorld, searchRangeFt).ToList();

            int bestStartId = 0, bestEndId = 0;
            double bestStartDist = double.PositiveInfinity, bestEndDist = double.PositiveInfinity;
            double bestStartWidth = 0.0, bestEndWidth = 0.0;

            var inv = beamTr.Inverse;

            bool TryEval(int idInt, bool isStart, out double distFt, out double widthFt)
            {
                distFt = double.PositiveInfinity;
                widthFt = 0.0;
                Element e = null;
                try { e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idInt)); } catch { e = null; }
                if (e == null) return false;

                int catId = 0;
                try { catId = e.Category != null ? e.Category.Id.IntValue() : 0; } catch { catId = 0; }
                if (catId != (int)BuiltInCategory.OST_StructuralColumns && catId != (int)BuiltInCategory.OST_Columns) return false;

                BoundingBoxXYZ bb = null;
                try { bb = e.get_BoundingBox(null); } catch { bb = null; }
                if (bb == null || bb.Min == null || bb.Max == null) return false;

                var min = bb.Min;
                var max = bb.Max;
                var corners = new[]
                {
                    new XYZ(min.X, min.Y, min.Z),
                    new XYZ(max.X, min.Y, min.Z),
                    new XYZ(min.X, max.Y, min.Z),
                    new XYZ(max.X, max.Y, min.Z),
                    new XYZ(min.X, min.Y, max.Z),
                    new XYZ(max.X, min.Y, max.Z),
                    new XYZ(min.X, max.Y, max.Z),
                    new XYZ(max.X, max.Y, max.Z)
                };

                double minA = double.PositiveInfinity, maxA = double.NegativeInfinity;
                foreach (var p in corners)
                {
                    XYZ q;
                    try { q = inv.OfPoint(p); } catch { q = p; }
                    double a = axisIndex == 0 ? q.X : (axisIndex == 1 ? q.Y : q.Z);
                    if (double.IsNaN(a) || double.IsInfinity(a)) continue;
                    if (a < minA) minA = a;
                    if (a > maxA) maxA = a;
                }
                if (double.IsInfinity(minA) || double.IsInfinity(maxA) || !(maxA > minA)) return false;

                widthFt = maxA - minA;
                double face = isStart ? maxA : minA; // face toward the beam interior
                double target = isStart ? beamMinAxis : beamMaxAxis;
                distFt = Math.Abs(face - target);
                return true;
            }

            foreach (var id in startCandidates)
            {
                if (TryEval(id, true, out var d, out var w) && d <= faceTolFt && d < bestStartDist)
                {
                    bestStartDist = d;
                    bestStartWidth = w;
                    bestStartId = id;
                }
            }
            foreach (var id in endCandidates)
            {
                if (TryEval(id, false, out var d, out var w) && d <= faceTolFt && d < bestEndDist)
                {
                    bestEndDist = d;
                    bestEndWidth = w;
                    bestEndId = id;
                }
            }

            startColumnWidthFt = bestStartWidth;
            endColumnWidthFt = bestEndWidth;
            debug = new JObject
            {
                ["startColumnId"] = bestStartId != 0 ? (int?)bestStartId : null,
                ["endColumnId"] = bestEndId != 0 ? (int?)bestEndId : null,
                ["startFaceDistMm"] = double.IsInfinity(bestStartDist) ? (double?)null : UnitHelper.FtToMm(bestStartDist),
                ["endFaceDistMm"] = double.IsInfinity(bestEndDist) ? (double?)null : UnitHelper.FtToMm(bestEndDist),
                ["searchRangeMm"] = UnitHelper.FtToMm(searchRangeFt),
                ["faceToleranceMm"] = UnitHelper.FtToMm(faceTolFt)
            };

            return (startColumnWidthFt > 0.0) || (endColumnWidthFt > 0.0);
        }

        private static bool TryGetBeamLocalEndpointsFromLocationCurve(Element host, Transform tr, out XYZ localP0, out XYZ localP1)
        {
            localP0 = null;
            localP1 = null;
            if (host == null) return false;
            if (tr == null) tr = Transform.Identity;

            try
            {
                var lc = host.Location as LocationCurve;
                if (lc == null) return false;
                var c = lc.Curve;
                if (c == null) return false;

                var p0 = c.GetEndPoint(0);
                var p1 = c.GetEndPoint(1);
                if (p0 == null || p1 == null) return false;

                var inv = tr.Inverse;
                localP0 = inv.OfPoint(p0);
                localP1 = inv.OfPoint(p1);
                return localP0 != null && localP1 != null;
            }
            catch
            {
                localP0 = null;
                localP1 = null;
                return false;
            }
        }

        private static bool TryAdjustBeamAxisRangeToJoinedColumnFaces(
            Document doc,
            Element beam,
            Transform beamTr,
            int axisIndex,
            double baseMin,
            double baseMax,
            out double adjustedMin,
            out double adjustedMax,
            out JObject debug)
        {
            adjustedMin = baseMin;
            adjustedMax = baseMax;
            debug = null;

            if (doc == null || beam == null) return false;
            if (beamTr == null) beamTr = Transform.Identity;
            if (!(baseMax > baseMin)) return false;

            // Use joined Structural Columns' bounding boxes to infer "support face" along the beam axis.
            // This is best-effort and intended to move stirrup start/end closer to column faces.
            if (!TryGetBeamLocalEndpointsFromLocationCurve(beam, beamTr, out var lp0, out var lp1) || lp0 == null || lp1 == null)
                return false;

            double a0 = axisIndex == 0 ? lp0.X : (axisIndex == 1 ? lp0.Y : lp0.Z);
            double a1 = axisIndex == 0 ? lp1.X : (axisIndex == 1 ? lp1.Y : lp1.Z);
            bool p0IsStart = a0 <= a1;
            var startP = p0IsStart ? lp0 : lp1;
            var endP = p0IsStart ? lp1 : lp0;

            var joined = new List<ElementId>();
            try { joined = JoinGeometryUtils.GetJoinedElements(doc, beam).ToList(); } catch { joined = new List<ElementId>(); }

            // Fallback: if not joined, try collecting nearby columns around the beam endpoints.
            var candidateColumnIds = new HashSet<int>();
            if (joined != null)
            {
                foreach (var jid in joined)
                {
                    try
                    {
                        int v = jid.IntValue();
                        if (v != 0) candidateColumnIds.Add(v);
                    }
                    catch { /* ignore */ }
                }
            }

            if (candidateColumnIds.Count == 0)
            {
                try
                {
                    var lc = beam.Location as LocationCurve;
                    var curve = lc != null ? lc.Curve : null;
                    if (curve != null)
                    {
                        var wp0 = curve.GetEndPoint(0);
                        var wp1 = curve.GetEndPoint(1);
                        var near = new HashSet<int>();
                        foreach (var id in CollectColumnLikeIdsNearPoint(doc, wp0, UnitHelper.MmToFt(1500.0))) near.Add(id);
                        foreach (var id in CollectColumnLikeIdsNearPoint(doc, wp1, UnitHelper.MmToFt(1500.0))) near.Add(id);
                        foreach (var id in near) candidateColumnIds.Add(id);
                    }
                }
                catch { /* ignore */ }
            }

            if (candidateColumnIds.Count == 0) return false;

            var inv = beamTr.Inverse;
            int other1 = axisIndex == 0 ? 1 : 0;
            int other2 = axisIndex == 2 ? 1 : 2;
            if (axisIndex == 1) other2 = 2;

            double tol = UnitHelper.MmToFt(50.0);
            double bestStartFace = double.NegativeInfinity;
            double bestEndFace = double.PositiveInfinity;
            int startColId = 0, endColId = 0;

            foreach (var idInt in candidateColumnIds)
            {
                Element e = null;
                try { e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idInt)); } catch { e = null; }
                if (e == null) continue;

                int catId = 0;
                try { catId = e.Category != null ? e.Category.Id.IntValue() : 0; } catch { catId = 0; }
                if (catId != (int)BuiltInCategory.OST_StructuralColumns && catId != (int)BuiltInCategory.OST_Columns) continue;

                BoundingBoxXYZ bb = null;
                try { bb = e.get_BoundingBox(null); } catch { bb = null; }
                if (bb == null || bb.Min == null || bb.Max == null) continue;

                var min = bb.Min;
                var max = bb.Max;
                var corners = new[]
                {
                    new XYZ(min.X, min.Y, min.Z),
                    new XYZ(max.X, min.Y, min.Z),
                    new XYZ(min.X, max.Y, min.Z),
                    new XYZ(max.X, max.Y, min.Z),
                    new XYZ(min.X, min.Y, max.Z),
                    new XYZ(max.X, min.Y, max.Z),
                    new XYZ(min.X, max.Y, max.Z),
                    new XYZ(max.X, max.Y, max.Z)
                };

                double minA = double.PositiveInfinity, minB = double.PositiveInfinity, minC = double.PositiveInfinity;
                double maxA = double.NegativeInfinity, maxB = double.NegativeInfinity, maxC = double.NegativeInfinity;
                foreach (var p in corners)
                {
                    XYZ q;
                    try { q = inv.OfPoint(p); } catch { q = p; }
                    double ax = q.X, ay = q.Y, az = q.Z;
                    if (ax < minA) minA = ax; if (ax > maxA) maxA = ax;
                    if (ay < minB) minB = ay; if (ay > maxB) maxB = ay;
                    if (az < minC) minC = az; if (az > maxC) maxC = az;
                }

                // Pick range by axisIndex
                double colMinAxis = axisIndex == 0 ? minA : (axisIndex == 1 ? minB : minC);
                double colMaxAxis = axisIndex == 0 ? maxA : (axisIndex == 1 ? maxB : maxC);

                double colMinO1 = other1 == 0 ? minA : (other1 == 1 ? minB : minC);
                double colMaxO1 = other1 == 0 ? maxA : (other1 == 1 ? maxB : maxC);
                double colMinO2 = other2 == 0 ? minA : (other2 == 1 ? minB : minC);
                double colMaxO2 = other2 == 0 ? maxA : (other2 == 1 ? maxB : maxC);

                double startO1 = other1 == 0 ? startP.X : (other1 == 1 ? startP.Y : startP.Z);
                double startO2 = other2 == 0 ? startP.X : (other2 == 1 ? startP.Y : startP.Z);
                double endO1 = other1 == 0 ? endP.X : (other1 == 1 ? endP.Y : endP.Z);
                double endO2 = other2 == 0 ? endP.X : (other2 == 1 ? endP.Y : endP.Z);

                bool overlapsStart = (startO1 >= colMinO1 - tol && startO1 <= colMaxO1 + tol)
                    && (startO2 >= colMinO2 - tol && startO2 <= colMaxO2 + tol);
                bool overlapsEnd = (endO1 >= colMinO1 - tol && endO1 <= colMaxO1 + tol)
                    && (endO2 >= colMinO2 - tol && endO2 <= colMaxO2 + tol);

                // Start face: the column face toward the beam interior (max axis near the start end).
                if (overlapsStart && colMaxAxis > bestStartFace && colMaxAxis <= baseMax + UnitHelper.MmToFt(5000))
                {
                    bestStartFace = colMaxAxis;
                    try { startColId = e.Id.IntValue(); } catch { startColId = 0; }
                }
                // End face: the column face toward the beam interior (min axis near the end).
                if (overlapsEnd && colMinAxis < bestEndFace && colMinAxis >= baseMin - UnitHelper.MmToFt(5000))
                {
                    bestEndFace = colMinAxis;
                    try { endColId = e.Id.IntValue(); } catch { endColId = 0; }
                }
            }

            bool hasStart = !double.IsInfinity(bestStartFace) && !double.IsNegativeInfinity(bestStartFace);
            bool hasEnd = !double.IsInfinity(bestEndFace) && !double.IsPositiveInfinity(bestEndFace);
            if (!hasStart && !hasEnd) return false;

            double newMin = baseMin;
            double newMax = baseMax;
            if (hasStart) newMin = Math.Max(newMin, bestStartFace);
            if (hasEnd) newMax = Math.Min(newMax, bestEndFace);
            if (!(newMax > newMin)) return false;

            adjustedMin = newMin;
            adjustedMax = newMax;
            debug = new JObject
            {
                ["source"] = "columnsBboxSupportFaces",
                ["axisIndex"] = axisIndex,
                ["baseMinMm"] = UnitHelper.FtToMm(baseMin),
                ["baseMaxMm"] = UnitHelper.FtToMm(baseMax),
                ["adjustedMinMm"] = UnitHelper.FtToMm(adjustedMin),
                ["adjustedMaxMm"] = UnitHelper.FtToMm(adjustedMax),
                ["startColumnId"] = startColId > 0 ? (int?)startColId : null,
                ["endColumnId"] = endColId > 0 ? (int?)endColId : null,
                ["candidateColumnCount"] = candidateColumnIds.Count
            };
            return true;
        }

        private static IEnumerable<int> CollectColumnLikeIdsNearPoint(Document doc, XYZ worldPoint, double rangeFt)
        {
            if (doc == null || worldPoint == null) yield break;
            if (!(rangeFt > 1e-9)) yield break;

            Outline outline = null;
            try
            {
                var min = new XYZ(worldPoint.X - rangeFt, worldPoint.Y - rangeFt, worldPoint.Z - rangeFt);
                var max = new XYZ(worldPoint.X + rangeFt, worldPoint.Y + rangeFt, worldPoint.Z + rangeFt);
                outline = new Outline(min, max);
            }
            catch { outline = null; }
            if (outline == null) yield break;

            FilteredElementCollector col = null;
            try
            {
                var cats = new List<BuiltInCategory> { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns };
                var catFilter = new ElementMulticategoryFilter(cats);
                col = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(catFilter);
            }
            catch { col = null; }
            if (col == null) yield break;

            try
            {
                var bbFilter = new BoundingBoxIntersectsFilter(outline);
                col = col.WherePasses(bbFilter);
            }
            catch { /* ignore */ }

            IList<Element> elems = null;
            try { elems = col.ToElements(); } catch { elems = null; }
            if (elems == null) yield break;

            foreach (var e in elems)
            {
                if (e == null) continue;
                int id = 0;
                try { id = e.Id.IntValue(); } catch { id = 0; }
                if (id != 0) yield return id;
            }
        }

        private static XYZ GuessHostAxisDirection(Element host, Transform tr)
        {
            try
            {
                var lc = host.Location as LocationCurve;
                if (lc != null)
                {
                    var c = lc.Curve;
                    if (c != null)
                    {
                        var p0 = c.GetEndPoint(0);
                        var p1 = c.GetEndPoint(1);
                        var v = p1 - p0;
                        if (v != null && v.GetLength() > 1e-9) return v.Normalize();
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                var z = tr.BasisZ;
                if (z != null && z.GetLength() > 1e-9) return z.Normalize();
            }
            catch { /* ignore */ }

            return XYZ.BasisZ;
        }

        private static int AxisIndexFromLocalVector(XYZ v)
        {
            double ax = Math.Abs(v.X);
            double ay = Math.Abs(v.Y);
            double az = Math.Abs(v.Z);
            if (ax >= ay && ax >= az) return 0;
            if (ay >= ax && ay >= az) return 1;
            return 2;
        }

        private static XYZ GetBasisVectorByIndex(Transform tr, int idx)
        {
            if (idx == 0) return tr.BasisX;
            if (idx == 1) return tr.BasisY;
            return tr.BasisZ;
        }

        private static double GetMinByIndex(LocalBox b, int idx)
        {
            if (idx == 0) return b.minX;
            if (idx == 1) return b.minY;
            return b.minZ;
        }

        private static double GetMaxByIndex(LocalBox b, int idx)
        {
            if (idx == 0) return b.maxX;
            if (idx == 1) return b.maxY;
            return b.maxZ;
        }

        private static XYZ MakeLocalPoint(int idx1, double v1, int idx2, double v2, int idx3, double v3)
        {
            double x = 0, y = 0, z = 0;
            SetByIndex(idx1, v1, ref x, ref y, ref z);
            SetByIndex(idx2, v2, ref x, ref y, ref z);
            SetByIndex(idx3, v3, ref x, ref y, ref z);
            return new XYZ(x, y, z);
        }

        private static void SetByIndex(int idx, double val, ref double x, ref double y, ref double z)
        {
            if (idx == 0) x = val;
            else if (idx == 1) y = val;
            else z = val;
        }

        private static bool TryFindBarTypeByName(Document doc, string name, out RebarBarType barType)
        {
            barType = null;
            if (doc == null || string.IsNullOrWhiteSpace(name)) return false;
            var target = name.Trim();
            try
            {
                var it = new FilteredElementCollector(doc)
                    .OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>()
                    .FirstOrDefault(x => string.Equals((x.Name ?? string.Empty).Trim(), target, StringComparison.OrdinalIgnoreCase));
                if (it != null) { barType = it; return true; }
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool TryParseDiameterMmFromBarTypeText(string text, out int diameterMm)
        {
            diameterMm = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                var s = text.Trim();
                int i = 0;
                while (i < s.Length && !char.IsDigit(s[i])) i++;
                if (i >= s.Length) return false;

                int j = i;
                while (j < s.Length && char.IsDigit(s[j])) j++;
                if (j <= i) return false;

                var token = s.Substring(i, j - i);
                if (!int.TryParse(token, out diameterMm)) return false;
                return diameterMm > 0 && diameterMm <= 200;
            }
            catch
            {
                diameterMm = 0;
                return false;
            }
        }

        private static bool TryFindBarTypeByDiameterMm(Document doc, int diameterMm, out RebarBarType barType)
        {
            barType = null;
            if (doc == null || diameterMm <= 0) return false;

            try
            {
                var list = new FilteredElementCollector(doc)
                    .OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>()
                    .Select(t => new { t, dMm = UnitHelper.FtToMm(t.BarModelDiameter) })
                    .Where(x => Math.Abs(x.dMm - diameterMm) <= 0.5)
                    .Select(x => x.t)
                    .OrderBy(x => x.Name ?? string.Empty)
                    .ToList();

                if (list.Count == 0) return false;

                // Prefer common naming pattern if present, but do not require it.
                var needle = "D" + diameterMm;
                foreach (var t in list)
                {
                    var n = (t.Name ?? string.Empty);
                    if (n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        barType = t;
                        return true;
                    }
                }

                barType = list[0];
                return barType != null;
            }
            catch
            {
                barType = null;
                return false;
            }
        }

        private static RebarHookType TryGetAnyHookType(Document doc)
        {
            return TryGetAnyHookType(doc, null);
        }

        private static RebarHookType TryGetAnyHookType(Document doc, RebarStyle? styleHint)
        {
            try
            {
                var desiredStyle = styleHint ?? RebarStyle.Standard;
                RebarHookType best = null;
                int bestScore = -1;
                foreach (var ht in new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>())
                {
                    if (ht == null) continue;
                    if (!styleHint.HasValue) return ht;

                    var score = GetHookTypeStylePriority(ht, desiredStyle);
                    if (score <= 0) continue;
                    if (score > bestScore) { best = ht; bestScore = score; }
                }
                return best;
            }
            catch
            {
                return null;
            }
        }

        private static JObject BuildLayoutFromMapping(JObject values, bool preferMappingArrayLength, double arrayLengthMmComputed)
        {
            string rule = "maximum_spacing";
            double spacingMm = 150.0;
            int? nBars = null;
            bool includeFirst = true;
            bool includeLast = true;
            bool barsOnNormalSide = true;
            double? mappingArrayLengthMm = null;

            try
            {
                if (values != null)
                {
                    rule = (values.Value<string>("Common.Arrangement.Rule") ?? "maximum_spacing").Trim().ToLowerInvariant();
                    spacingMm = values.Value<double?>("Common.Arrangement.Spacing") ?? 150.0;
                    mappingArrayLengthMm = values.Value<double?>("Common.Arrangement.ArrayLength");
                    nBars = values.Value<int?>("Common.Arrangement.NumberOfBarPositions");
                    includeFirst = values.Value<bool?>("Common.Arrangement.IncludeFirstBar") ?? true;
                    includeLast = values.Value<bool?>("Common.Arrangement.IncludeLastBar") ?? true;
                    barsOnNormalSide = values.Value<bool?>("Common.Arrangement.BarsOnNormalSide") ?? true;
                }
            }
            catch { /* ignore */ }

            var obj = new JObject
            {
                ["rule"] = rule,
                ["spacingMm"] = spacingMm,
                ["includeFirstBar"] = includeFirst,
                ["includeLastBar"] = includeLast,
                ["barsOnNormalSide"] = barsOnNormalSide
            };

            if (nBars.HasValue) obj["numberOfBarPositions"] = nBars.Value;

            if (preferMappingArrayLength && mappingArrayLengthMm.HasValue && mappingArrayLengthMm.Value > 0.0)
                obj["arrayLengthMm"] = mappingArrayLengthMm.Value;
            else
                obj["arrayLengthMm"] = arrayLengthMmComputed;

            return obj;
        }

        private static JArray CollectCandidateCoverParamNames(Element host, int limit = 80)
        {
            var result = new JArray();
            if (host == null) return result;

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (Parameter p in host.Parameters)
                {
                    if (p == null) continue;
                    if (p.StorageType != StorageType.Double) continue;

                    string n = string.Empty;
                    try { n = (p.Definition != null ? p.Definition.Name : string.Empty) ?? string.Empty; }
                    catch { n = string.Empty; }
                    n = n.Trim();
                    if (n.Length == 0) continue;

                    bool hit = false;
                    try
                    {
                        if (n.IndexOf("かぶり", StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                        else if (n.IndexOf("被り", StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                        else if (n.IndexOf("cover", StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                    }
                    catch { hit = false; }
                    if (!hit) continue;

                    bool isLength = false;
                    try
                    {
                        var dt = p.Definition != null ? p.Definition.GetDataType() : null;
                        var typeId = dt != null ? (dt.TypeId ?? string.Empty) : string.Empty;
                        if (!string.IsNullOrWhiteSpace(typeId) && typeId.IndexOf("length", StringComparison.OrdinalIgnoreCase) >= 0)
                            isLength = true;
                    }
                    catch { /* ignore */ }

                    if (!isLength) continue;

                    if (seen.Add(n))
                    {
                        names.Add(n);
                        if (names.Count >= limit) break;
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { /* ignore */ }

            foreach (var n in names)
            {
                result.Add(n);
            }

            return result;
        }

        private static bool TryGetHostCoverParamMm(Element host, IEnumerable<string> names, out double coverMm, out string matchedName)
        {
            coverMm = 0.0;
            matchedName = string.Empty;
            if (host == null || names == null) return false;

            foreach (var n in names)
            {
                var name = (n ?? string.Empty).Trim();
                if (name.Length == 0) continue;

                try
                {
                    var p = host.LookupParameter(name);
                    if (p == null) continue;
                    if (p.StorageType != StorageType.Double) continue;

                    double raw = 0.0;
                    try { raw = p.AsDouble(); } catch { raw = 0.0; }

                    bool isLength = false;
                    try
                    {
                        // Revit 2021+: Definition.GetDataType() returns ForgeTypeId.
                        var dt = p.Definition != null ? p.Definition.GetDataType() : null;
                        var typeId = dt != null ? (dt.TypeId ?? string.Empty) : string.Empty;
                        if (!string.IsNullOrWhiteSpace(typeId) && typeId.IndexOf("length", StringComparison.OrdinalIgnoreCase) >= 0)
                            isLength = true;
                    }
                    catch { /* ignore */ }

                    coverMm = isLength ? UnitHelper.FtToMm(raw) : raw;
                    matchedName = name;
                    return true;
                }
                catch
                {
                    // ignore and try next name
                }
            }

            return false;
        }

        private static bool TryGetHostCoverMm(Document doc, Element host, BuiltInParameter bip, out double coverMm)
        {
            coverMm = 0.0;
            if (doc == null || host == null) return false;

            try
            {
                var p = host.get_Parameter(bip);
                if (p == null) return false;

                if (p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        var coverType = doc.GetElement(id) as RebarCoverType;
                        if (coverType != null)
                        {
                            coverMm = UnitHelper.FtToMm(coverType.CoverDistance);
                            return coverMm > 0.0;
                        }
                    }
                }
                else if (p.StorageType == StorageType.Double)
                {
                    coverMm = UnitHelper.FtToMm(p.AsDouble());
                    return coverMm > 0.0;
                }
            }
            catch { /* ignore */ }

            // RebarHostData fallback (best-effort; API differences across versions).
            try
            {
                if (RebarHostData.IsValidHost(host))
                {
                    var hd = RebarHostData.GetRebarHostData(host);
                    if (hd != null)
                    {
                        // Avoid compile-time dependency on StructuralFaceOrientation (API differences).
                        var orientType = Type.GetType("Autodesk.Revit.DB.Structure.StructuralFaceOrientation, RevitAPI");
                        if (orientType != null && orientType.IsEnum)
                        {
                            var mi = hd.GetType().GetMethod("GetCoverTypeId", new[] { orientType });
                            if (mi != null)
                            {
                                object o = null;
                                try
                                {
                                    string enumName = "Other";
                                    if (bip == BuiltInParameter.CLEAR_COVER_TOP) enumName = "Top";
                                    else if (bip == BuiltInParameter.CLEAR_COVER_BOTTOM) enumName = "Bottom";

                                    try { o = Enum.Parse(orientType, enumName, true); }
                                    catch
                                    {
                                        // Fallback for "Other" depending on API: try Left, else first enum value.
                                        if (enumName.Equals("Other", StringComparison.OrdinalIgnoreCase))
                                        {
                                            try { o = Enum.Parse(orientType, "Left", true); } catch { o = null; }
                                        }
                                        if (o == null)
                                        {
                                            var vals = Enum.GetValues(orientType);
                                            o = vals != null && vals.Length > 0 ? vals.GetValue(0) : null;
                                        }
                                    }
                                }
                                catch { o = null; }

                                if (o != null)
                                {
                                    var idObj = mi.Invoke(hd, new object[] { o });
                                    if (idObj is ElementId id && id != ElementId.InvalidElementId)
                                    {
                                        var coverType = doc.GetElement(id) as RebarCoverType;
                                        if (coverType != null)
                                        {
                                            coverMm = UnitHelper.FtToMm(coverType.CoverDistance);
                                            return coverMm > 0.0;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private static XYZ TryParsePointMm(JObject obj)
        {
            double x = obj.Value<double?>("x") ?? 0.0;
            double y = obj.Value<double?>("y") ?? 0.0;
            double z = obj.Value<double?>("z") ?? 0.0;
            return UnitHelper.MmToXyz(x, y, z);
        }

        private static XYZ TryParseVectorMm(JObject obj)
        {
            double x = obj.Value<double?>("x") ?? 0.0;
            double y = obj.Value<double?>("y") ?? 0.0;
            double z = obj.Value<double?>("z") ?? 0.0;
            return UnitHelper.MmToXyz(x, y, z);
        }
    }
}
