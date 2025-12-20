// ================================================================
// File: Commands/DatumOps/UpdateLevelParameterCommand.cs (UnitHelper統一版)
// - Name は直接 level.Name を変更
// - それ以外は UnitHelper.TrySetParameterByExternalValue に一元化
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DatumOps
{
    public class UpdateLevelParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_level_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int lid = p.Value<int>("levelId");
            var level = doc.GetElement(new ElementId(lid)) as Level
                        ?? throw new InvalidOperationException($"Level not found: {lid}");

            string paramName = p.Value<string>("paramName") ?? string.Empty;
            var valueToken = p["value"];

            using (var tx = new Transaction(doc, "Update Level Parameter"))
            {
                tx.Start();
                try
                {
                    // Level.Name の特例
                    if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                        paramName.Equals("名前", StringComparison.OrdinalIgnoreCase))
                    {
                        level.Name = (valueToken?.ToString() ?? string.Empty);
                        tx.Commit();
                        return new { ok = true, name = level.Name };
                    }

                    var param = ParamResolver.ResolveByPayload(level, p, out var resolvedBy);
                    if (param == null)
                    {
                        tx.RollBack();
                        return new { ok = false, message = $"Parameter not found (name/builtIn/guid) on level." };
                    }
                    if (param.IsReadOnly)
                    {
                        tx.RollBack();
                        return new { ok = false, message = $"Parameter '{param.Definition?.Name}' is read-only." };
                    }

                    if (!UnitHelper.TrySetParameterByExternalValue(param, valueToken?.ToObject<object>(), out var err))
                    {
                        tx.RollBack();
                        return new { ok = false, message = err ?? "Failed to set parameter value." };
                    }

                    tx.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, error = ex.Message };
                }
            }
        }
    }
}
