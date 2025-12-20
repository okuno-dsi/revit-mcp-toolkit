using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class CreateSectionViewCommand : IRevitCommandHandler
    {
        public string CommandName => "create_section";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // 1. 範囲パラメータを取得（mm 単位）
            double xMin = p["range"]["minX"].Value<double>();
            double yMin = p["range"]["minY"].Value<double>();
            double zMin = p["range"]["minZ"].Value<double>();
            double xMax = p["range"]["maxX"].Value<double>();
            double yMax = p["range"]["maxY"].Value<double>();
            double zMax = p["range"]["maxZ"].Value<double>();

            // mm→内部単位（フィート）に変換
            // ForgeTypeId 型で直接渡す
            var p1 = new XYZ(
                ConvertToInternalUnits(xMin, UnitTypeId.Millimeters),
                ConvertToInternalUnits(yMin, UnitTypeId.Millimeters),
                ConvertToInternalUnits(zMin, UnitTypeId.Millimeters)
                        );
            var p2 = new XYZ(
                ConvertToInternalUnits(xMax, UnitTypeId.Millimeters),
                ConvertToInternalUnits(yMax, UnitTypeId.Millimeters),
                ConvertToInternalUnits(zMax, UnitTypeId.Millimeters)
            );

            var bbox = new BoundingBoxXYZ();
            bbox.Min = p1;
            bbox.Max = p2;

            // 2. ViewFamilyType を「断面 (Section)」で取得
            var sectionVft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vf => vf.ViewFamily == ViewFamily.Section)
                ?? throw new InvalidOperationException("断面ビュータイプが見つかりません。");

            // 3. 断面ビュー作成
            using var tx = new Transaction(doc, "Create Section View");
            tx.Start();
            var viewSection = ViewSection.CreateSection(doc, sectionVft.Id, bbox);
            // 任意で名前付け
            viewSection.Name = p.Value<string>("name") ?? "Section";
            tx.Commit();

            return new { viewId = viewSection.Id.IntegerValue, name = viewSection.Name };
        }
    }
}
