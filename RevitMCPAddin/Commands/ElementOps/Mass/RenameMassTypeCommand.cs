// File: RevitMCPAddin/Commands/ElementOps/Mass/RenameMassTypeCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    public class RenameMassTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_mass_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // typeId の取得と検証
            if (!p.TryGetValue("typeId", out var typeTok))
                throw new InvalidOperationException("Parameter 'typeId' is required.");
            int typeId = typeTok.Value<int>();

            // newTypeName の取得と検証
            if (!p.TryGetValue("newTypeName", out var nameTok) || string.IsNullOrWhiteSpace(nameTok.Value<string>()))
                throw new InvalidOperationException("Parameter 'newTypeName' is required and cannot be empty.");
            string newName = nameTok.Value<string>();

            // 要素取得
            var elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId));
            if (!(elem is FamilySymbol symbol))
            {
                return new { ok = false, message = $"Element {typeId} is not a Mass FamilySymbol and cannot be renamed." };
            }

            // 名前変更
            using var tx = new Transaction(doc, "Rename Mass Type");
            tx.Start();
            symbol.Name = newName;
            tx.Commit();

            return new { ok = true, typeId, newName };
        }
    }
}

