using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class CreateWindowCommand : IRevitCommandHandler
    {
        public string CommandName => "create_window";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // 1) Window シンボル取得
            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Windows)
                .Cast<FamilySymbol>()
                .ToList();

            FamilySymbol symbol = null;
            if (p.TryGetValue("typeId", out var tid))
                symbol = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>())) as FamilySymbol;
            else if (p.TryGetValue("typeName", out var tn))
                symbol = symbols.FirstOrDefault(s => s.Name == tn.Value<string>());

            symbol ??= symbols.FirstOrDefault()
                ?? throw new InvalidOperationException("Window FamilySymbol が見つかりません");

            // 2) レベル取得
            Level level;
            if (p.TryGetValue("levelId", out var lidToken))
            {
                var lid = lidToken.Value<int>();
                level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(lid)) as Level
                    ?? throw new InvalidOperationException($"Level not found: {lid}");
            }
            else
            {
                level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException("ドキュメントにレベルがありません");
            }

            // 3) 挿入位置 (mm → internal)
            var loc = p["location"];
            var pos = UnitHelper.MmToXyz(
                loc.Value<double>("x"),
                loc.Value<double>("y"),
                loc.Value<double>("z")
            );

            // 4) インスタンス生成
            using var tx = new Transaction(doc, "Create Window");
            tx.Start();

            if (!symbol.IsActive)
                symbol.Activate();

            var newInst = doc.Create.NewFamilyInstance(
                pos,
                symbol,
                level,
                StructuralType.NonStructural
            );

            tx.Commit();

            return new
            {
                ok = true,
                elementId = newInst.Id.IntValue(),
                typeId = symbol.Id.IntValue()
            };
        }
    }
}


