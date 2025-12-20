using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class UpdateCurtainWallParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_curtain_wall_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int id = (int)p["elementId"]!;
            var valueTok = p["value"]!; // SI 入力（mm/m2/m3/deg 等）を期待

            var doc = uiapp.ActiveUIDocument.Document;
            var elem = doc.GetElement(new ElementId(id))
                       ?? throw new InvalidOperationException("Element not found");

            var prm = ParamResolver.ResolveByPayload(elem, p, out var resolvedBy)
                      ?? throw new InvalidOperationException($"Parameter not found (name/builtIn/guid)");
            if (prm.IsReadOnly)
                throw new InvalidOperationException($"Parameter '{prm.Definition?.Name}' is read-only");

            // オプション: レベルオフセット等の変更を Z 方向の移動としても反映する
            bool applyOffsetAsMove = p.Value<bool?>("applyOffsetAsMove") ?? false;
            double? deltaOffsetMm = null;
            if (applyOffsetAsMove && prm.StorageType == StorageType.Double)
            {
                try
                {
                    double oldInternal = prm.AsDouble();
                    double oldMm = UnitUtils.ConvertFromInternalUnits(oldInternal, UnitTypeId.Millimeters);
                    double newMm = valueTok.Value<double>();
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

            using (var tx = new Transaction(doc, $"Update Parameter {prm.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                string err;
                var ok = UnitHelper.TrySetParameterByExternalValue(prm, (valueTok as JValue)?.Value, out err);
                if (!ok)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"更新に失敗: {err}" };
                }

                // 必要に応じて、オフセット差分に応じてカーテンウォール全体を Z 方向に移動
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
                        RevitMCPAddin.Core.RevitLogger.Warn($"update_curtain_wall_parameter: move by offset failed for element {elem.Id.IntegerValue}: {ex.Message}");
                    }
                }

                tx.Commit();
            }
            return new { ok = true };
        }
    }
}
