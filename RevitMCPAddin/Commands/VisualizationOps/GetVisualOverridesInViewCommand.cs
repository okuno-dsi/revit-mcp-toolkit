// ================================================================
// File: Commands/VisualizationOps/GetVisualOverridesInViewCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: 指定ビューで「要素にグラフィックオーバーライドが適用されている」ものを列挙
// JSONRPC : method = "get_visual_overrides_in_view"
// Params  : { viewId?: int, elementIds?: int[], include?: { color?:bool, transparency?:bool, lineColor?:bool, patternIds?:bool }, maxCount?: int }
// Result  : { ok, viewId, count, overridden:[ { elementId, color?{r,g,b}, transparency?, lineColor?{r,g,b}, patternIds?{surfaceFg, surfaceBg, cutFg, cutBg}, hasAny }] }
// Notes   : viewId 省略時はアクティブビュー。elementIds 指定時はその集合のみをチェック。
//           AreGraphicsOverridesApplied は環境により見えないことがあるため、反射での呼び出しを試行。
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

namespace RevitMCPAddin.Commands.VisualizationOps
{
    public class GetVisualOverridesInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_visual_overrides_in_view";

        private sealed class Input
        {
            public int? viewId { get; set; }
            public List<int> elementIds { get; set; } = new List<int>();
            public IncludeFlags include { get; set; } = new IncludeFlags();
            public int? maxCount { get; set; }
        }
        private sealed class IncludeFlags
        {
            public bool color { get; set; } = true;
            public bool transparency { get; set; } = true;
            public bool lineColor { get; set; } = false;
            public bool patternIds { get; set; } = false;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uiDoc = uiapp.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var inp = SafeParse(cmd.Params);
            View view = ResolveView(doc, inp.viewId);
            if (view == null)
                return new { ok = false, msg = $"View が見つかりません: viewId={inp.viewId}" };

            // 反射: View.AreGraphicsOverridesApplied(ElementId) があれば使う
            var areOverridesAppliedMI = typeof(View).GetMethod("AreGraphicsOverridesApplied", new[] { typeof(ElementId) });

            // 候補
            IEnumerable<ElementId> candidates =
                (inp.elementIds != null && inp.elementIds.Count > 0)
                ? inp.elementIds.Select(x => Autodesk.Revit.DB.ElementIdCompat.From(x))
                : new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElementIds();

            var overridden = new List<JObject>();
            int limit = Math.Max(1, inp.maxCount ?? int.MaxValue);

            foreach (var eid in candidates)
            {
                try
                {
                    // 1) 使えるなら高速判定
                    if (areOverridesAppliedMI != null)
                    {
                        var appliedObj = areOverridesAppliedMI.Invoke(view, new object[] { eid });
                        if (appliedObj is bool applied && !applied)
                            continue;
                    }

                    // 2) 詳細取得 → デフォルト比較
                    var ogs = view.GetElementOverrides(eid); // OverrideGraphicSettings
                    if (IsDefaultOverrides(ogs))
                        continue;

                    var item = new JObject
                    {
                        ["elementId"] = eid.IntValue(),
                        ["hasAny"] = true
                    };

                    if (inp.include.transparency)
                    {
                        int t = SafeGetTransparency(ogs);
                        if (t > 0) item["transparency"] = t; // 0〜100
                    }

                    if (inp.include.patternIds)
                    {
                        item["patternIds"] = new JObject
                        {
                            ["surfaceFg"] = SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceForegroundPatternId)),
                            ["surfaceBg"] = SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceBackgroundPatternId)),
                            ["cutFg"] = SafeGetId(ogs, nameof(OverrideGraphicSettings.CutForegroundPatternId)),
                            ["cutBg"] = SafeGetId(ogs, nameof(OverrideGraphicSettings.CutBackgroundPatternId))
                        };
                    }

                    // Revit 2023 は色getterが無い環境があるのでベストエフォート取得
                    if (inp.include.color)
                    {
                        var c = SafeGetColor(ogs, "SurfaceForegroundPatternColor");
                        if (c != null)
                            item["color"] = new JObject { ["r"] = c.Red, ["g"] = c.Green, ["b"] = c.Blue };
                    }
                    if (inp.include.lineColor)
                    {
                        var lc = SafeGetColor(ogs, "ProjectionLineColor");
                        if (lc != null)
                            item["lineColor"] = new JObject { ["r"] = lc.Red, ["g"] = lc.Green, ["b"] = lc.Blue };
                    }

                    overridden.Add(item);
                    if (overridden.Count >= limit) break;
                }
                catch (Exception ex)
                {
                    RevitLogger.Warn($"get_visual_overrides_in_view: elementId={eid.IntValue()} {ex.Message}");
                }
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                count = overridden.Count,
                overridden
            };
        }

        private static Input SafeParse(JToken? prm)
        {
            try { return prm?.ToObject<Input>() ?? new Input(); }
            catch { return new Input(); }
        }

        private static View ResolveView(Document doc, int? viewId)
        {
            if (viewId.HasValue && viewId.Value > 0)
            {
                var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
                if (v != null) return v;
            }
            return doc.ActiveView;
        }

        /// <summary>OverrideGraphicSettings がデフォルトかどうか（最小限の項目で比較）</summary>
        private static bool IsDefaultOverrides(OverrideGraphicSettings ogs)
        {
            if (ogs == null) return true;

            // 透明度・各パターンIDいずれかが設定されていれば「デフォルトではない」
            if (SafeGetTransparency(ogs) != 0) return false;

            if (SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceForegroundPatternId)) != 0) return false;
            if (SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceBackgroundPatternId)) != 0) return false;
            if (SafeGetId(ogs, nameof(OverrideGraphicSettings.CutForegroundPatternId)) != 0) return false;
            if (SafeGetId(ogs, nameof(OverrideGraphicSettings.CutBackgroundPatternId)) != 0) return false;

            // 色は 2023 だと取得不可な場合があるので “判定には使わない”
            return true;
        }

        private static int SafeGetTransparency(OverrideGraphicSettings ogs)
        {
            try
            {
                // 2023 では Transparency プロパティで OK
                return ogs.Transparency;
            }
            catch { return 0; }
        }

        private static int SafeGetId(OverrideGraphicSettings ogs, string propName)
        {
            try
            {
                var pi = typeof(OverrideGraphicSettings).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null) return 0;
                var id = pi.GetValue(ogs) as ElementId;
                return id?.IntValue() ?? 0;
            }
            catch { return 0; }
        }

        private static Color? SafeGetColor(OverrideGraphicSettings ogs, string propName)
        {
            try
            {
                var pi = typeof(OverrideGraphicSettings).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (pi == null) return null;
                return pi.GetValue(ogs) as Color;
            }
            catch { return null; }
        }
    }
}


