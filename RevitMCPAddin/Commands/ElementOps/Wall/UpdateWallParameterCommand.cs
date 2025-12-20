using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class UpdateWallParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_wall_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var eidToken))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            int elementId = eidToken.Value<int>();

            var wall = doc.GetElement(new ElementId(elementId)) as Autodesk.Revit.DB.Wall;
            if (wall == null) return new { ok = false, msg = $"Wall not found: {elementId}" };

            // Resolve parameter by builtInId/builtInName/guid/name (fallback)
            var param = ParamResolver.ResolveByPayload(wall, p, out var resolvedBy);
            if (param == null)
            {
                string paramName = p.Value<string>("paramName") ?? p.Value<string>("builtInName") ?? p.Value<string>("guid") ?? "";
                return new { ok = false, msg = $"Parameter not found (name/builtIn/guid): {paramName}" };
            }

            if (!p.TryGetValue("value", out var valToken))
                throw new InvalidOperationException("Parameter 'value' is required.");

            // オプション: レベルオフセットなどの変更を Z 方向の移動としても反映する
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
                    deltaOffsetMm = null;
                }
            }

            using (var tx = new Transaction(doc, $"Set Wall Param {param.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                if (!UnitHelper.TrySetParameterFromSi(param, valToken, out var reason))
                {
                    tx.RollBack();
                    return new { ok = false, msg = reason ?? "Failed to set parameter." };
                }

                if (deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                {
                    try
                    {
                        var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                        ElementTransformUtils.MoveElement(doc, wall.Id, offset);
                    }
                    catch (Exception ex)
                    {
                        RevitMCPAddin.Core.RevitLogger.Warn($"update_wall_parameter: move by offset failed for element {wall.Id.IntegerValue}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId,
                resolvedBy,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}
