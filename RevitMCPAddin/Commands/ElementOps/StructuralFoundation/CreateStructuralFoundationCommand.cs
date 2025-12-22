// File: Commands/ElementOps/Foundation/CreateStructuralFoundationCommand.cs (UnitHelper対応)
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class CreateStructuralFoundationCommand : IRevitCommandHandler
    {
        public string CommandName => "create_structural_foundation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)cmd.Params;

            // 必須: typeId / levelId / location{x,y,z(mm)}
            int typeId = p.Value<int>("typeId");
            int levelId = p.Value<int>("levelId");

            var loc = p["location"] as JObject ?? throw new ArgumentException("location パラメータが必要です");
            double xMm = loc.Value<double>("x");
            double yMm = loc.Value<double>("y");
            double zMm = loc.Value<double>("z");

            var symbol = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FamilySymbol;
            if (symbol == null) return ResultUtil.Err($"FamilySymbol が見つかりません: {typeId}");
            var level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId)) as Level;
            if (level == null) return ResultUtil.Err($"Level が見つかりません: {levelId}");

            FamilyInstance instance;
            using (var tx = new Transaction(doc, "Create Structural Foundation"))
            {
                tx.Start();
                if (!symbol.IsActive) symbol.Activate();

                var xyz = FoundationUnits.MmXYZ(xMm, yMm, zMm);
                instance = doc.Create.NewFamilyInstance(xyz, symbol, level, StructuralType.Footing);

                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = instance.Id.IntValue(),
                uniqueId = instance.UniqueId,
                typeId = instance.GetTypeId()?.IntValue(),
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }
}


