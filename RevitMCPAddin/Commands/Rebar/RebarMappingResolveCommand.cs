// ================================================================
// Command: rebar_mapping_resolve
// Purpose: Resolve RebarMapping.json logical keys for host elements (debug/inspection).
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    public sealed class RebarMappingResolveCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_mapping_resolve";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params ?? new JObject();

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

            string profile = (p.Value<string>("profile") ?? string.Empty).Trim();
            bool includeDebug = p.Value<bool?>("includeDebug") ?? false;

            var keys = new List<string>();
            try
            {
                var arr = p["keys"] as JArray;
                if (arr != null)
                {
                    foreach (var t in arr)
                    {
                        if (t == null || t.Type != JTokenType.String) continue;
                        var s = (t.Value<string>() ?? string.Empty).Trim();
                        if (s.Length > 0) keys.Add(s);
                    }
                }
            }
            catch { /* ignore */ }

            var status = JObject.FromObject(RebarMappingService.GetStatus());

            var items = new List<object>();
            foreach (var hostIntId in hostIds.Distinct())
            {
                Element host = null;
                try { host = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(hostIntId)); } catch { host = null; }
                if (host == null)
                {
                    items.Add(new
                    {
                        ok = false,
                        elementId = hostIntId,
                        code = "NOT_FOUND",
                        msg = "Host element not found."
                    });
                    continue;
                }

                JObject resolved;
                try
                {
                    resolved = RebarMappingService.ResolveForElement(doc, host, profile.Length > 0 ? profile : null, keys.Count > 0 ? (IEnumerable<string>)keys : null, includeDebug);
                }
                catch (Exception ex)
                {
                    resolved = new JObject
                    {
                        ["ok"] = false,
                        ["code"] = "REVIT_EXCEPTION",
                        ["msg"] = ex.Message
                    };
                }

                items.Add(resolved);
            }

            return ResultUtil.Ok(new
            {
                ok = true,
                mappingStatus = status,
                items = items.ToArray()
            });
        }
    }
}

