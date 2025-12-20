// File: Commands/ElementOps/Foundation/ChangeStructuralFoundationTypeCommand.cs (UnitHelper対応/返却整備)
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class ChangeStructuralFoundationTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_structural_foundation_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            // 対象は elementId / uniqueId の両対応
            var inst = FoundationUnits.ResolveInstance(doc, p) as FamilyInstance;
            if (inst == null) return ResultUtil.Err("FamilyInstance が見つかりません（elementId/uniqueId）。");

            // 新タイプ: newTypeId or typeId
            int newTypeId = p.Value<int?>("newTypeId") ?? p.Value<int?>("typeId") ?? 0;
            var newType = (newTypeId > 0) ? doc.GetElement(new ElementId(newTypeId)) as ElementType : null;
            if (newType == null) return ResultUtil.Err($"TypeElement が見つかりません: {newTypeId}");

            int oldTypeId = inst.GetTypeId()?.IntegerValue ?? -1;

            using (var tx = new Transaction(doc, "Change Structural Foundation Type"))
            {
                tx.Start();
                inst.ChangeTypeId(newType.Id);
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = inst.Id.IntegerValue,
                uniqueId = inst.UniqueId,
                oldTypeId,
                typeId = newType.Id.IntegerValue
            };
        }
    }
}
