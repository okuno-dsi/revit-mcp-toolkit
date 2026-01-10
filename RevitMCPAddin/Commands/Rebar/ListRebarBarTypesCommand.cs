// ================================================================
// Command: list_rebar_bar_types
// Purpose: List available RebarBarType definitions in the project.
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
    [RpcCommand("list_rebar_bar_types",
        Category = "Rebar",
        Kind = "read",
        Risk = RiskLevel.Low,
        Summary = "List RebarBarType definitions (name/id/diameter) in the current document.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"list_rebar_bar_types\", \"params\":{ \"includeCountByDiameter\":true } }"
    )]
    public sealed class ListRebarBarTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "list_rebar_bar_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();
            bool includeCountByDiameter = p.Value<bool?>("includeCountByDiameter") ?? true;

            var items = new JArray();
            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>()
                    .Select(t => new
                    {
                        id = t.Id.IntValue(),
                        name = (t.Name ?? string.Empty).Trim(),
                        diaMm = UnitHelper.FtToMm(t.BarModelDiameter)
                    })
                    .OrderBy(x => x.diaMm)
                    .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var t in types)
                {
                    items.Add(new JObject
                    {
                        ["elementId"] = t.id,
                        ["name"] = t.name,
                        ["diameterMm"] = Math.Round(t.diaMm, 3)
                    });
                }

                JObject byDia = null;
                if (includeCountByDiameter)
                {
                    byDia = new JObject();
                    foreach (var g in types.GroupBy(x => (int)Math.Round(x.diaMm)))
                    {
                        byDia[g.Key.ToString()] = g.Count();
                    }
                }

                return new JObject
                {
                    ["ok"] = true,
                    ["count"] = items.Count,
                    ["items"] = items,
                    ["countByDiameterMm"] = byDia
                };
            }
            catch (Exception ex)
            {
                return ResultUtil.Err("RebarBarType の収集に失敗しました: " + ex.Message, "LIST_FAILED");
            }
        }
    }
}

