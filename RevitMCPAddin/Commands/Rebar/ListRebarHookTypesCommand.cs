// ================================================================
// Command: list_rebar_hook_types
// Purpose: List available RebarHookType definitions in the project.
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Kind   : read
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    [RpcCommand("list_rebar_hook_types",
        Category = "Rebar",
        Kind = "read",
        Risk = RiskLevel.Low,
        Summary = "List RebarHookType definitions (name/id/hookStyle/angle) in the current document.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"list_rebar_hook_types\", \"params\":{ \"includeCountByAngle\":true } }"
    )]
    public sealed class ListRebarHookTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "list_rebar_hook_types";

        private static string TryGetHookStyleName(RebarHookType ht)
        {
            if (ht == null) return null;
            try
            {
                var p = ht.GetType().GetProperty("HookStyle");
                if (p == null) return null;
                var v = p.GetValue(ht, null);
                return v != null ? v.ToString() : null;
            }
            catch { return null; }
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();
            bool includeCountByAngle = p.Value<bool?>("includeCountByAngle") ?? true;

            var items = new JArray();
            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>()
                    .Select(t => new
                    {
                        id = t.Id.IntValue(),
                        name = (t.Name ?? string.Empty).Trim(),
                        angleRad = (double?)t.HookAngle,
                        style = (TryGetHookStyleName(t) ?? string.Empty).Trim()
                    })
                    .Select(x => new
                    {
                        x.id,
                        x.name,
                        x.style,
                        angleRad = x.angleRad ?? 0.0,
                        angleDeg = (x.angleRad ?? 0.0) * 180.0 / Math.PI
                    })
                    .OrderBy(x => x.style ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.angleDeg)
                    .ThenBy(x => x.name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var t in types)
                {
                    items.Add(new JObject
                    {
                        ["elementId"] = t.id,
                        ["name"] = t.name,
                        ["hookStyle"] = t.style,
                        ["hookAngleDeg"] = Math.Round(t.angleDeg, 6)
                    });
                }

                JObject byAngle = null;
                if (includeCountByAngle)
                {
                    byAngle = new JObject();
                    foreach (var g in types.GroupBy(x => (int)Math.Round(x.angleDeg)))
                    {
                        byAngle[g.Key.ToString()] = g.Count();
                    }
                }

                return new JObject
                {
                    ["ok"] = true,
                    ["count"] = items.Count,
                    ["items"] = items,
                    ["countByAngleDeg"] = byAngle
                };
            }
            catch (Exception ex)
            {
                return ResultUtil.Err("RebarHookType の収集に失敗しました: " + ex.Message, "LIST_FAILED");
            }
        }
    }
}

