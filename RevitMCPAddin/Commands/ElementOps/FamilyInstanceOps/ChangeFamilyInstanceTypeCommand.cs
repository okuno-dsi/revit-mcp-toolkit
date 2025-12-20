// File: Commands/ElementOps/FamilyInstanceOps/ChangeFamilyInstanceTypeCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FamilyInstanceOps
{
    public class ChangeFamilyInstanceTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_family_instance_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;

            // 対象インスタンス解決（elementId / uniqueId）
            var el = FamUtil.ResolveElement(doc, p);
            var inst = el as FamilyInstance;
            if (inst == null || !FamUtil.IsLoadableFamilyInstance(inst))
                return new { ok = false, msg = "ロード可能な FamilyInstance が見つかりません（elementId/uniqueId を確認）。" };

            // 変更先タイプ解決
            FamilySymbol newSym;
            try
            {
                newSym = FamUtil.ResolveSymbolByArgs(doc, p);
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = $"タイプ解決に失敗: {ex.Message}" };
            }

            if (newSym == null || newSym.Family == null || newSym.Family.IsInPlace)
                return new { ok = false, msg = "新しいタイプがロード可能ファミリではありません。" };

            // カテゴリ整合性チェック
            var instCatId = inst.Category?.Id?.IntegerValue;
            var symCatId = newSym.Category?.Id?.IntegerValue;
            if (instCatId.HasValue && symCatId.HasValue && instCatId.Value != symCatId.Value)
                return new { ok = false, msg = $"カテゴリ不一致のため変更できません（instance:{inst.Category?.Name} / type:{newSym.Category?.Name}）。" };

            int oldTypeId = inst.Symbol?.Id.IntegerValue ?? -1;

            // 変更実行
            using (var tx = new Transaction(doc, "Change Family Instance Type"))
            {
                tx.Start();
                try
                {
                    inst.ChangeTypeId(newSym.Id);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"タイプ変更に失敗: {ex.Message}" };
                }
                tx.Commit();
            }

            // 結果
            var newType = doc.GetElement(inst.GetTypeId()) as FamilySymbol;
            return new
            {
                ok = true,
                elementId = inst.Id.IntegerValue,
                uniqueId = inst.UniqueId,
                oldTypeId = oldTypeId,
                typeId = newType?.Id.IntegerValue,
                typeName = newType?.Name ?? "",
                familyName = newType?.Family?.Name ?? "",
                categoryId = inst.Category?.Id.IntegerValue,
                categoryName = inst.Category?.Name ?? ""
            };
        }
    }
}
