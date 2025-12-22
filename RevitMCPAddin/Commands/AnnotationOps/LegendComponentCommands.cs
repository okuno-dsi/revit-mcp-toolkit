// ================================================================
// File   : Commands/AnnotationOps/LegendComponentCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose: Legend component helpers
//          - set_legend_component_type
// Notes  :
//  - LegendComponent の型切り替えには ChangeTypeId ではなく
//    BuiltInParameter.LEGEND_COMPONENT (ElementId) を用いる。
//  - 1 回の呼び出しで 1 要素のみを処理し、トランザクションを短く保つ。
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    /// <summary>
    /// set_legend_component_type
    /// Legend ビュー上の凡例コンポーネント (OST_LegendComponents) のタイプを変更する。
    ///
    /// パラメータ:
    /// - legendComponentId / elementId : 対象要素の ElementId (int)
    /// - targetTypeId / typeId        : 変更先タイプの ElementId (int, FamilySymbol 等)
    /// - expectedCategory             : (任意) "Doors" / "Windows" など。指定時はタイプのカテゴリを簡易チェックする。
    ///
    /// 戻り値:
    /// { ok, msg, elementId, oldTypeId, newTypeId, changed }
    /// </summary>
    public class SetLegendComponentTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "set_legend_component_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            var p = cmd.Params as JObject ?? new JObject();

            // 任意の期待コンテキストチェック
            var guard = ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            int? elemId1 = p.Value<int?>("legendComponentId");
            int? elemId2 = p.Value<int?>("elementId");
            int? typeId1 = p.Value<int?>("targetTypeId");
            int? typeId2 = p.Value<int?>("typeId");

            if (!elemId1.HasValue && !elemId2.HasValue)
            {
                return new
                {
                    ok = false,
                    msg = "legendComponentId または elementId を指定してください。"
                };
            }

            if (!typeId1.HasValue && !typeId2.HasValue)
            {
                return new
                {
                    ok = false,
                    msg = "targetTypeId または typeId を指定してください。"
                };
            }

            int elementIdInt = elemId1 ?? elemId2!.Value;
            int typeIdInt = typeId1 ?? typeId2!.Value;

            var elementId = Autodesk.Revit.DB.ElementIdCompat.From(elementIdInt);
            var targetTypeId = Autodesk.Revit.DB.ElementIdCompat.From(typeIdInt);

            var elem = doc.GetElement(elementId);
            if (elem == null)
            {
                return new
                {
                    ok = false,
                    msg = $"ElementId {elementIdInt} が見つかりません (legend component)。",
                    elementId = elementIdInt
                };
            }

            // 一応カテゴリチェック（凡例コンポーネント以外は対象外）
            try
            {
                var cat = elem.Category;
                if (cat == null || cat.Id.IntValue() != (int)BuiltInCategory.OST_LegendComponents)
                {
                    return new
                    {
                        ok = false,
                        msg = "対象要素は凡例コンポーネント (OST_LegendComponents) ではありません。",
                        elementId = elementIdInt,
                        categoryName = cat?.Name
                    };
                }
            }
            catch
            {
                // Category 取得に失敗した場合はそのまま続行（古いバージョン対策）
            }

            var targetTypeElem = doc.GetElement(targetTypeId) as ElementType;
            if (targetTypeElem == null)
            {
                return new
                {
                    ok = false,
                    msg = $"targetTypeId {typeIdInt} に対応する ElementType が見つかりません。",
                    typeId = typeIdInt
                };
            }

            // 任意のカテゴリ期待値チェック（Doors / Windows など）
            var expectedCategory = p.Value<string>("expectedCategory");
            if (!string.IsNullOrWhiteSpace(expectedCategory))
            {
                BuiltInCategory? expectedBic = null;
                if (expectedCategory.Equals("Doors", StringComparison.OrdinalIgnoreCase))
                {
                    expectedBic = BuiltInCategory.OST_Doors;
                }
                else if (expectedCategory.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    expectedBic = BuiltInCategory.OST_Windows;
                }

                if (expectedBic.HasValue)
                {
                    var tc = targetTypeElem.Category;
                    if (tc == null || tc.Id.IntValue() != (int)expectedBic.Value)
                    {
                        return new
                        {
                            ok = false,
                            msg = $"指定されたタイプのカテゴリ '{tc?.Name}' は expectedCategory='{expectedCategory}' と一致しません。",
                            typeId = typeIdInt,
                            actualCategory = tc?.Name,
                            expectedCategory
                        };
                    }
                }
            }

            // LegendComponent が参照している実際のタイプ (FamilySymbol) は
            // LEGEND_COMPONENT パラメータで管理されている。
            var legendParam = elem.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
            if (legendParam == null)
            {
                return new
                {
                    ok = false,
                    msg = "LEGEND_COMPONENT パラメータが見つからないため、タイプ変更できません。",
                    elementId = elementIdInt
                };
            }
            if (legendParam.IsReadOnly)
            {
                return new
                {
                    ok = false,
                    msg = "LEGEND_COMPONENT パラメータが読み取り専用のため、タイプ変更できません。",
                    elementId = elementIdInt
                };
            }

            // 現在割り当てられているタイプ (ElementId)
            ElementId oldTypeId = ElementId.InvalidElementId;
            try
            {
                if (legendParam.StorageType == StorageType.ElementId)
                {
                    oldTypeId = legendParam.AsElementId();
                }
            }
            catch
            {
                oldTypeId = ElementId.InvalidElementId;
            }

            bool changed = false;

            using (var tx = new Transaction(doc, "Set Legend Component Type"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                try
                {
                    if (oldTypeId != null && oldTypeId.IntValue() == typeIdInt)
                    {
                        // 既に同じタイプであれば何もしない
                        tx.Commit();
                    }
                    else
                    {
                        // LEGEND_COMPONENT に FamilySymbol.Id をセットするのが正しい手段
                        if (legendParam.StorageType != StorageType.ElementId)
                        {
                            throw new InvalidOperationException("LEGEND_COMPONENT parameter is not of ElementId storage type.");
                        }

                        legendParam.Set(targetTypeId);
                        // typeName 等の表示更新のため Regenerate
                        doc.Regenerate();

                        changed = true;
                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        try { tx.RollBack(); } catch { }
                    }

                    return new
                    {
                        ok = false,
                        msg = "set_legend_component_type でエラーが発生しました: " + ex.Message,
                        elementId = elementIdInt,
                        typeId = typeIdInt
                    };
                }
            }

            return new
            {
                ok = true,
                msg = changed
                    ? "Legend コンポーネントのタイプを変更しました。"
                    : "Legend コンポーネントは既に指定されたタイプです。",
                elementId = elementIdInt,
                oldTypeId = oldTypeId != null ? oldTypeId.IntValue() : -1,
                newTypeId = typeIdInt,
                changed
            };
        }
    }
}


