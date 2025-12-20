// ================================================================
// File: Commands/MassOps/UpdateDirectShapeParameterCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Purpose: DirectShape（Mass相当）要素のパラメータを “安全に” 更新
// Inputs : { elementId | uniqueId, paramName, value, [unitsMode] }
// Notes  : Double は Spec に基づき mm/deg 入力 → 内部単位(ft/rad)へ変換
//          String/Integer/ElementId もサポート（UnitHelper に準拠）
// Returns: { ok, elementId, uniqueId, paramName, newValue?, display?, meta? }
// Errors : { ok:false, msg, code? }
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // ResultUtil, UnitHelper, IRevitCommandHandler, RequestCommand

namespace RevitMCPAddin.Commands.MassOps
{
    public class UpdateDirectShapeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_direct_shape_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
                return ResultUtil.Err("アクティブドキュメントがありません。", "NO_ACTIVE_DOC");

            var p = (JObject)cmd.Params;

            // ---- 1) ターゲット解決（elementId or uniqueId 必須）
            Element? elem = null;
            int? elementId = p.Value<int?>("elementId");
            string? uniqueId = p.Value<string>("uniqueId");

            if (elementId.HasValue && elementId.Value > 0)
            {
                elem = doc.GetElement(new ElementId(elementId.Value));
            }
            else if (!string.IsNullOrWhiteSpace(uniqueId))
            {
                elem = doc.GetElement(uniqueId);
            }
            else
            {
                return ResultUtil.Err("elementId または uniqueId を指定してください。", "MISSING_TARGET");
            }

            if (elem == null)
                return ResultUtil.Err("要素が見つかりません。", "NOT_FOUND");

            // ---- 2) DirectShape 専用ガード
            if (!(elem is DirectShape))
                return ResultUtil.Err("対象要素は DirectShape ではありません。FamilyInstance 等は別コマンドを使用してください。", "UNSUPPORTED_ELEMENT_KIND");

            // ---- 3) パラメータ名
            var paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName))
                return ResultUtil.Err("paramName を指定してください。", "MISSING_PARAM_NAME");

            var prm = elem.LookupParameter(paramName);
            if (prm == null)
                return ResultUtil.Err($"パラメータ '{paramName}' が見つかりません。", "PARAM_NOT_FOUND");

            if (prm.IsReadOnly)
                return ResultUtil.Err($"パラメータ '{paramName}' は読み取り専用です。", "PARAM_READONLY");

            // ---- 4) 値（JTokenそのまま UnitHelper に渡して安全セット）
            var valueToken = p["value"];
            if (valueToken == null)
                return ResultUtil.Err("value を指定してください。", "MISSING_VALUE");

            // オプション: レベルオフセット等の変更を Z 方向の移動としても反映する
            bool applyOffsetAsMove = p.Value<bool?>("applyOffsetAsMove") ?? false;
            double? deltaOffsetMm = null;
            if (applyOffsetAsMove && prm.StorageType == StorageType.Double)
            {
                try
                {
                    var spec = UnitHelper.GetSpec(prm);
                    if (spec != null && spec.Equals(SpecTypeId.Length))
                    {
                        double oldInternal = prm.AsDouble();
                        double oldMm = UnitUtils.ConvertFromInternalUnits(oldInternal, UnitTypeId.Millimeters);
                        double newMm = valueToken.Value<double>();
                        double diff = newMm - oldMm;
                        if (Math.Abs(diff) > 1e-6)
                            deltaOffsetMm = diff;
                    }
                }
                catch
                {
                    // 差分計算に失敗した場合は位置移動を行わず、従来通りパラメータ更新のみを行う
                    deltaOffsetMm = null;
                }
            }

            using (var tx = new Transaction(doc, "Update DirectShape Parameter"))
            {
                try
                {
                    tx.Start();

                    if (!UnitHelper.TrySetParameterFromSi(prm, valueToken, out string reason))
                    {
                        tx.RollBack();
                        return ResultUtil.Err(reason ?? "値の設定に失敗しました。", "SET_VALUE_FAILED");
                    }

                    // 必要に応じて、オフセット差分に応じて DirectShape 全体を Z 方向に移動
                    if (deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                    {
                        try
                        {
                            var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                            ElementTransformUtils.MoveElement(doc, elem.Id, offset);
                        }
                        catch (Exception exMove)
                        {
                            // 位置移動に失敗してもパラメータ更新自体は成功させる
                            RevitMCPAddin.Core.RevitLogger.Warn($"update_direct_shape_parameter: move by offset failed for element {elem.Id.IntegerValue}: {exMove.Message}");
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return ResultUtil.Err($"トランザクションで例外が発生しました: {ex.Message}", "TRANSACTION_FAILED");
                }
            }

            // ---- 5) 返却（human readable display を付与）
            string? display;
            try { display = prm.AsValueString(); } catch { display = null; }

            // SI 値（Double の場合のみ）も軽く返す（UI/AI補助用）
            object? valueSi = null;
            if (prm.StorageType == StorageType.Double)
            {
                try
                {
                    var spec = UnitHelper.GetSpec(prm);
                    var raw = prm.AsDouble(); // internal
                    valueSi = UnitHelper.ToExternal(raw, spec, 3);
                }
                catch { /* ignore */ }
            }
            else if (prm.StorageType == StorageType.Integer)
            {
                valueSi = prm.AsInteger();
            }
            else if (prm.StorageType == StorageType.String)
            {
                valueSi = prm.AsString();
            }
            else if (prm.StorageType == StorageType.ElementId)
            {
                valueSi = prm.AsElementId()?.IntegerValue ?? 0;
            }

            // メタ（入力/内部単位ラベル）
            var meta = new
            {
                inputUnits = UnitHelper.DefaultUnitsMeta(), // { Length=mm, ... }
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };

            return ResultUtil.Ok(new
            {
                elementId = elem.Id.IntegerValue,
                uniqueId = elem.UniqueId,
                paramName,
                value = valueSi,      // できる限り SI or 生の表示値（型に応じて）
                display = display,    // Revit の人間可読表記
                meta
            });
        }
    }
}
