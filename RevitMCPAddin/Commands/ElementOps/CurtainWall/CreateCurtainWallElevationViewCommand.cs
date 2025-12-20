using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class CreateCurtainWallElevationViewCommand : IRevitCommandHandler
    {
        public string CommandName => "create_curtain_wall_elevation_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int wallId = (int)p["elementId"]!;
            string name = (string)p["viewName"]!;

            var doc = uiapp.ActiveUIDocument.Document;
            var wall = doc.GetElement(new ElementId(wallId)) as Autodesk.Revit.DB.Wall
                           ?? throw new InvalidOperationException("Curtain wall not found");
            // ホスティングする平面ビューを取得（現在のビューが ViewPlan でない場合は最初の ViewPlan を使う）
            ViewPlan plan = doc.ActiveView as ViewPlan
                            ?? new FilteredElementCollector(doc)
                                .OfClass(typeof(ViewPlan))
                                .Cast<ViewPlan>()
                                .FirstOrDefault()
                            ?? throw new InvalidOperationException("No plan view available");

            // Elevation 用の ViewFamilyType を取得  
            var vft = new FilteredElementCollector(doc)
                      .OfClass(typeof(ViewFamilyType))
                      .Cast<ViewFamilyType>()
                      .First(x => x.ViewFamily == ViewFamily.Elevation);

            // Marker の設置位置を要素のバウンディングボックス中心とする
            var bb = wall.get_BoundingBox(plan)
                         ?? throw new InvalidOperationException("BoundingBox not available");
            var origin = (bb.Min + bb.Max) * 0.5;

            ViewSection elevView;
            using (var tx = new Transaction(doc, "Create Curtain Wall Elevation"))
            {
                tx.Start();
                // マーカーを作成 :contentReference[oaicite:0]{index=0}
                var marker = ElevationMarker.CreateElevationMarker(
                                doc, vft.Id, origin, 100);
                // 最初のスロットに Elevation ViewSection を作成 :contentReference[oaicite:1]{index=1}
                elevView = marker.CreateElevation(doc, plan.Id, 0);
                elevView.Name = name;
                tx.Commit();
            }

            return new
            {
                ok = true,
                viewId = elevView.Id.IntegerValue
            };
        }
    }
}
