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
            public string[] coverRoundParamNames = null;

            // Concrete detection tokens (optional overrides)
            public string[] concreteTokens = null;
            public string[] concreteExcludeTokens = null;
            public string[] concreteExcludeMaterialClasses = null;

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

            // Column: axis-end cover handling.
            // If true (default), top/bottom cover along the column axis is applied only when the end is "exposed".
            // When the column end is in contact with another *concrete* structural column / structural foundation,
            // the axis-direction cover is treated as 0 (joint interior), so bars/ties can reach the joint.
            public bool columnAxisEndCoverUsesConcreteNeighborCheck = true;
            public double columnConcreteNeighborSearchRangeMm = 1500.0;
            public double columnConcreteNeighborTouchTolMm = 50.0;

            // Column ties: split into base/head by mid-height (default true).
            // - lower half => "base"
            // - upper half => "head"
            public bool columnTieSplitByMidHeight = true;
            // Column main bars: split into foot/head by mid-height (default true).
            public bool columnMainBarSplitByMidHeight = true;
            // Optional per-zone spacing overrides (mm). If one side is missing/<=0, it is copied from the other side.
            public double? columnTiePitchBaseMm = null;
            public double? columnTiePitchHeadMm = null;

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
            // Column ties: preferred RebarShape (use selected sample when available).
            public bool columnTieShapeFromSelection = true;
            public int columnTieShapeId = 0;

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
                    o.columnAxisEndCoverUsesConcreteNeighborCheck = obj.Value<bool?>("columnAxisEndCoverUsesConcreteNeighborCheck") ?? true;
                    o.columnConcreteNeighborSearchRangeMm = obj.Value<double?>("columnConcreteNeighborSearchRangeMm") ?? o.columnConcreteNeighborSearchRangeMm;
                    o.columnConcreteNeighborTouchTolMm = obj.Value<double?>("columnConcreteNeighborTouchTolMm") ?? o.columnConcreteNeighborTouchTolMm;
                    if (!(o.columnConcreteNeighborSearchRangeMm > 1.0)) o.columnConcreteNeighborSearchRangeMm = 1500.0;
                    if (!(o.columnConcreteNeighborTouchTolMm > 0.0)) o.columnConcreteNeighborTouchTolMm = 50.0;

                    o.columnTieSplitByMidHeight = obj.Value<bool?>("columnTieSplitByMidHeight") ?? true;
                    o.columnMainBarSplitByMidHeight = obj.Value<bool?>("columnMainBarSplitByMidHeight") ?? true;
                    o.columnTiePitchBaseMm = obj.Value<double?>("columnTiePitchBaseMm");
                    o.columnTiePitchHeadMm = obj.Value<double?>("columnTiePitchHeadMm");

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
                    o.columnTieShapeFromSelection = obj.Value<bool?>("columnTieShapeFromSelection") ?? true;
                    o.columnTieShapeId = obj.Value<int?>("columnTieShapeId") ?? 0;

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
                            o.coverRoundParamNames = ReadStringArray(coverNames["round"]);
                        }
                    }
                    catch { /* ignore */ }

                    try
                    {
                        o.concreteTokens = ReadStringArray(obj["concreteTokens"]);
                        o.concreteExcludeTokens = ReadStringArray(obj["concreteExcludeTokens"]);
                        o.concreteExcludeMaterialClasses = ReadStringArray(obj["concreteExcludeMaterialClasses"]);
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
            public int? mainBarsPerFaceX;
            public int? mainBarsPerFaceY;
            public int? mainBarsPerFaceXHead;
            public int? mainBarsPerFaceXFoot;
            public int? mainBarsPerFaceYHead;
            public int? mainBarsPerFaceYFoot;
            public int? mainBarTotalCount;
            public int? mainBarTotalCountHead;
            public int? mainBarTotalCountFoot;
            public string tiePatternJson = string.Empty;
            public double? tiePitchBaseMm;
            public double? tiePitchHeadMm;
        }

        private sealed class ColumnTiePatternSpec
        {
            public string referenceKind = "beam_top"; // beam_top|beam_bottom|column_top|column_bottom|column_mid
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

                Autodesk.Revit.DB.Structure.RebarShape desiredShape = null;
                try
                {
                    int shapeIdVal = aTok.Value<int?>("shapeId") ?? 0;
                    if (shapeIdVal > 0)
                        desiredShape = doc.GetElement(ElementIdCompat.From(shapeIdVal)) as Autodesk.Revit.DB.Structure.RebarShape;
                }
                catch { /* ignore */ }

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
                    else if (kind == "arc")
                    {
                        var s = cTok["start"] as JObject;
                        var e = cTok["end"] as JObject;
                        var c = cTok["center"] as JObject;
                        if (s == null || e == null || c == null) continue;
                        var p0 = TryParsePointMm(s);
                        var p1 = TryParsePointMm(e);
                        var pc = TryParsePointMm(c);
                        try
                        {
                            var v0 = p0 - pc;
                            var v1 = p1 - pc;
                            if (v0.GetLength() < 1e-9 || v1.GetLength() < 1e-9) continue;
                            var xAxis = v0.Normalize();
                            XYZ nrm = XYZ.BasisZ;
                            var nTok = cTok["normal"] as JObject;
                            if (nTok != null)
                            {
                                nrm = TryParseVectorMm(nTok);
                            }
                            if (nrm.GetLength() < 1e-9)
                            {
                                // Fallback to cross product
                                nrm = v0.CrossProduct(v1);
                            }
                            if (nrm.GetLength() < 1e-9) nrm = XYZ.BasisZ;
                            nrm = nrm.Normalize();
                            var yAxis = nrm.CrossProduct(xAxis);
                            if (yAxis.GetLength() < 1e-9) continue;
                            yAxis = yAxis.Normalize();
                            double ang = Math.Atan2(v1.DotProduct(yAxis), v1.DotProduct(xAxis));
                            if (ang <= 1e-9) ang += Math.PI * 2.0;
                            double radius = v0.GetLength();
                            var arc = Arc.Create(pc, radius, 0.0, ang, xAxis, yAxis);
                            curves.Add(arc);
                        }
                        catch { /* ignore */ }
                    }
                    else if (kind == "polyline")
                    {
                        var ptsArr = cTok["points"] as JArray;
                        if (ptsArr == null || ptsArr.Count < 2) continue;
                        XYZ prev = null;
                        foreach (var pTok in ptsArr.OfType<JObject>())
                        {
                            var p = TryParsePointMm(pTok);
                            if (prev != null)
                            {
                                curves.Add(Line.CreateBound(prev, p));
                            }
                            prev = p;
                        }
                    }
                }
                if (curves.Count == 0) continue;

                List<Curve> curvesAlt = null;
                try
                {
                    curvesAlt = BuildPolylineFromCurves(curves);
                }
                catch { /* ignore */ }

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
                        // If a preferred RebarShape is provided (e.g., from a manual sample), try it first.
                        if (desiredShape != null)
                        {
                            rebar = TryCreateFromCurvesAndShape(doc, desiredShape, barType, null, null, host, normal, curves,
                                RebarHookOrientation.Left, RebarHookOrientation.Right);
                        }
                        if (rebar == null)
                        {
                            // Try using existing shapes only (avoid creating new shape from arcs).
                            try
                            {
                                rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                    doc, style, barType, null, null, host, normal, curves,
                                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, false);
                            }
                            catch { /* ignore */ }
                        }
                        if (rebar == null)
                        {
                            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                doc, style, barType, null, null, host, normal, curves,
                                RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                        }
                    }
                    else
                    {
                        if (desiredShape != null)
                        {
                            rebar = TryCreateFromCurvesAndShape(doc, desiredShape, barType, startHook, endHook, host, normal, curves,
                                startOrient, endOrient);
                        }
                        if (rebar == null)
                        {
                            try
                            {
                                rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                    doc, style, barType, startHook, endHook, host, normal, curves,
                                    startOrient, endOrient, true, false);
                            }
                            catch { /* ignore */ }
                        }
                        if (rebar == null)
                        {
                            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                doc, style, barType, startHook, endHook, host, normal, curves,
                                startOrient, endOrient, true, true);
                        }
                    }
                }
                catch
                {
                    // Best-effort fallbacks:
                    // 1) Try without hooks.
                    try
                    {
                        try
                        {
                            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                doc, style, barType, null, null, host, normal, curves,
                                RebarHookOrientation.Left, RebarHookOrientation.Right, true, false);
                        }
                        catch { /* ignore */ }
                        if (rebar == null)
                        {
                            rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                doc, style, barType, null, null, host, normal, curves,
                                RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                        }
                    }
                    catch
                    {
                        // 1.5) Try polyline approximation (when arc loops cause internal errors).
                        try
                        {
                            if (curvesAlt != null && curvesAlt.Count > 1)
                            {
                                rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                    doc, style, barType, null, null, host, normal, curvesAlt,
                                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                            }
                        }
                        catch { /* ignore */ }

                        // 2) Try any hook type (some rebar styles reject some hook styles).
                        // Only try if we still have no rebar, and never propagate the exception.
                        if (rebar == null && style == RebarStyle.StirrupTie)
                        {
                            try
                            {
                                var hook = TryGetAnyHookType(doc, style);
                                if (hook != null)
                                {
                                    rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                                        doc, style, barType, hook, hook, host, normal, curves,
                                        RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                                }
                            }
                            catch { /* ignore */ }
                        }
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
            public bool hasZoneCounts;
            // Zone counts (Start/Mid/End) for layers (Top/Bottom + 2nd/3rd).
            public int? topCountStart;
            public int? topCountMid;
            public int? topCountEnd;
            public int? topCount2Start;
            public int? topCount2Mid;
            public int? topCount2End;
            public int? topCount3Start;
            public int? topCount3Mid;
            public int? topCount3End;
            public int? bottomCountStart;
            public int? bottomCountMid;
            public int? bottomCountEnd;
            public int? bottomCount2Start;
            public int? bottomCount2Mid;
            public int? bottomCount2End;
            public int? bottomCount3Start;
            public int? bottomCount3Mid;
            public int? bottomCount3End;

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

                // Zone counts (start/mid/end) for main bars (layer 1..3)
                var topStart = values.Value<int?>("Beam.Attr.MainBar.TopCountStart");
                var topMid = values.Value<int?>("Beam.Attr.MainBar.TopCountMid");
                var topEnd = values.Value<int?>("Beam.Attr.MainBar.TopCountEnd");
                var top2Start = values.Value<int?>("Beam.Attr.MainBar.TopCount2Start");
                var top2Mid = values.Value<int?>("Beam.Attr.MainBar.TopCount2Mid");
                var top2End = values.Value<int?>("Beam.Attr.MainBar.TopCount2End");
                var top3Start = values.Value<int?>("Beam.Attr.MainBar.TopCount3Start");
                var top3Mid = values.Value<int?>("Beam.Attr.MainBar.TopCount3Mid");
                var top3End = values.Value<int?>("Beam.Attr.MainBar.TopCount3End");

                var bottomStart = values.Value<int?>("Beam.Attr.MainBar.BottomCountStart");
                var bottomMid = values.Value<int?>("Beam.Attr.MainBar.BottomCountMid");
                var bottomEnd = values.Value<int?>("Beam.Attr.MainBar.BottomCountEnd");
                var bottom2Start = values.Value<int?>("Beam.Attr.MainBar.BottomCount2Start");
                var bottom2Mid = values.Value<int?>("Beam.Attr.MainBar.BottomCount2Mid");
                var bottom2End = values.Value<int?>("Beam.Attr.MainBar.BottomCount2End");
                var bottom3Start = values.Value<int?>("Beam.Attr.MainBar.BottomCount3Start");
                var bottom3Mid = values.Value<int?>("Beam.Attr.MainBar.BottomCount3Mid");
                var bottom3End = values.Value<int?>("Beam.Attr.MainBar.BottomCount3End");

                spec.hasZoneCounts = topStart.HasValue || topMid.HasValue || topEnd.HasValue
                    || top2Start.HasValue || top2Mid.HasValue || top2End.HasValue
                    || top3Start.HasValue || top3Mid.HasValue || top3End.HasValue
                    || bottomStart.HasValue || bottomMid.HasValue || bottomEnd.HasValue
                    || bottom2Start.HasValue || bottom2Mid.HasValue || bottom2End.HasValue
                    || bottom3Start.HasValue || bottom3Mid.HasValue || bottom3End.HasValue;

                if (topStart.HasValue) spec.topCountStart = Math.Max(0, topStart.Value);
                if (topMid.HasValue) spec.topCountMid = Math.Max(0, topMid.Value);
                if (topEnd.HasValue) spec.topCountEnd = Math.Max(0, topEnd.Value);
                if (top2Start.HasValue) spec.topCount2Start = Math.Max(0, top2Start.Value);
                if (top2Mid.HasValue) spec.topCount2Mid = Math.Max(0, top2Mid.Value);
                if (top2End.HasValue) spec.topCount2End = Math.Max(0, top2End.Value);
                if (top3Start.HasValue) spec.topCount3Start = Math.Max(0, top3Start.Value);
                if (top3Mid.HasValue) spec.topCount3Mid = Math.Max(0, top3Mid.Value);
                if (top3End.HasValue) spec.topCount3End = Math.Max(0, top3End.Value);
                if (bottomStart.HasValue) spec.bottomCountStart = Math.Max(0, bottomStart.Value);
                if (bottomMid.HasValue) spec.bottomCountMid = Math.Max(0, bottomMid.Value);
                if (bottomEnd.HasValue) spec.bottomCountEnd = Math.Max(0, bottomEnd.Value);
                if (bottom2Start.HasValue) spec.bottomCount2Start = Math.Max(0, bottom2Start.Value);
                if (bottom2Mid.HasValue) spec.bottomCount2Mid = Math.Max(0, bottom2Mid.Value);
                if (bottom2End.HasValue) spec.bottomCount2End = Math.Max(0, bottom2End.Value);
                if (bottom3Start.HasValue) spec.bottomCount3Start = Math.Max(0, bottom3Start.Value);
                if (bottom3Mid.HasValue) spec.bottomCount3Mid = Math.Max(0, bottom3Mid.Value);
                if (bottom3End.HasValue) spec.bottomCount3End = Math.Max(0, bottom3End.Value);

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
                        ["countsFound"] = spec.hasMainCounts,
                        ["zoneCountsFound"] = spec.hasZoneCounts,
                        ["topCountStart"] = spec.topCountStart,
                        ["topCountMid"] = spec.topCountMid,
                        ["topCountEnd"] = spec.topCountEnd,
                        ["topCount2Start"] = spec.topCount2Start,
                        ["topCount2Mid"] = spec.topCount2Mid,
                        ["topCount2End"] = spec.topCount2End,
                        ["topCount3Start"] = spec.topCount3Start,
                        ["topCount3Mid"] = spec.topCount3Mid,
                        ["topCount3End"] = spec.topCount3End,
                        ["bottomCountStart"] = spec.bottomCountStart,
                        ["bottomCountMid"] = spec.bottomCountMid,
                        ["bottomCountEnd"] = spec.bottomCountEnd,
                        ["bottomCount2Start"] = spec.bottomCount2Start,
                        ["bottomCount2Mid"] = spec.bottomCount2Mid,
                        ["bottomCount2End"] = spec.bottomCount2End,
                        ["bottomCount3Start"] = spec.bottomCount3Start,
                        ["bottomCount3Mid"] = spec.bottomCount3Mid,
                        ["bottomCount3End"] = spec.bottomCount3End
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
                var pfx = values.Value<int?>("Column.Attr.MainBar.BarsPerFaceX");
                if (pfx.HasValue) spec.mainBarsPerFaceX = Math.Max(2, pfx.Value);
                var pfy = values.Value<int?>("Column.Attr.MainBar.BarsPerFaceY");
                if (pfy.HasValue) spec.mainBarsPerFaceY = Math.Max(2, pfy.Value);

                var pf = values.Value<int?>("Column.Attr.MainBar.BarsPerFace");
                if (pf.HasValue) spec.mainBarsPerFace = Math.Max(2, pf.Value);

                var pfxH = values.Value<int?>("Column.Attr.MainBar.BarsPerFaceXHead");
                if (pfxH.HasValue) spec.mainBarsPerFaceXHead = Math.Max(2, pfxH.Value);
                var pfxF = values.Value<int?>("Column.Attr.MainBar.BarsPerFaceXFoot");
                if (pfxF.HasValue) spec.mainBarsPerFaceXFoot = Math.Max(2, pfxF.Value);
                var pfyH = values.Value<int?>("Column.Attr.MainBar.BarsPerFaceYHead");
                if (pfyH.HasValue) spec.mainBarsPerFaceYHead = Math.Max(2, pfyH.Value);
                var pfyF = values.Value<int?>("Column.Attr.MainBar.BarsPerFaceYFoot");
                if (pfyF.HasValue) spec.mainBarsPerFaceYFoot = Math.Max(2, pfyF.Value);

                var pt = values.Value<int?>("Column.Attr.MainBar.TotalCount");
                if (pt.HasValue) spec.mainBarTotalCount = Math.Max(0, pt.Value);
                var ptH = values.Value<int?>("Column.Attr.MainBar.TotalCountHead");
                if (ptH.HasValue) spec.mainBarTotalCountHead = Math.Max(0, ptH.Value);
                var ptF = values.Value<int?>("Column.Attr.MainBar.TotalCountFoot");
                if (ptF.HasValue) spec.mainBarTotalCountFoot = Math.Max(0, ptF.Value);

                var pat = (values.Value<string>("Column.Attr.Tie.PatternJson") ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(pat)) spec.tiePatternJson = pat;

                var pb = values.Value<double?>("Column.Attr.Tie.PitchBaseMm");
                if (pb.HasValue) spec.tiePitchBaseMm = pb;
                var ph = values.Value<double?>("Column.Attr.Tie.PitchHeadMm");
                if (ph.HasValue) spec.tiePitchHeadMm = ph;

                debug = new JObject
                {
                    ["mainBarsPerFace"] = spec.mainBarsPerFace,
                    ["mainBarsPerFaceX"] = spec.mainBarsPerFaceX,
                    ["mainBarsPerFaceY"] = spec.mainBarsPerFaceY,
                    ["mainBarsPerFaceXHead"] = spec.mainBarsPerFaceXHead,
                    ["mainBarsPerFaceXFoot"] = spec.mainBarsPerFaceXFoot,
                    ["mainBarsPerFaceYHead"] = spec.mainBarsPerFaceYHead,
                    ["mainBarsPerFaceYFoot"] = spec.mainBarsPerFaceYFoot,
                    ["mainBarTotalCount"] = spec.mainBarTotalCount,
                    ["mainBarTotalCountHead"] = spec.mainBarTotalCountHead,
                    ["mainBarTotalCountFoot"] = spec.mainBarTotalCountFoot,
                    ["tiePatternJson"] = string.IsNullOrWhiteSpace(spec.tiePatternJson) ? null : "(present)",
                    ["tiePitchBaseMm"] = spec.tiePitchBaseMm,
                    ["tiePitchHeadMm"] = spec.tiePitchHeadMm
                };

                return spec.mainBarsPerFace.HasValue
                       || spec.mainBarsPerFaceX.HasValue
                       || spec.mainBarsPerFaceY.HasValue
                       || spec.mainBarsPerFaceXHead.HasValue
                       || spec.mainBarsPerFaceXFoot.HasValue
                       || spec.mainBarsPerFaceYHead.HasValue
                       || spec.mainBarsPerFaceYFoot.HasValue
                       || spec.mainBarTotalCount.HasValue
                       || spec.mainBarTotalCountHead.HasValue
                       || spec.mainBarTotalCountFoot.HasValue
                       || !string.IsNullOrWhiteSpace(spec.tiePatternJson)
                       || (spec.tiePitchBaseMm.HasValue && spec.tiePitchBaseMm.Value > 0.0)
                       || (spec.tiePitchHeadMm.HasValue && spec.tiePitchHeadMm.Value > 0.0);
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
                if (rk != "beam_top" && rk != "beam_bottom" && rk != "column_top" && rk != "column_bottom" && rk != "column_mid")
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
            else if (rk == "column_mid")
            {
                refFt = (axisStartFt + axisEndFt) / 2.0;
                ok = true;
                dbg = new JObject { ["source"] = "column_mid" };
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
                            var el = uidoc.Document.GetElement(id);
                            if (el == null) continue;
                            int catId = 0;
                            try { catId = el.Category != null && el.Category.Id != null ? el.Category.Id.IntValue() : 0; } catch { catId = 0; }
                            if (catId != (int)BuiltInCategory.OST_StructuralColumns
                                && catId != (int)BuiltInCategory.OST_StructuralFraming)
                                continue;
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

        private static bool TryGetSelectedTieShapeId(UIApplication uiapp, Document doc, out ElementId shapeId, out string shapeName)
        {
            shapeId = ElementId.InvalidElementId;
            shapeName = string.Empty;

            var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
            if (uidoc == null || doc == null) return false;

            try
            {
                foreach (var id in uidoc.Selection.GetElementIds())
                {
                    Element el = null;
                    try { el = doc.GetElement(id); } catch { el = null; }
                    if (el == null) continue;

                    var rebar = el as Autodesk.Revit.DB.Structure.Rebar;
                    if (rebar == null) continue;

                    ElementId sid = ElementId.InvalidElementId;
                    try
                    {
                        var pShape = rebar.get_Parameter(BuiltInParameter.REBAR_SHAPE);
                        if (pShape != null) sid = pShape.AsElementId();
                    }
                    catch { /* ignore */ }

                    if (sid == null || sid == ElementId.InvalidElementId) continue;

                    shapeId = sid;
                    try
                    {
                        var shape = doc.GetElement(sid) as Autodesk.Revit.DB.Structure.RebarShape;
                        if (shape != null) shapeName = shape.Name ?? string.Empty;
                    }
                    catch { /* ignore */ }

                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        public static JObject BuildPlan(UIApplication uiapp, Document doc, JObject p)
        {
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            if (!TryCollectHostIds(uiapp, p, out var hostIds, out var code, out var msg))
                return ResultUtil.Err(msg, string.IsNullOrWhiteSpace(code) ? "INVALID_ARGS" : code);

            var opts = AutoOptions.Parse(p["options"] as JObject);
            var profile = (p.Value<string>("profile") ?? string.Empty).Trim();
            if (profile.Length == 0) profile = null;

            // Concrete detection tokens (include/exclude) and material-class filters
            var concreteTokens = (opts.concreteTokens != null && opts.concreteTokens.Length > 0) ? opts.concreteTokens : DefaultConcreteTokens;
            var concreteExcludeTokens = (opts.concreteExcludeTokens != null && opts.concreteExcludeTokens.Length > 0) ? opts.concreteExcludeTokens : DefaultConcreteExcludeTokens;
            var concreteExcludeMaterialClasses = (opts.concreteExcludeMaterialClasses != null && opts.concreteExcludeMaterialClasses.Length > 0)
                ? opts.concreteExcludeMaterialClasses
                : DefaultConcreteExcludeMaterialClasses;

            // Optional: use a selected stirrup/hoop sample to choose RebarShape for column ties.
            ElementId selectedTieShapeId = ElementId.InvalidElementId;
            string selectedTieShapeName = string.Empty;
            try
            {
                if (opts.columnTieShapeId > 0)
                {
                    selectedTieShapeId = ElementIdCompat.From(opts.columnTieShapeId);
                    var shape = doc.GetElement(selectedTieShapeId) as Autodesk.Revit.DB.Structure.RebarShape;
                    if (shape != null) selectedTieShapeName = shape.Name ?? string.Empty;
                }
                if (selectedTieShapeId == null || selectedTieShapeId == ElementId.InvalidElementId)
                {
                    if (opts.columnTieShapeFromSelection)
                    {
                        TryGetSelectedTieShapeId(uiapp, doc, out selectedTieShapeId, out selectedTieShapeName);
                    }
                }
            }
            catch { /* ignore */ }

            bool hasDeleteTagged = p["deleteExistingTaggedInHosts"] != null;
            bool hasDeleteUntagged = p["deleteExistingUntaggedInHosts"] != null;
            bool hasDeleteAll = p["deleteExistingAllInHosts"] != null;

            // Default behavior: delete tool-tagged rebars before creation.
            bool deleteExistingTaggedInHosts = hasDeleteTagged ? (p.Value<bool?>("deleteExistingTaggedInHosts") ?? false) : true;
            bool deleteExistingUntaggedInHosts = hasDeleteUntagged ? (p.Value<bool?>("deleteExistingUntaggedInHosts") ?? false) : false;
            bool deleteExistingAllInHosts = hasDeleteAll ? (p.Value<bool?>("deleteExistingAllInHosts") ?? false) : false;

            try
            {
                var o = p["options"] as JObject;
                if (o != null)
                {
                    if (!hasDeleteTagged && o["deleteExistingTaggedInHosts"] != null)
                        deleteExistingTaggedInHosts = o.Value<bool?>("deleteExistingTaggedInHosts") ?? deleteExistingTaggedInHosts;
                    if (!hasDeleteUntagged && o["deleteExistingUntaggedInHosts"] != null)
                        deleteExistingUntaggedInHosts = o.Value<bool?>("deleteExistingUntaggedInHosts") ?? deleteExistingUntaggedInHosts;
                    if (!hasDeleteAll && o["deleteExistingAllInHosts"] != null)
                        deleteExistingAllInHosts = o.Value<bool?>("deleteExistingAllInHosts") ?? deleteExistingAllInHosts;
                }
            }
            catch { /* ignore */ }

            if (deleteExistingAllInHosts)
            {
                deleteExistingTaggedInHosts = true;
                deleteExistingUntaggedInHosts = true;
            }

             var mappingKeysCommon = new[]
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
                 "Host.Cover.Other"
             };

             var mappingKeysColumnAttr = new[]
             {
                 // Column attribute keys (optional; prefer attributes over hardcoded options)
                 "Column.Attr.MainBar.BarsPerFace",
                 "Column.Attr.MainBar.BarsPerFaceX",
                 "Column.Attr.MainBar.BarsPerFaceY",
                 "Column.Attr.MainBar.BarsPerFaceXHead",
                 "Column.Attr.MainBar.BarsPerFaceXFoot",
                 "Column.Attr.MainBar.BarsPerFaceYHead",
                 "Column.Attr.MainBar.BarsPerFaceYFoot",
                 "Column.Attr.MainBar.TotalCount",
                 "Column.Attr.MainBar.TotalCountHead",
                 "Column.Attr.MainBar.TotalCountFoot",
                 "Column.Attr.Tie.PatternJson",
                 // Column tie zoning (optional)
                 "Column.Attr.Tie.PitchBaseMm",
                 "Column.Attr.Tie.PitchHeadMm"
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
                // Beam main bars (zoned counts: start/mid/end)
                "Beam.Attr.MainBar.TopCountStart",
                "Beam.Attr.MainBar.TopCountMid",
                "Beam.Attr.MainBar.TopCountEnd",
                "Beam.Attr.MainBar.TopCount2Start",
                "Beam.Attr.MainBar.TopCount2Mid",
                "Beam.Attr.MainBar.TopCount2End",
                "Beam.Attr.MainBar.TopCount3Start",
                "Beam.Attr.MainBar.TopCount3Mid",
                "Beam.Attr.MainBar.TopCount3End",
                "Beam.Attr.MainBar.BottomCountStart",
                "Beam.Attr.MainBar.BottomCountMid",
                "Beam.Attr.MainBar.BottomCountEnd",
                "Beam.Attr.MainBar.BottomCount2Start",
                "Beam.Attr.MainBar.BottomCount2Mid",
                "Beam.Attr.MainBar.BottomCount2End",
                "Beam.Attr.MainBar.BottomCount3Start",
                "Beam.Attr.MainBar.BottomCount3Mid",
                "Beam.Attr.MainBar.BottomCount3End",
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
                if (selectedTieShapeId != null && selectedTieShapeId != ElementId.InvalidElementId)
                {
                    hostObj["columnTieShape"] = new JObject
                    {
                        ["id"] = selectedTieShapeId.IntValue(),
                        ["name"] = selectedTieShapeName
                    };
                }

                JObject resolved;
                IEnumerable<string> keyList = mappingKeysCommon;
                try
                {
                    if (isColumn)
                        keyList = keyList.Concat(mappingKeysColumnAttr);

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
                                    // Include beam attribute keys whenever the profile defines ANY Beam.Attr.* key,
                                    // not only mid-pitch. This ensures start/end/center settings are all honored.
                                    try
                                    {
                                        if (prof.Keys.Any(k => k.StartsWith("Beam.Attr.", StringComparison.OrdinalIgnoreCase)))
                                        {
                                            includeBeamKeys = true;
                                        }
                                    }
                                    catch
                                    {
                                        // Fallback: previous minimal checks (still allow start/end/mid)
                            if (prof.TryGetEntry("Beam.Attr.MainBar.DiameterMm", out var _)
                                || prof.TryGetEntry("Beam.Attr.MainBar.TopCount", out var _2)
                                || prof.TryGetEntry("Beam.Attr.MainBar.TopCount2", out var _2b)
                                || prof.TryGetEntry("Beam.Attr.MainBar.TopCount3", out var _2c)
                                || prof.TryGetEntry("Beam.Attr.MainBar.BottomCount", out var _3a)
                                || prof.TryGetEntry("Beam.Attr.MainBar.BottomCount2", out var _3b)
                                || prof.TryGetEntry("Beam.Attr.MainBar.BottomCount3", out var _3c)
                                || prof.TryGetEntry("Beam.Attr.MainBar.TopCountStart", out var _zs1)
                                || prof.TryGetEntry("Beam.Attr.MainBar.TopCountMid", out var _zm1)
                                || prof.TryGetEntry("Beam.Attr.MainBar.TopCountEnd", out var _ze1)
                                || prof.TryGetEntry("Beam.Attr.MainBar.BottomCountStart", out var _zs2)
                                || prof.TryGetEntry("Beam.Attr.MainBar.BottomCountMid", out var _zm2)
                                || prof.TryGetEntry("Beam.Attr.MainBar.BottomCountEnd", out var _ze2)
                                || prof.TryGetEntry("Beam.Attr.Stirrup.DiameterMm", out var _4)
                                || prof.TryGetEntry("Beam.Attr.Stirrup.PitchMidMm", out var _5)
                                || prof.TryGetEntry("Beam.Attr.Stirrup.PitchStartMm", out var _6)
                                || prof.TryGetEntry("Beam.Attr.Stirrup.PitchEndMm", out var _7))
                                        {
                                            includeBeamKeys = true;
                                        }
                                    }
                                }
                            }
                        }
                        catch { includeBeamKeys = false; }

                        if (includeBeamKeys)
                            keyList = keyList.Concat(mappingKeysBeamAttr);
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

                // Circular columns may expose a single cover parameter (e.g., "かぶり厚-丸").
                // If present, apply it to all faces and axis ends.
                try
                {
                    double mm;
                    string hit;
                    var roundNames = (opts.coverRoundParamNames != null && opts.coverRoundParamNames.Length > 0)
                        ? opts.coverRoundParamNames
                        : new[] { "かぶり厚-丸", "Rebar Cover - Round", "CoverRound", "Cover_Round" };
                    if (TryGetHostCoverParamMm(host, roundNames, out mm, out hit) && mm >= 0.0)
                    {
                        coverTopMm = mm;
                        coverBottomMm = mm;
                        coverOtherMm = mm;
                        coverTopSource = "instanceParam:" + hit;
                        coverBottomSource = "instanceParam:" + hit;
                        coverOtherSource = "instanceParam:" + hit;
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

                int columnMainBarsPerFaceXEffective = Math.Max(2, opts.columnMainBarsPerFace);
                int columnMainBarsPerFaceYEffective = Math.Max(2, opts.columnMainBarsPerFace);
                string columnMainBarsPerFaceXSource = "options";
                string columnMainBarsPerFaceYSource = "options";
                int columnMainBarTotalCountEffective = 0;
                string columnMainBarTotalCountSource = "none";
                int columnMainBarsPerFaceXHead = 0, columnMainBarsPerFaceXFoot = 0;
                int columnMainBarsPerFaceYHead = 0, columnMainBarsPerFaceYFoot = 0;
                int columnMainBarTotalCountHead = 0, columnMainBarTotalCountFoot = 0;
                bool hasHeadFootCounts = false;
                try
                {
                    if (isColumn && colSpec != null)
                    {
                        bool hasXY = (colSpec.mainBarsPerFaceX.HasValue && colSpec.mainBarsPerFaceX.Value >= 2)
                                     || (colSpec.mainBarsPerFaceY.HasValue && colSpec.mainBarsPerFaceY.Value >= 2);

                        if (hasXY)
                        {
                            if (colSpec.mainBarsPerFaceX.HasValue && colSpec.mainBarsPerFaceX.Value >= 2)
                            {
                                columnMainBarsPerFaceXEffective = colSpec.mainBarsPerFaceX.Value;
                                columnMainBarsPerFaceXSource = "mapping";
                            }
                            if (colSpec.mainBarsPerFaceY.HasValue && colSpec.mainBarsPerFaceY.Value >= 2)
                            {
                                columnMainBarsPerFaceYEffective = colSpec.mainBarsPerFaceY.Value;
                                columnMainBarsPerFaceYSource = "mapping";
                            }

                            // If only one side is specified, apply it to both (common BIM convention).
                            if (columnMainBarsPerFaceXSource != "mapping" && columnMainBarsPerFaceYSource == "mapping")
                            {
                                columnMainBarsPerFaceXEffective = columnMainBarsPerFaceYEffective;
                                columnMainBarsPerFaceXSource = "mapping";
                            }
                            if (columnMainBarsPerFaceYSource != "mapping" && columnMainBarsPerFaceXSource == "mapping")
                            {
                                columnMainBarsPerFaceYEffective = columnMainBarsPerFaceXEffective;
                                columnMainBarsPerFaceYSource = "mapping";
                            }
                        }
                        else if (colSpec.mainBarsPerFace.HasValue && colSpec.mainBarsPerFace.Value >= 2)
                        {
                            columnMainBarsPerFaceXEffective = colSpec.mainBarsPerFace.Value;
                            columnMainBarsPerFaceYEffective = colSpec.mainBarsPerFace.Value;
                            columnMainBarsPerFaceXSource = "mapping";
                            columnMainBarsPerFaceYSource = "mapping";
                        }

                        if (colSpec.mainBarTotalCount.HasValue && colSpec.mainBarTotalCount.Value > 0)
                        {
                            columnMainBarTotalCountEffective = Math.Max(0, colSpec.mainBarTotalCount.Value);
                            columnMainBarTotalCountSource = "mapping";
                        }

                        // Head/Foot overrides (if present)
                        if (colSpec.mainBarsPerFaceXHead.HasValue && colSpec.mainBarsPerFaceXHead.Value >= 2)
                        {
                            columnMainBarsPerFaceXHead = colSpec.mainBarsPerFaceXHead.Value;
                            hasHeadFootCounts = true;
                        }
                        if (colSpec.mainBarsPerFaceXFoot.HasValue && colSpec.mainBarsPerFaceXFoot.Value >= 2)
                        {
                            columnMainBarsPerFaceXFoot = colSpec.mainBarsPerFaceXFoot.Value;
                            hasHeadFootCounts = true;
                        }
                        if (colSpec.mainBarsPerFaceYHead.HasValue && colSpec.mainBarsPerFaceYHead.Value >= 2)
                        {
                            columnMainBarsPerFaceYHead = colSpec.mainBarsPerFaceYHead.Value;
                            hasHeadFootCounts = true;
                        }
                        if (colSpec.mainBarsPerFaceYFoot.HasValue && colSpec.mainBarsPerFaceYFoot.Value >= 2)
                        {
                            columnMainBarsPerFaceYFoot = colSpec.mainBarsPerFaceYFoot.Value;
                            hasHeadFootCounts = true;
                        }
                        if (colSpec.mainBarTotalCountHead.HasValue && colSpec.mainBarTotalCountHead.Value > 0)
                        {
                            columnMainBarTotalCountHead = colSpec.mainBarTotalCountHead.Value;
                            hasHeadFootCounts = true;
                        }
                        if (colSpec.mainBarTotalCountFoot.HasValue && colSpec.mainBarTotalCountFoot.Value > 0)
                        {
                            columnMainBarTotalCountFoot = colSpec.mainBarTotalCountFoot.Value;
                            hasHeadFootCounts = true;
                        }
                    }
                }
                catch { /* ignore */ }
                if (isColumn)
                {
                    hostObj["columnMainBarsPerFace"] = new JObject
                    {
                        ["value"] = (columnMainBarsPerFaceXEffective == columnMainBarsPerFaceYEffective) ? (JToken)columnMainBarsPerFaceXEffective : JValue.CreateNull(),
                        ["source"] = (columnMainBarsPerFaceXSource == columnMainBarsPerFaceYSource) ? (JToken)columnMainBarsPerFaceXSource : "mixed",
                        ["x"] = columnMainBarsPerFaceXEffective,
                        ["y"] = columnMainBarsPerFaceYEffective,
                        ["sourceX"] = columnMainBarsPerFaceXSource,
                        ["sourceY"] = columnMainBarsPerFaceYSource
                    };
                    if (columnMainBarTotalCountEffective > 0)
                    {
                        hostObj["columnMainBarTotalCount"] = new JObject
                        {
                            ["value"] = columnMainBarTotalCountEffective,
                            ["source"] = columnMainBarTotalCountSource
                        };
                    }
                    if (hasHeadFootCounts)
                    {
                        hostObj["columnMainBarHeadFootCounts"] = new JObject
                        {
                            ["xHead"] = columnMainBarsPerFaceXHead > 0 ? (JToken)columnMainBarsPerFaceXHead : JValue.CreateNull(),
                            ["xFoot"] = columnMainBarsPerFaceXFoot > 0 ? (JToken)columnMainBarsPerFaceXFoot : JValue.CreateNull(),
                            ["yHead"] = columnMainBarsPerFaceYHead > 0 ? (JToken)columnMainBarsPerFaceYHead : JValue.CreateNull(),
                            ["yFoot"] = columnMainBarsPerFaceYFoot > 0 ? (JToken)columnMainBarsPerFaceYFoot : JValue.CreateNull(),
                            ["totalHead"] = columnMainBarTotalCountHead > 0 ? (JToken)columnMainBarTotalCountHead : JValue.CreateNull(),
                            ["totalFoot"] = columnMainBarTotalCountFoot > 0 ? (JToken)columnMainBarTotalCountFoot : JValue.CreateNull()
                        };
                    }
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

                double tieBarDiaFt = 0.0;
                try
                {
                    if (tieBarType != null && (opts.includeTies || opts.includeStirrups))
                        tieBarDiaFt = tieBarType.BarModelDiameter;
                }
                catch { /* ignore */ }

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

                // Detect circular column section (best-effort) and diameter
                bool isCircularColumn = false;
                double columnDiameterFt = 0.0;
                string columnCircularSource = null;
                try
                {
                    if (isColumn)
                    {
                        ElementType typeElem = null;
                        try
                        {
                            var tid = host?.GetTypeId();
                            if (tid != null && tid != ElementId.InvalidElementId)
                                typeElem = doc.GetElement(tid) as ElementType;
                        }
                        catch { typeElem = null; }

                        if (TryDetectCircularColumn(doc, host, typeElem, localBox, crossA, crossB, out var diaFt, out var src))
                        {
                            isCircularColumn = true;
                            columnDiameterFt = diaFt;
                            columnCircularSource = src;
                        }

                        hostObj["columnSection"] = new JObject
                        {
                            ["shape"] = isCircularColumn ? "circular" : "rectangular",
                            ["diameterMm"] = isCircularColumn ? (JToken)UnitHelper.FtToMm(columnDiameterFt) : JValue.CreateNull(),
                            ["source"] = string.IsNullOrWhiteSpace(columnCircularSource) ? null : columnCircularSource
                        };
                    }
                }
                catch { /* ignore */ }

                var actions = new JArray();
                hostObj["actions"] = actions;

                // Axis-end cover uses coverTop/Bottom unless they are zero/unset; then fall back to per-face values.
                double axisCoverTopMm = coverTopMm;
                double axisCoverBottomMm = coverBottomMm;
                string axisCoverTopSource = coverTopSource;
                string axisCoverBottomSource = coverBottomSource;
                if (axisCoverTopMm <= 0.0 && faceUpMm > 0.0)
                {
                    axisCoverTopMm = faceUpMm;
                    axisCoverTopSource = "faceUp";
                }
                if (axisCoverBottomMm <= 0.0 && faceDownMm > 0.0)
                {
                    axisCoverBottomMm = faceDownMm;
                    axisCoverBottomSource = "faceDown";
                }
                // If min-cover clamped face values are larger, honor them for axis ends too (unless later zeroed by concrete-neighbor check).
                if (faceUpMm > axisCoverTopMm)
                {
                    axisCoverTopMm = faceUpMm;
                    axisCoverTopSource = "faceUp";
                }
                if (faceDownMm > axisCoverBottomMm)
                {
                    axisCoverBottomMm = faceDownMm;
                    axisCoverBottomSource = "faceDown";
                }
                if (isColumn)
                {
                    hostObj["columnAxisEndCoverBaseMm"] = new JObject
                    {
                        ["top"] = axisCoverTopMm,
                        ["bottom"] = axisCoverBottomMm,
                        ["sourceTop"] = axisCoverTopSource,
                        ["sourceBottom"] = axisCoverBottomSource
                    };
                }

                double coverTopFt = UnitHelper.MmToFt(axisCoverTopMm);
                double coverBottomFt = UnitHelper.MmToFt(axisCoverBottomMm);
                double coverOtherFt = UnitHelper.MmToFt(coverOtherMm);
                double faceUpFt = UnitHelper.MmToFt(faceUpMm);
                double faceDownFt = UnitHelper.MmToFt(faceDownMm);
                double faceLeftFt = UnitHelper.MmToFt(faceLeftMm);
                double faceRightFt = UnitHelper.MmToFt(faceRightMm);

                var axisBasis = GetBasisVectorByIndex(tr, axisIndex);
                if (axisBasis.GetLength() < 1e-9) axisBasis = XYZ.BasisZ;

                // Column axis direction vs world Z (some families may have inverted local Z).
                bool axisPositiveIsUp = true;
                double axisDotZ = 0.0;
                try
                {
                    var n = axisBasis;
                    if (n != null && n.GetLength() > 1e-9)
                    {
                        n = n.Normalize();
                        axisDotZ = n.DotProduct(XYZ.BasisZ);
                        axisPositiveIsUp = axisDotZ >= 0.0;
                    }
                }
                catch { axisPositiveIsUp = true; axisDotZ = 0.0; }
                if (isColumn)
                {
                    hostObj["axisPositiveIsUp"] = axisPositiveIsUp;
                    hostObj["axisDotZ"] = Math.Round(axisDotZ, 6);
                }
  
                // Beam "start/end" definition:
                //  - start = LocationCurve.EndPoint(0)
                //  - end   = LocationCurve.EndPoint(1)
                // The axis range computations typically use min/max along axisIndex, so we keep a mapping flag here.
                bool beamStartIsMinAxis = true; // default (safe)
                try
                {
                    if (isFraming && TryGetBeamAxisCoordsFromLocationCurve(host, tr, axisIndex, out var end0Axis, out var end1Axis))
                    {
                        beamStartIsMinAxis = end0Axis <= end1Axis;
                        hostObj["beamAxisStartEndByLocationCurve"] = new JObject
                        {
                            ["end0AxisMm"] = UnitHelper.FtToMm(end0Axis),
                            ["end1AxisMm"] = UnitHelper.FtToMm(end1Axis),
                            ["startIsMinAxis"] = beamStartIsMinAxis,
                            ["startDefinition"] = "LocationCurve.EndPoint(0)",
                            ["endDefinition"] = "LocationCurve.EndPoint(1)"
                        };
                    }
                }
                catch { /* ignore */ }

                // Column: detect concrete neighbors (structural column / structural foundation) above/below,
                // and use it to decide whether axis-end cover should be applied.
                bool columnHasConcreteAbove = false;
                bool columnHasConcreteBelow = false;
                if (isColumn)
                {
                    if (opts.columnAxisEndCoverUsesConcreteNeighborCheck)
                    {
                        try
                        {
                            double searchRangeFt = UnitHelper.MmToFt(Math.Max(0.0, opts.columnConcreteNeighborSearchRangeMm));
                            double touchTolFt = UnitHelper.MmToFt(Math.Max(0.0, opts.columnConcreteNeighborTouchTolMm));

                            double baseMinCol = GetMinByIndex(localBox, axisIndex);
                            double baseMaxCol = GetMaxByIndex(localBox, axisIndex);
                            double topPlaneCol = axisPositiveIsUp ? baseMaxCol : baseMinCol;
                            double bottomPlaneCol = axisPositiveIsUp ? baseMinCol : baseMaxCol;
                            double aMid = (GetMinByIndex(localBox, crossA) + GetMaxByIndex(localBox, crossA)) / 2.0;
                            double bMid = (GetMinByIndex(localBox, crossB) + GetMaxByIndex(localBox, crossB)) / 2.0;

                            XYZ bottomWorld = null;
                            XYZ topWorld = null;
                            try
                            {
                                var wMin = tr.OfPoint(MakeLocalPoint(axisIndex, baseMinCol, crossA, aMid, crossB, bMid));
                                var wMax = tr.OfPoint(MakeLocalPoint(axisIndex, baseMaxCol, crossA, aMid, crossB, bMid));
                                bottomWorld = axisPositiveIsUp ? wMin : wMax;
                                topWorld = axisPositiveIsUp ? wMax : wMin;
                            }
                            catch { bottomWorld = null; topWorld = null; }

                            var aboveMatches = new JArray();
                            var belowMatches = new JArray();

                            Transform invTr = null;
                            try { invTr = tr.Inverse; } catch { invTr = Transform.Identity; }

                            IEnumerable<int> GetJoinedCandidateIdsSafe()
                            {
                                var ids = new List<int>();
                                try
                                {
                                    var joined = JoinGeometryUtils.GetJoinedElements(doc, host);
                                    if (joined != null)
                                    {
                                        foreach (var jid in joined)
                                        {
                                            try
                                            {
                                                int v = jid.IntValue();
                                                if (v != 0) ids.Add(v);
                                            }
                                            catch { /* ignore */ }
                                        }
                                    }
                                }
                                catch { /* ignore */ }
                                return ids;
                            }

                            void EvalCandidate(int id, bool forTop)
                            {
                                if (id == 0) return;
                                if (id == host.Id.IntValue()) return;

                                Element e = null;
                                try { e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)); } catch { e = null; }
                                if (e == null) return;

                                int catId = 0;
                                try { catId = e.Category != null ? e.Category.Id.IntValue() : 0; } catch { catId = 0; }

                                bool isCandidateColumn = catId == (int)BuiltInCategory.OST_StructuralColumns || catId == (int)BuiltInCategory.OST_Columns;
                                bool isCandidateFoundation = catId == (int)BuiltInCategory.OST_StructuralFoundation;
                                if (!isCandidateColumn && !isCandidateFoundation) return;

                                if (!TryGetElementAxisRangeInHostLocal(e, invTr, axisIndex, out var cMin, out var cMax)) return;

                                // Require cross-section overlap in host local coordinates to avoid false positives
                                // from nearby (but not connected) concrete elements.
                                XYZ cMinPt;
                                XYZ cMaxPt;
                                if (!TryGetElementLocalBounds(e, invTr, out cMinPt, out cMaxPt)) return;
                                double cAmin = GetByIndex(cMinPt, crossA);
                                double cAmax = GetByIndex(cMaxPt, crossA);
                                double cBmin = GetByIndex(cMinPt, crossB);
                                double cBmax = GetByIndex(cMaxPt, crossB);

                                double hAmin = GetMinByIndex(localBox, crossA);
                                double hAmax = GetMaxByIndex(localBox, crossA);
                                double hBmin = GetMinByIndex(localBox, crossB);
                                double hBmax = GetMaxByIndex(localBox, crossB);

                                double overlapTol = touchTolFt;
                                bool overlapA = cAmax >= (hAmin - overlapTol) && cAmin <= (hAmax + overlapTol);
                                bool overlapB = cBmax >= (hBmin - overlapTol) && cBmin <= (hBmax + overlapTol);
                                if (!overlapA || !overlapB) return;

                                double plane = forTop ? topPlaneCol : bottomPlaneCol;
                                bool touches = cMin <= plane + touchTolFt && cMax >= plane - touchTolFt;
                                if (!touches) return;

                                // Determine whether the candidate extends outward beyond the plane on the world-top/world-bottom side.
                                bool extendsOutward = false;
                                if (axisPositiveIsUp)
                                    extendsOutward = forTop ? (cMax > plane + touchTolFt) : (cMin < plane - touchTolFt);
                                else
                                    extendsOutward = forTop ? (cMin < plane - touchTolFt) : (cMax > plane + touchTolFt);
                                if (!extendsOutward) return;

                                if (!TryElementLooksConcrete(doc, e, concreteTokens, concreteExcludeTokens, concreteExcludeMaterialClasses,
                                    out var matName, out var matSource, out var token)) return;

                                var item = new JObject
                                {
                                    ["elementId"] = id,
                                    ["category"] = e.Category != null ? e.Category.Name : null,
                                    ["catId"] = catId,
                                    ["materialName"] = matName,
                                    ["materialSource"] = matSource,
                                    ["matchedToken"] = token,
                                    ["axisMinMm"] = UnitHelper.FtToMm(cMin),
                                    ["axisMaxMm"] = UnitHelper.FtToMm(cMax)
                                };

                                if (forTop)
                                {
                                    columnHasConcreteAbove = true;
                                    aboveMatches.Add(item);
                                }
                                else
                                {
                                    columnHasConcreteBelow = true;
                                    belowMatches.Add(item);
                                }
                            }

                            var topCand = new HashSet<int>();
                            var bottomCand = new HashSet<int>();
                            foreach (var id in GetJoinedCandidateIdsSafe()) { topCand.Add(id); bottomCand.Add(id); }
                            if (topWorld != null)
                            {
                                foreach (var id in CollectColumnLikeIdsNearPoint(doc, topWorld, searchRangeFt)) topCand.Add(id);
                                foreach (var id in CollectFoundationLikeIdsNearPoint(doc, topWorld, searchRangeFt)) topCand.Add(id);
                            }
                            if (bottomWorld != null)
                            {
                                foreach (var id in CollectColumnLikeIdsNearPoint(doc, bottomWorld, searchRangeFt)) bottomCand.Add(id);
                                foreach (var id in CollectFoundationLikeIdsNearPoint(doc, bottomWorld, searchRangeFt)) bottomCand.Add(id);
                            }

                            foreach (var id in topCand) EvalCandidate(id, true);
                            foreach (var id in bottomCand) EvalCandidate(id, false);

                            hostObj["columnAxisEndConcreteNeighbor"] = new JObject
                            {
                                ["enabled"] = true,
                                ["searchRangeMm"] = opts.columnConcreteNeighborSearchRangeMm,
                                ["touchTolMm"] = opts.columnConcreteNeighborTouchTolMm,
                                ["hasConcreteAbove"] = columnHasConcreteAbove,
                                ["hasConcreteBelow"] = columnHasConcreteBelow,
                                ["matchesAbove"] = aboveMatches.Count > 0 ? (JToken)aboveMatches : JValue.CreateNull(),
                                ["matchesBelow"] = belowMatches.Count > 0 ? (JToken)belowMatches : JValue.CreateNull()
                            };
                        }
                        catch (Exception ex)
                        {
                            columnHasConcreteAbove = false;
                            columnHasConcreteBelow = false;
                            hostObj["columnAxisEndConcreteNeighbor"] = new JObject
                            {
                                ["enabled"] = true,
                                ["hasConcreteAbove"] = false,
                                ["hasConcreteBelow"] = false,
                                ["error"] = ex.Message
                            };
                        }
                    }
                    else
                    {
                        hostObj["columnAxisEndConcreteNeighbor"] = new JObject { ["enabled"] = false };
                    }
                }

                // Clear span for beam zoning (used by main bars + stirrups).
                double clearStartFt = double.NaN;
                double clearEndFt = double.NaN;
                bool hasClearRange = false;
                double beamAxisMinFt = double.NaN;
                double beamAxisMaxFt = double.NaN;
                bool beamAxisRangeValid = false;
                JObject beamSupportFaceDbg = null;

                if (isFraming)
                {
                    try
                    {
                        beamAxisMinFt = GetMinByIndex(localBox, axisIndex);
                        beamAxisMaxFt = GetMaxByIndex(localBox, axisIndex);
                        // Prefer solid-geometry-derived extents when available (captures cutbacks at supports).
                        try
                        {
                            if (TryGetHostAxisRangeFromSolidGeometry(host, tr, axisIndex, out var gMin, out var gMax, out var gMsg) && (gMax > gMin))
                            {
                                beamAxisMinFt = gMin;
                                beamAxisMaxFt = gMax;
                                hostObj["beamAxisRangeFrom_shared"] = "solidGeometry";
                                hostObj["beamAxisRangeSolidMm_shared"] = new JObject
                                {
                                    ["min"] = UnitHelper.FtToMm(gMin),
                                    ["max"] = UnitHelper.FtToMm(gMax),
                                    ["length"] = UnitHelper.FtToMm(gMax - gMin)
                                };
                            }
                            else
                            {
                                hostObj["beamAxisRangeFrom_shared"] = "bbox";
                                if (!string.IsNullOrWhiteSpace(gMsg)) hostObj["beamAxisRangeSolidError_shared"] = gMsg;
                            }
                        }
                        catch { /* ignore */ }

                        beamAxisRangeValid = (beamAxisMaxFt > beamAxisMinFt);

                        // Best-effort: move clear span to connected column faces (support faces).
                        if (TryAdjustBeamAxisRangeToJoinedColumnFaces(doc, host, tr, axisIndex, beamAxisMinFt, beamAxisMaxFt, out var adjMin, out var adjMax, out var dbg) && (adjMax > adjMin))
                        {
                            clearStartFt = adjMin;
                            clearEndFt = adjMax;
                            hasClearRange = true;
                            beamSupportFaceDbg = dbg;
                        }
                        else
                        {
                            clearStartFt = beamAxisMinFt;
                            clearEndFt = beamAxisMaxFt;
                            hasClearRange = beamAxisRangeValid;
                        }

                        if (hasClearRange)
                        {
                            hostObj["beamClearAxisRangeMm"] = new JObject
                            {
                                ["min"] = UnitHelper.FtToMm(clearStartFt),
                                ["max"] = UnitHelper.FtToMm(clearEndFt),
                                ["length"] = UnitHelper.FtToMm(clearEndFt - clearStartFt)
                            };
                        }
                    }
                    catch { /* ignore */ }
                }

                // Main bars (4 corners)
                 if (opts.includeMainBars)
                  {
                     double r = mainBarType.BarModelDiameter / 2.0;
 
                     double bboxMin = GetMinByIndex(localBox, axisIndex);
                     double bboxMax = GetMaxByIndex(localBox, axisIndex);
                     double baseMin = (isFraming && beamAxisRangeValid) ? beamAxisMinFt : bboxMin;
                     double baseMax = (isFraming && beamAxisRangeValid) ? beamAxisMaxFt : bboxMax;
                     if (isFraming)
                     {
                        // Keep the raw bbox range for debugging.
                        try
                        {
                            hostObj["beamAxisRangeBboxMm_raw"] = new JObject
                            {
                                ["min"] = UnitHelper.FtToMm(bboxMin),
                                ["max"] = UnitHelper.FtToMm(bboxMax),
                                ["length"] = UnitHelper.FtToMm(bboxMax - bboxMin)
                            };
                        }
                        catch { /* ignore */ }

                        // IMPORTANT: For beams, LocationCurve endpoints may be at the support centerline,
                        // while the physical geometry is cut back to the support face. Using solid/bbox extents
                        // keeps rebar start/end aligned to the actual concrete faces (typical RC detailing).
                        hostObj["beamAxisSource"] = beamAxisRangeValid ? "shared" : "bbox";

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
                        // If a shared range was already computed, reuse it to keep main bars/stirrups consistent.
                        if (beamAxisRangeValid)
                        {
                            hostObj["beamAxisRangeFrom"] = "shared";
                            if (hostObj["beamAxisRangeSolidMm_shared"] != null)
                                hostObj["beamAxisRangeSolidMm"] = hostObj["beamAxisRangeSolidMm_shared"];
                            if (hostObj["beamAxisRangeSolidError_shared"] != null)
                                hostObj["beamAxisRangeSolidError"] = hostObj["beamAxisRangeSolidError_shared"];
                        }
                        else
                        {
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
                        }

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
 
                    double colAxisCoverBottomFt = coverBottomFt;
                    double colAxisCoverTopFt = coverTopFt;
                    if (isColumn && opts.columnAxisEndCoverUsesConcreteNeighborCheck)
                    {
                        // When the end is not exposed (concrete neighbor exists), axis-direction cover is not needed.
                        if (columnHasConcreteBelow) colAxisCoverBottomFt = 0.0;
                        if (columnHasConcreteAbove) colAxisCoverTopFt = 0.0;
                        hostObj["columnAxisEndCoverEffectiveMm"] = new JObject
                        {
                            ["bottom"] = UnitHelper.FtToMm(colAxisCoverBottomFt),
                            ["top"] = UnitHelper.FtToMm(colAxisCoverTopFt),
                            ["source"] = "concreteNeighborCheck"
                        };
                    }

                    double colAxisCoverMinFt = colAxisCoverBottomFt;
                    double colAxisCoverMaxFt = colAxisCoverTopFt;
                    if (isColumn)
                    {
                        // baseMin/baseMax are local min/max on axisIndex. Map world-top/world-bottom cover to local ends
                        // based on whether positive axis points upward.
                        colAxisCoverMinFt = axisPositiveIsUp ? colAxisCoverBottomFt : colAxisCoverTopFt;
                        colAxisCoverMaxFt = axisPositiveIsUp ? colAxisCoverTopFt : colAxisCoverBottomFt;
                    }

                    double axisStart = baseMin + (isColumn ? (colAxisCoverMinFt + r) : (coverOtherFt + r));
                    double axisEnd = baseMax - (isColumn ? (colAxisCoverMaxFt + r) : (coverOtherFt + r));

                    // Beam: embed main bars into support columns by ratio of column width (along beam axis).
                    if (isFraming)
                    {
                        // Beam axis-end cover:
                        // - If an end is connected to *concrete* structural column or structural framing, treat it as continuous (no cover at that end).
                        // - Otherwise (cantilever/free end), apply the same cover as the beam side faces.
                        bool beamHasConcreteAtMinEnd = false;
                        bool beamHasConcreteAtMaxEnd = false;
                        try
                        {
                            if (TryDetectConcreteSupportsAtBeamAxisEnds(
                                doc, host, tr, localBox, axisIndex, sideIndex, upIndex, baseMin, baseMax,
                                UnitHelper.MmToFt(opts.beamSupportSearchRangeMm),
                                UnitHelper.MmToFt(opts.beamSupportFaceToleranceMm),
                                concreteTokens, concreteExcludeTokens, concreteExcludeMaterialClasses,
                                out beamHasConcreteAtMinEnd, out beamHasConcreteAtMaxEnd, out var beamEndDbg) && beamEndDbg != null)
                            {
                                // Map min/max end results to "start/end" (LocationCurve.EndPoint(0/1)) for clarity.
                                beamEndDbg["startDefinition"] = "LocationCurve.EndPoint(0)";
                                beamEndDbg["endDefinition"] = "LocationCurve.EndPoint(1)";
                                beamEndDbg["startIsMinAxis"] = beamStartIsMinAxis;
                                beamEndDbg["hasConcreteAtStart"] = beamStartIsMinAxis ? beamHasConcreteAtMinEnd : beamHasConcreteAtMaxEnd;
                                beamEndDbg["hasConcreteAtEnd"] = beamStartIsMinAxis ? beamHasConcreteAtMaxEnd : beamHasConcreteAtMinEnd;
                                hostObj["beamAxisEndConcreteNeighbor"] = beamEndDbg;
                            }
                        }
                        catch { /* ignore */ }

                        // Use the larger of (Host.Cover.Other) and explicit per-face left/right covers (after clamp).
                        double beamEndCoverFt = Math.Max(coverOtherFt, Math.Max(faceLeftFt, faceRightFt));
                        double gapMinFt = beamHasConcreteAtMinEnd ? 0.0 : (beamEndCoverFt + r);
                        double gapMaxFt = beamHasConcreteAtMaxEnd ? 0.0 : (beamEndCoverFt + r);
                        axisStart = baseMin + gapMinFt;
                        axisEnd = baseMax - gapMaxFt;
                        try
                        {
                            hostObj["beamAxisEndCoverEffectiveMm"] = new JObject
                            {
                                ["source"] = "concreteSupport+sideCover",
                                ["sideCoverMm"] = UnitHelper.FtToMm(beamEndCoverFt),
                                ["minEnd"] = beamHasConcreteAtMinEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt),
                                ["maxEnd"] = beamHasConcreteAtMaxEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt),
                                ["start"] = beamStartIsMinAxis
                                    ? (beamHasConcreteAtMinEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt))
                                    : (beamHasConcreteAtMaxEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt)),
                                ["end"] = beamStartIsMinAxis
                                    ? (beamHasConcreteAtMaxEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt))
                                    : (beamHasConcreteAtMinEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt)),
                                ["gapMinEndMm"] = UnitHelper.FtToMm(gapMinFt),
                                ["gapMaxEndMm"] = UnitHelper.FtToMm(gapMaxFt),
                                ["startIsMinAxis"] = beamStartIsMinAxis
                            };
                        }
                        catch { /* ignore */ }

                        try
                        {
                            double ratio = opts.beamMainBarEmbedIntoSupportColumnRatio;
                            if (opts.beamMainBarEmbedIntoSupportColumns && ratio > 0.0)
                            {
                                if (TryGetBeamSupportColumnWidthsAlongAxis(doc, host, tr, localBox, axisIndex, sideIndex, upIndex, baseMin, baseMax,
                                        UnitHelper.MmToFt(opts.beamSupportSearchRangeMm),
                                        UnitHelper.MmToFt(opts.beamSupportFaceToleranceMm),
                                        concreteTokens, concreteExcludeTokens, concreteExcludeMaterialClasses,
                                        requireConcrete: false,
                                        out var startColWidthFt, out var endColWidthFt, out var dbg))
                                {
                                    // NOTE:
                                    // TryGetBeamSupportColumnWidthsAlongAxis returns widths at min/max ends on axisIndex.
                                    // Map to beam start/end by LocationCurve.EndPoint(0/1) for reporting clarity.
                                    double embedMinFt = startColWidthFt > 0.0 ? (startColWidthFt * ratio) : 0.0;
                                    double embedMaxFt = endColWidthFt > 0.0 ? (endColWidthFt * ratio) : 0.0;
                                    axisStart -= embedMinFt;
                                    axisEnd += embedMaxFt;

                                    double? colMinMm = startColWidthFt > 0.0 ? (double?)UnitHelper.FtToMm(startColWidthFt) : null;
                                    double? colMaxMm = endColWidthFt > 0.0 ? (double?)UnitHelper.FtToMm(endColWidthFt) : null;
                                    double? embedMinMm = embedMinFt > 0.0 ? (double?)UnitHelper.FtToMm(embedMinFt) : null;
                                    double? embedMaxMm = embedMaxFt > 0.0 ? (double?)UnitHelper.FtToMm(embedMaxFt) : null;

                                    double? colStartMm = beamStartIsMinAxis ? colMinMm : colMaxMm;
                                    double? colEndMm = beamStartIsMinAxis ? colMaxMm : colMinMm;
                                    double? embedStartMm = beamStartIsMinAxis ? embedMinMm : embedMaxMm;
                                    double? embedEndMm = beamStartIsMinAxis ? embedMaxMm : embedMinMm;

                                    hostObj["beamMainBarEmbed"] = new JObject
                                    {
                                        ["enabled"] = true,
                                        ["ratio"] = ratio,
                                        ["startDefinition"] = "LocationCurve.EndPoint(0)",
                                        ["endDefinition"] = "LocationCurve.EndPoint(1)",
                                        ["startIsMinAxis"] = beamStartIsMinAxis,
                                        ["startColumnWidthMm"] = colStartMm,
                                        ["endColumnWidthMm"] = colEndMm,
                                        ["startEmbedMm"] = embedStartMm,
                                        ["endEmbedMm"] = embedEndMm,
                                        ["minEndColumnWidthMm"] = colMinMm,
                                        ["maxEndColumnWidthMm"] = colMaxMm,
                                        ["minEndEmbedMm"] = embedMinMm,
                                        ["maxEndEmbedMm"] = embedMaxMm,
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
                            double extBotFt = UnitHelper.MmToFt(opts.columnMainBarBottomExtensionMm);
                            double extTopFt = UnitHelper.MmToFt(opts.columnMainBarTopExtensionMm);
                            double axisStartBefore = axisStart;
                            double axisEndBefore = axisEnd;

                            // axisStart/axisEnd are in local min->max order, while the user-facing "top/bottom" are world-based.
                            // If axisPositiveIsUp==false, world-top corresponds to local min and world-bottom to local max.
                            if (axisPositiveIsUp)
                            {
                                axisStart -= extBotFt; // bottom extension
                                axisEnd += extTopFt;   // top extension
                            }
                            else
                            {
                                axisStart -= extTopFt; // top extension (world-top is local min)
                                axisEnd += extBotFt;   // bottom extension (world-bottom is local max)
                            }

                            // Safety: if the column end is exposed (no concrete neighbor),
                            // do not allow *positive* extensions to violate cover at that end.
                            bool clampBot = false;
                            bool clampTop = false;
                            if (axisPositiveIsUp)
                            {
                                if (!columnHasConcreteBelow && extBotFt > 1e-9 && axisStart < axisStartBefore)
                                {
                                    axisStart = axisStartBefore;
                                    clampBot = true;
                                }
                                if (!columnHasConcreteAbove && extTopFt > 1e-9 && axisEnd > axisEndBefore)
                                {
                                    axisEnd = axisEndBefore;
                                    clampTop = true;
                                }
                            }
                            else
                            {
                                if (!columnHasConcreteAbove && extTopFt > 1e-9 && axisStart < axisStartBefore)
                                {
                                    axisStart = axisStartBefore;
                                    clampTop = true;
                                }
                                if (!columnHasConcreteBelow && extBotFt > 1e-9 && axisEnd > axisEndBefore)
                                {
                                    axisEnd = axisEndBefore;
                                    clampBot = true;
                                }
                            }

                            hostObj["columnMainBarAxisExtensionMm"] = new JObject
                            {
                                ["bottomRequested"] = opts.columnMainBarBottomExtensionMm,
                                ["topRequested"] = opts.columnMainBarTopExtensionMm,
                                ["bottomApplied"] = axisPositiveIsUp ? UnitHelper.FtToMm(axisStartBefore - axisStart) : UnitHelper.FtToMm(axisEnd - axisEndBefore),
                                ["topApplied"] = axisPositiveIsUp ? UnitHelper.FtToMm(axisEnd - axisEndBefore) : UnitHelper.FtToMm(axisStartBefore - axisStart),
                                ["allowBottomExtension"] = columnHasConcreteBelow,
                                ["allowTopExtension"] = columnHasConcreteAbove,
                                ["clampedBottom"] = clampBot,
                                ["clampedTop"] = clampTop,
                                ["axisPositiveIsUp"] = axisPositiveIsUp
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
                            double extMinFt = beamStartIsMinAxis ? extStartFt : extEndFt;
                            double extMaxFt = beamStartIsMinAxis ? extEndFt : extStartFt;
                            axisStart -= extMinFt;
                            axisEnd += extMaxFt;
                            hostObj["beamMainBarAxisExtensionMm"] = new JObject
                            {
                                ["start"] = opts.beamMainBarStartExtensionMm,
                                ["end"] = opts.beamMainBarEndExtensionMm,
                                ["appliedToMinEndMm"] = UnitHelper.FtToMm(extMinFt),
                                ["appliedToMaxEndMm"] = UnitHelper.FtToMm(extMaxFt),
                                ["startIsMinAxis"] = beamStartIsMinAxis
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
                        double tieOffsetFt = (opts.includeStirrups && tieBarDiaFt > 1e-9) ? tieBarDiaFt : 0.0;
                        double left = sMin + faceLeftFt + tieOffsetFt + r;
                        double right = sMax - faceRightFt - tieOffsetFt - r;
                        double bottom = uMin + faceDownFt + tieOffsetFt + r;
                        double top = uMax - faceUpFt - tieOffsetFt - r;
                        if (tieOffsetFt > 1e-9)
                        {
                            hostObj["beamMainBarInsideTieOffsetMm"] = UnitHelper.FtToMm(tieOffsetFt);
                        }

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
                        else if (beamSpec != null && (beamSpec.hasMainCounts || beamSpec.hasLayerCounts || beamSpec.hasZoneCounts))
                        {
                            if (beamSpec.topCount.HasValue) topCount = Math.Max(0, beamSpec.topCount.Value);
                            if (beamSpec.bottomCount.HasValue) bottomCount = Math.Max(0, beamSpec.bottomCount.Value);
                            if (beamSpec.topCount2.HasValue) topCount2 = Math.Max(0, beamSpec.topCount2.Value);
                            if (beamSpec.topCount3.HasValue) topCount3 = Math.Max(0, beamSpec.topCount3.Value);
                            if (beamSpec.bottomCount2.HasValue) bottomCount2 = Math.Max(0, beamSpec.bottomCount2.Value);
                            if (beamSpec.bottomCount3.HasValue) bottomCount3 = Math.Max(0, beamSpec.bottomCount3.Value);
                            if (beamSpec.hasZoneCounts) countSource = "mapping_zones";
                            else countSource = (topCount2 > 0 || topCount3 > 0 || bottomCount2 > 0 || bottomCount3 > 0) ? "mapping_layers" : "mapping";
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

                        // Resolve zone counts (start/mid/end) for main bars when provided by mapping.
                        // If a zone value is missing/zero, it falls back to a non-zero peer (mid->start/end, start/end->mid).
                        // If all three are missing, fall back to base count for that layer.
                        int topStart = topCount, topMid = topCount, topEnd = topCount;
                        int top2Start = topCount2, top2Mid = topCount2, top2End = topCount2;
                        int top3Start = topCount3, top3Mid = topCount3, top3End = topCount3;
                        int bottomStart = bottomCount, bottomMid = bottomCount, bottomEnd = bottomCount;
                        int bottom2Start = bottomCount2, bottom2Mid = bottomCount2, bottom2End = bottomCount2;
                        int bottom3Start = bottomCount3, bottom3Mid = bottomCount3, bottom3End = bottomCount3;

                        bool useZoneCounts = false;
                        if (beamSpec != null && beamSpec.hasZoneCounts)
                        {
                            useZoneCounts = true;

                            void ResolveZoneCounts(int baseCount, int? zs, int? zm, int? ze, out int rs, out int rm, out int re)
                            {
                                int s = zs.HasValue ? Math.Max(0, zs.Value) : 0;
                                int m = zm.HasValue ? Math.Max(0, zm.Value) : 0;
                                int e = ze.HasValue ? Math.Max(0, ze.Value) : 0;
                                bool any = (s > 0 || m > 0 || e > 0);
                                if (!any)
                                {
                                    rs = rm = re = baseCount;
                                    return;
                                }
                                if (m <= 0) m = (s > 0 ? s : e);
                                if (s <= 0) s = (m > 0 ? m : e);
                                if (e <= 0) e = (m > 0 ? m : s);
                                if (s <= 0 && m <= 0 && e <= 0)
                                {
                                    rs = rm = re = baseCount;
                                    return;
                                }
                                rs = s; rm = m; re = e;
                            }

                            ResolveZoneCounts(topCount, beamSpec.topCountStart, beamSpec.topCountMid, beamSpec.topCountEnd, out topStart, out topMid, out topEnd);
                            ResolveZoneCounts(topCount2, beamSpec.topCount2Start, beamSpec.topCount2Mid, beamSpec.topCount2End, out top2Start, out top2Mid, out top2End);
                            ResolveZoneCounts(topCount3, beamSpec.topCount3Start, beamSpec.topCount3Mid, beamSpec.topCount3End, out top3Start, out top3Mid, out top3End);
                            ResolveZoneCounts(bottomCount, beamSpec.bottomCountStart, beamSpec.bottomCountMid, beamSpec.bottomCountEnd, out bottomStart, out bottomMid, out bottomEnd);
                            ResolveZoneCounts(bottomCount2, beamSpec.bottomCount2Start, beamSpec.bottomCount2Mid, beamSpec.bottomCount2End, out bottom2Start, out bottom2Mid, out bottom2End);
                            ResolveZoneCounts(bottomCount3, beamSpec.bottomCount3Start, beamSpec.bottomCount3Mid, beamSpec.bottomCount3End, out bottom3Start, out bottom3Mid, out bottom3End);
                        }

                        if (useZoneCounts)
                        {
                            hostObj["beamMainCountsZones"] = new JObject
                            {
                                ["top"] = new JObject { ["start"] = topStart, ["mid"] = topMid, ["end"] = topEnd },
                                ["top2"] = new JObject { ["start"] = top2Start, ["mid"] = top2Mid, ["end"] = top2End },
                                ["top3"] = new JObject { ["start"] = top3Start, ["mid"] = top3Mid, ["end"] = top3End },
                                ["bottom"] = new JObject { ["start"] = bottomStart, ["mid"] = bottomMid, ["end"] = bottomEnd },
                                ["bottom2"] = new JObject { ["start"] = bottom2Start, ["mid"] = bottom2Mid, ["end"] = bottom2End },
                                ["bottom3"] = new JObject { ["start"] = bottom3Start, ["mid"] = bottom3Mid, ["end"] = bottom3End }
                            };
                        }

                        int idx = 0;

                        List<double> BuildMainBarTPositions(string side, int layerIndex, int count, int refCount)
                        {
                            var ts = new List<double>();
                            if (count <= 0) return ts;

                            // Layer 1: keep current behavior (count==1 => center).
                            if (layerIndex <= 1)
                            {
                                if (count == 1) { ts.Add(0.5); return ts; }
                                for (int i = 0; i < count; i++) ts.Add((double)i / (count - 1));
                                return ts;
                            }

                            // Layer 2/3: align to layer-1 positions as much as possible.
                            // - count==1 => align to the leftmost bar of layer 1
                            // - count>=2 => always include left & right; intermediate bars snap to layer-1 positions.
                            if (count == 1)
                            {
                                if (refCount == 1) ts.Add(0.5);
                                else ts.Add(0.0);
                                return ts;
                            }

                            // If no usable reference exists, fall back to even distribution.
                            if (refCount <= 1 || count > refCount)
                            {
                                for (int i = 0; i < count; i++) ts.Add((double)i / (count - 1));
                                return ts;
                            }

                            int prev = -1;
                            for (int i = 0; i < count; i++)
                            {
                                double t = (double)i / (count - 1);
                                int j = (int)Math.Round(t * (refCount - 1));
                                if (j < 0) j = 0;
                                if (j > refCount - 1) j = refCount - 1;
                                if (j <= prev) j = prev + 1;
                                if (j > refCount - 1) { ts.Clear(); break; }
                                ts.Add((double)j / (refCount - 1));
                                prev = j;
                            }

                            // If we couldn't build a valid snapped set, fall back to even.
                            if (ts.Count != count)
                            {
                                ts.Clear();
                                for (int i = 0; i < count; i++) ts.Add((double)i / (count - 1));
                            }

                            return ts;
                        }

                        void AddBarsAtU(string side, int layerIndex, double u, int count, int refCount, double rangeStart, double rangeEnd, string zone)
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

                            double span = right - left;
                            var ts = BuildMainBarTPositions(side, layerIndex, count, refCount);
                            for (int i = 0; i < ts.Count; i++)
                            {
                                double t = ts[i];
                                if (t < 0.0) t = 0.0;
                                if (t > 1.0) t = 1.0;
                                double s = left + span * t;

                            // Beam start/end definition:
                            // - start = LocationCurve.EndPoint(0)
                            // - end   = LocationCurve.EndPoint(1)
                            // rangeStart/rangeEnd are min/max on axisIndex, so map them to start/end when needed.
                            double a0 = rangeStart;
                            double a1 = rangeEnd;
                            if (isFraming && !beamStartIsMinAxis)
                            {
                                a0 = rangeEnd;   // start at max end
                                a1 = rangeStart; // end at min end
                            }

                                var p0 = MakeLocalPoint(axisIndex, a0, sIdx, s, uIdx, u);
                                var p1 = MakeLocalPoint(axisIndex, a1, sIdx, s, uIdx, u);
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
                                ["side"] = side,
                                ["layerIndex"] = layerIndex,
                                ["zone"] = string.IsNullOrWhiteSpace(zone) ? "full" : zone,
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

                        void AddLayerSafe(string side, int layerIndex, double uCoord, int count, int refCount)
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
                            AddBarsAtU(side, layerIndex, uCoord, count, refCount, axisStart, axisEnd, "full");
                        }

                        void AddLayerSafeZoned(string side, int layerIndex, double uCoord, int countStart, int countMid, int countEnd, int refCount, double clearStart, double clearEnd)
                        {
                            if (uCoord < bottom || uCoord > top) return;
                            double totalLen = clearEnd - clearStart;
                            if (!(totalLen > 1e-9))
                            {
                                // fallback to full if clear range is invalid
                                if (countStart > 0 || countMid > 0 || countEnd > 0)
                                {
                                    int c = Math.Max(countStart, Math.Max(countMid, countEnd));
                                    if (c > 0) AddBarsAtU(side, layerIndex, uCoord, c, refCount, axisStart, axisEnd, "full");
                                }
                                return;
                            }

                            double seg1Start = clearStart;
                            double seg1End = clearStart + totalLen * 0.25;
                            double seg2Start = seg1End;
                            double seg2End = clearStart + totalLen * 0.75;
                            double seg3Start = seg2End;
                            double seg3End = clearEnd;

                            // For beams, allow start/end zones to extend into supports (embed),
                            // while keeping zone boundaries based on clear span.
                            if (isFraming)
                            {
                                if (axisStart < seg1Start) seg1Start = axisStart;
                                if (axisEnd > seg3End) seg3End = axisEnd;
                            }

                            double a0, a1;
                            // start zone
                            a0 = Math.Max(axisStart, seg1Start);
                            a1 = Math.Min(axisEnd, seg1End);
                            if (countStart > 0 && a1 > a0 + 1e-9)
                                AddBarsAtU(side, layerIndex, uCoord, countStart, refCount, a0, a1, "start");

                            // mid zone
                            a0 = Math.Max(axisStart, seg2Start);
                            a1 = Math.Min(axisEnd, seg2End);
                            if (countMid > 0 && a1 > a0 + 1e-9)
                                AddBarsAtU(side, layerIndex, uCoord, countMid, refCount, a0, a1, "mid");

                            // end zone
                            a0 = Math.Max(axisStart, seg3Start);
                            a1 = Math.Min(axisEnd, seg3End);
                            if (countEnd > 0 && a1 > a0 + 1e-9)
                                AddBarsAtU(side, layerIndex, uCoord, countEnd, refCount, a0, a1, "end");
                        }

                        // Spacing checks (plan-time, based on the same local box / covers used for placement).
                        try
                        {
                            double RequiredCcMmForMain()
                            {
                                if (requiredCcMm > 0.0) return requiredCcMm;
                                return Math.Max(0.0, barDiaMm) + Math.Max(0.0, opts.beamMainBarLayerClearMm);
                            }

                            double MinWithinLayerSpacingMm(string side, int layerIndex, int count, int refCount)
                            {
                                if (count <= 1) return 0.0;
                                double spanFt = right - left;
                                if (spanFt <= 1e-9) return 0.0;

                                var ts = BuildMainBarTPositions(side, layerIndex, count, refCount);
                                if (ts == null || ts.Count <= 1) return 0.0;

                                double minDt = double.PositiveInfinity;
                                for (int i = 0; i + 1 < ts.Count; i++)
                                {
                                    double dt = ts[i + 1] - ts[i];
                                    if (dt < minDt) minDt = dt;
                                }
                                if (double.IsInfinity(minDt) || minDt <= 1e-9) return 0.0;
                                return UnitHelper.FtToMm(spanFt * minDt);
                            }

                            var req = RequiredCcMmForMain();
                            var checks = new JObject
                            {
                                ["requiredCcMm"] = req,
                                ["requiredCcSource"] = requiredCcSource
                            };

                            var layers = new JArray();
                            void AddLayerCheck(string side, int layerIndex, int count, int refCount)
                            {
                                if (count <= 0) return;
                                var sp = MinWithinLayerSpacingMm(side, layerIndex, count, refCount);
                                layers.Add(new JObject
                                {
                                    ["side"] = side,
                                    ["layerIndex"] = layerIndex,
                                    ["count"] = count,
                                    ["withinLayerSpacingMm"] = sp > 0.0 ? Math.Round(sp, 3) : 0.0,
                                    ["ok"] = (count <= 1) ? true : (sp + 1e-6 >= req)
                                });
                            }

                            int topCountEff = useZoneCounts ? Math.Max(topStart, Math.Max(topMid, topEnd)) : topCount;
                            int top2CountEff = useZoneCounts ? Math.Max(top2Start, Math.Max(top2Mid, top2End)) : topCount2;
                            int top3CountEff = useZoneCounts ? Math.Max(top3Start, Math.Max(top3Mid, top3End)) : topCount3;
                            int bottomCountEff = useZoneCounts ? Math.Max(bottomStart, Math.Max(bottomMid, bottomEnd)) : bottomCount;
                            int bottom2CountEff = useZoneCounts ? Math.Max(bottom2Start, Math.Max(bottom2Mid, bottom2End)) : bottomCount2;
                            int bottom3CountEff = useZoneCounts ? Math.Max(bottom3Start, Math.Max(bottom3Mid, bottom3End)) : bottomCount3;

                            AddLayerCheck("top", 1, topCountEff, topCountEff);
                            AddLayerCheck("top", 2, top2CountEff, topCountEff);
                            AddLayerCheck("top", 3, top3CountEff, topCountEff);
                            AddLayerCheck("bottom", 1, bottomCountEff, bottomCountEff);
                            AddLayerCheck("bottom", 2, bottom2CountEff, bottomCountEff);
                            AddLayerCheck("bottom", 3, bottom3CountEff, bottomCountEff);

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

                        double clearStart = (hasClearRange && clearEndFt > clearStartFt) ? clearStartFt : axisStart;
                        double clearEnd = (hasClearRange && clearEndFt > clearStartFt) ? clearEndFt : axisEnd;
                        bool useZonedMainBars = useZoneCounts && (clearEnd > clearStart + 1e-9);

                        // Top layers (downwards)
                        if (useZonedMainBars)
                            AddLayerSafeZoned("top", 1, top, topStart, topMid, topEnd, topCount, clearStart, clearEnd);
                        else
                            AddLayerSafe("top", 1, top, topCount, topCount);
                        if (layerPitchFt > 1e-9)
                        {
                            if (useZonedMainBars)
                            {
                                AddLayerSafeZoned("top", 2, top - layerPitchFt, top2Start, top2Mid, top2End, topCount, clearStart, clearEnd);
                                AddLayerSafeZoned("top", 3, top - (layerPitchFt * 2.0), top3Start, top3Mid, top3End, topCount, clearStart, clearEnd);
                            }
                            else
                            {
                                AddLayerSafe("top", 2, top - layerPitchFt, topCount2, topCount);
                                AddLayerSafe("top", 3, top - (layerPitchFt * 2.0), topCount3, topCount);
                            }
                        }
                        else
                        {
                            bool hasTop2 = useZoneCounts ? (top2Start > 0 || top2Mid > 0 || top2End > 0) : (topCount2 > 0);
                            bool hasTop3 = useZoneCounts ? (top3Start > 0 || top3Mid > 0 || top3End > 0) : (topCount3 > 0);
                            if (hasTop2 || hasTop3)
                            {
                                var skips = hostObj["beamMainLayerSkips"] as JArray;
                                if (skips == null) { skips = new JArray(); hostObj["beamMainLayerSkips"] = skips; }
                                if (hasTop2) skips.Add(new JObject { ["side"] = "top", ["layerIndex"] = 2, ["count"] = topCount2, ["reason"] = "layerPitchMm<=0" });
                                if (hasTop3) skips.Add(new JObject { ["side"] = "top", ["layerIndex"] = 3, ["count"] = topCount3, ["reason"] = "layerPitchMm<=0" });
                            }
                        }

                        // Bottom layers (upwards)
                        if (useZonedMainBars)
                            AddLayerSafeZoned("bottom", 1, bottom, bottomStart, bottomMid, bottomEnd, bottomCount, clearStart, clearEnd);
                        else
                            AddLayerSafe("bottom", 1, bottom, bottomCount, bottomCount);
                        if (layerPitchFt > 1e-9)
                        {
                            if (useZonedMainBars)
                            {
                                AddLayerSafeZoned("bottom", 2, bottom + layerPitchFt, bottom2Start, bottom2Mid, bottom2End, bottomCount, clearStart, clearEnd);
                                AddLayerSafeZoned("bottom", 3, bottom + (layerPitchFt * 2.0), bottom3Start, bottom3Mid, bottom3End, bottomCount, clearStart, clearEnd);
                            }
                            else
                            {
                                AddLayerSafe("bottom", 2, bottom + layerPitchFt, bottomCount2, bottomCount);
                                AddLayerSafe("bottom", 3, bottom + (layerPitchFt * 2.0), bottomCount3, bottomCount);
                            }
                        }
                        else
                        {
                            bool hasBot2 = useZoneCounts ? (bottom2Start > 0 || bottom2Mid > 0 || bottom2End > 0) : (bottomCount2 > 0);
                            bool hasBot3 = useZoneCounts ? (bottom3Start > 0 || bottom3Mid > 0 || bottom3End > 0) : (bottomCount3 > 0);
                            if (hasBot2 || hasBot3)
                            {
                                var skips = hostObj["beamMainLayerSkips"] as JArray;
                                if (skips == null) { skips = new JArray(); hostObj["beamMainLayerSkips"] = skips; }
                                if (hasBot2) skips.Add(new JObject { ["side"] = "bottom", ["layerIndex"] = 2, ["count"] = bottomCount2, ["reason"] = "layerPitchMm<=0" });
                                if (hasBot3) skips.Add(new JObject { ["side"] = "bottom", ["layerIndex"] = 3, ["count"] = bottomCount3, ["reason"] = "layerPitchMm<=0" });
                            }
                        }
                    }
                    else
                    {
                        double aMin = GetMinByIndex(localBox, crossA);
                        double aMax = GetMaxByIndex(localBox, crossA);
                        double bMin = GetMinByIndex(localBox, crossB);
                        double bMax = GetMaxByIndex(localBox, crossB);
                        var nrm = GetBasisVectorByIndex(tr, crossA);
                        if (nrm.GetLength() < 1e-9) nrm = XYZ.BasisX;

                        double centerA = (aMin + aMax) * 0.5;
                        double centerB = (bMin + bMax) * 0.5;
                        double halfA = (aMax - aMin) * 0.5;
                        double halfB = (bMax - bMin) * 0.5;

                        double sideCoverFt = Math.Max(coverOtherFt, Math.Max(faceLeftFt, Math.Max(faceRightFt, Math.Max(faceUpFt, faceDownFt))));
                        if (opts.includeTies && tieBarDiaFt > 1e-9)
                        {
                            sideCoverFt += tieBarDiaFt;
                            hostObj["columnMainBarInsideTieOffsetMm"] = UnitHelper.FtToMm(tieBarDiaFt);
                        }
                        bool useCircular = isColumn && isCircularColumn;

                        // Determine head/foot counts with fallback
                        int perFaceXHeadEff = columnMainBarsPerFaceXHead > 0 ? columnMainBarsPerFaceXHead
                            : (columnMainBarsPerFaceXFoot > 0 ? columnMainBarsPerFaceXFoot : columnMainBarsPerFaceXEffective);
                        int perFaceXFootEff = columnMainBarsPerFaceXFoot > 0 ? columnMainBarsPerFaceXFoot
                            : (columnMainBarsPerFaceXHead > 0 ? columnMainBarsPerFaceXHead : columnMainBarsPerFaceXEffective);
                        int perFaceYHeadEff = columnMainBarsPerFaceYHead > 0 ? columnMainBarsPerFaceYHead
                            : (columnMainBarsPerFaceYFoot > 0 ? columnMainBarsPerFaceYFoot : columnMainBarsPerFaceYEffective);
                        int perFaceYFootEff = columnMainBarsPerFaceYFoot > 0 ? columnMainBarsPerFaceYFoot
                            : (columnMainBarsPerFaceYHead > 0 ? columnMainBarsPerFaceYHead : columnMainBarsPerFaceYEffective);

                        int totalHeadEff = columnMainBarTotalCountHead > 0 ? columnMainBarTotalCountHead
                            : (columnMainBarTotalCountFoot > 0 ? columnMainBarTotalCountFoot : columnMainBarTotalCountEffective);
                        int totalFootEff = columnMainBarTotalCountFoot > 0 ? columnMainBarTotalCountFoot
                            : (columnMainBarTotalCountHead > 0 ? columnMainBarTotalCountHead : columnMainBarTotalCountEffective);
                        if (totalHeadEff <= 0 && totalFootEff > 0) totalHeadEff = totalFootEff;
                        if (totalFootEff <= 0 && totalHeadEff > 0) totalFootEff = totalHeadEff;
                        if (useCircular && totalHeadEff <= 0 && totalFootEff <= 0 && colSpec != null
                            && colSpec.mainBarsPerFace.HasValue
                            && !colSpec.mainBarsPerFaceX.HasValue && !colSpec.mainBarsPerFaceY.HasValue)
                        {
                            totalHeadEff = totalFootEff = Math.Max(0, colSpec.mainBarsPerFace.Value);
                        }

                        bool splitHeadFoot = isColumn && opts.columnMainBarSplitByMidHeight;
                        double axisMid = (axisStart + axisEnd) * 0.5;
                        if (axisMid < axisStart) axisMid = axisStart;
                        if (axisMid > axisEnd) axisMid = axisEnd;
                        double footStart = axisPositiveIsUp ? axisStart : axisMid;
                        double footEnd = axisPositiveIsUp ? axisMid : axisEnd;
                        double headStart = axisPositiveIsUp ? axisMid : axisStart;
                        double headEnd = axisPositiveIsUp ? axisEnd : axisMid;
                        try
                        {
                            hostObj["columnMainBarSplit"] = new JObject
                            {
                                ["enabled"] = splitHeadFoot,
                                ["axisMidMm"] = UnitHelper.FtToMm(axisMid),
                                ["axisPositiveIsUp"] = axisPositiveIsUp
                            };
                        }
                        catch { /* ignore */ }

                        bool AddCircularBars(double startFt, double endFt, int count)
                        {
                            if (count < 3) return false;
                            double baseRadiusFt = (columnDiameterFt > 1e-9) ? (columnDiameterFt * 0.5) : Math.Min(halfA, halfB);
                            double tieOffsetFt = (opts.includeTies && tieBarDiaFt > 1e-9) ? tieBarDiaFt : 0.0;
                            double radiusFt = baseRadiusFt - sideCoverFt - tieOffsetFt - r;
                            if (radiusFt <= 1e-6)
                            {
                                hostObj["columnCircularMainBars"] = new JObject
                                {
                                    ["used"] = false,
                                    ["reason"] = "radius<=0",
                                    ["baseRadiusMm"] = UnitHelper.FtToMm(baseRadiusFt),
                                    ["sideCoverMm"] = UnitHelper.FtToMm(sideCoverFt),
                                    ["tieOffsetMm"] = UnitHelper.FtToMm(tieOffsetFt)
                                };
                                return false;
                            }

                            hostObj["columnCircularMainBars"] = new JObject
                            {
                                ["used"] = true,
                                ["totalCount"] = count,
                                ["radiusMm"] = UnitHelper.FtToMm(radiusFt),
                                ["baseRadiusMm"] = UnitHelper.FtToMm(baseRadiusFt),
                                ["sideCoverMm"] = UnitHelper.FtToMm(sideCoverFt),
                                ["tieOffsetMm"] = UnitHelper.FtToMm(tieOffsetFt)
                            };

                            int idx = 0;
                            for (int i = 0; i < count; i++)
                            {
                                double ang = (2.0 * Math.PI * i) / count;
                                double a = centerA + radiusFt * Math.Cos(ang);
                                double b = centerB + radiusFt * Math.Sin(ang);
                                var p0 = MakeLocalPoint(axisIndex, startFt, crossA, a, crossB, b);
                                var p1 = MakeLocalPoint(axisIndex, endFt, crossA, a, crossB, b);
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
                            return idx > 0;
                        }

                        void AddRectBars(double startFt, double endFt, int perFaceX, int perFaceY)
                        {
                            double tieOffsetFt = (opts.includeTies && tieBarDiaFt > 1e-9) ? tieBarDiaFt : 0.0;
                            double ax1 = aMin + faceLeftFt + tieOffsetFt + r;
                            double ax2 = aMax - faceRightFt - tieOffsetFt - r;
                            double bx1 = bMin + faceDownFt + tieOffsetFt + r;
                            double bx2 = bMax - faceUpFt - tieOffsetFt - r;

                            // NOTE (important): per-face bar-count definitions
                            // In common RC-family conventions (incl. SIRBIM), "X/Y" counts describe the number of bars on
                            // the faces parallel to the opposite axis:
                            // - BarsPerFaceX => faces parallel to Y (i.e., faces at a=ax1/ax2, varying b => bPositions)
                            // - BarsPerFaceY => faces parallel to X (i.e., faces at b=bx1/bx2, varying a => aPositions)
                            // Therefore, aPositions should use BarsPerFaceY, and bPositions should use BarsPerFaceX.
                            int perFaceA = Math.Max(2, perFaceY);
                            int perFaceB = Math.Max(2, perFaceX);

                            double[] Linspace(double start, double end, int count)
                            {
                                if (count <= 1) return new[] { start };
                                var arr = new double[count];
                                double step = (end - start) / (count - 1);
                                for (int i = 0; i < count; i++) arr[i] = start + step * i;
                                return arr;
                            }

                            var aPositions = Linspace(ax1, ax2, perFaceA);
                            var bPositions = Linspace(bx1, bx2, perFaceB);

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
                                var p0 = MakeLocalPoint(axisIndex, startFt, crossA, pt.Item1, crossB, pt.Item2);
                                var p1 = MakeLocalPoint(axisIndex, endFt, crossA, pt.Item1, crossB, pt.Item2);
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

                        // For circular columns, fall back to per-face counts when total counts are missing.
                        // This avoids rectangular placement on circular sections and reduces "outside host" warnings.
                        int circHeadEff = totalHeadEff;
                        int circFootEff = totalFootEff;
                        if (circHeadEff <= 0 && circFootEff > 0) circHeadEff = circFootEff;
                        if (circFootEff <= 0 && circHeadEff > 0) circFootEff = circHeadEff;
                        string circSource = "explicit";
                        if (useCircular && circHeadEff <= 0 && circFootEff <= 0)
                        {
                            int fallback = 0;
                            if (columnMainBarTotalCountEffective >= 3)
                            {
                                fallback = columnMainBarTotalCountEffective;
                                circSource = "totalCount";
                            }
                            else
                            {
                                // Prefer head/foot counts when provided, otherwise use per-face effective counts.
                                fallback = Math.Max(fallback, columnMainBarsPerFaceXHead);
                                fallback = Math.Max(fallback, columnMainBarsPerFaceXFoot);
                                fallback = Math.Max(fallback, columnMainBarsPerFaceYHead);
                                fallback = Math.Max(fallback, columnMainBarsPerFaceYFoot);
                                fallback = Math.Max(fallback, columnMainBarsPerFaceXEffective);
                                fallback = Math.Max(fallback, columnMainBarsPerFaceYEffective);
                                circSource = "perFaceFallback";
                            }
                            if (fallback >= 3)
                            {
                                circHeadEff = fallback;
                                circFootEff = fallback;
                            }
                        }

                        bool usedCircularBars = false;
                        if (useCircular && (circHeadEff > 0 || circFootEff > 0))
                        {
                            if (!splitHeadFoot)
                            {
                                usedCircularBars = AddCircularBars(axisStart, axisEnd, Math.Max(circHeadEff, circFootEff));
                            }
                            else
                            {
                                if (circFootEff > 0) usedCircularBars |= AddCircularBars(footStart, footEnd, circFootEff);
                                if (circHeadEff > 0) usedCircularBars |= AddCircularBars(headStart, headEnd, circHeadEff);
                            }
                        }
                        else if (useCircular && columnMainBarTotalCountEffective >= 3)
                        {
                            usedCircularBars = AddCircularBars(axisStart, axisEnd, columnMainBarTotalCountEffective);
                        }
                        if (!usedCircularBars)
                        {
                            if (!splitHeadFoot)
                            {
                                AddRectBars(axisStart, axisEnd, perFaceXHeadEff, perFaceYHeadEff);
                            }
                            else
                            {
                                AddRectBars(footStart, footEnd, perFaceXFootEff, perFaceYFootEff);
                                AddRectBars(headStart, headEnd, perFaceXHeadEff, perFaceYHeadEff);
                            }
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
 
                     double bboxMin = GetMinByIndex(localBox, axisIndex);
                     double bboxMax = GetMaxByIndex(localBox, axisIndex);
                     double baseMin = (isFraming && beamAxisRangeValid) ? beamAxisMinFt : bboxMin;
                     double baseMax = (isFraming && beamAxisRangeValid) ? beamAxisMaxFt : bboxMax;
                     if (isFraming)
                     {
                        try
                        {
                            hostObj["beamAxisRangeBboxMm_raw_stirrups"] = new JObject
                            {
                                ["min"] = UnitHelper.FtToMm(bboxMin),
                                ["max"] = UnitHelper.FtToMm(bboxMax),
                                ["length"] = UnitHelper.FtToMm(bboxMax - bboxMin)
                            };
                        }
                        catch { /* ignore */ }

                        hostObj["beamAxisSourceStirrups"] = beamAxisRangeValid ? "shared" : "bbox";

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

                        if (beamAxisRangeValid)
                        {
                            hostObj["beamAxisRangeFrom_stirrups"] = "shared";
                            if (hostObj["beamAxisRangeSolidMm_shared"] != null)
                                hostObj["beamAxisRangeSolidMm_stirrups"] = hostObj["beamAxisRangeSolidMm_shared"];
                            if (hostObj["beamAxisRangeSolidError_shared"] != null)
                                hostObj["beamAxisRangeSolidError_stirrups"] = hostObj["beamAxisRangeSolidError_shared"];
                        }
                        else
                        {
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
                        }

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
 
                        // Use shared clear span if available (keeps main bars/stirrups consistent).
                        if (beamSupportFaceDbg != null) hostObj["beamStirrupSupportFaces"] = beamSupportFaceDbg;
                        if (hasClearRange)
                        {
                            baseMin = clearStartFt;
                            baseMax = clearEndFt;
                            try
                            {
                                hostObj["beamClearAxisRangeMm_stirrups"] = new JObject
                                {
                                    ["min"] = UnitHelper.FtToMm(clearStartFt),
                                    ["max"] = UnitHelper.FtToMm(clearEndFt),
                                    ["length"] = UnitHelper.FtToMm(clearEndFt - clearStartFt)
                                };
                            }
                            catch { /* ignore */ }
                        }
                    }

                    double colAxisCoverBottomFt = coverBottomFt;
                    double colAxisCoverTopFt = coverTopFt;
                    if (isColumn && opts.columnAxisEndCoverUsesConcreteNeighborCheck)
                    {
                        if (columnHasConcreteBelow) colAxisCoverBottomFt = 0.0;
                        if (columnHasConcreteAbove) colAxisCoverTopFt = 0.0;
                        if (hostObj["columnAxisEndCoverEffectiveMm_ties"] == null)
                        {
                            hostObj["columnAxisEndCoverEffectiveMm_ties"] = new JObject
                            {
                                ["bottom"] = UnitHelper.FtToMm(colAxisCoverBottomFt),
                                ["top"] = UnitHelper.FtToMm(colAxisCoverTopFt),
                                ["source"] = "concreteNeighborCheck"
                            };
                        }
                    }

                    double colAxisCoverMinFt = colAxisCoverBottomFt;
                    double colAxisCoverMaxFt = colAxisCoverTopFt;
                    if (isColumn)
                    {
                        colAxisCoverMinFt = axisPositiveIsUp ? colAxisCoverBottomFt : colAxisCoverTopFt;
                        colAxisCoverMaxFt = axisPositiveIsUp ? colAxisCoverTopFt : colAxisCoverBottomFt;
                    }

                    double axisStart = baseMin + (isColumn ? (colAxisCoverMinFt + r) : (coverOtherFt + r));
                    double axisEnd = baseMax - (isColumn ? (colAxisCoverMaxFt + r) : (coverOtherFt + r));
                    if (isFraming)
                    {
                        // Beam axis-end cover for stirrups:
                        // - If an end is connected to *concrete* structural column or structural framing => no cover at that end.
                        // - Otherwise => apply the same cover as the beam side faces.
                        bool beamHasConcreteAtMinEnd = false;
                        bool beamHasConcreteAtMaxEnd = false;
                        try
                        {
                            if (TryDetectConcreteSupportsAtBeamAxisEnds(
                                doc, host, tr, localBox, axisIndex, sideIndex, upIndex, baseMin, baseMax,
                                UnitHelper.MmToFt(opts.beamSupportSearchRangeMm),
                                UnitHelper.MmToFt(opts.beamSupportFaceToleranceMm),
                                concreteTokens, concreteExcludeTokens, concreteExcludeMaterialClasses,
                                out beamHasConcreteAtMinEnd, out beamHasConcreteAtMaxEnd, out var beamEndDbg) && beamEndDbg != null)
                            {
                                beamEndDbg["startDefinition"] = "LocationCurve.EndPoint(0)";
                                beamEndDbg["endDefinition"] = "LocationCurve.EndPoint(1)";
                                beamEndDbg["startIsMinAxis"] = beamStartIsMinAxis;
                                beamEndDbg["hasConcreteAtStart"] = beamStartIsMinAxis ? beamHasConcreteAtMinEnd : beamHasConcreteAtMaxEnd;
                                beamEndDbg["hasConcreteAtEnd"] = beamStartIsMinAxis ? beamHasConcreteAtMaxEnd : beamHasConcreteAtMinEnd;
                                hostObj["beamAxisEndConcreteNeighbor_ties"] = beamEndDbg;
                            }
                        }
                        catch { /* ignore */ }

                        double beamEndCoverFt = Math.Max(coverOtherFt, Math.Max(faceLeftFt, faceRightFt));
                        double gapMinFt = beamHasConcreteAtMinEnd ? 0.0 : (beamEndCoverFt + r);
                        double gapMaxFt = beamHasConcreteAtMaxEnd ? 0.0 : (beamEndCoverFt + r);
                        axisStart = baseMin + gapMinFt;
                        axisEnd = baseMax - gapMaxFt;
                        try
                        {
                            hostObj["beamAxisEndCoverEffectiveMm_ties"] = new JObject
                            {
                                ["source"] = "concreteSupport+sideCover",
                                ["sideCoverMm"] = UnitHelper.FtToMm(beamEndCoverFt),
                                ["minEnd"] = beamHasConcreteAtMinEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt),
                                ["maxEnd"] = beamHasConcreteAtMaxEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt),
                                ["start"] = beamStartIsMinAxis
                                    ? (beamHasConcreteAtMinEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt))
                                    : (beamHasConcreteAtMaxEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt)),
                                ["end"] = beamStartIsMinAxis
                                    ? (beamHasConcreteAtMaxEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt))
                                    : (beamHasConcreteAtMinEnd ? 0.0 : UnitHelper.FtToMm(beamEndCoverFt)),
                                ["gapMinEndMm"] = UnitHelper.FtToMm(gapMinFt),
                                ["gapMaxEndMm"] = UnitHelper.FtToMm(gapMaxFt),
                                ["startIsMinAxis"] = beamStartIsMinAxis
                            };
                        }
                        catch { /* ignore */ }
                    }
                    // Start/end offsets for ties/stirrups (mm)
                    try
                    {
                        if (isFraming)
                        {
                            // IMPORTANT:
                            // Beam "start/end" are defined by the LocationCurve endpoints:
                            //  - start = EndPoint(0)
                            //  - end   = EndPoint(1)
                            //
                            // Our axis range is min/max on the chosen axisIndex, so to keep axisStart<axisEnd
                            // (required by shape-driven layout), apply the start/end offsets to the min/max ends
                            // depending on whether EndPoint(0) lies on the min side or max side.
                            bool startIsMin = beamStartIsMinAxis;

                            double startOffFt = UnitHelper.MmToFt(opts.beamStirrupStartOffsetMm);
                            double endOffFt = UnitHelper.MmToFt(opts.beamStirrupEndOffsetMm);
                            double offMinFt = startIsMin ? startOffFt : endOffFt;
                            double offMaxFt = startIsMin ? endOffFt : startOffFt;

                            axisStart += offMinFt;
                            axisEnd -= offMaxFt;

                            hostObj["beamStirrupAxisOffsetsMm"] = new JObject
                            {
                                ["start"] = opts.beamStirrupStartOffsetMm,
                                ["end"] = opts.beamStirrupEndOffsetMm,
                                ["appliedToMinEndMm"] = UnitHelper.FtToMm(offMinFt),
                                ["appliedToMaxEndMm"] = UnitHelper.FtToMm(offMaxFt)
                            };
                        }
                        else if (isColumn)
                        {
                            double offBotFt = UnitHelper.MmToFt(opts.columnTieBottomOffsetMm);
                            double offTopFt = UnitHelper.MmToFt(opts.columnTieTopOffsetMm);
                            if (axisPositiveIsUp)
                            {
                                axisStart += offBotFt;
                                axisEnd -= offTopFt;
                            }
                            else
                            {
                                // world-top is local min, world-bottom is local max
                                axisStart += offTopFt;
                                axisEnd -= offBotFt;
                            }
                            hostObj["columnTieAxisOffsetsMm"] = new JObject
                            {
                                ["bottom"] = opts.columnTieBottomOffsetMm,
                                ["top"] = opts.columnTieTopOffsetMm,
                                ["axisPositiveIsUp"] = axisPositiveIsUp
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

                    // Circular column ties (best-effort)
                    bool useCircularTie = false;
                    double circleCenterS = 0.0;
                    double circleCenterU = 0.0;
                    double circleRadiusTieFt = 0.0;
                    if (isColumn && isCircularColumn)
                    {
                        try
                        {
                            double sLen = sMax - sMin;
                            double uLen = uMax - uMin;
                            circleCenterS = (sMin + sMax) * 0.5;
                            circleCenterU = (uMin + uMax) * 0.5;
                            double sideCoverFt = Math.Max(coverOtherFt, Math.Max(faceLeftFt, Math.Max(faceRightFt, Math.Max(faceUpFt, faceDownFt))));
                            double baseRadiusFt = (columnDiameterFt > 1e-9) ? (columnDiameterFt * 0.5) : (Math.Min(sLen, uLen) * 0.5);
                            circleRadiusTieFt = baseRadiusFt - sideCoverFt - r;
                            if (circleRadiusTieFt > 1e-6)
                            {
                                useCircularTie = true;
                                hostObj["columnCircularTies"] = new JObject
                                {
                                    ["used"] = true,
                                    ["radiusMm"] = UnitHelper.FtToMm(circleRadiusTieFt),
                                    ["baseRadiusMm"] = UnitHelper.FtToMm(baseRadiusFt),
                                    ["sideCoverMm"] = UnitHelper.FtToMm(sideCoverFt)
                                };
                            }
                            else
                            {
                                hostObj["columnCircularTies"] = new JObject
                                {
                                    ["used"] = false,
                                    ["reason"] = "radius<=0",
                                    ["baseRadiusMm"] = UnitHelper.FtToMm(baseRadiusFt),
                                    ["sideCoverMm"] = UnitHelper.FtToMm(sideCoverFt)
                                };
                            }
                        }
                        catch { /* ignore */ }
                    }

                    ElementId tieShapeId = selectedTieShapeId;
                    string tieShapeName = selectedTieShapeName;
                    string tieShapeSource = (tieShapeId != null && tieShapeId != ElementId.InvalidElementId) ? "selection" : string.Empty;
                    if (useCircularTie && (tieShapeId == null || tieShapeId == ElementId.InvalidElementId))
                    {
                        try
                        {
                            if (TryFindCircularTieShape(doc, out var autoShape, out var src))
                            {
                                tieShapeId = autoShape.Id;
                                tieShapeName = autoShape.Name ?? string.Empty;
                                tieShapeSource = src;
                            }
                        }
                        catch { /* ignore */ }
                    }
                    if (tieShapeId != null && tieShapeId != ElementId.InvalidElementId)
                    {
                        try
                        {
                            hostObj["columnTieShape"] = new JObject
                            {
                                ["id"] = tieShapeId.IntValue(),
                                ["name"] = tieShapeName,
                                ["source"] = tieShapeSource
                            };
                        }
                        catch { /* ignore */ }
                    }

                    JArray BuildTieCurvesAtAxisCoord(double axisCoordFt)
                    {
                        if (useCircularTie)
                        {
                            var centerLocal = MakeLocalPoint(axisIndex, axisCoordFt, sIdx, circleCenterS, uIdx, circleCenterU);
                            var centerWorld = tr.OfPoint(centerLocal);

                            var xAxis = GetBasisVectorByIndex(tr, sIdx);
                            var yAxis = GetBasisVectorByIndex(tr, uIdx);
                            if (xAxis.GetLength() < 1e-9) xAxis = XYZ.BasisX;
                            if (yAxis.GetLength() < 1e-9) yAxis = XYZ.BasisY;
                            xAxis = xAxis.Normalize();
                            yAxis = yAxis.Normalize();

                            var curvesCircular = new JArray();
                            double q = Math.PI * 0.5;
                            var a1 = Arc.Create(centerWorld, circleRadiusTieFt, 0, q, xAxis, yAxis);
                            var a2 = Arc.Create(centerWorld, circleRadiusTieFt, q, q * 2.0, xAxis, yAxis);
                            var a3 = Arc.Create(centerWorld, circleRadiusTieFt, q * 2.0, q * 3.0, xAxis, yAxis);
                            var a4 = Arc.Create(centerWorld, circleRadiusTieFt, q * 3.0, q * 4.0, xAxis, yAxis);
                            curvesCircular.Add(GeometryJsonHelper.CurveToJson(a1));
                            curvesCircular.Add(GeometryJsonHelper.CurveToJson(a2));
                            curvesCircular.Add(GeometryJsonHelper.CurveToJson(a3));
                            curvesCircular.Add(GeometryJsonHelper.CurveToJson(a4));
                            return curvesCircular;
                        }

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
                                bool addedZonedBeamStirrups = false;
                                bool addedZonedColumnTies = false;

                                // Beam: if pitch is provided by mapping keys, apply it.
                                // If start/end pitches are specified and differ from mid, split into 3 zones by clear length:
                                // - start end (near LocationCurve.EndPoint(0)): 1/4
                                // - middle: 1/2
                                // - end end (near LocationCurve.EndPoint(1)): 1/4
                                if (isFraming && beamSpec != null && beamSpec.hasPitchParams && opts.layoutOverride == null)
                                {
                                    try
                                    {
                                        bool startIsMin = beamStartIsMinAxis;

                                        double pitchMid = beamSpec.pitchMidMm > 0.0 ? beamSpec.pitchMidMm : beamSpec.pitchEffectiveMm;
                                        if (!(pitchMid > 0.0)) pitchMid = beamSpec.pitchStartMm > 0.0 ? beamSpec.pitchStartMm : beamSpec.pitchEndMm;
                                        double pitchStart = beamSpec.pitchStartMm > 0.0 ? beamSpec.pitchStartMm : pitchMid;
                                        double pitchEnd = beamSpec.pitchEndMm > 0.0 ? beamSpec.pitchEndMm : pitchMid;

                                // Map to min/max ends (axisStart=MinEnd, axisEnd=MaxEnd)
                                double pitchMin = startIsMin ? pitchStart : pitchEnd;
                                double pitchMax = startIsMin ? pitchEnd : pitchStart;

                                bool hasStartOrEnd = (beamSpec.pitchStartMm > 0.0) || (beamSpec.pitchEndMm > 0.0);
                                // If start/end pitch params are provided, always split into 3 zones (start/mid/end),
                                // even when the values happen to be equal. This preserves the user's intent and
                                // matches the "start/end = 25% each of clear length" rule.
                                bool needsZoned = hasStartOrEnd;

                                hostObj["beamStirrupPitchZones"] = new JObject
                                {
                                    ["mode"] = "quarter-mid-half-quarter",
                                    ["startDefinition"] = "LocationCurve.EndPoint(0)",
                                    ["endDefinition"] = "LocationCurve.EndPoint(1)",
                                    ["startIsMinAxis"] = startIsMin,
                                    ["clearStartMm"] = UnitHelper.FtToMm((hasClearRange && clearEndFt > clearStartFt) ? clearStartFt : axisStart),
                                    ["clearEndMm"] = UnitHelper.FtToMm((hasClearRange && clearEndFt > clearStartFt) ? clearEndFt : axisEnd),
                                    ["pitchStartMm"] = pitchStart,
                                    ["pitchMidMm"] = pitchMid,
                                    ["pitchEndMm"] = pitchEnd,
                                    ["pitchAtMinEndMm"] = pitchMin,
                                    ["pitchAtMaxEndMm"] = pitchMax
                                };

                                if (needsZoned)
                                {
                                    // Use clear span (support faces) for zoning lengths when available.
                                    double clearStart = (hasClearRange && clearEndFt > clearStartFt) ? clearStartFt : axisStart;
                                    double clearEnd = (hasClearRange && clearEndFt > clearStartFt) ? clearEndFt : axisEnd;
                                    double totalLenFt = clearEnd - clearStart;
                                    if (totalLenFt > UnitHelper.MmToFt(1.0))
                                    {
                                        double qFt = totalLenFt * 0.25;
                                        double seg1Start = clearStart;
                                        double seg1Len = qFt;
                                        double seg2Start = clearStart + qFt;
                                        double seg2Len = totalLenFt * 0.5;
                                        double seg3Start = clearStart + totalLenFt * 0.75;
                                        double seg3Len = totalLenFt - (seg1Len + seg2Len);

                                        bool AddSeg(string segName, double segStartFt, double segLenFt, double pitchMm, bool includeFirst, bool includeLast)
                                        {
                                            if (!(segLenFt > UnitHelper.MmToFt(1.0))) return false;
                                            if (!(pitchMm > 0.0)) return false;

                                            // Clamp to actual placement range (axisStart/axisEnd) after cover adjustments.
                                            double segEndFt = segStartFt + segLenFt;
                                            double segStartClamped = Math.Max(segStartFt, axisStart);
                                            double segEndClamped = Math.Min(segEndFt, axisEnd);
                                            double segLenClamped = segEndClamped - segStartClamped;
                                            if (!(segLenClamped > UnitHelper.MmToFt(1.0))) return false;

                                            var segCurves = BuildTieCurvesAtAxisCoord(segStartClamped);
                                            var segLayout = new JObject
                                            {
                                                ["rule"] = "maximum_spacing",
                                                ["spacingMm"] = pitchMm,
                                                ["arrayLengthMm"] = UnitHelper.FtToMm(segLenClamped),
                                                ["includeFirstBar"] = includeFirst,
                                                ["includeLastBar"] = includeLast,
                                                ["barsOnNormalSide"] = true
                                            };

                                            actions.Add(new JObject
                                            {
                                                ["role"] = "beam_stirrups",
                                                ["segment"] = segName,
                                                ["style"] = actionStyle,
                                                ["barTypeName"] = tieBarTypeName,
                                                ["curves"] = segCurves,
                                                ["normal"] = GeometryJsonHelper.VectorToJson(axisBasis.Normalize()),
                                                ["layout"] = segLayout,
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
                                            return true;
                                        }

                                        // Avoid duplicates at zone junctions:
                                        // - segment1 includeLast=false, segment2 includeFirst=true
                                        // - segment2 includeLast=false, segment3 includeFirst=true
                                        // Label zones by beam start/end (LocationCurve.EndPoint(0/1)), while the coordinates
                                        // are still expressed in min->max axis order.
                                        string minEndZoneName = startIsMin ? "start" : "end";
                                        string maxEndZoneName = startIsMin ? "end" : "start";

                                        bool ok1 = AddSeg(minEndZoneName, seg1Start, seg1Len, pitchMin, includeFirst: true, includeLast: false);
                                        bool ok2 = AddSeg("mid", seg2Start, seg2Len, pitchMid, includeFirst: true, includeLast: false);
                                        bool ok3 = AddSeg(maxEndZoneName, seg3Start, seg3Len, pitchMax, includeFirst: true, includeLast: true);

                                        addedZonedBeamStirrups = ok1 || ok2 || ok3;
                                        if (addedZonedBeamStirrups)
                                        {
                                            hostObj["beamStirrups"] = new JObject
                                            {
                                                ["source"] = "mapping_zoned",
                                                ["zones"] = new JArray
                                                {
                                                    new JObject { ["name"] = minEndZoneName, ["lengthMm"] = UnitHelper.FtToMm(seg1Len), ["spacingMm"] = pitchMin },
                                                    new JObject { ["name"] = "mid", ["lengthMm"] = UnitHelper.FtToMm(seg2Len), ["spacingMm"] = pitchMid },
                                                    new JObject { ["name"] = maxEndZoneName, ["lengthMm"] = UnitHelper.FtToMm(seg3Len), ["spacingMm"] = pitchMax }
                                                }
                                            };
                                        }
                                    }
                                }

                                if (!addedZonedBeamStirrups && pitchMid > 0.0)
                                {
                                    hostObj["beamStirrups"] = new JObject
                                    {
                                        ["source"] = "mapping",
                                        ["spacingMm"] = pitchMid
                                    };
                                }
                            }
                            catch { /* ignore */ }
                        }

                        // Column: split ties into base/head by mid-height (default ON).
                        // If one side pitch is missing or 0, use the other side pitch for both halves.
                        if (isColumn && opts.columnTieSplitByMidHeight && opts.layoutOverride == null)
                        {
                            try
                            {
                                double pitchBase = 0.0;
                                double pitchHead = 0.0;
                                string pitchSource = "layout";

                                // 1) options overrides
                                if (opts.columnTiePitchBaseMm.HasValue) { pitchBase = opts.columnTiePitchBaseMm.Value; pitchSource = "options"; }
                                if (opts.columnTiePitchHeadMm.HasValue) { pitchHead = opts.columnTiePitchHeadMm.Value; pitchSource = "options"; }

                                // 2) mapping (optional)
                                if (pitchSource != "options" && colSpec != null)
                                {
                                    if (colSpec.tiePitchBaseMm.HasValue && colSpec.tiePitchBaseMm.Value > 0.0) { pitchBase = colSpec.tiePitchBaseMm.Value; pitchSource = "mapping"; }
                                    if (colSpec.tiePitchHeadMm.HasValue && colSpec.tiePitchHeadMm.Value > 0.0) { pitchHead = colSpec.tiePitchHeadMm.Value; pitchSource = "mapping"; }
                                }

                                // 3) default from mapping layout
                                double pitchDefault = 0.0;
                                try
                                {
                                    if (values != null) pitchDefault = values.Value<double?>("Common.Arrangement.Spacing") ?? 0.0;
                                }
                                catch { /* ignore */ }
                                if (!(pitchDefault > 0.0)) pitchDefault = 150.0;

                                // Fallback rule: if base/head is missing or 0, use the other side (or default).
                                if (!(pitchBase > 0.0) && pitchHead > 0.0) pitchBase = pitchHead;
                                if (!(pitchHead > 0.0) && pitchBase > 0.0) pitchHead = pitchBase;
                                if (!(pitchBase > 0.0)) pitchBase = pitchDefault;
                                if (!(pitchHead > 0.0)) pitchHead = pitchDefault;

                                double mid = (baseMin + baseMax) / 2.0;
                                if (mid < axisStart) mid = axisStart;
                                if (mid > axisEnd) mid = axisEnd;

                                double baseLenFt = mid - axisStart;
                                double headLenFt = axisEnd - mid;

                                bool AddColumnSeg(string segName, double segStartFt, double segLenFt, double pitchMm, bool includeFirst, bool includeLast)
                                {
                                    if (!(segLenFt > UnitHelper.MmToFt(1.0))) return false;
                                    if (!(pitchMm > 0.0)) return false;

                                    var segCurves = BuildTieCurvesAtAxisCoord(segStartFt);
                                    var segLayout = new JObject
                                    {
                                        ["rule"] = "maximum_spacing",
                                        ["spacingMm"] = pitchMm,
                                        ["arrayLengthMm"] = UnitHelper.FtToMm(segLenFt),
                                        ["includeFirstBar"] = includeFirst,
                                        ["includeLastBar"] = includeLast,
                                        ["barsOnNormalSide"] = true
                                    };

                                    var actionObj = new JObject
                                    {
                                        ["role"] = "column_ties",
                                        ["segment"] = segName,
                                        ["style"] = actionStyle,
                                        ["barTypeName"] = tieBarTypeName,
                                        ["curves"] = segCurves,
                                        ["normal"] = GeometryJsonHelper.VectorToJson(axisBasis.Normalize()),
                                        ["layout"] = segLayout,
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
                                    };
                                    if (tieShapeId != null && tieShapeId != ElementId.InvalidElementId)
                                    {
                                        actionObj["shapeId"] = tieShapeId.IntValue();
                                        if (!string.IsNullOrWhiteSpace(tieShapeName))
                                            actionObj["shapeName"] = tieShapeName;
                                    }
                                    actions.Add(actionObj);
                                    return true;
                                }

                                string seg1Name = axisPositiveIsUp ? "base" : "head";
                                string seg2Name = axisPositiveIsUp ? "head" : "base";
                                double seg1Pitch = axisPositiveIsUp ? pitchBase : pitchHead;
                                double seg2Pitch = axisPositiveIsUp ? pitchHead : pitchBase;

                                bool okSeg1 = AddColumnSeg(seg1Name, axisStart, baseLenFt, seg1Pitch, includeFirst: true, includeLast: false);
                                bool okSeg2 = AddColumnSeg(seg2Name, mid, headLenFt, seg2Pitch, includeFirst: true, includeLast: true);
                                addedZonedColumnTies = okSeg1 || okSeg2;
                                if (addedZonedColumnTies)
                                {
                                    hostObj["columnTies"] = new JObject
                                    {
                                        ["source"] = "mid_split",
                                        ["pitchSource"] = pitchSource,
                                        ["midAxisMm"] = UnitHelper.FtToMm(mid),
                                        ["axisPositiveIsUp"] = axisPositiveIsUp,
                                        ["zones"] = new JArray
                                        {
                                            new JObject { ["name"] = seg1Name, ["lengthMm"] = UnitHelper.FtToMm(baseLenFt), ["spacingMm"] = seg1Pitch },
                                            new JObject { ["name"] = seg2Name, ["lengthMm"] = UnitHelper.FtToMm(headLenFt), ["spacingMm"] = seg2Pitch }
                                        }
                                    };
                                }
                            }
                            catch { /* ignore */ }
                        }

                        if (!addedZonedBeamStirrups && !addedZonedColumnTies)
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
                                    // Prefer mid pitch when available; fall back to pitchEffective (legacy).
                                    var pitchMid = beamSpec.pitchMidMm > 0.0 ? beamSpec.pitchMidMm : beamSpec.pitchEffectiveMm;
                                    layoutObj["spacingMm"] = pitchMid;
                                    if (layoutObj["includeFirstBar"] == null) layoutObj["includeFirstBar"] = true;
                                    if (layoutObj["includeLastBar"] == null) layoutObj["includeLastBar"] = true;
                                    if (layoutObj["barsOnNormalSide"] == null) layoutObj["barsOnNormalSide"] = true;

                                    if (hostObj["beamStirrups"] == null)
                                    {
                                        hostObj["beamStirrups"] = new JObject
                                        {
                                            ["source"] = "mapping",
                                            ["spacingMm"] = pitchMid
                                        };
                                    }
                                }
                                catch { /* ignore */ }
                            }

                            var actionObj = new JObject
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
                            };
                            if (isColumn && tieShapeId != null && tieShapeId != ElementId.InvalidElementId)
                            {
                                actionObj["shapeId"] = tieShapeId.IntValue();
                                if (!string.IsNullOrWhiteSpace(tieShapeName))
                                    actionObj["shapeName"] = tieShapeName;
                            }
                            actions.Add(actionObj);
                        }
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
                ["deleteExistingUntaggedInHosts"] = deleteExistingUntaggedInHosts,
                ["deleteExistingAllInHosts"] = deleteExistingAllInHosts,
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

            bool deleteExistingTaggedInHosts = plan.Value<bool?>("deleteExistingTaggedInHosts") ?? true;
            bool deleteExistingUntaggedInHosts = plan.Value<bool?>("deleteExistingUntaggedInHosts") ?? false;
            bool deleteExistingAllInHosts = plan.Value<bool?>("deleteExistingAllInHosts") ?? false;
            string tagComments = (plan.Value<string>("tagComments") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(tagComments)) tagComments = DefaultTagComments;
            if (deleteExistingAllInHosts)
            {
                deleteExistingTaggedInHosts = true;
                deleteExistingUntaggedInHosts = true;
            }

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
                            if (deleteExistingTaggedInHosts || deleteExistingUntaggedInHosts)
                            {
                                var toDelete = new HashSet<int>();
                                if (deleteExistingAllInHosts)
                                {
                                    var allIds = RebarDeleteService.CollectAllRebarIdsInHost(doc, host);
                                    if (allIds != null)
                                    {
                                        foreach (var v in allIds) toDelete.Add(v);
                                    }
                                }
                                if (deleteExistingTaggedInHosts)
                                {
                                    var tagged = RebarDeleteService.CollectTaggedRebarIdsInHost(doc, host, tagComments);
                                    if (tagged != null)
                                    {
                                        foreach (var v in tagged) toDelete.Add(v);
                                    }
                                }
                                if (deleteExistingUntaggedInHosts)
                                {
                                    var untagged = RebarDeleteService.CollectUntaggedRebarIdsInHost(doc, host, tagComments);
                                    if (untagged != null)
                                    {
                                        foreach (var v in untagged) toDelete.Add(v);
                                    }
                                }
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

        private static bool TryGetBeamAxisCoordsFromLocationCurve(Element host, Transform tr, int axisIndex, out double end0Axis, out double end1Axis)
        {
            end0Axis = 0.0;
            end1Axis = 0.0;
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

                end0Axis = axisIndex == 0 ? lp0.X : (axisIndex == 1 ? lp0.Y : lp0.Z);
                end1Axis = axisIndex == 0 ? lp1.X : (axisIndex == 1 ? lp1.Y : lp1.Z);
                return true;
            }
            catch
            {
                end0Axis = 0.0;
                end1Axis = 0.0;
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
            string[] concreteTokens,
            string[] concreteExcludeTokens,
            string[] concreteExcludeMaterialClasses,
            bool requireConcrete,
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
                 // Optionally restrict to *concrete* supports.
                 if (requireConcrete)
                 {
                     if (!TryElementLooksConcrete(doc, e, concreteTokens, concreteExcludeTokens, concreteExcludeMaterialClasses,
                         out var _, out var _, out var _)) return false;
                 }
 
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

        private static bool TryDetectConcreteSupportsAtBeamAxisEnds(
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
            double touchTolFt,
            string[] concreteTokens,
            string[] concreteExcludeTokens,
            string[] concreteExcludeMaterialClasses,
            out bool hasConcreteAtMinEnd,
            out bool hasConcreteAtMaxEnd,
            out JObject debug)
        {
            hasConcreteAtMinEnd = false;
            hasConcreteAtMaxEnd = false;
            debug = null;
            if (doc == null || beam == null) return false;
            if (beamTr == null) beamTr = Transform.Identity;
            if (beamLocalBox == null) return false;
            if (!(beamMaxAxis > beamMinAxis)) return false;

            double sMid = (GetMinByIndex(beamLocalBox, sideIndex) + GetMaxByIndex(beamLocalBox, sideIndex)) / 2.0;
            double uMid = (GetMinByIndex(beamLocalBox, upIndex) + GetMaxByIndex(beamLocalBox, upIndex)) / 2.0;

            XYZ minWorld = null;
            XYZ maxWorld = null;
            try
            {
                minWorld = beamTr.OfPoint(MakeLocalPoint(axisIndex, beamMinAxis, sideIndex, sMid, upIndex, uMid));
                maxWorld = beamTr.OfPoint(MakeLocalPoint(axisIndex, beamMaxAxis, sideIndex, sMid, upIndex, uMid));
            }
            catch { /* ignore */ }
            if (minWorld == null || maxWorld == null) return false;

            var minCandidates = new HashSet<int>();
            var maxCandidates = new HashSet<int>();

            try
            {
                foreach (var id in CollectColumnLikeIdsNearPoint(doc, minWorld, searchRangeFt)) minCandidates.Add(id);
                foreach (var id in CollectFramingLikeIdsNearPoint(doc, minWorld, searchRangeFt)) minCandidates.Add(id);
                foreach (var id in CollectColumnLikeIdsNearPoint(doc, maxWorld, searchRangeFt)) maxCandidates.Add(id);
                foreach (var id in CollectFramingLikeIdsNearPoint(doc, maxWorld, searchRangeFt)) maxCandidates.Add(id);
            }
            catch { /* ignore */ }

            // Joined elements often include the true supports (best-effort).
            try
            {
                var joined = JoinGeometryUtils.GetJoinedElements(doc, beam);
                if (joined != null)
                {
                    foreach (var jid in joined)
                    {
                        int v = 0;
                        try { v = jid.IntValue(); } catch { v = 0; }
                        if (v <= 0) continue;
                        minCandidates.Add(v);
                        maxCandidates.Add(v);
                    }
                }
            }
            catch { /* ignore */ }

            var all = new HashSet<int>();
            foreach (var id in minCandidates) all.Add(id);
            foreach (var id in maxCandidates) all.Add(id);

            var minMatches = new JArray();
            var maxMatches = new JArray();

            var inv = beamTr.Inverse;

            foreach (var id in all)
            {
                if (id <= 0) continue;
                try
                {
                    if (beam.Id != null && id == beam.Id.IntValue()) continue;
                }
                catch { /* ignore */ }

                Element e = null;
                try { e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)); } catch { e = null; }
                if (e == null) continue;

                int catId = 0;
                try { catId = e.Category != null ? e.Category.Id.IntValue() : 0; } catch { catId = 0; }
                bool isColumnCat = (catId == (int)BuiltInCategory.OST_StructuralColumns) || (catId == (int)BuiltInCategory.OST_Columns);
                bool isFramingCat = (catId == (int)BuiltInCategory.OST_StructuralFraming);
                if (!isColumnCat && !isFramingCat) continue;

                if (!TryElementLooksConcrete(doc, e, concreteTokens, concreteExcludeTokens, concreteExcludeMaterialClasses,
                    out var matName, out var matSource, out var tok)) continue;

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
                if (double.IsInfinity(minA) || double.IsInfinity(maxA) || !(maxA > minA)) continue;

                double distToMinFt = Math.Abs(maxA - beamMinAxis); // face toward beam interior at min end
                double distToMaxFt = Math.Abs(minA - beamMaxAxis); // face toward beam interior at max end

                bool touchesMin = distToMinFt <= touchTolFt && minCandidates.Contains(id);
                bool touchesMax = distToMaxFt <= touchTolFt && maxCandidates.Contains(id);

                if (!touchesMin && !touchesMax) continue;

                var item = new JObject
                {
                    ["elementId"] = id,
                    ["category"] = e.Category != null ? (e.Category.Name ?? string.Empty) : string.Empty,
                    ["catId"] = catId,
                    ["materialName"] = matName,
                    ["materialSource"] = matSource,
                    ["matchedToken"] = tok,
                    ["axisMinMm"] = UnitHelper.FtToMm(minA),
                    ["axisMaxMm"] = UnitHelper.FtToMm(maxA),
                    ["distToMinEndMm"] = UnitHelper.FtToMm(distToMinFt),
                    ["distToMaxEndMm"] = UnitHelper.FtToMm(distToMaxFt)
                };

                if (touchesMin)
                {
                    hasConcreteAtMinEnd = true;
                    minMatches.Add(item);
                }
                if (touchesMax)
                {
                    hasConcreteAtMaxEnd = true;
                    maxMatches.Add(item);
                }
            }

            debug = new JObject
            {
                ["enabled"] = true,
                ["searchRangeMm"] = UnitHelper.FtToMm(searchRangeFt),
                ["touchTolMm"] = UnitHelper.FtToMm(touchTolFt),
                ["hasConcreteAtMinEnd"] = hasConcreteAtMinEnd,
                ["hasConcreteAtMaxEnd"] = hasConcreteAtMaxEnd,
                ["minCandidates"] = minCandidates.Count,
                ["maxCandidates"] = maxCandidates.Count,
                ["matchesMinEnd"] = minMatches.Count > 0 ? (JToken)minMatches : JValue.CreateNull(),
                ["matchesMaxEnd"] = maxMatches.Count > 0 ? (JToken)maxMatches : JValue.CreateNull()
            };

            return true;
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

        private static IEnumerable<int> CollectFramingLikeIdsNearPoint(Document doc, XYZ worldPoint, double rangeFt)
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
                col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType();
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

        private static IEnumerable<int> CollectFoundationLikeIdsNearPoint(Document doc, XYZ worldPoint, double rangeFt)
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
                var cats = new List<BuiltInCategory> { BuiltInCategory.OST_StructuralFoundation };
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

        private static bool TryGetElementAxisRangeInHostLocal(Element e, Transform hostInvTr, int axisIndex, out double minAxis, out double maxAxis)
        {
            minAxis = double.PositiveInfinity;
            maxAxis = double.NegativeInfinity;
            if (e == null) return false;
            if (hostInvTr == null) hostInvTr = Transform.Identity;

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

            foreach (var p in corners)
            {
                XYZ q;
                try { q = hostInvTr.OfPoint(p); } catch { q = p; }
                double a = axisIndex == 0 ? q.X : (axisIndex == 1 ? q.Y : q.Z);
                if (double.IsNaN(a) || double.IsInfinity(a)) continue;
                if (a < minAxis) minAxis = a;
                if (a > maxAxis) maxAxis = a;
            }

            return !double.IsInfinity(minAxis) && !double.IsInfinity(maxAxis) && (maxAxis > minAxis);
        }

        private static readonly string[] DefaultConcreteTokens = new[]
        {
            "コンクリ",
            "コンクリート",
            "concrete",
            "RC",
            "FC",
            "ＲＣ",
            "ＦＣ"
        };
        private static readonly string[] DefaultConcreteExcludeTokens = new[]
        {
            "Steel",
            "鋼",
            "鉄骨",
            "SS",
            "SN",
            "SM"
        };
        private static readonly string[] DefaultConcreteExcludeMaterialClasses = new[]
        {
            "Metal",
            "Steel",
            "鋼",
            "金属",
            "鉄"
        };

        private static bool IsExcludedMaterialClass(string materialClass, string[] excludeClasses)
        {
            if (string.IsNullOrWhiteSpace(materialClass)) return false;
            var s = materialClass.Trim();
            var list = (excludeClasses != null && excludeClasses.Length > 0) ? excludeClasses : DefaultConcreteExcludeMaterialClasses;
            foreach (var t in list)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (s.IndexOf(t.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool ContainsAbbrevToken(string s, string tokenUpper)
        {
            if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(tokenUpper)) return false;
            var u = s.ToUpperInvariant();
            var t = tokenUpper.ToUpperInvariant();
            int idx = 0;
            while (true)
            {
                idx = u.IndexOf(t, idx, StringComparison.Ordinal);
                if (idx < 0) break;
                char prev = idx > 0 ? u[idx - 1] : '\0';
                char next = (idx + t.Length) < u.Length ? u[idx + t.Length] : '\0';
                bool prevOk = idx == 0 || !char.IsLetterOrDigit(prev);
                bool nextOk = (idx + t.Length) >= u.Length || !char.IsLetterOrDigit(next) || char.IsDigit(next);
                if (prevOk && nextOk) return true;
                idx += t.Length;
            }
            return false;
        }

        private static bool ContainsToken(string s, string token)
        {
            if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(token)) return false;
            var t = token.Trim();
            if (t.Length == 0) return false;
            bool hasNonAscii = t.Any(ch => ch > 0x7F);
            if (hasNonAscii) return s.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
            bool isAlnum = t.All(char.IsLetterOrDigit);
            if (isAlnum && t.Length <= 3)
                return ContainsAbbrevToken(s, t);
            return s.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsConcreteMaterialName(string name, out string matchedToken, string[] includeTokens = null, string[] excludeTokens = null)
        {
            matchedToken = string.Empty;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var s = name.Trim();

            var exList = (excludeTokens != null && excludeTokens.Length > 0) ? excludeTokens : DefaultConcreteExcludeTokens;
            foreach (var ex in exList)
            {
                if (string.IsNullOrWhiteSpace(ex)) continue;
                if (ContainsToken(s, ex)) return false;
            }

            var inList = (includeTokens != null && includeTokens.Length > 0) ? includeTokens : null;
            if (inList != null)
            {
                foreach (var tok in inList)
                {
                    if (string.IsNullOrWhiteSpace(tok)) continue;
                    if (ContainsToken(s, tok))
                    {
                        matchedToken = tok.Trim();
                        return true;
                    }
                }
                return false;
            }

            if (ContainsToken(s, "コンクリート") || ContainsToken(s, "コンクリ"))
            {
                matchedToken = "コンクリ";
                return true;
            }
            if (ContainsToken(s, "concrete"))
            {
                matchedToken = "concrete";
                return true;
            }
            if (ContainsAbbrevToken(s, "RC"))
            {
                matchedToken = "RC";
                return true;
            }
            if (ContainsAbbrevToken(s, "FC"))
            {
                matchedToken = "FC";
                return true;
            }
            if (ContainsToken(s, "ＲＣ"))
            {
                matchedToken = "ＲＣ";
                return true;
            }
            if (ContainsToken(s, "ＦＣ"))
            {
                matchedToken = "ＦＣ";
                return true;
            }

            return false;
        }

        private static bool TryElementLooksConcrete(
            Document doc,
            Element e,
            string[] includeTokens,
            string[] excludeTokens,
            string[] excludeMaterialClasses,
            out string materialName,
            out string source,
            out string matchedToken)
        {
            materialName = string.Empty;
            source = string.Empty;
            matchedToken = string.Empty;
            if (doc == null || e == null) return false;

            bool TryCheckMaterialId(ElementId mid, out string name, out string matClass, out string tok)
            {
                name = string.Empty;
                matClass = string.Empty;
                tok = string.Empty;
                if (mid == null) return false;
                try
                {
                    if (mid == ElementId.InvalidElementId) return false;
                }
                catch { /* ignore */ }

                Material mat = null;
                try { mat = doc.GetElement(mid) as Material; } catch { mat = null; }
                if (mat == null) return false;

                // Prefer MaterialClass when available (less ambiguity than name tokens).
                try
                {
                    matClass = (mat.MaterialClass ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(matClass))
                    {
                        if (IsExcludedMaterialClass(matClass, excludeMaterialClasses)) return false;
                        if (IsConcreteMaterialName(matClass, out var tc, includeTokens, excludeTokens))
                        {
                            name = (mat.Name ?? string.Empty).Trim();
                            tok = "MaterialClass:" + tc;
                            return true;
                        }
                    }
                }
                catch { /* ignore */ }

                var n = (mat.Name ?? string.Empty).Trim();
                if (!IsConcreteMaterialName(n, out var t, includeTokens, excludeTokens)) return false;
                name = n;
                tok = t;
                return true;
            }

            // 1) Instance structural material
            try
            {
                var p = e.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var mid = p.AsElementId();
                    if (TryCheckMaterialId(mid, out var n, out var mc, out var tok))
                    {
                        materialName = n;
                        source = !string.IsNullOrWhiteSpace(mc) ? ("structuralMaterial(instance):" + mc) : "structuralMaterial(instance)";
                        matchedToken = tok;
                        return true;
                    }
                }
            }
            catch { /* ignore */ }

            // 2) Type structural material
            try
            {
                var tid = e.GetTypeId();
                if (tid != null && tid != ElementId.InvalidElementId)
                {
                    var t = doc.GetElement(tid);
                    if (t != null)
                    {
                        var p = t.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                        if (p != null && p.StorageType == StorageType.ElementId)
                        {
                            var mid = p.AsElementId();
                            if (TryCheckMaterialId(mid, out var n, out var mc, out var tok))
                            {
                                materialName = n;
                                source = !string.IsNullOrWhiteSpace(mc) ? ("structuralMaterial(type):" + mc) : "structuralMaterial(type)";
                                matchedToken = tok;
                                return true;
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }

            // 3) Geometry/material ids (best-effort; can be expensive)
            try
            {
                var mids = e.GetMaterialIds(false);
                if (mids != null)
                {
                    foreach (var mid in mids)
                    {
                        if (TryCheckMaterialId(mid, out var n, out var mc, out var tok))
                        {
                            materialName = n;
                            source = !string.IsNullOrWhiteSpace(mc) ? ("geometryMaterials:" + mc) : "geometryMaterials";
                            matchedToken = tok;
                            return true;
                        }
                    }
                }
            }
            catch { /* ignore */ }

            return false;
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

        private static bool TryGetDiameterMmFromParams(Element inst, ElementType typeElem, out double diaMm, out string source)
        {
            diaMm = 0.0;
            source = null;
            if (inst == null && typeElem == null) return false;

            // Prefer explicit diameter-like parameter names and avoid bar/rebar-related params.
            bool IsDiameterName(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return false;
                if (NameContainsAny(name, "鉄筋", "rebar", "bar")) return false;
                return NameContainsAny(name, "直径", "径", "Diameter", "diameter", "Φ", "φ");
            }

            bool TryRead(Element e, out double vMm, out string nameUsed)
            {
                vMm = 0.0;
                nameUsed = null;
                if (e == null) return false;
                try
                {
                    foreach (Parameter p in e.Parameters)
                    {
                        var n = p?.Definition?.Name ?? string.Empty;
                        if (!IsDiameterName(n)) continue;
                        try
                        {
                            var si = UnitHelper.ParamToSiInfo(p);
                            var obj = JObject.FromObject(si);
                            var vTok = obj["value"];
                            double v = 0.0;
                            if (vTok != null)
                            {
                                if (vTok.Type == JTokenType.Integer) v = vTok.Value<double>();
                                else if (vTok.Type == JTokenType.Float) v = vTok.Value<double>();
                                else if (vTok.Type == JTokenType.String)
                                {
                                    double tmp;
                                    if (UnitHelper.TryParseDouble(vTok.Value<string>(), out tmp)) v = tmp;
                                }
                            }
                            if (v > 0.0)
                            {
                                vMm = v;
                                nameUsed = n;
                                return true;
                            }
                        }
                        catch { /* ignore */ }
                    }
                }
                catch { /* ignore */ }
                return false;
            }

            double vmm;
            string nameUsed;
            if (TryRead(inst, out vmm, out nameUsed))
            {
                diaMm = vmm;
                source = "instance:" + nameUsed;
                return true;
            }
            if (TryRead(typeElem, out vmm, out nameUsed))
            {
                diaMm = vmm;
                source = "type:" + nameUsed;
                return true;
            }
            return false;
        }

        private static bool TryDetectCircularColumn(Document doc, Element host, ElementType typeElem, LocalBox localBox, int crossA, int crossB, out double diameterFt, out string source)
        {
            diameterFt = 0.0;
            source = null;
            if (host == null) return false;
            bool hasLocalBox = localBox != null;

            bool HasParamName(Element e, string name)
            {
                if (e == null || string.IsNullOrWhiteSpace(name)) return false;
                try
                {
                    var p = e.LookupParameter(name);
                    return p != null;
                }
                catch { return false; }
            }

            // 0) Prefer geometry-based detection (family-agnostic)
            double geoDiaFt;
            string geoSrc;
            if (TryDetectCircularColumnByGeometry(host, out geoDiaFt, out geoSrc))
            {
                if (geoDiaFt > 1e-6)
                {
                    diameterFt = geoDiaFt;
                    source = geoSrc;
                    return true;
                }
                // geometry says circular but diameter not resolved -> fallback to bbox
                if (hasLocalBox)
                {
                    double aLen = GetMaxByIndex(localBox, crossA) - GetMinByIndex(localBox, crossA);
                    double bLen = GetMaxByIndex(localBox, crossB) - GetMinByIndex(localBox, crossB);
                    if (aLen > 1e-6 && bLen > 1e-6)
                    {
                        diameterFt = Math.Min(aLen, bLen);
                        source = string.IsNullOrWhiteSpace(geoSrc) ? "geom:bbox" : (geoSrc + "+bbox");
                        return true;
                    }
                }
            }

            // 1) Prefer explicit diameter parameter
            double diaMm;
            if (TryGetDiameterMmFromParams(host, typeElem, out diaMm, out var src))
            {
                if (diaMm > 0.0)
                {
                    diameterFt = UnitHelper.MmToFt(diaMm);
                    source = src;
                    return true;
                }
            }

            // 2) Round-cover param + bbox similarity (best-effort)
            // NOTE: do NOT treat "square bbox" alone as circular (square columns exist).
            // Only allow this fallback when an explicit round-cover parameter is present.
            bool hasRoundCoverParam = HasParamName(host, "かぶり厚-丸") || HasParamName(typeElem, "かぶり厚-丸");
            if (hasRoundCoverParam && hasLocalBox)
            {
                double aLen = GetMaxByIndex(localBox, crossA) - GetMinByIndex(localBox, crossA);
                double bLen = GetMaxByIndex(localBox, crossB) - GetMinByIndex(localBox, crossB);
                double tolFt = UnitHelper.MmToFt(10.0);
                if (aLen > 1e-6 && bLen > 1e-6 && Math.Abs(aLen - bLen) <= tolFt)
                {
                    diameterFt = Math.Min(aLen, bLen);
                    source = "bbox+roundParam";
                    return true;
                }
            }

            return false;
        }

        private static bool TryDetectCircularColumnByGeometry(Element host, out double diameterFt, out string source)
        {
            diameterFt = 0.0;
            source = null;
            if (host == null) return false;

            bool TryDetectFromSolid(Solid solid, out double diaFt, out string src)
            {
                diaFt = 0.0;
                src = null;
                if (solid == null || solid.Faces == null || solid.Faces.Size == 0) return false;

                double bestDia = 0.0;
                string bestSrc = null;

                foreach (Face face in solid.Faces)
                {
                    // 1) Cylindrical face (most reliable)
                    var cyl = face as CylindricalFace;
                    if (cyl != null)
                    {
                        var axis = cyl.Axis;
                        double cylRadius = TryGetCylindricalFaceRadiusFt(cyl);
                        if (IsAxisVertical(axis) && cylRadius > 1e-6)
                        {
                            double d = cylRadius * 2.0;
                            if (d > bestDia)
                            {
                                bestDia = d;
                                bestSrc = "geom:cylindrical_face";
                            }
                        }
                        continue;
                    }

                    // 2) Horizontal planar face with circular edge loop
                    var pf = face as PlanarFace;
                    if (pf == null) continue;
                    if (!IsAxisVertical(pf.FaceNormal)) continue;

                    try
                    {
                        var loops = pf.GetEdgesAsCurveLoops();
                        foreach (var loop in loops)
                        {
                            double loopDia;
                            if (TryGetCircularLoopDiameter(loop, out loopDia))
                            {
                                if (loopDia > bestDia)
                                {
                                    bestDia = loopDia;
                                    bestSrc = "geom:planar_loop_arcs";
                                }
                            }
                            else if (TryGetCircularLoopDiameterFromTessellation(loop, out loopDia))
                            {
                                if (loopDia > bestDia)
                                {
                                    bestDia = loopDia;
                                    bestSrc = "geom:planar_loop_tess";
                                }
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                if (bestDia > 1e-6)
                {
                    diaFt = bestDia;
                    src = bestSrc;
                    return true;
                }
                return false;
            }

            bool TryDetectFromGeomElem(GeometryElement ge, out double diaFt, out string src)
            {
                diaFt = 0.0;
                src = null;
                if (ge == null) return false;
                foreach (GeometryObject obj in ge)
                {
                    if (obj is Solid solid)
                    {
                        if (TryDetectFromSolid(solid, out diaFt, out src)) return true;
                    }
                    else if (obj is GeometryInstance gi)
                    {
                        try
                        {
                            var instGeom = gi.GetInstanceGeometry();
                            if (TryDetectFromGeomElem(instGeom, out diaFt, out src)) return true;
                        }
                        catch { /* ignore */ }
                        try
                        {
                            var symGeom = gi.GetSymbolGeometry();
                            if (TryDetectFromGeomElem(symGeom, out diaFt, out src)) return true;
                        }
                        catch { /* ignore */ }
                    }
                }
                return false;
            }

            try
            {
                var opt = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Fine
                };
                var ge = host.get_Geometry(opt);
                double dia;
                string src;
                if (TryDetectFromGeomElem(ge, out dia, out src))
                {
                    diameterFt = dia;
                    source = src;
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool TryGetCircularLoopDiameter(CurveLoop loop, out double diameterFt)
        {
            diameterFt = 0.0;
            if (loop == null) return false;
            XYZ center = null;
            double radius = 0.0;
            double angleSum = 0.0;
            int arcCount = 0;
            double tolFt = UnitHelper.MmToFt(2.0);

            foreach (Curve c in loop)
            {
                var arc = c as Arc;
                if (arc == null) return false;
                if (arc.Radius <= 1e-6) return false;
                if (center == null)
                {
                    center = arc.Center;
                    radius = arc.Radius;
                }
                else
                {
                    if (center.DistanceTo(arc.Center) > tolFt) return false;
                    if (Math.Abs(arc.Radius - radius) > tolFt) return false;
                }
                angleSum += arc.Length / arc.Radius;
                arcCount++;
            }

            if (arcCount == 0) return false;
            double twoPi = Math.PI * 2.0;
            if (Math.Abs(angleSum - twoPi) > 0.6)
            {
                // allow small tolerance; if far off, not a full circle loop
                return false;
            }
            diameterFt = radius * 2.0;
            return true;
        }

        private static bool TryGetCircularLoopDiameterFromTessellation(CurveLoop loop, out double diameterFt)
        {
            diameterFt = 0.0;
            if (loop == null) return false;

            var pts = new List<XYZ>();
            double mergeTol = UnitHelper.MmToFt(1.0);

            try
            {
                foreach (Curve c in loop)
                {
                    if (c == null) continue;
                    IList<XYZ> seg = null;
                    try { seg = c.Tessellate(); } catch { seg = null; }
                    if (seg == null || seg.Count == 0) continue;
                    foreach (var p in seg)
                    {
                        if (p == null) continue;
                        if (pts.Count == 0)
                        {
                            pts.Add(p);
                            continue;
                        }
                        var last = pts[pts.Count - 1];
                        if (last.DistanceTo(p) > mergeTol)
                            pts.Add(p);
                    }
                }
            }
            catch { /* ignore */ }

            if (pts.Count < 8) return false;

            // Use centroid as circle center approximation
            double cx = 0, cy = 0, cz = 0;
            foreach (var p in pts) { cx += p.X; cy += p.Y; cz += p.Z; }
            cx /= pts.Count; cy /= pts.Count; cz /= pts.Count;
            var center = new XYZ(cx, cy, cz);

            double sumR = 0.0;
            double maxDev = 0.0;
            var angles = new List<double>();
            foreach (var p in pts)
            {
                var v = p - center;
                double r = v.GetLength();
                if (r <= 1e-9) return false;
                sumR += r;
                double ang = Math.Atan2(v.Y, v.X);
                if (ang < 0) ang += Math.PI * 2.0;
                angles.Add(ang);
            }
            double avgR = sumR / pts.Count;
            if (avgR <= 1e-9) return false;

            foreach (var p in pts)
            {
                var v = p - center;
                double r = v.GetLength();
                double dev = Math.Abs(r - avgR);
                if (dev > maxDev) maxDev = dev;
            }

            double tolAbs = UnitHelper.MmToFt(5.0);
            double tolRel = avgR * 0.02; // 2%
            if (maxDev > Math.Max(tolAbs, tolRel)) return false;

            // Ensure coverage around the circle (not just an arc)
            angles.Sort();
            double maxGap = 0.0;
            for (int i = 0; i < angles.Count; i++)
            {
                double a0 = angles[i];
                double a1 = (i == angles.Count - 1) ? (angles[0] + Math.PI * 2.0) : angles[i + 1];
                double gap = a1 - a0;
                if (gap > maxGap) maxGap = gap;
            }
            if (maxGap > Math.PI) return false; // more than half circle missing

            diameterFt = avgR * 2.0;
            return true;
        }

        private static bool IsAxisVertical(XYZ axis)
        {
            if (axis == null) return false;
            XYZ a;
            try { a = axis.Normalize(); }
            catch { return false; }
            return Math.Abs(Math.Abs(a.Z) - 1.0) <= 0.1;
        }

        private static double TryGetCylindricalFaceRadiusFt(CylindricalFace cyl)
        {
            if (cyl == null) return 0.0;
            try
            {
                var prop = cyl.GetType().GetProperty("Radius");
                if (prop != null && prop.PropertyType == typeof(double))
                {
                    var v = prop.GetValue(cyl, null);
                    if (v is double d) return d;
                }
            }
            catch { /* ignore */ }

            try
            {
                var m = cyl.GetType().GetMethod("get_Radius", new[] { typeof(int) });
                if (m != null)
                {
                    var rv = m.Invoke(cyl, new object[] { 0 });
                    if (rv is double d) return d;
                    if (rv is XYZ v) return v.GetLength();
                }
            }
            catch { /* ignore */ }

            return 0.0;
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

        private static double GetByIndex(XYZ p, int idx)
        {
            if (idx == 0) return p.X;
            if (idx == 1) return p.Y;
            return p.Z;
        }

        private static bool TryGetElementLocalBounds(Element e, Transform hostInvTr, out XYZ min, out XYZ max)
        {
            min = new XYZ(0, 0, 0);
            max = new XYZ(0, 0, 0);
            if (e == null) return false;
            if (hostInvTr == null) hostInvTr = Transform.Identity;

            BoundingBoxXYZ bb = null;
            try { bb = e.get_BoundingBox(null); } catch { bb = null; }
            if (bb == null || bb.Min == null || bb.Max == null) return false;

            var bmin = bb.Min;
            var bmax = bb.Max;
            var corners = new[]
            {
                new XYZ(bmin.X, bmin.Y, bmin.Z),
                new XYZ(bmax.X, bmin.Y, bmin.Z),
                new XYZ(bmin.X, bmax.Y, bmin.Z),
                new XYZ(bmax.X, bmax.Y, bmin.Z),
                new XYZ(bmin.X, bmin.Y, bmax.Z),
                new XYZ(bmax.X, bmin.Y, bmax.Z),
                new XYZ(bmin.X, bmax.Y, bmax.Z),
                new XYZ(bmax.X, bmax.Y, bmax.Z)
            };

            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            foreach (var p in corners)
            {
                XYZ q;
                try { q = hostInvTr.OfPoint(p); } catch { q = p; }
                if (double.IsNaN(q.X) || double.IsInfinity(q.X)) continue;
                if (double.IsNaN(q.Y) || double.IsInfinity(q.Y)) continue;
                if (double.IsNaN(q.Z) || double.IsInfinity(q.Z)) continue;
                if (q.X < minX) minX = q.X;
                if (q.Y < minY) minY = q.Y;
                if (q.Z < minZ) minZ = q.Z;
                if (q.X > maxX) maxX = q.X;
                if (q.Y > maxY) maxY = q.Y;
                if (q.Z > maxZ) maxZ = q.Z;
            }

            if (double.IsInfinity(minX) || double.IsInfinity(maxX)) return false;
            min = new XYZ(minX, minY, minZ);
            max = new XYZ(maxX, maxY, maxZ);
            return (max.X > min.X || max.Y > min.Y || max.Z > min.Z);
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

        private static Autodesk.Revit.DB.Structure.Rebar TryCreateFromCurvesAndShape(
            Document doc,
            Autodesk.Revit.DB.Structure.RebarShape shape,
            Autodesk.Revit.DB.Structure.RebarBarType barType,
            Autodesk.Revit.DB.Structure.RebarHookType startHook,
            Autodesk.Revit.DB.Structure.RebarHookType endHook,
            Element host,
            XYZ normal,
            IList<Curve> curves,
            RebarHookOrientation startOrient,
            RebarHookOrientation endOrient)
        {
            if (doc == null || shape == null || barType == null || host == null || curves == null) return null;

            try
            {
                var methods = typeof(Autodesk.Revit.DB.Structure.Rebar)
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Where(m => m.Name == "CreateFromCurvesAndShape")
                    .ToList();
                if (methods.Count == 0) return null;

                foreach (var m in methods)
                {
                    var ps = m.GetParameters();
                    if (ps == null || ps.Length == 0) continue;

                    var args = new object[ps.Length];
                    int hookIdx = 0;
                    int orientIdx = 0;
                    bool ok = true;

                    for (int i = 0; i < ps.Length; i++)
                    {
                        var t = ps[i].ParameterType;
                        if (t == typeof(Document)) args[i] = doc;
                        else if (t == typeof(Autodesk.Revit.DB.Structure.RebarShape)) args[i] = shape;
                        else if (t == typeof(Autodesk.Revit.DB.Structure.RebarBarType)) args[i] = barType;
                        else if (t == typeof(Autodesk.Revit.DB.Structure.RebarHookType))
                        {
                            args[i] = hookIdx == 0 ? (object)startHook : (object)endHook;
                            hookIdx++;
                        }
                        else if (t == typeof(Element)) args[i] = host;
                        else if (t == typeof(XYZ)) args[i] = normal;
                        else if (typeof(IList<Curve>).IsAssignableFrom(t)) args[i] = curves;
                        else if (t == typeof(RebarHookOrientation))
                        {
                            args[i] = orientIdx == 0 ? (object)startOrient : (object)endOrient;
                            orientIdx++;
                        }
                        else if (t == typeof(bool)) args[i] = true;
                        else { ok = false; break; }
                    }

                    if (!ok) continue;

                    try
                    {
                        var obj = m.Invoke(null, args);
                        var rb = obj as Autodesk.Revit.DB.Structure.Rebar;
                        if (rb != null) return rb;
                    }
                    catch { /* try next overload */ }
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static List<Curve> BuildPolylineFromCurves(IList<Curve> curves)
        {
            if (curves == null || curves.Count == 0) return null;
            var pts = new List<XYZ>();
            foreach (var c in curves)
            {
                if (c == null) continue;
                IList<XYZ> tess = null;
                try { tess = c.Tessellate(); } catch { tess = null; }
                if (tess == null || tess.Count < 2) continue;
                if (pts.Count == 0)
                {
                    pts.AddRange(tess);
                }
                else
                {
                    try
                    {
                        var last = pts[pts.Count - 1];
                        if (last.DistanceTo(tess[0]) < 1e-6)
                            pts.AddRange(tess.Skip(1));
                        else
                            pts.AddRange(tess);
                    }
                    catch { pts.AddRange(tess); }
                }
            }
            if (pts.Count < 2) return null;

            // Close loop if endpoints coincide or very close.
            try
            {
                if (pts[0].DistanceTo(pts[pts.Count - 1]) < 1e-6)
                {
                    pts[pts.Count - 1] = pts[0];
                }
            }
            catch { /* ignore */ }

            var res = new List<Curve>();
            for (int i = 1; i < pts.Count; i++)
            {
                res.Add(Line.CreateBound(pts[i - 1], pts[i]));
            }
            return res;
        }

        private static bool TryFindCircularTieShape(Document doc, out Autodesk.Revit.DB.Structure.RebarShape shape, out string source)
        {
            shape = null;
            source = string.Empty;
            if (doc == null) return false;

            try
            {
                var list = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Structure.RebarShape))
                    .Cast<Autodesk.Revit.DB.Structure.RebarShape>()
                    .ToList();
                if (list.Count == 0) return false;

                int bestScore = -1;
                Autodesk.Revit.DB.Structure.RebarShape best = null;
                foreach (var s in list)
                {
                    if (s == null) continue;
                    var n = (s.Name ?? string.Empty).Trim();
                    if (n.Length == 0) continue;

                    int score = 0;
                    if (n.IndexOf("31", StringComparison.OrdinalIgnoreCase) >= 0) score += 100;
                    if (NameContainsAny(n, "丸", "円", "circle", "circular", "round")) score += 60;
                    if (NameContainsAny(n, "フープ", "スターラップ", "stirrup", "tie", "hoop")) score += 30;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = s;
                    }
                }

                if (best != null)
                {
                    shape = best;
                    source = "auto:" + (best.Name ?? string.Empty);
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
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
