// ================================================================
// File: Commands/Room/SetRoomParamCommand.cs  (UnitHelper)
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Room
{
    public class SetRoomParamCommand : IRevitCommandHandler
    {
        public string CommandName => "set_room_param";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("elementId", out var eidToken))
                throw new InvalidOperationException("Parameter 'elementId' is required.");
            int elementId = eidToken.Value<int>();

            if (!p.TryGetValue("paramName", out var pnameToken))
                throw new InvalidOperationException("Parameter 'paramName' is required.");
            string paramName = (pnameToken.Value<string>() ?? string.Empty).Trim();

            if (!p.TryGetValue("value", out var valToken))
                throw new InvalidOperationException("Parameter 'value' is required.");

            var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId)) as Autodesk.Revit.DB.Architecture.Room;
            if (room == null) return new { ok = false, message = $"Room not found: {elementId}" };

            using (var tx = new Transaction(doc, $"Set Room Param {paramName}"))
            {
                tx.Start();
                try
                {
                    // : OύXiparam̃[JCY/pꗼΉj
                    if (paramName.Equals("O", StringComparison.OrdinalIgnoreCase) ||
                        paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        room.Name = valToken.Value<string>() ?? string.Empty;
                    }
                    else
                    {
                        var param = room.LookupParameter(paramName);
                        if (param == null)
                            return new { ok = false, message = $"Parameter '{paramName}' not found on the room." };
                        if (param.IsReadOnly)
                            return new { ok = false, message = $"Parameter '{paramName}' is read-only." };

                        if (!UnitHelper.TrySetParameterByExternalValue(param, valToken.ToObject<object>(), out var err))
                            return new { ok = false, message = err ?? "Failed to set parameter value." };
                    }

                    tx.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, message = $"Failed to set parameter: {ex.Message}" };
                }
            }
        }
    }
}

