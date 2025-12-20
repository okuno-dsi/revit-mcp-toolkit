// File: RevitMCPAddin/Commands/ElementOps/Mass/UpdateMassInstanceParameterCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    public class UpdateMassInstanceParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_mass_instance_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var idTok))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            int elementId = idTok.Value<int>();
            var element = doc.GetElement(new ElementId(elementId));
            if (element == null)
                return new { ok = false, message = $"Element not found: {elementId}" };

            if (!(element is FamilyInstance inst))
                return new { ok = false, message = "Element is not a Mass FamilyInstance. Parameter update not supported." };

            if (!p.TryGetValue("paramName", out var nameTok) || string.IsNullOrWhiteSpace(nameTok.Value<string>()))
                throw new InvalidOperationException("Parameter 'paramName' is required.");
            string paramName = nameTok.Value<string>();

            var param = inst.LookupParameter(paramName);
            if (param == null)
                return new { ok = false, message = $"Parameter '{paramName}' not found." };
            if (param.IsReadOnly)
                return new { ok = false, message = $"Parameter '{paramName}' is read-only." };

            if (!p.TryGetValue("value", out var valTok))
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
                    double newMm = valTok.Value<double>();
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

            using var tx = new Transaction(doc, $"Update Mass Param {paramName}");
            tx.Start();
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(valTok.Value<string>() ?? "");
                        break;

                    case StorageType.Integer:
                        param.Set(valTok.Value<int>());
                        break;

                    case StorageType.Double:
                        {
                            // 既定: mm を受け取り内部(ft)にして格納
                            // ※将来的に Spec による自動変換を使う場合は UnitHelper の Spec API に差し替え
                            double mm = valTok.Value<double>();
                            double ft = UnitHelper.MmToFt(mm);
                            param.Set(ft);
                            break;
                        }

                    case StorageType.ElementId:
                        param.Set(new ElementId(valTok.Value<int>()));
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported StorageType: {param.StorageType}");
                }

                // 必要に応じて、オフセット差分に応じて Mass インスタンス全体を Z 方向に移動
                if (deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                {
                    try
                    {
                        var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                        ElementTransformUtils.MoveElement(doc, inst.Id, offset);
                    }
                    catch (Exception ex)
                    {
                        // 位置移動に失敗してもパラメータ更新自体は成功させる
                        RevitMCPAddin.Core.RevitLogger.Warn($"update_mass_instance_parameter: move by offset failed for element {inst.Id.IntegerValue}: {ex.Message}");
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
