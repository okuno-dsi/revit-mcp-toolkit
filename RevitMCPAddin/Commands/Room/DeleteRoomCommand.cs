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
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, code = "NO_ACTIVE_DOC", msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            int elementIdInt = p.Value<int?>("elementId") ?? 0;
            if (elementIdInt <= 0) return new { ok = false, code = "INVALID_INPUT", msg = "elementId が必要です。" };

            var elemId = Autodesk.Revit.DB.ElementIdCompat.From(elementIdInt);

            var room = doc.GetElement(elemId) as RevitRoom;
            if (room == null)
                return new { ok = false, code = "NOT_FOUND", msg = $"Room with ElementId {elementIdInt} not found." };

            using (var tx = new Transaction(doc, "Delete Room"))
            {
                tx.Start();
                try
                {
                    // Avoid modal warning dialogs during automation.
                    try { TxnUtil.ConfigureProceedWithWarnings(tx); } catch { }

                    var deleted = doc.Delete(elemId);
                    var txStatus = tx.Commit();
                    if (txStatus != TransactionStatus.Committed)
                    {
                        return new
                        {
                            ok = false,
                            code = "TX_NOT_COMMITTED",
                            msg = "Transaction did not commit.",
                            detail = new { transactionStatus = txStatus.ToString() }
                        };
                    }

                    bool success = deleted.Contains(elemId);
                    return new
                    {
                        ok = success,
                        code = success ? "OK" : "DELETE_FAILED",
                        msg = success ? "OK" : $"Failed to delete Room {elementIdInt}."
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, code = "EXCEPTION", msg = $"Error deleting Room {elementIdInt}: {ex.Message}" };
                }
            }
        }
    }
}
