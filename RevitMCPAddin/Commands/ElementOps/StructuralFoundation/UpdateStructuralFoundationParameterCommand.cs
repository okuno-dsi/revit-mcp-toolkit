// ================================================================
// File: Commands/ElementOps/Foundation/UpdateStructuralFoundationParameterCommand.cs (UnitHelper対応版)
// - Double は Definition.GetDataType() を使い spec 依存で mm/m2/m3/deg → 内部へ
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class UpdateStructuralFoundationParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_foundation_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var elemToken))
                return ResultUtil.Err("Parameter 'elementId' is required.");
            int elementId = elemToken.Value<int>();

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return ResultUtil.Err("paramName or builtInName/builtInId/guid is required.");

            if (!p.TryGetValue("value", out var valueToken))
                return ResultUtil.Err("Parameter 'value' is required.");

            var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            if (element == null) return ResultUtil.Err($"Element not found: {elementId}");

            var param = ParamResolver.ResolveByPayload(element, p, out var resolvedBy);
            if (param == null) return ResultUtil.Err($"Parameter not found (name/builtIn/guid)");
            if (param.IsReadOnly) return ResultUtil.Err($"Parameter '{paramName}' は読み取り専用です");

            // オプション: レベルオフセット等の変更を Z 方向の移動としても反映する
            bool applyOffsetAsMove = p.Value<bool?>("applyOffsetAsMove") ?? false;
            double? deltaOffsetMm = null;
            if (applyOffsetAsMove && param.StorageType == StorageType.Double)
            {
                try
                {
                    double oldInternal = param.AsDouble();
                    double oldMm = UnitUtils.ConvertFromInternalUnits(oldInternal, UnitTypeId.Millimeters);
                    double newMm = valueToken.Value<double>();
                    double diff = newMm - oldMm;
                    if (Math.Abs(diff) > 1e-6)
                        deltaOffsetMm = diff;
                }
                catch
                {
                    // 差分計算に失敗した場合は位置移動を行わず、従来通りパラメータ更新のみを行う
                    deltaOffsetMm = null;
                }
            }

            bool success = false;
            using (var tx = new Transaction(doc, $"Set Parameter {param.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                try
                {
                    switch (param.StorageType)
                    {
                        case StorageType.Double:
                            {
                                ForgeTypeId spec = null;
                                try { spec = param.Definition?.GetDataType(); } catch { /* noop */ }
                                double user = valueToken.Value<double>();
                                double internalVal = UnitHelper.ToInternalBySpec(user, spec ?? SpecTypeId.Length);
                                success = param.Set(internalVal);
                                break;
                            }
                        case StorageType.Integer:
                            success = param.Set(valueToken.Value<int>()); break;
                        case StorageType.String:
                            success = param.Set(valueToken.Value<string>() ?? ""); break;
                        case StorageType.ElementId:
                            success = param.Set(Autodesk.Revit.DB.ElementIdCompat.From(valueToken.Value<int>())); break;
                        default:
                            success = false; break;
                    }

                    // 必要に応じて、オフセット差分に応じて基礎全体を Z 方向に移動
                    if (success && deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                    {
                        try
                        {
                            var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                            ElementTransformUtils.MoveElement(doc, element.Id, offset);
                        }
                        catch (Exception ex)
                        {
                            // 位置移動に失敗してもパラメータ更新自体は成功させる
                            RevitMCPAddin.Core.RevitLogger.Warn($"update_structural_foundation_parameter: move by offset failed for element {element.Id.IntValue()}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return ResultUtil.Err($"Failed to set parameter '{paramName}': {ex.Message}");
                }

                if (success) tx.Commit(); else tx.RollBack();
            }

            if (!success) return ResultUtil.Err($"Failed to set parameter '{paramName}'.");
            return ResultUtil.Ok(new { elementId, uniqueId = element.UniqueId });
        }
    }
}


