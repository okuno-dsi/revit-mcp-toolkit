// ================================================================
// Command: rebar_layout_inspect
// Purpose: Inspect layout of selected rebar(s): rule, spacing, arrayLength, etc.
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    public sealed class RebarLayoutInspectCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_layout_inspect";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params ?? new JObject();

            var ids = new List<int>();
            try
            {
                foreach (var key in new[] { "rebarElementIds", "elementIds" })
                {
                    var arr = p[key] as JArray;
                    if (arr == null) continue;
                    foreach (var t in arr)
                    {
                        if (t == null) continue;
                        if (t.Type == JTokenType.Integer)
                        {
                            int v = t.Value<int>();
                            if (v > 0) ids.Add(v);
                        }
                        else if (t.Type == JTokenType.Float)
                        {
                            int v = (int)t.Value<double>();
                            if (v > 0) ids.Add(v);
                        }
                        else if (t.Type == JTokenType.String && int.TryParse(t.Value<string>(), out var v2))
                        {
                            if (v2 > 0) ids.Add(v2);
                        }
                    }
                }

                int one = p.Value<int?>("rebarElementId") ?? p.Value<int?>("rebarId") ?? p.Value<int?>("elementId") ?? 0;
                if (one > 0) ids.Add(one);
            }
            catch { /* ignore */ }

            bool useSelectionIfEmpty = p.Value<bool?>("useSelectionIfEmpty") ?? true;
            if (ids.Count == 0 && useSelectionIfEmpty && uidoc != null)
            {
                try
                {
                    foreach (var id in uidoc.Selection.GetElementIds())
                    {
                        try
                        {
                            int v = id.IntValue();
                            if (v > 0) ids.Add(v);
                        }
                        catch { /* ignore */ }
                    }
                }
                catch { /* ignore */ }
            }

            if (ids.Count == 0)
            {
                return ResultUtil.Err("rebarElementIds/elementIds が空で、選択も空です。", "INVALID_ARGS");
            }

            var outItems = new List<object>();

            foreach (var intId in ids.Distinct())
            {
                var item = new JObject
                {
                    ["rebarElementId"] = intId
                };

                Element elem = null;
                try { elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(intId)); } catch { elem = null; }
                var rebar = elem as Autodesk.Revit.DB.Structure.Rebar;

                if (rebar == null)
                {
                    item["isShapeDriven"] = false;
                    item["layoutRule"] = elem == null ? "NOT_FOUND" : "NOT_REBAR";
                    outItems.Add(item);
                    continue;
                }

                // Basic flags/properties
                try { item["layoutRule"] = rebar.LayoutRule.ToString(); } catch { item["layoutRule"] = ""; }
                try { item["includeFirstBar"] = rebar.IncludeFirstBar; } catch { }
                try { item["includeLastBar"] = rebar.IncludeLastBar; } catch { }
                try { item["numberOfBarPositions"] = rebar.NumberOfBarPositions; } catch { }

                try
                {
                    var ms = rebar.MaxSpacing;
                    item["maxSpacingMm"] = Math.Round(UnitHelper.FtToMm(ms), 6);
                }
                catch { /* ignore */ }

                // Shape-driven accessor details
                if (RebarArrangementService.TryGetShapeDrivenAccessor(rebar, out var acc))
                {
                    item["isShapeDriven"] = true;
                    try { item["barsOnNormalSide"] = acc.BarsOnNormalSide; } catch { }
                    try { item["arrayLengthMm"] = Math.Round(UnitHelper.FtToMm(acc.ArrayLength), 6); } catch { }

                    // Try to read spacing from accessor by reflection (API differs by rule/version)
                    try
                    {
                        double? spFt = null;
                        var pi = acc.GetType().GetProperty("BarSpacing");
                        if (pi != null && pi.PropertyType == typeof(double))
                        {
                            spFt = (double)pi.GetValue(acc, null);
                        }
                        else
                        {
                            var pi2 = acc.GetType().GetProperty("Spacing");
                            if (pi2 != null && pi2.PropertyType == typeof(double))
                                spFt = (double)pi2.GetValue(acc, null);
                        }

                        if (spFt.HasValue)
                            item["spacingMm"] = Math.Round(UnitHelper.FtToMm(spFt.Value), 6);
                    }
                    catch { /* ignore */ }
                }
                else
                {
                    item["isShapeDriven"] = false;
                }

                outItems.Add(item);
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                rebars = outItems.ToArray(),
                msg = "OK"
            });
        }
    }
}
