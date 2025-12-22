// File: RevitMCPAddin/Commands/ElementOps/Mass/MoveMassInstanceCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using static Autodesk.Revit.DB.UnitUtils;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    public class MoveMassInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "move_mass_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // elementId の取得と検証
            if (!p.TryGetValue("elementId", out var elemTok))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            int elementId = elemTok.Value<int>();
            var element = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            if (element == null)
                return new { ok = false, message = $"Element not found: {elementId}" };

            // オフセットの取得と検証
            if (!p.TryGetValue("dx", out var dxTok) ||
                !p.TryGetValue("dy", out var dyTok) ||
                !p.TryGetValue("dz", out var dzTok))
            {
                throw new InvalidOperationException("Parameters 'dx', 'dy', and 'dz' are all required.");
            }
            double dx = ConvertToInternalUnits(dxTok.Value<double>(), UnitTypeId.Millimeters);
            double dy = ConvertToInternalUnits(dyTok.Value<double>(), UnitTypeId.Millimeters);
            double dz = ConvertToInternalUnits(dzTok.Value<double>(), UnitTypeId.Millimeters);

            // トランザクションで移動
            using var tx = new Transaction(doc, "Move Mass Element");
            tx.Start();
            ElementTransformUtils.MoveElement(doc, element.Id, new XYZ(dx, dy, dz));
            tx.Commit();

            return new { ok = true };
        }
    }
}

