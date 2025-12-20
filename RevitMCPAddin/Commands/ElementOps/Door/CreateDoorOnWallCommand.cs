// RevitMCPAddin/Commands/ElementOps/Door/CreateDoorOnWallCommand.cs
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class CreateDoorOnWallCommand : IRevitCommandHandler
    {
        public string CommandName => "create_door_on_wall";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

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

            var wallId = p.Value<int>("wallId");
            var hostWall = doc.GetElement(new ElementId(wallId)) as Autodesk.Revit.DB.Wall
                          ?? throw new System.InvalidOperationException($"Wall not found: {wallId}");

            var level = doc.GetElement(hostWall.LevelId) as Level
                        ?? throw new System.InvalidOperationException("Host wall's level not found");

            var loc = p["location"];
            var pos = new XYZ(
                UnitHelper.MmToFt(loc.Value<double>("x")),
                UnitHelper.MmToFt(loc.Value<double>("y")),
                UnitHelper.MmToFt(loc.Value<double>("z"))
            );

            using var tx = new Transaction(doc, "Create Door On Wall");
            tx.Start();
            if (!symbol.IsActive) symbol.Activate();

            var inst = doc.Create.NewFamilyInstance(
                pos, symbol, hostWall, level, StructuralType.NonStructural);

            tx.Commit();
            return new { ok = true, elementId = inst.Id.IntegerValue, typeId = symbol.Id.IntegerValue };
        }
    }
}
