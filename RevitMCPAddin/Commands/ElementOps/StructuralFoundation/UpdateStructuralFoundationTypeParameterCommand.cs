// ================================================================
// File: Commands/ElementOps/Foundation/UpdateStructuralFoundationTypeParameterCommand.cs (UnitHelper対応版)
// - Double は spec 依存で mm/m2/m3/deg → 内部値に変換
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class UpdateStructuralFoundationTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_foundation_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("typeId", out var tidToken))
                return ResultUtil.Err("Parameter 'typeId' is required.");
            int typeId = tidToken.Value<int>();

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return ResultUtil.Err("paramName or builtInName/builtInId/guid is required.");

            if (!p.TryGetValue("value", out var valueToken))
                return ResultUtil.Err("Parameter 'value' is required.");

            var typeElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as ElementType;
            if (typeElem == null) return ResultUtil.Err($"TypeElement not found: {typeId}");

            var param = ParamResolver.ResolveByPayload(typeElem, p, out var resolvedBy);
            if (param == null) return ResultUtil.Err($"Parameter not found (name/builtIn/guid)");
            if (param.IsReadOnly) return ResultUtil.Err($"Parameter '{param.Definition?.Name}' は読み取り専用です");

            bool success = false;
            using (var tx = new Transaction(doc, $"Set Type Param {param.Definition?.Name ?? "(unknown)"}"))
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
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return ResultUtil.Err($"Failed to set parameter '{paramName}': {ex.Message}");
                }

                if (success) tx.Commit(); else tx.RollBack();
            }

            if (!success) return ResultUtil.Err($"Failed to set parameter '{paramName}'.");
            return ResultUtil.Ok(new { typeId = typeElem.Id.IntValue(), uniqueId = typeElem.UniqueId });
        }
    }
}


