// File: RevitMCPAddin/Commands/ElementOps/Mass/ChangeMassInstanceTypeCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;

namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    public class ChangeMassInstanceTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_mass_instance_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            if (!p.TryGetValue("elementId", out var elemTok))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            var elemId = new ElementId(elemTok.Value<int>());
            var element = doc.GetElement(elemId);

            if (!(element is FamilyInstance inst))
                return new { ok = false, message = "Element is not a Mass FamilyInstance. Type change not supported." };

            if (!p.TryGetValue("newTypeId", out var typeTok))
                throw new InvalidOperationException("Parameter 'newTypeId' is required.");
            var newType = new ElementId(typeTok.Value<int>());

            using var tx = new Transaction(doc, "Change Mass Type");
            tx.Start();
            inst.ChangeTypeId(newType);
            tx.Commit();

            return new { ok = true };
        }
    }
}
