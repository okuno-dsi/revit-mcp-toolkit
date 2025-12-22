// File: RevitMCPAddin/Commands/ElementOps/Mass/DuplicateMassTypeCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    public class DuplicateMassTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_mass_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // typeId の取得
            if (!p.TryGetValue("typeId", out var typeTok))
                throw new InvalidOperationException("Parameter 'typeId' is required.");
            var typeId = Autodesk.Revit.DB.ElementIdCompat.From(typeTok.Value<int>());

            // 要素取得＆型チェック
            var elem = doc.GetElement(typeId);
            if (!(elem is FamilySymbol symbol))
            {
                return new
                {
                    ok = false,
                    message = $"Element {typeId.IntValue()} is not a Mass FamilySymbol and cannot be duplicated."
                };
            }

            // newTypeName の取得
            var newName = p.Value<string>("newTypeName");
            if (string.IsNullOrWhiteSpace(newName))
                throw new InvalidOperationException("Parameter 'newTypeName' is required.");

            // 複製処理
            using var tx = new Transaction(doc, "Duplicate Mass Type");
            tx.Start();
            var newSym = (FamilySymbol)symbol.Duplicate(newName);
            tx.Commit();

            return new
            {
                ok = true,
                newTypeId = newSym.Id.IntValue(),
                newName
            };
        }
    }
}


