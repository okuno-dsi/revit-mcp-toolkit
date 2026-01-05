// ================================================================
// Command: rebar_apply_plan
// Purpose: Apply an auto-rebar plan (or generate+apply from selection).
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// ================================================================
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    public sealed class RebarApplyPlanCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_apply_plan";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params ?? new JObject();
            bool dryRun = p.Value<bool?>("dryRun") ?? false;

            // If a plan is provided, apply it; otherwise build from current selection/hostElementIds.
            var planObj = p["plan"] as JObject;
            if (planObj == null)
            {
                planObj = RebarAutoModelService.BuildPlan(uiapp, doc, p);
                if (!(planObj.Value<bool?>("ok") ?? false))
                    return planObj; // propagate plan build error
            }

            return RebarAutoModelService.ApplyPlan(doc, planObj, dryRun);
        }
    }
}

