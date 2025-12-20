using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class CreateWindowOnWallCommand : IRevitCommandHandler
    {
        public string CommandName => "create_window_on_wall";

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
                symbol = doc.GetElement(new ElementId(tid.Value<int>())) as FamilySymbol;
            else if (p.TryGetValue("typeName", out var tn))
                symbol = symbols.FirstOrDefault(s => s.Name == tn.Value<string>());

            symbol ??= symbols.FirstOrDefault()
                ?? throw new InvalidOperationException("Window FamilySymbol が見つかりません");

            // 2) Host Wall
            var wallId = p.Value<int>("wallId");
            var hostWall = doc.GetElement(new ElementId(wallId)) as Autodesk.Revit.DB.Wall
                          ?? throw new InvalidOperationException($"Wall not found: {wallId}");

            // 3) レベル
            var level = doc.GetElement(hostWall.LevelId) as Level
                        ?? throw new InvalidOperationException("Host wall's level not found");

            // 4) 位置 (mm → internal)
            var loc = p["location"];
            var pos = UnitHelper.MmToXyz(
                loc.Value<double>("x"),
                loc.Value<double>("y"),
                loc.Value<double>("z")
            );

            using var tx = new Transaction(doc, "Create Window On Wall");
            tx.Start();

            if (!symbol.IsActive)
                symbol.Activate();

            var inst = doc.Create.NewFamilyInstance(
                pos,
                symbol,
                hostWall,
                level,
                StructuralType.NonStructural
            );

            tx.Commit();

            return new
            {
                ok = true,
                elementId = inst.Id.IntegerValue,
                typeId = symbol.Id.IntegerValue
            };
        }
    }
}
