// ================================================================
// File: Commands/ElementOps/Foundation/UpdateStructuralFoundationGeometryCommand.cs (UnitHelper対応版)
// - offset: mm → ft
// - rotation: deg → rad（Z軸回り）
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class UpdateStructuralFoundationGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_foundation_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var eidToken))
                return ResultUtil.Err("Parameter 'elementId' is required.");
            int elementId = eidToken.Value<int>();
            var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            if (element == null) return ResultUtil.Err($"Element not found: {elementId}");

            var lp = element.Location as LocationPoint;
            var center = lp?.Point;
            if (center == null) return ResultUtil.Err("Element does not have a LocationPoint.");

            // offset(mm) → ft
            XYZ offset = null;
            if (p.TryGetValue("offset", out var offToken))
            {
                var o = (JObject)offToken;
                offset = new XYZ(
                    UnitHelper.ToInternalBySpec(o.Value<double>("x"), SpecTypeId.Length),
                    UnitHelper.ToInternalBySpec(o.Value<double>("y"), SpecTypeId.Length),
                    UnitHelper.ToInternalBySpec(o.Value<double>("z"), SpecTypeId.Length)
                );
            }

            // rotation(deg) → rad
            double? angleRad = null;
            if (p.TryGetValue("rotation", out var rotToken))
            {
                var deg = rotToken.Value<double>();
                angleRad = UnitHelper.ToInternalBySpec(deg, SpecTypeId.Angle);
            }

            using (var tx = new Transaction(doc, "Update Structural Foundation Geometry"))
            {
                tx.Start();

                if (offset != null)
                    ElementTransformUtils.MoveElement(doc, element.Id, offset);

                if (angleRad.HasValue)
                {
                    var axis = Line.CreateBound(center, center + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(doc, element.Id, axis, angleRad.Value);
                }

                tx.Commit();
            }

            return ResultUtil.Ok(new
            {
                elementId = element.Id.IntValue(),
                uniqueId = element.UniqueId,
                inputUnits = new { Length = "mm", Angle = "deg" },
                internalUnits = new { Length = "ft", Angle = "rad" }
            });
        }
    }
}


