// File: RevitMCPAddin/Commands/ElementOps/Mass/RotateMassInstanceCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    public class RotateMassInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "rotate_mass_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var elemTok))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            int elementId = elemTok.Value<int>();
            var element = doc.GetElement(new ElementId(elementId));
            if (element == null)
                return new { ok = false, message = $"Element not found: {elementId}" };

            if (!(element is FamilyInstance) && !(element is DirectShape))
                return new { ok = false, message = "Element is not rotatable Mass (FamilyInstance or DirectShape only)." };

            if (p["axisPoint"] == null || p["axisDirection"] == null)
                throw new InvalidOperationException("Parameters 'axisPoint' and 'axisDirection' are required.");

            var ap = p["axisPoint"];
            var ad = p["axisDirection"];
            if (!ad.HasValues || !ap.HasValues)
                throw new InvalidOperationException("Invalid 'axisPoint' or 'axisDirection'.");

            // mm → ft
            var pt = new XYZ(
                UnitHelper.MmToFt(ap.Value<double>("x")),
                UnitHelper.MmToFt(ap.Value<double>("y")),
                UnitHelper.MmToFt(ap.Value<double>("z"))
            );

            var dir = new XYZ(
                ad.Value<double>("x"),
                ad.Value<double>("y"),
                ad.Value<double>("z")
            );
            if (dir.IsZeroLength())
                throw new InvalidOperationException("Axis direction vector cannot be zero.");
            dir = dir.Normalize();

            if (!p.TryGetValue("angle", out var angTok))
                throw new InvalidOperationException("Parameter 'angle' is required.");

            // deg → rad
            double angleRad = UnitHelper.DegToRad(angTok.Value<double>());

            var axisLine = Line.CreateBound(pt, pt + dir);
            using var tx = new Transaction(doc, "Rotate Mass Element");
            tx.Start();
            try
            {
                ElementTransformUtils.RotateElement(doc, element.Id, axisLine, angleRad);
                tx.Commit();
                return new { ok = true };
            }
            catch (Exception ex)
            {
                tx.RollBack();
                return new { ok = false, message = $"Rotate failed: {ex.Message}" };
            }
        }
    }
}
