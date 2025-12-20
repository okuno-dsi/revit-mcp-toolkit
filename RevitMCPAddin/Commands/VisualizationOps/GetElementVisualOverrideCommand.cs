// ================================================================
// File   : Commands/VisualizationOps/VisualOverrides.Commands.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Depends: Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq
//          RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand, ResultUtil, RevitLogger)
// Purpose: Visual Override controls per command-class (single file)
//   - clear_visual_override
//   - reset_all_view_overrides
//   - get_element_visual_override
//
// JSON-RPC Examples:
// 1) clear_visual_override
// {"jsonrpc":"2.0","id":1,"method":"clear_visual_override",
//  "params":{"viewId":60566862,"elementIds":[60548284,60548341,60548061]}}
//
// 2) reset_all_view_overrides
// {"jsonrpc":"2.0","id":2,"method":"reset_all_view_overrides",
//  "params":{"viewId":60566862}}
//
// 3) get_element_visual_override
// {"jsonrpc":"2.0","id":3,"method":"get_element_visual_override",
//  "params":{"viewId":60566862,"elementIds":[60548284,60548341],
//            "include":{"color":true,"transparency":true,
//                       "lineColor":true,"patternIds":true,"visibilityFlags":true}}}
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.VisualizationOps
{
    // ------------------------------------------------------------
    // Helpers (shared by the 3 commands)
    // ------------------------------------------------------------
    internal static class VisualOverrideHelpers
    {
        public static IList<ElementId> ParseElementIds(JToken? token)
        {
            var list = new List<ElementId>();
            if (token is JArray arr)
            {
                foreach (var t in arr)
                {
                    var v = t?.Value<int?>();
                    if (v is int i && i != 0) list.Add(new ElementId(i));
                }
            }
            return list;
        }

        public static View? ResolveView(Document doc, int? viewId)
        {
            if (viewId.HasValue && viewId.Value > 0)
                return doc.GetElement(new ElementId(viewId.Value)) as View;
            return doc.ActiveView;
        }

        public static bool IsDefaultOverrides(OverrideGraphicSettings ogs)
        {
            if (ogs == null) return true;
            // 透過（0..100; 0は未設定扱いに近い運用）
            try { if (ogs.Transparency != 0) return false; } catch { /* ignore */ }

            // 代表的なパターンIDが1つでも設定されていれば上書きありとみなす
            if (SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceForegroundPatternId)) != 0) return false;
            if (SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceBackgroundPatternId)) != 0) return false;
            if (SafeGetId(ogs, nameof(OverrideGraphicSettings.CutForegroundPatternId)) != 0) return false;
            if (SafeGetId(ogs, nameof(OverrideGraphicSettings.CutBackgroundPatternId)) != 0) return false;

            // 色Getterはバージョンにより取得不可のため判定対象から外す
            // Halftone 等、必要に応じて拡張可
            try { if (ogs.Halftone) return false; } catch { /* ignore */ }

            return true;
        }

        public static int SafeGetTransparency(OverrideGraphicSettings ogs)
        {
            try { return ogs.Transparency; } catch { return 0; }
        }

        public static int SafeGetId(OverrideGraphicSettings ogs, string propName)
        {
            try
            {
                var pi = typeof(OverrideGraphicSettings).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                var id = pi?.GetValue(ogs) as ElementId;
                return id?.IntegerValue ?? 0;
            }
            catch { return 0; }
        }

        public static bool? SafeGetBool(OverrideGraphicSettings ogs, string propName)
        {
            try
            {
                var pi = typeof(OverrideGraphicSettings).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                var v = pi?.GetValue(ogs);
                if (v is bool b) return b;
                return null;
            }
            catch { return null; }
        }

        public static Color? SafeGetColor(OverrideGraphicSettings ogs, string propName)
        {
            try
            {
                var pi = typeof(OverrideGraphicSettings).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                return pi?.GetValue(ogs) as Color;
            }
            catch { return null; }
        }
    }

    // ------------------------------------------------------------
    //  get_element_visual_override : 指定要素(複数可)の上書き状態を詳細取得
    // ------------------------------------------------------------
    public sealed class GetElementVisualOverrideCommand : IRevitCommandHandler
    {
        public string CommandName => "get_element_visual_override";

        private sealed class Input
        {
            public int? viewId { get; set; }
            public int? elementId { get; set; }
            public List<int>? elementIds { get; set; }
            public IncludeFlags? include { get; set; }
        }

        private sealed class IncludeFlags
        {
            public bool color { get; set; } = true;
            public bool transparency { get; set; } = true;
            public bool lineColor { get; set; } = false;
            public bool patternIds { get; set; } = false;
            public bool visibilityFlags { get; set; } = false;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var uiDoc = uiapp.ActiveUIDocument;
                var doc = uiDoc?.Document;
                if (doc == null) return ResultUtil.Err("No active document.");

                var inp = SafeParse(cmd.Params);
                var view = VisualOverrideHelpers.ResolveView(doc, inp.viewId) ?? uiDoc.ActiveView;
                if (view == null) return ResultUtil.Err($"View not found: viewId={inp.viewId}");

                var targets = new List<ElementId>();
                if (inp.elementId.HasValue && inp.elementId.Value > 0)
                    targets.Add(new ElementId(inp.elementId.Value));
                if (inp.elementIds != null && inp.elementIds.Count > 0)
                    targets.AddRange(inp.elementIds.Select(i => new ElementId(i)));
                if (targets.Count == 0) return ResultUtil.Err("elementId or elementIds required.");

                var flags = inp.include ?? new IncludeFlags();

                // AreGraphicsOverridesApplied(ElementId) が使える場合は高速判定に使用
                var areOverridesAppliedMI = typeof(View).GetMethod("AreGraphicsOverridesApplied", new[] { typeof(ElementId) });

                var items = new List<JObject>();
                foreach (var eid in targets)
                {
                    try
                    {
                        if (areOverridesAppliedMI != null)
                        {
                            var appliedObj = areOverridesAppliedMI.Invoke(view, new object[] { eid });
                            if (appliedObj is bool applied && !applied)
                            {
                                items.Add(new JObject { ["elementId"] = eid.IntegerValue, ["hasAny"] = false });
                                continue;
                            }
                        }

                        var ogs = view.GetElementOverrides(eid);
                        if (VisualOverrideHelpers.IsDefaultOverrides(ogs))
                        {
                            items.Add(new JObject { ["elementId"] = eid.IntegerValue, ["hasAny"] = false });
                            continue;
                        }

                        var item = new JObject
                        {
                            ["elementId"] = eid.IntegerValue,
                            ["hasAny"] = true
                        };

                        if (flags.transparency)
                        {
                            int t = VisualOverrideHelpers.SafeGetTransparency(ogs);
                            if (t != 0) item["transparency"] = t; // 0..100
                        }

                        if (flags.patternIds)
                        {
                            item["patterns"] = new JObject
                            {
                                ["surfaceFg"] = VisualOverrideHelpers.SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceForegroundPatternId)),
                                ["surfaceBg"] = VisualOverrideHelpers.SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceBackgroundPatternId)),
                                ["cutFg"] = VisualOverrideHelpers.SafeGetId(ogs, nameof(OverrideGraphicSettings.CutForegroundPatternId)),
                                ["cutBg"] = VisualOverrideHelpers.SafeGetId(ogs, nameof(OverrideGraphicSettings.CutBackgroundPatternId))
                            };
                        }

                        if (flags.visibilityFlags)
                        {
                            item["flags"] = new JObject
                            {
                                ["surfaceFgVisible"] = VisualOverrideHelpers.SafeGetBool(ogs, nameof(OverrideGraphicSettings.IsSurfaceForegroundPatternVisible)),
                                ["surfaceBgVisible"] = VisualOverrideHelpers.SafeGetBool(ogs, nameof(OverrideGraphicSettings.IsSurfaceBackgroundPatternVisible)),
                                ["cutFgVisible"] = VisualOverrideHelpers.SafeGetBool(ogs, nameof(OverrideGraphicSettings.IsCutForegroundPatternVisible)),
                                ["cutBgVisible"] = VisualOverrideHelpers.SafeGetBool(ogs, nameof(OverrideGraphicSettings.IsCutBackgroundPatternVisible)),
                                ["halftone"] = VisualOverrideHelpers.SafeGetBool(ogs, nameof(OverrideGraphicSettings.Halftone))
                            };
                        }

                        if (flags.color)
                        {
                            var surf = VisualOverrideHelpers.SafeGetColor(ogs, "SurfaceForegroundPatternColor");
                            var cut = VisualOverrideHelpers.SafeGetColor(ogs, "CutForegroundPatternColor");
                            if (surf != null || cut != null)
                            {
                                var colors = new JObject();
                                if (surf != null) colors["surface"] = new JObject { ["r"] = surf.Red, ["g"] = surf.Green, ["b"] = surf.Blue };
                                if (cut != null) colors["cut"] = new JObject { ["r"] = cut.Red, ["g"] = cut.Green, ["b"] = cut.Blue };
                                item["colors"] = colors;
                            }
                        }

                        if (flags.lineColor)
                        {
                            var projL = VisualOverrideHelpers.SafeGetColor(ogs, "ProjectionLineColor");
                            var cutL = VisualOverrideHelpers.SafeGetColor(ogs, "CutLineColor");
                            if (projL != null || cutL != null)
                            {
                                var lines = new JObject();
                                if (projL != null) lines["projection"] = new JObject { ["r"] = projL.Red, ["g"] = projL.Green, ["b"] = projL.Blue };
                                if (cutL != null) lines["cut"] = new JObject { ["r"] = cutL.Red, ["g"] = cutL.Green, ["b"] = cutL.Blue };
                                item["lineColors"] = lines;
                            }
                        }

                        items.Add(item);
                    }
                    catch (Exception ex)
                    {
                        RevitLogger.Warn($"get_element_visual_override: eid={eid.IntegerValue} {ex.Message}");
                        items.Add(new JObject
                        {
                            ["elementId"] = eid.IntegerValue,
                            ["hasAny"] = false,
                            ["error"] = ex.Message
                        });
                    }
                }

                return ResultUtil.Ok(new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    count = items.Count,
                    items,
                    elapsedMs = sw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"Exception: {ex.Message}", ex.ToString());
            }
            finally { sw.Stop(); }
        }

        private static Input SafeParse(JToken? prm)
        {
            try { return prm?.ToObject<Input>() ?? new Input(); }
            catch { return new Input(); }
        }
    }
}
