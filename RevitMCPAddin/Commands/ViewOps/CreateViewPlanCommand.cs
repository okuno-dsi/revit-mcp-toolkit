// RevitMCPAddin/Commands/ViewOps/CreateViewPlanCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Linq;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class CreateViewPlanCommand : IRevitCommandHandler
    {
        public string CommandName => "create_view_plan";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            string viewName = p.Value<string>("name") ?? "New Plan";
            string levelName = p.Value<string>("levelName") ?? "";

            // Optional: allow creating other plan families (e.g., CeilingPlan for RCP).
            // Supported keys: viewFamily / view_family (e.g., "FloorPlan" | "CeilingPlan").
            string viewFamilyRaw = p.Value<string>("viewFamily") ?? p.Value<string>("view_family") ?? "";
            ViewFamily desiredFamily = ViewFamily.FloorPlan;
            if (!string.IsNullOrWhiteSpace(viewFamilyRaw))
            {
                var vf = (viewFamilyRaw ?? "").Trim();
                if (vf.Equals("CeilingPlan", StringComparison.OrdinalIgnoreCase) ||
                    vf.Equals("Ceiling", StringComparison.OrdinalIgnoreCase) ||
                    vf.Equals("RCP", StringComparison.OrdinalIgnoreCase))
                {
                    desiredFamily = ViewFamily.CeilingPlan;
                }
            }

            // レベル取得
            var level = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level))
                            .Cast<Level>()
                            .FirstOrDefault(l => l.Name == levelName);
            if (level == null)
                throw new InvalidOperationException($"Level not found: {levelName}");

            // ビュータイプ取得
            var viewFamilyType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(ViewFamilyType))
                                    .Cast<ViewFamilyType>()
                                    .FirstOrDefault(vft => vft.ViewFamily == desiredFamily);
            if (viewFamilyType == null)
                throw new InvalidOperationException("No ViewFamilyType found for: " + desiredFamily);

            using var tx = new Transaction(doc, "Create View Plan");
            tx.Start();
            var view = ViewPlan.Create(doc, viewFamilyType.Id, level.Id);
            view.Name = viewName;
            tx.Commit();

            return new { ok = true, viewId = view.Id.IntValue(), name = view.Name };
        }
    }
}

