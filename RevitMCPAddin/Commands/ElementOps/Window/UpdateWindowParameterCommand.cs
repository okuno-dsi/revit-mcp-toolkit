using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Window
{
    public class UpdateWindowParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_window_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var eidToken))
                throw new ArgumentException("Parameter 'elementId' is required.");
            int elementId = eidToken.Value<int>();

            var elem = doc.GetElement(new ElementId(elementId));
            if (elem == null)
                return new { ok = false, msg = $"Window not found: {elementId}" };

            var param = ParamResolver.ResolveByPayload(elem, p, out var resolvedBy);
            if (param == null)
                return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)." };
            if (param.IsReadOnly)
                return new { ok = false, msg = $"Parameter '{param.Definition?.Name}' is read-only." };

            if (!p.TryGetValue("value", out var valToken))
                throw new ArgumentException("Parameter 'value' is required.");

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

            bool success;
            string? reason;

            using (var tx = new Transaction(doc, $"Set Window Param {param.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                success = UnitHelper.TrySetParameterFromSi(param, valToken, out reason);
                if (!success)
                {
                    tx.RollBack();
                    return new { ok = false, msg = reason ?? "Failed to set parameter." };
                }

                // 必要に応じて、オフセット差分に応じて窓全体を Z 方向に移動
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
                        RevitMCPAddin.Core.RevitLogger.Warn($"update_window_parameter: move by offset failed for element {elem.Id.IntegerValue}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return new { ok = true, elementId, param = param.Definition?.Name, value = valToken };
        }
    }
}
