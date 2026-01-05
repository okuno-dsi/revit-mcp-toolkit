// ================================================================
// Command: rebar_layout_update
// Purpose: Update layout rule for given rebar ids (shape-driven only).
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
    public sealed class RebarLayoutUpdateCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_layout_update|rebar_arrangement_update";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params ?? new JObject();

            // ids
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

            bool useSelectionIfEmpty = p.Value<bool?>("useSelectionIfEmpty") ?? false;
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
                return ResultUtil.Err("rebarElementIds/elementIds が空です。", "INVALID_ARGS");
            }

            // layout spec
            var layoutObj = p["layout"] as JObject;
            if (!RebarArrangementService.TryParseLayoutSpec(layoutObj, out var spec, out var parseCode, out var parseMsg))
            {
                return ResultUtil.Err(parseMsg, string.IsNullOrWhiteSpace(parseCode) ? "INVALID_ARGS" : parseCode);
            }

            var updated = new HashSet<int>();
            var skipped = new List<object>();

            using (var tx = new Transaction(doc, "RevitMcp - Update Rebar Layout"))
            {
                tx.Start();

                foreach (var intId in ids.Distinct())
                {
                    var rid = Autodesk.Revit.DB.ElementIdCompat.From(intId);
                    var rebar = doc.GetElement(rid) as Autodesk.Revit.DB.Structure.Rebar;
                    if (rebar == null)
                    {
                        skipped.Add(new { elementId = intId, reason = "NOT_REBAR" });
                        continue;
                    }

                    if (RebarArrangementService.TryUpdateLayout(rebar, spec, out var code, out var msg))
                    {
                        updated.Add(intId);
                    }
                    else
                    {
                        skipped.Add(new { elementId = intId, reason = string.IsNullOrWhiteSpace(code) ? "FAILED" : code, msg });
                    }
                }

                tx.Commit();
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                updatedRebarIds = updated.OrderBy(x => x).ToArray(),
                skipped = skipped.ToArray(),
                msg = "Layout updated."
            });
        }
    }
}
