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

            // Beam main bars: user-specified counts (overrides mapping/default when provided)
            public int? beamMainTopCount;
            public int? beamMainBottomCount;

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

            // Beam stirrups: which corner is the first segment start.
            public string beamStirrupStartCorner = "top_left"; // bottom_left|bottom_right|top_right|top_left

            // Beam stirrups: optional hook at both ends (best-effort).
            // When enabled, the stirrup curve becomes an open polyline and hooks are applied by Revit.
            public bool beamStirrupUseHooks;
            public double beamStirrupHookAngleDeg; // e.g. 135
            public string beamStirrupHookTypeName = string.Empty; // optional exact name (preferred)
            public string beamStirrupHookOrientationStart = "left";
            public string beamStirrupHookOrientationEnd = "right";

            // Beam stirrups: start/end offsets from the physical end faces (mm).
            public double beamStirrupStartOffsetMm;
            public double beamStirrupEndOffsetMm;

            // Column ties: optional hook at both ends (best-effort).
            public bool columnTieUseHooks;
            public double columnTieHookAngleDeg; // e.g. 135
            public string columnTieHookTypeName = string.Empty;
            public string columnTieHookOrientationStart = "left";
            public string columnTieHookOrientationEnd = "right";

            // Column ties: start/end offsets from the physical end faces (mm) along the column axis.
            public double columnTieBottomOffsetMm;
            public double columnTieTopOffsetMm;

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

                    o.beamStirrupStartCorner = (obj.Value<string>("beamStirrupStartCorner") ?? "top_left").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.beamStirrupStartCorner)) o.beamStirrupStartCorner = "top_left";

                    o.beamStirrupUseHooks = obj.Value<bool?>("beamStirrupUseHooks") ?? false;
                    o.beamStirrupHookAngleDeg = obj.Value<double?>("beamStirrupHookAngleDeg") ?? 0.0;
                    o.beamStirrupHookTypeName = (obj.Value<string>("beamStirrupHookTypeName") ?? string.Empty).Trim();
                    o.beamStirrupHookOrientationStart = (obj.Value<string>("beamStirrupHookOrientationStart") ?? "left").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.beamStirrupHookOrientationStart)) o.beamStirrupHookOrientationStart = "left";
                    o.beamStirrupHookOrientationEnd = (obj.Value<string>("beamStirrupHookOrientationEnd") ?? "right").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.beamStirrupHookOrientationEnd)) o.beamStirrupHookOrientationEnd = "right";

                    o.beamStirrupStartOffsetMm = obj.Value<double?>("beamStirrupStartOffsetMm") ?? 0.0;
                    o.beamStirrupEndOffsetMm = obj.Value<double?>("beamStirrupEndOffsetMm") ?? 0.0;

                    o.columnTieUseHooks = obj.Value<bool?>("columnTieUseHooks") ?? false;
                    o.columnTieHookAngleDeg = obj.Value<double?>("columnTieHookAngleDeg") ?? 0.0;
                    o.columnTieHookTypeName = (obj.Value<string>("columnTieHookTypeName") ?? string.Empty).Trim();
                    o.columnTieHookOrientationStart = (obj.Value<string>("columnTieHookOrientationStart") ?? "left").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.columnTieHookOrientationStart)) o.columnTieHookOrientationStart = "left";
                    o.columnTieHookOrientationEnd = (obj.Value<string>("columnTieHookOrientationEnd") ?? "right").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(o.columnTieHookOrientationEnd)) o.columnTieHookOrientationEnd = "right";
                    o.columnTieBottomOffsetMm = obj.Value<double?>("columnTieBottomOffsetMm") ?? 0.0;
                    o.columnTieTopOffsetMm = obj.Value<double?>("columnTieTopOffsetMm") ?? 0.0;

                    o.includeMappingDebug = obj.Value<bool?>("includeMappingDebug") ?? false;
                    o.preferMappingArrayLength = obj.Value<bool?>("preferMappingArrayLength") ?? false;

                    var lo = obj["layout"] as JObject;
                    if (lo != null) o.layoutOverride = lo;
                }
                catch { /* ignore */ }
                return o;
            }
        }

        private sealed class LocalBox
        {
            public double minX, minY, minZ;
            public double maxX, maxY, maxZ;
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
                try
                {
                    RebarHookType startHook = null;
                    RebarHookType endHook = null;
                    RebarHookOrientation startOrient = RebarHookOrientation.Left;
                    RebarHookOrientation endOrient = RebarHookOrientation.Right;
                    // If hook is requested, resolve it regardless of the rebar style (Standard/StirrupTie).
                    TryResolveHookSpec(doc, aTok, out startHook, out endHook, out startOrient, out endOrient, out var hookWarn);
                    if (!string.IsNullOrWhiteSpace(hookWarn))
                        layoutWarnings.Add(hookWarn);

                    rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
                        doc, style, barType, startHook, endHook, host, normal, curves,
                        startOrient, endOrient, true, true);
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
                            var hook = TryGetAnyHookType(doc);
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
                "Host.Cover.Other"
            };

            var mappingKeysBeamAttr = new[]
            {
                "Beam.Attr.MainBar.DiameterMm",
                "Beam.Attr.MainBar.TopCount",
                "Beam.Attr.MainBar.BottomCount",
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

                double coverTopMm = values != null ? (values.Value<double?>("Host.Cover.Top") ?? 40.0) : 40.0;
                double coverBottomMm = values != null ? (values.Value<double?>("Host.Cover.Bottom") ?? 40.0) : 40.0;
                double coverOtherMm = values != null ? (values.Value<double?>("Host.Cover.Other") ?? 40.0) : 40.0;

                hostObj["coversMm"] = new JObject { ["top"] = coverTopMm, ["bottom"] = coverBottomMm, ["other"] = coverOtherMm };

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
                    if (!TryFindBarTypeByName(doc, mainBarTypeName, out mainBarType))
                    {
                        hostObj["ok"] = false;
                        hostObj["code"] = "BAR_TYPE_NOT_FOUND";
                        hostObj["msg"] = "Main bar type not found: " + mainBarTypeName;
                        hostsArr.Add(hostObj);
                        continue;
                    }
                }
                if (opts.includeTies || opts.includeStirrups)
                {
                    if (!TryFindBarTypeByName(doc, tieBarTypeName, out tieBarType))
                    {
                        hostObj["ok"] = false;
                        hostObj["code"] = "BAR_TYPE_NOT_FOUND";
                        hostObj["msg"] = "Tie/Stirrup bar type not found: " + tieBarTypeName;
                        hostsArr.Add(hostObj);
                        continue;
                    }
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
                        double left = sMin + coverOtherFt + r;
                        double right = sMax - coverOtherFt - r;
                        double bottom = uMin + coverBottomFt + r;
                        double top = uMax - coverTopFt - r;

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
                        int topCount = 2;
                        int bottomCount = 2;
                        string countSource = "default";
                        if (opts.beamMainTopCount.HasValue || opts.beamMainBottomCount.HasValue)
                        {
                            topCount = Math.Max(0, opts.beamMainTopCount ?? topCount);
                            bottomCount = Math.Max(0, opts.beamMainBottomCount ?? bottomCount);
                            countSource = "options";
                        }
                        else if (beamSpec != null && beamSpec.hasMainCounts)
                        {
                            if (beamSpec.topCount.HasValue) topCount = Math.Max(0, beamSpec.topCount.Value);
                            if (beamSpec.bottomCount.HasValue) bottomCount = Math.Max(0, beamSpec.bottomCount.Value);
                            countSource = "mapping";
                        }

                        hostObj["beamMainCounts"] = new JObject
                        {
                            ["source"] = countSource,
                            ["top"] = topCount,
                            ["bottom"] = bottomCount,
                            ["total"] = topCount + bottomCount
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

                        AddBarsAtU(top, topCount);
                        AddBarsAtU(bottom, bottomCount);
                    }
                    else
                    {
                        double aMin = GetMinByIndex(localBox, crossA);
                        double aMax = GetMaxByIndex(localBox, crossA);
                        double bMin = GetMinByIndex(localBox, crossB);
                        double bMax = GetMaxByIndex(localBox, crossB);
                        double ax1 = aMin + coverOtherFt + r;
                        double ax2 = aMax - coverOtherFt - r;
                        double bx1 = bMin + coverOtherFt + r;
                        double bx2 = bMax - coverOtherFt - r;

                        var nrm = GetBasisVectorByIndex(tr, crossA);
                        if (nrm.GetLength() < 1e-9) nrm = XYZ.BasisX;

                        var points = new[]
                        {
                            new { a = ax1, b = bx1 },
                            new { a = ax2, b = bx1 },
                            new { a = ax2, b = bx2 },
                            new { a = ax1, b = bx2 }
                        };

                        int idx = 0;
                        foreach (var pt in points)
                        {
                            var p0 = MakeLocalPoint(axisIndex, axisStart, crossA, pt.a, crossB, pt.b);
                            var p1 = MakeLocalPoint(axisIndex, axisEnd, crossA, pt.a, crossB, pt.b);
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

                    double left = sMin + coverOtherFt + r;
                    double right = sMax - coverOtherFt - r;
                    double bottom = isFraming ? (uMin + coverBottomFt + r) : (uMin + coverOtherFt + r);
                    double top = isFraming ? (uMax - coverTopFt - r) : (uMax - coverOtherFt - r);

                    if (!(left < right && bottom < top))
                    {
                        hostObj["ok"] = false;
                        hostObj["code"] = "INVALID_TIE_GEOMETRY";
                        hostObj["msg"] = "Computed tie/stirrup rectangle is invalid (covers too large?).";
                        hostsArr.Add(hostObj);
                        continue;
                    }

                    var p1 = MakeLocalPoint(axisIndex, axisStart, sIdx, left, uIdx, bottom);
                    var p2 = MakeLocalPoint(axisIndex, axisStart, sIdx, right, uIdx, bottom);
                    var p3 = MakeLocalPoint(axisIndex, axisStart, sIdx, right, uIdx, top);
                    var p4 = MakeLocalPoint(axisIndex, axisStart, sIdx, left, uIdx, top);
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
                    bool wantHooks = false;
                    try
                    {
                        if (isFraming)
                        {
                            wantHooks = opts.beamStirrupUseHooks
                                && ((opts.beamStirrupHookAngleDeg > 1.0) || !string.IsNullOrWhiteSpace(opts.beamStirrupHookTypeName));
                        }
                        else if (isColumn)
                        {
                            wantHooks = opts.columnTieUseHooks
                                && ((opts.columnTieHookAngleDeg > 1.0) || !string.IsNullOrWhiteSpace(opts.columnTieHookTypeName));
                        }
                    }
                    catch { wantHooks = false; }

                    // If hooks are requested, create an open polyline and let Revit apply hook types at both ends.
                    curves.Add(GeometryJsonHelper.CurveToJson(Line.CreateBound(c1, c2)));
                    curves.Add(GeometryJsonHelper.CurveToJson(Line.CreateBound(c2, c3)));
                    curves.Add(GeometryJsonHelper.CurveToJson(Line.CreateBound(c3, c4)));
                    if (!wantHooks)
                        curves.Add(GeometryJsonHelper.CurveToJson(Line.CreateBound(c4, c1)));

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

                    string actionStyle = "StirrupTie";
                    try
                    {
                        // Some hook types are only compatible with RebarStyle.Standard.
                        // If the user explicitly requested a "標準フック", use Standard style.
                        if (wantHooks)
                        {
                            if (isFraming && !string.IsNullOrWhiteSpace(opts.beamStirrupHookTypeName) && opts.beamStirrupHookTypeName.Contains("標準フック"))
                                actionStyle = "Standard";
                            else if (isColumn && !string.IsNullOrWhiteSpace(opts.columnTieHookTypeName) && opts.columnTieHookTypeName.Contains("標準フック"))
                                actionStyle = "Standard";
                        }
                    }
                    catch { /* ignore */ }

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
                            ["angleDeg"] = isFraming ? opts.beamStirrupHookAngleDeg : opts.columnTieHookAngleDeg,
                            ["typeName"] = isFraming
                                ? (string.IsNullOrWhiteSpace(opts.beamStirrupHookTypeName) ? null : opts.beamStirrupHookTypeName)
                                : (string.IsNullOrWhiteSpace(opts.columnTieHookTypeName) ? null : opts.columnTieHookTypeName),
                            ["startOrientation"] = isFraming ? opts.beamStirrupHookOrientationStart : opts.columnTieHookOrientationStart,
                            ["endOrientation"] = isFraming ? opts.beamStirrupHookOrientationEnd : opts.columnTieHookOrientationEnd
                        } : null
                    });
                }

                hostObj["ok"] = true;
                hostsArr.Add(hostObj);
            }

            return new JObject
            {
                ["ok"] = true,
                ["planVersion"] = PlanVersion,
                ["mappingStatus"] = mappingStatus,
                ["tagComments"] = opts.tagComments,
                ["deleteExistingTaggedInHosts"] = deleteExistingTaggedInHosts,
                ["hosts"] = hostsArr
            };
        }

        public static JObject ApplyPlan(Document doc, JObject plan, bool dryRun)
        {
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");
            if (plan == null) return ResultUtil.Err("plan is required.", "INVALID_ARGS");

            var hostsArr = plan["hosts"] as JArray;
            if (hostsArr == null) return ResultUtil.Err("plan.hosts is required.", "INVALID_ARGS");

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

        private static bool TryResolveHookSpec(
            Document doc,
            JObject actionObj,
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
            double angleDeg = hookObj.Value<double?>("angleDeg") ?? 0.0;

            RebarHookType ht = null;
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                ht = TryFindHookTypeByExactName(doc, typeName);
            }
            if (ht == null && angleDeg > 1.0)
            {
                ht = TryFindHookTypeByAngleDeg(doc, angleDeg);
            }
            if (ht == null && angleDeg > 1.0)
            {
                // last resort: name contains digits (e.g. "135", "135度")
                var token = ((int)Math.Round(angleDeg)).ToString();
                ht = TryFindHookTypeByNameContains(doc, token);
            }
            if (ht == null && angleDeg > 1.0)
            {
                ht = TryFindHookTypeByNameContains(doc, "度");
            }

            if (ht == null)
            {
                warning = "Hook requested but RebarHookType not found (angleDeg=" + angleDeg + ", typeName='" + typeName + "').";
                return false;
            }

            startHook = ht;
            endHook = ht;
            return true;
        }

        private static RebarHookType TryFindHookTypeByExactName(Document doc, string name)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>()
                    .FirstOrDefault(x => x != null && x.Name != null && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        private static RebarHookType TryFindHookTypeByNameContains(Document doc, string token)
        {
            if (doc == null || string.IsNullOrWhiteSpace(token)) return null;
            token = token.Trim();
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>()
                    .FirstOrDefault(x => x != null && x.Name != null && x.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return null; }
        }

        private static RebarHookType TryFindHookTypeByAngleDeg(Document doc, double angleDeg)
        {
            if (doc == null || !(angleDeg > 1.0)) return null;
            double target = angleDeg * Math.PI / 180.0;
            double tol = 1.0 * Math.PI / 180.0;
            try
            {
                foreach (var ht in new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>())
                {
                    if (ht == null) continue;
                    try
                    {
                        if (Math.Abs(ht.HookAngle - target) <= tol) return ht;
                    }
                    catch { /* ignore */ }
                }
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
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>()
                    .FirstOrDefault();
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
