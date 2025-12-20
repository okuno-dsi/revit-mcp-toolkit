// ================================================================
// File: Commands/Space/UpdateSpaceCommand.cs
// (UnitHelper完全統一 + DataType対応 / Revit 2023 / .NET 4.8 / C# 8)
// 変更点:
//  - Definition.GetDataTypeId() → Definition.GetDataType() に修正
//  - 三項演算子で匿名型の形が異なる問題を if/else に変更
//  - エラー時は ResultUtil.Err(...) を極力使用（統一的レスポンス）
//  - Double の単位変換は DataType(SpecTypeId) に基づき実施
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using SpaceElem = Autodesk.Revit.DB.Mechanical.Space;

namespace RevitMCPAddin.Commands.Space
{
    public class UpdateSpaceCommand : IRevitCommandHandler
    {
        public string CommandName => "update_space";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("elementId", out var eidToken))
                return ResultUtil.Err("Parameter 'elementId' is required.");
            int elementId = eidToken.Value<int>();

            var space = doc.GetElement(new ElementId(elementId)) as SpaceElem;
            if (space == null) return ResultUtil.Err($"Space not found: {elementId}");

            if (!p.TryGetValue("paramName", out var pnameToken))
                return ResultUtil.Err("Parameter 'paramName' is required.");
            string paramName = pnameToken.Value<string>();

            var param = space.LookupParameter(paramName);
            if (param == null) return ResultUtil.Err($"Parameter '{paramName}' not found.");
            if (param.IsReadOnly) return ResultUtil.Err($"Parameter '{paramName}' is read-only.");

            if (!p.TryGetValue("value", out var valToken))
                return ResultUtil.Err("Parameter 'value' is required.");

            object setValue;

            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        setValue = valToken.Type == JTokenType.Null ? string.Empty : valToken.Value<string>();
                        break;

                    case StorageType.Integer:
                        // 整数はそのまま（Yes/No, マスク値含む）
                        setValue = valToken.Value<int>();
                        break;

                    case StorageType.ElementId:
                        setValue = new ElementId(valToken.Value<int>());
                        break;

                    case StorageType.Double:
                        {
                            // 入力値は外部値（既定: SI想定）。DataType を見て内部単位(ft/rad等)に変換。
                            // Revit 2023: Definition.GetDataType() が正しい（GetDataTypeId は存在しない）
                            ForgeTypeId dt = param.Definition?.GetDataType();

                            // 入力の外部値（例: mm, m2, m3, deg）
                            double ext = valToken.Value<double>();

                            if (dt == SpecTypeId.Length)
                            {
                                setValue = UnitHelper.MmToInternal(ext, doc); // mm → ft
                            }
                            else if (dt == SpecTypeId.Area)
                            {
                                setValue = UnitHelper.SqmToInternal(ext); // m2 → ft2
                            }
                            else if (dt == SpecTypeId.Volume)
                            {
                                setValue = UnitHelper.CubicMetersToInternal(ext); // m3 → ft3
                            }
                            else if (dt == SpecTypeId.Angle)
                            {
                                setValue = UnitHelper.DegToInternal(ext); // deg → rad
                            }
                            else
                            {
                                // DataType が分からない／未対応 → 推測を拒否
                                return ResultUtil.Err(new
                                {
                                    msg = $"Unsupported or unknown unit for '{paramName}'.",
                                    hint = "This parameter's data type is not Length/Area/Volume/Angle; refusing to guess."
                                });
                            }
                            break;
                        }

                    default:
                        return ResultUtil.Err($"Unsupported storage type: {param.StorageType}");
                }
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"Value parsing error: {ex.Message}");
            }

            bool success = false;
            using (var tx = new Transaction(doc, $"Update Space Param '{paramName}'"))
            {
                tx.Start();
                switch (param.StorageType)
                {
                    case StorageType.Double:
                        success = param.Set((double)setValue);
                        break;
                    case StorageType.Integer:
                        success = param.Set((int)setValue);
                        break;
                    case StorageType.String:
                        success = param.Set((string)setValue);
                        break;
                    case StorageType.ElementId:
                        success = param.Set((ElementId)setValue);
                        break;
                }
                tx.Commit();
            }

            if (success)
            {
                return new { ok = true };
            }
            else
            {
                return ResultUtil.Err("Failed to set parameter value.");
            }
        }
    }
}
