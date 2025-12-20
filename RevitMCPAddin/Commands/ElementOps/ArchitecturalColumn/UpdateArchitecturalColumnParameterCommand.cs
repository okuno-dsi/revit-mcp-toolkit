// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/UpdateArchitecturalColumnParameterCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class UpdateArchitecturalColumnParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_architectural_column_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int id = p.Value<int>("elementId");
            string paramName = p.Value<string>("paramName");
            var valueToken = p["value"] ?? throw new InvalidOperationException("'value' が必要です");
            var valueObj = valueToken.ToObject<object>()
                           ?? throw new InvalidOperationException("'value' が必要です");

            var fi = doc.GetElement(new ElementId(id)) as FamilyInstance
                     ?? throw new InvalidOperationException($"要素が見つかりません: {id}");

            var prm = fi.LookupParameter(paramName)
                      ?? throw new InvalidOperationException($"パラメータ '{paramName}' が見つかりません");
            if (prm.IsReadOnly)
                return new { ok = false, msg = $"パラメータ '{paramName}' は読み取り専用です" };

            // オプション: レベルオフセット等の変更を Z 方向の移動としても反映する
            bool applyOffsetAsMove = p.Value<bool?>("applyOffsetAsMove") ?? false;
            double? deltaOffsetMm = null;
            if (applyOffsetAsMove && prm.StorageType == StorageType.Double)
            {
                try
                {
                    double oldInternal = prm.AsDouble();
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

            using (var tx = new Transaction(doc, $"Update Param '{paramName}'"))
            {
                tx.Start();
                if (!UnitHelper.TrySetParameterByExternalValue(prm, valueObj, out var err))
                {
                    tx.RollBack();
                    return new { ok = false, msg = err ?? "Failed to set parameter." };
                }

                // 必要に応じて、オフセット差分に応じて柱全体を Z 方向に移動
                if (deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                {
                    try
                    {
                        var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                        ElementTransformUtils.MoveElement(doc, fi.Id, offset);
                    }
                    catch (Exception ex)
                    {
                        // 位置移動に失敗してもパラメータ更新自体は成功させる
                        RevitMCPAddin.Core.RevitLogger.Warn($"update_architectural_column_parameter: move by offset failed for element {fi.Id.IntegerValue}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }
}
