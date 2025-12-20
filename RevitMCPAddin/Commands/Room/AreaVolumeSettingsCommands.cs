// ================================================================
// File: Commands/Room/AreaVolumeSettingsCommands.cs
// Revit 2023 / .NET Framework 4.8
// Purpose: Area/Volume 計算設定の参照・更新（ComputeVolumes）
// Notes  : 体積系の値を扱う処理の前に、この設定を確認できるようにする
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class GetAreaVolumeSettingsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_volume_settings";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            try
            {
                var avs = AreaVolumeSettings.GetAreaVolumeSettings(doc);
                var compute = avs.ComputeVolumes;
                return ResultUtil.Ok(new
                {
                    computeVolumes = compute,
                    hint = compute
                        ? "体積計算は有効です。体積/重心の値は信頼できます。"
                        : "体積計算が無効です。体積/重心の値は正確でない可能性があります（有効化を検討してください）。",
                    units = UnitHelper.DefaultUnitsMeta()
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"設定の取得に失敗: {ex.Message}");
            }
        }
    }

    public class SetAreaVolumeSettingsCommand : IRevitCommandHandler
    {
        public string CommandName => "set_area_volume_settings";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)(cmd.Params ?? new JObject());
            bool? desired = p.Value<bool?>("computeVolumes");
            if (desired == null) return ResultUtil.Err("computeVolumes (true/false) を指定してください。");

            try
            {
                var avs = AreaVolumeSettings.GetAreaVolumeSettings(doc);
                var before = avs.ComputeVolumes;

                if (before == desired.Value)
                {
                    return ResultUtil.Ok(new
                    {
                        changed = false,
                        computeVolumes = before,
                        msg = "設定は既に希望どおりです。",
                        units = UnitHelper.DefaultUnitsMeta()
                    });
                }

                using var tx = new Transaction(doc, "Set Area/Volume Settings");
                tx.Start();
                avs.ComputeVolumes = desired.Value;
                tx.Commit();

                return ResultUtil.Ok(new
                {
                    changed = true,
                    before = before,
                    after = desired.Value,
                    computeVolumes = desired.Value,
                    units = UnitHelper.DefaultUnitsMeta()
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"設定の更新に失敗: {ex.Message}");
            }
        }
    }
}
