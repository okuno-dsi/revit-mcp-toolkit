// RevitMCPAddin/Commands/ElementOps/Door/CreateDoorCommand.cs
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class CreateDoorCommand : IRevitCommandHandler
    {
        public string CommandName => "create_door";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // 1) Door シンボル
            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Doors)
                .Cast<FamilySymbol>()
                .ToList();

            FamilySymbol symbol = null;
            if (p.TryGetValue("typeId", out var tid))
                symbol = doc.GetElement(new ElementId(tid.Value<int>())) as FamilySymbol;
            else if (p.TryGetValue("typeName", out var tn))
                symbol = symbols.FirstOrDefault(s => s.Name == tn.Value<string>());

            symbol ??= symbols.FirstOrDefault()
                ?? throw new System.InvalidOperationException("Door FamilySymbol が見つかりません");

            // 2) レベル
            Level level;
            if (p.TryGetValue("levelId", out var lidToken))
            {
                var lid = lidToken.Value<int>();
                level = doc.GetElement(new ElementId(lid)) as Level
                    ?? throw new System.InvalidOperationException($"Level not found: {lid}");
            }
            else
            {
                level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault()
                    ?? throw new System.InvalidOperationException("ドキュメントにレベルがありません");
            }

            // 3) 位置（mm→ft）
            var loc = p["location"];
            var pos = new XYZ(
                UnitHelper.MmToFt(loc.Value<double>("x")),
                UnitHelper.MmToFt(loc.Value<double>("y")),
                UnitHelper.MmToFt(loc.Value<double>("z"))
            );

            // 4) 生成
            using var tx = new Transaction(doc, "Create Door");
            tx.Start();
            if (!symbol.IsActive) symbol.Activate();

            var newInst = doc.Create.NewFamilyInstance(
                pos, symbol, level, StructuralType.NonStructural);

            tx.Commit();

            return new { ok = true, doorId = newInst.Id.IntegerValue, typeId = symbol.Id.IntegerValue };
        }
    }
}
