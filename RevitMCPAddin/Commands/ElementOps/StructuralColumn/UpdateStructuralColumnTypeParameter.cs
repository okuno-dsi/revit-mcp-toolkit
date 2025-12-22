using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.StructuralColumn
{
    /// <summary>構造柱タイプのパラメータ更新（SI入力→内部に自動変換）</summary>
    public class UpdateStructuralColumnTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_column_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("typeId", out var tidToken))
                throw new InvalidOperationException("Parameter 'typeId' is required.");
            var type = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tidToken.Value<int>())) as FamilySymbol
                       ?? throw new InvalidOperationException($"Structural column type not found: {tidToken.Value<int>()}");

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, message = "paramName or builtInName/builtInId/guid is required." };
            var prm = ParamResolver.ResolveByPayload(type, p, out var resolvedBy)
                      ?? throw new InvalidOperationException($"Parameter not found on type (name/builtIn/guid)");
            if (prm.IsReadOnly) return new { ok = false, message = $"Parameter '{paramName}' is read-only." };

            if (!p.TryGetValue("value", out var valToken))
                throw new InvalidOperationException("Parameter 'value' is required.");

            using (var tx = new Transaction(doc, $"Set Type Param {prm.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                string err;
                var ok = UnitHelper.TrySetParameterByExternalValue(prm, (valToken as JValue)?.Value, out err);
                if (!ok) { tx.RollBack(); return new { ok = false, message = err ?? "Failed to set parameter." }; }
                tx.Commit();
            }
            return new { ok = true };
        }
    }
}

