// RevitMCPAddin/Commands/ElementOps/Door/UpdateDoorParameterCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class UpdateDoorParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_door_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var idToken))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            var id = new ElementId(idToken.Value<int>());
            var elem = doc.GetElement(id)
                       ?? throw new InvalidOperationException($"Door not found: {id.IntegerValue}");

            var param = ParamResolver.ResolveByPayload(elem, p, out var resolvedBy)
                       ?? throw new InvalidOperationException($"Parameter not found (name/builtIn/guid).");

            if (param.IsReadOnly)
                return new { ok = false, message = $"Parameter '{param.Definition?.Name}' is read-only." };

            if (!p.TryGetValue("value", out var valToken))
                throw new InvalidOperationException("Parameter 'value' is required.");

            // オプション: レベルオフセット等の変更を Z 方向の移動としても反映する
            bool applyOffsetAsMove = p.Value<bool?>("applyOffsetAsMove") ?? false;
            double? deltaOffsetMm = null;
            if (applyOffsetAsMove && param.StorageType == StorageType.Double)
            {
                try
                {
                    double oldInternal = param.AsDouble();
                    double oldMm = UnitUtils.ConvertFromInternalUnits(oldInternal, UnitTypeId.Millimeters);
                    double newMm = valToken.Value<double>();
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

            using (var tx = new Transaction(doc, $"Update Door Param {param.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    // ★ UnitHelper が StorageType と DataType(Spec)をみて SI→内部へ安全変換
                    string? err;
                    var ok = UnitHelper.TrySetParameterByExternalValue(param, (valToken as JValue)?.Value, out err);
                    if (!ok)
                    {
                        tx.RollBack();
                        return new { ok = false, message = err ?? "Failed to set parameter." };
                    }

                    // 必要に応じて、オフセット差分に応じてドア全体を Z 方向に移動
                    if (deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                    {
                        try
                        {
                            var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                            ElementTransformUtils.MoveElement(doc, elem.Id, offset);
                        }
                        catch (Exception ex)
                        {
                            // 位置移動に失敗してもパラメータ更新自体は成功させる
                            RevitMCPAddin.Core.RevitLogger.Warn($"update_door_parameter: move by offset failed for element {elem.Id.IntegerValue}: {ex.Message}");
                        }
                    }

                    tx.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, message = ex.Message };
                }
            }
        }
    }
}
