// ================================================================
// Command: rebar_layout_update_by_host
// Purpose: Find rebars hosted by selected host(s) and update their layout.
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
    public sealed class RebarLayoutUpdateByHostCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_layout_update_by_host";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params ?? new JObject();

            // hosts
            var hostIds = new List<int>();
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
            if (hostIds.Count == 0 && useSelectionIfEmpty && uidoc != null)
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

            if (hostIds.Count == 0)
                return ResultUtil.Err("hostElementIds が空で、選択も空です。", "INVALID_ARGS");

            // layout spec
            var layoutObj = p["layout"] as JObject;
            if (!RebarArrangementService.TryParseLayoutSpec(layoutObj, out var spec, out var parseCode, out var parseMsg))
                return ResultUtil.Err(parseMsg, string.IsNullOrWhiteSpace(parseCode) ? "INVALID_ARGS" : parseCode);

            // filter: commentsTagEquals
            string commentsTagEquals = string.Empty;
            try
            {
                var f = p["filter"] as JObject;
                if (f != null) commentsTagEquals = (f.Value<string>("commentsTagEquals") ?? string.Empty).Trim();
            }
            catch { /* ignore */ }

            var updated = new HashSet<int>();
            var skipped = new List<object>();

            using (var tx = new Transaction(doc, "RevitMcp - Update Rebar Layout By Host"))
            {
                tx.Start();

                foreach (var hostIntId in hostIds.Distinct())
                {
                    var hid = Autodesk.Revit.DB.ElementIdCompat.From(hostIntId);
                    var host = doc.GetElement(hid);
                    if (host == null)
                    {
                        skipped.Add(new { elementId = hostIntId, reason = "NOT_FOUND" });
                        continue;
                    }

                    bool validHost = false;
                    try { validHost = RebarHostData.IsValidHost(host); } catch { validHost = false; }
                    if (!validHost)
                    {
                        skipped.Add(new { elementId = hostIntId, reason = "NOT_VALID_REBAR_HOST" });
                        continue;
                    }

                    RebarHostData hd = null;
                    try { hd = RebarHostData.GetRebarHostData(host); } catch { hd = null; }
                    if (hd == null)
                    {
                        skipped.Add(new { elementId = hostIntId, reason = "NO_HOST_DATA" });
                        continue;
                    }

                    // Revit API signature differs by version (ElementId list vs Rebar list), so use reflection.
                    var rebarIds = new List<ElementId>();
                    try
                    {
                        var mi = hd.GetType().GetMethod("GetRebarsInHost", Type.EmptyTypes);
                        var obj = mi != null ? mi.Invoke(hd, null) : null;
                        if (obj is System.Collections.IEnumerable en)
                        {
                            foreach (var x in en)
                            {
                                if (x is ElementId eid) rebarIds.Add(eid);
                                else if (x is Autodesk.Revit.DB.Structure.Rebar rb) rebarIds.Add(rb.Id);
                            }
                        }
                    }
                    catch { /* ignore */ }

                    foreach (var rid in rebarIds)
                    {
                        var rebar = doc.GetElement(rid) as Autodesk.Revit.DB.Structure.Rebar;
                        if (rebar == null) continue;

                        // Optional filter by comments tag
                        if (!string.IsNullOrEmpty(commentsTagEquals))
                        {
                            try
                            {
                                var c = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                var s = c != null ? c.AsString() : null;
                                if (!string.Equals((s ?? string.Empty).Trim(), commentsTagEquals, StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }
                            catch { /* ignore */ }
                        }

                        int rebarIntId = 0;
                        try { rebarIntId = rebar.Id.IntValue(); } catch { rebarIntId = 0; }
                        if (rebarIntId <= 0) continue;

                        if (RebarArrangementService.TryUpdateLayout(rebar, spec, out var code, out var msg))
                        {
                            updated.Add(rebarIntId);
                        }
                        else
                        {
                            skipped.Add(new { elementId = rebarIntId, reason = string.IsNullOrWhiteSpace(code) ? "FAILED" : code, msg });
                        }
                    }
                }

                tx.Commit();
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                updatedRebarIds = updated.OrderBy(x => x).ToArray(),
                skipped = skipped.ToArray(),
                msg = "Layout updated by host."
            });
        }
    }
}
