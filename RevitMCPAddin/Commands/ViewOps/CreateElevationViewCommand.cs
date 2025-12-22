using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Linq;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class CreateElevationViewCommand : IRevitCommandHandler
    {
        public string CommandName => "create_elevation_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // 基準レベル（levelNameまたはlevelId）
            string levelName = p.Value<string>("levelName");
            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == levelName);
            if (level == null)
                throw new InvalidOperationException($"Level not found: {levelName}");

            // Elevationマークの配置位置
            var location = p["location"];
            double x = UnitUtils.ConvertToInternalUnits(location["x"].Value<double>(), UnitTypeId.Millimeters);
            double y = UnitUtils.ConvertToInternalUnits(location["y"].Value<double>(), UnitTypeId.Millimeters);
            double z = level.Elevation;

            var pt = new XYZ(x, y, z);

            // Elevation用ViewFamilyType取得
            var elevationType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Elevation);
            if (elevationType == null)
                throw new InvalidOperationException("Elevation用のViewFamilyTypeが見つかりません。");

            using (var tx = new Transaction(doc, "Create Elevation View"))
            {
                tx.Start();
                var marker = ElevationMarker.CreateElevationMarker(doc, elevationType.Id, pt, 1000);
                var view = marker.CreateElevation(doc, doc.ActiveView.Id, 0); // 0: 正面
                view.Name = p.Value<string>("name") ?? "新しい軸組図";
                tx.Commit();

                return new { ok = true, viewId = view.Id.IntValue(), name = view.Name };
            }
        }
    }
}

