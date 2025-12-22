// ================================================================
// File: Commands/Room/DeleteRoomCommand.cs  (UnitHelper対応不要)
// Revit 2023 / .NET Framework 4.8
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitRoom = Autodesk.Revit.DB.Architecture.Room;

namespace RevitMCPAddin.Commands.Room
{
    public class DeleteRoomCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_room";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int elementIdInt = p.Value<int>("elementId");
            var elemId = Autodesk.Revit.DB.ElementIdCompat.From(elementIdInt);

            var room = doc.GetElement(elemId) as RevitRoom;
            if (room == null)
                return new { ok = false, message = $"Room with ElementId {elementIdInt} not found." };

            using (var tx = new Transaction(doc, "Delete Room"))
            {
                tx.Start();
                try
                {
                    var deleted = doc.Delete(elemId);
                    tx.Commit();
                    bool success = deleted.Contains(elemId);
                    return new { ok = success, message = success ? null : $"Failed to delete Room {elementIdInt}." };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, message = $"Error deleting Room {elementIdInt}: {ex.Message}" };
                }
            }
        }
    }
}
