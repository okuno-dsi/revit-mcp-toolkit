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
                                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);
            if (viewFamilyType == null)
                throw new InvalidOperationException("No FloorPlan ViewFamilyType found.");

            using var tx = new Transaction(doc, "Create View Plan");
            tx.Start();
            var view = ViewPlan.Create(doc, viewFamilyType.Id, level.Id);
            view.Name = viewName;
            tx.Commit();

            return new { ok = true, viewId = view.Id.IntValue(), name = view.Name };
        }
    }
}

