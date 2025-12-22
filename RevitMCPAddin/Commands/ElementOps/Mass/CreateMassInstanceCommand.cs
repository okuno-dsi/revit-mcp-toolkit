// File: RevitMCPAddin/Commands/ElementOps/Mass/CreateMassInstanceCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Linq;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    public class CreateMassInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "create_mass_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Mass)
                .Cast<FamilySymbol>()
                .ToList();

            FamilySymbol symbol = null;
            if (p.TryGetValue("typeId", out var tid))
                symbol = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>())) as FamilySymbol;
            symbol ??= symbols.FirstOrDefault()
                     ?? throw new InvalidOperationException("Mass FamilySymbol が見つかりません");

            Level level;
            if (p.TryGetValue("levelId", out var lid))
                level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(lid.Value<int>())) as Level
                        ?? throw new InvalidOperationException($"Level not found: {lid}");
            else
                level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault()
                        ?? throw new InvalidOperationException("レベルが見つかりません");

            // 位置（mm → ft）
            var loc = p["location"] ?? throw new InvalidOperationException("'location' is required.");
            var pos = new XYZ(
                UnitHelper.MmToFt(loc.Value<double>("x")),
                UnitHelper.MmToFt(loc.Value<double>("y")),
                UnitHelper.MmToFt(loc.Value<double>("z"))
            );

            using var tx = new Transaction(doc, "Create Mass Instance");
            tx.Start();
            if (!symbol.IsActive) symbol.Activate();
            var inst = doc.Create.NewFamilyInstance(pos, symbol, level, StructuralType.NonStructural);
            tx.Commit();

            return new
            {
                ok = true,
                elementId = inst.Id.IntValue(),
                typeId = symbol.Id.IntValue()
            };
        }
    }
}


