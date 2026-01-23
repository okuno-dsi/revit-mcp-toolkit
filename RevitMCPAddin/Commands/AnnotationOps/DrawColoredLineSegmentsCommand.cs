// ================================================================
// File: RevitMCPAddin/Commands/AnnotationOps/DrawColoredLineSegmentsCommand.cs
// Target: .NET Framework 4.8 / C# 8.0 / Revit 2023+
// Purpose:
//   Coordinate dataset (mm) -> draw colored detail line segments in a view.
// Notes:
//   - Supports both `segments:[{start,end,...}]` and `loops:[{segments:[...]}]` (same shape as room perimeter output).
//   - Applies per-element overrides (projection line color/weight) so it does NOT modify line styles.
//   - View template lock is detected; by default no changes are made in a templated view.
// ================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    [RpcCommand("draw_colored_line_segments",
        Aliases = new[] { "view.draw_colored_line_segments" },
        Category = "AnnotationOps",
        Tags = new[] { "view", "detail_line", "draw" },
        Risk = RiskLevel.Low,
        Kind = "write",
        Summary = "Draw colored detail line segments in a view from a coordinate dataset (mm).",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"view.draw_colored_line_segments\", \"params\":{ \"viewId\":123, \"segments\":[{\"start\":{\"x\":0,\"y\":0,\"z\":0},\"end\":{\"x\":1000,\"y\":0,\"z\":0}}], \"lineRgb\":{\"r\":255,\"g\":0,\"b\":0}, \"lineWeight\":6 } }",
        Requires = new[] { "segments|loops" })]
    public sealed class DrawColoredLineSegmentsCommand : IRevitCommandHandler
    {
        public string CommandName => "draw_colored_line_segments";

        private sealed class SegmentSpec
        {
            public XYZ startFt;
            public XYZ endFt;
            public Color color;
            public int? lineWeight;
        }

        private static int ClampInt(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static byte ClampByte(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)v;
        }

        private static bool TryReadRgb(JToken tok, out Color color)
        {
            color = null;
            try
            {
                if (!(tok is JObject o)) return false;
                int r = o.Value<int?>("r") ?? 0;
                int g = o.Value<int?>("g") ?? 0;
                int b = o.Value<int?>("b") ?? 0;
                color = new Color(ClampByte(r), ClampByte(g), ClampByte(b));
                return true;
            }
            catch { return false; }
        }

        private static Color ReadColor(JObject obj, Color fallback, out bool specified)
        {
            specified = false;
            if (obj == null) return fallback;
            try
            {
                // Preferred object forms
                if (TryReadRgb(obj["lineRgb"], out var c1)) { specified = true; return c1; }
                if (TryReadRgb(obj["rgb"], out var c2)) { specified = true; return c2; }
                if (TryReadRgb(obj["color"], out var c3)) { specified = true; return c3; }

                // Legacy r/g/b ints
                var rTok = obj.Value<int?>("r");
                var gTok = obj.Value<int?>("g");
                var bTok = obj.Value<int?>("b");
                if (rTok.HasValue || gTok.HasValue || bTok.HasValue)
                {
                    int r = rTok ?? fallback.Red;
                    int g = gTok ?? fallback.Green;
                    int b = bTok ?? fallback.Blue;
                    specified = true;
                    return new Color(ClampByte(r), ClampByte(g), ClampByte(b));
                }
            }
            catch { /* ignore */ }
            return fallback;
        }

        private static View ResolveTargetView(UIApplication uiapp, JObject p, out string reason)
        {
            reason = "";
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) { reason = "アクティブドキュメントがありません。"; return null; }

            View view = null;
            int reqViewId = p.Value<int?>("viewId") ?? 0;
            if (reqViewId > 0)
            {
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(reqViewId)) as View;
                if (view == null) { reason = "View not found: viewId=" + reqViewId; return null; }
            }
            else
            {
                view = uiapp.ActiveUIDocument?.ActiveGraphicalView
                    ?? (uiapp.ActiveUIDocument?.ActiveView is View av && av.ViewType != ViewType.ProjectBrowser ? av : null);
                if (view == null) { reason = "アクティブビューがありません。viewId を指定してください。"; return null; }
            }

            if (view.IsTemplate) { reason = "ビュー テンプレートには描画できません。"; return null; }

            // Detail lines are view-specific; in 3D views they are not supported.
            if (view.ViewType == ViewType.ThreeD)
            {
                reason = "3Dビューでは詳細線分を作成できません。平面/断面/立面/製図ビューで実行してください。";
                return null;
            }

            return view;
        }

        private static void AddSegmentsFromArray(JArray arr, List<SegmentSpec> outList, Color defaultColor, int? defaultWeight, List<object> parseErrors)
        {
            if (arr == null) return;
            int idx = 0;
            foreach (var t in arr)
            {
                try
                {
                    if (!(t is JObject segObj))
                    {
                        parseErrors.Add(new { index = idx, reason = "Segment must be an object {start,end}." });
                        idx++;
                        continue;
                    }

                    var startTok = segObj["start"] as JObject;
                    var endTok = segObj["end"] as JObject;
                    if (startTok == null || endTok == null)
                    {
                        parseErrors.Add(new { index = idx, reason = "Segment must have start/end {x,y,z}." });
                        idx++;
                        continue;
                    }

                    var s = DLHelpers.ReadPointMm(startTok);
                    var e = DLHelpers.ReadPointMm(endTok);

                    // Color: segment-level override if provided
                    bool segColorSpecified;
                    var segColor = ReadColor(segObj, defaultColor, out segColorSpecified);

                    // Line weight (optional; segment-level override)
                    int? segWeight = segObj.Value<int?>("lineWeight") ?? segObj.Value<int?>("line_weight") ?? defaultWeight;
                    if (segWeight.HasValue) segWeight = ClampInt(segWeight.Value, 1, 16);

                    outList.Add(new SegmentSpec
                    {
                        startFt = s,
                        endFt = e,
                        color = segColor,
                        lineWeight = segWeight
                    });
                }
                catch (Exception ex)
                {
                    parseErrors.Add(new { index = idx, reason = "Failed to parse segment: " + ex.Message });
                }
                idx++;
            }
        }

        private static void AddSegmentsFromLoops(JArray loops, List<SegmentSpec> outList, Color defaultColor, int? defaultWeight, List<object> parseErrors)
        {
            if (loops == null) return;
            int loopIndex = 0;
            foreach (var lt in loops)
            {
                try
                {
                    if (!(lt is JObject loopObj))
                    {
                        parseErrors.Add(new { loopIndex, reason = "Loop must be an object {segments:[...] }." });
                        loopIndex++;
                        continue;
                    }

                    var segs = loopObj["segments"] as JArray;
                    if (segs == null)
                    {
                        parseErrors.Add(new { loopIndex, reason = "Loop must have segments:[...]." });
                        loopIndex++;
                        continue;
                    }

                    AddSegmentsFromArray(segs, outList, defaultColor, defaultWeight, parseErrors);
                }
                catch (Exception ex)
                {
                    parseErrors.Add(new { loopIndex, reason = "Failed to parse loop: " + ex.Message });
                }
                loopIndex++;
            }
        }

        private static string MakeOgsKey(Color c, int? w)
        {
            int ww = w.HasValue ? w.Value : 0;
            return ((int)c.Red).ToString() + "-" + ((int)c.Green).ToString() + "-" + ((int)c.Blue).ToString() + "-" + ww.ToString();
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = cmd.Params as JObject ?? new JObject();

            var view = ResolveTargetView(uiapp, p, out var whyView);
            if (view == null) return ResultUtil.Err(whyView);

            // Options
            bool applyOverrides = p.Value<bool?>("applyOverrides") ?? true;
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            bool refreshView = p.Value<bool?>("refreshView") ?? true;
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            int batchSize = ClampInt(p.Value<int?>("batchSize") ?? 500, 1, 5000);
            int maxMillisPerTx = ClampInt(p.Value<int?>("maxMillisPerTx") ?? 3000, 200, 20000);
            bool returnIds = p.Value<bool?>("returnIds") ?? true;

            // View template handling
            bool templateApplied = view.ViewTemplateId != ElementId.InvalidElementId;
            int? templateViewId = templateApplied ? (int?)view.ViewTemplateId.IntValue() : null;
            if (templateApplied && applyOverrides && !detachTemplate)
            {
                return new
                {
                    ok = false,
                    errorCode = "VIEW_TEMPLATE_LOCK",
                    msg = "View has a template; detach view template before drawing colored segments (or set detachViewTemplate=true).",
                    viewId = view.Id.IntValue(),
                    templateApplied = true,
                    templateViewId = templateViewId
                };
            }
            if (templateApplied && detachTemplate)
            {
                using (var tx = new Transaction(doc, "Detach View Template (draw_colored_line_segments)"))
                {
                    try
                    {
                        tx.Start();
                        view.ViewTemplateId = ElementId.InvalidElementId;
                        tx.Commit();
                        templateApplied = false;
                        templateViewId = null;
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return new
                        {
                            ok = false,
                            errorCode = "TEMPLATE_DETACH_FAILED",
                            msg = "Failed to detach view template: " + ex.Message,
                            viewId = view.Id.IntValue(),
                            templateApplied = true,
                            templateViewId = templateViewId
                        };
                    }
                }
            }

            // Resolve optional line style (does not control color; override controls color)
            GraphicsStyle gs = DLHelpers.ResolveLineStyle(doc, p);

            // Defaults
            var defaultColor = ReadColor(p, new Color(255, 0, 0), out _);
            int? defaultWeight = p.Value<int?>("lineWeight") ?? p.Value<int?>("line_weight");
            if (defaultWeight.HasValue) defaultWeight = ClampInt(defaultWeight.Value, 1, 16);

            // Accept `segments`, `loops`, or `dataset:{segments/loops}`
            var parseErrors = new List<object>();
            var segList = new List<SegmentSpec>();

            var dataset = p["dataset"] as JObject;
            var segArr = (p["segments"] as JArray) ?? (dataset != null ? dataset["segments"] as JArray : null);
            var loopsArr = (p["loops"] as JArray) ?? (dataset != null ? dataset["loops"] as JArray : null);

            AddSegmentsFromArray(segArr, segList, defaultColor, defaultWeight, parseErrors);
            AddSegmentsFromLoops(loopsArr, segList, defaultColor, defaultWeight, parseErrors);

            if (segList.Count == 0)
            {
                var msg = "segments もしくは loops が必要です（mm座標の start/end）。";
                if (parseErrors.Count > 0) msg += " parseErrors も参照してください。";
                return new
                {
                    ok = false,
                    errorCode = "INVALID_PARAMS",
                    msg = msg,
                    parseErrors = parseErrors
                };
            }

            // Batched creation
            var createdIds = new List<int>();
            var errors = new List<object>();
            var swAll = Stopwatch.StartNew();
            int nextIndex = startIndex;

            // Cache OGS per (color, weight)
            var ogsCache = new Dictionary<string, OverrideGraphicSettings>(StringComparer.OrdinalIgnoreCase);
            OverrideGraphicSettings GetOrBuildOgs(Color c, int? w)
            {
                var key = MakeOgsKey(c, w);
                if (ogsCache.TryGetValue(key, out var existing)) return existing;
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(c);
                ogs.SetCutLineColor(c);
                if (w.HasValue && w.Value > 0)
                {
                    ogs.SetProjectionLineWeight(w.Value);
                    ogs.SetCutLineWeight(w.Value);
                }
                ogsCache[key] = ogs;
                return ogs;
            }

            using (var tg = new TransactionGroup(doc, "Draw Colored Line Segments"))
            {
                tg.Start();

                while (nextIndex < segList.Count)
                {
                    var swTx = Stopwatch.StartNew();
                    using (var tx = new Transaction(doc, "Draw Colored Line Segments (batched)"))
                    {
                        try
                        {
                            tx.Start();
                            int end = Math.Min(segList.Count, nextIndex + batchSize);
                            for (int i = nextIndex; i < end; i++)
                            {
                                var seg = segList[i];
                                try
                                {
                                    if (seg.startFt.IsAlmostEqualTo(seg.endFt))
                                    {
                                        errors.Add(new { index = i, reason = "Zero-length segment was skipped." });
                                        continue;
                                    }

                                    var line = Line.CreateBound(seg.startFt, seg.endFt);
                                    var ce = doc.Create.NewDetailCurve(view, line);
                                    if (gs != null) ce.LineStyle = gs;
                                    if (applyOverrides)
                                    {
                                        var ogs = GetOrBuildOgs(seg.color, seg.lineWeight);
                                        view.SetElementOverrides(ce.Id, ogs);
                                    }
                                    createdIds.Add(ce.Id.IntValue());
                                }
                                catch (Exception ex)
                                {
                                    errors.Add(new { index = i, reason = ex.Message });
                                }
                            }
                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            errors.Add(new { reason = "transaction failed: " + ex.Message });
                            break;
                        }
                    }

                    if (refreshView)
                    {
                        try { doc.Regenerate(); } catch { }
                        try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { }
                    }

                    nextIndex += batchSize;
                    if (swTx.ElapsedMilliseconds > maxMillisPerTx) break;
                }

                tg.Assimilate();
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                totalSegments = segList.Count,
                createdCount = createdIds.Count,
                createdElementIds = returnIds ? createdIds : null,
                parseErrors = parseErrors.Count > 0 ? parseErrors : null,
                errors = errors.Count > 0 ? errors : null,
                applyOverrides,
                defaultLineRgb = new { r = defaultColor.Red, g = defaultColor.Green, b = defaultColor.Blue },
                defaultLineWeight = defaultWeight,
                templateApplied,
                templateViewId,
                completed = nextIndex >= segList.Count,
                nextIndex,
                batchSize,
                elapsedMs = swAll.ElapsedMilliseconds
            };
        }
    }
}
