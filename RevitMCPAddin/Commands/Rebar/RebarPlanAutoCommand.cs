// ================================================================
// Command: rebar_plan_auto
// Purpose: Build an auto-rebar plan for selected hosts (columns/beams).
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// ================================================================
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    public sealed class RebarPlanAutoCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_plan_auto";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params ?? new JObject();
            return RebarAutoModelService.BuildPlan(uiapp, doc, p);
        }
    }
}

