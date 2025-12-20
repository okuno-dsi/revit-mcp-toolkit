// File: Commands/ElementOps/Foundation/DuplicateStructuralFoundationTypeCommand.cs (UnitHelper対応)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class DuplicateStructuralFoundationTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_structural_foundation_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("newTypeName", out var nameToken))
                return ResultUtil.Err("newTypeName パラメータが必要です。");
            string newName = nameToken.Value<string>();

            int typeId = p.Value<int>("typeId");
            var origSymbol = doc.GetElement(new ElementId(typeId)) as FamilySymbol;
            if (origSymbol == null) return ResultUtil.Err($"Structural Foundation タイプが見つかりません: {typeId}");

            ElementType dupType;
            using (var tx = new Transaction(doc, "Duplicate Structural Foundation Type"))
            {
                tx.Start();
                dupType = origSymbol.Duplicate(newName) as ElementType;
                tx.Commit();
            }

            return new { ok = true, typeId = dupType.Id.IntegerValue, typeName = newName };
        }
    }
}
