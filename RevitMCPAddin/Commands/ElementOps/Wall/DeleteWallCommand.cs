// RevitMCPAddin/Commands/ElementOps/Wall/DeleteWallCommand.cs
// UnitHelper化: レスポンスに units メタ追加
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class DeleteWallCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_wall";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            int elementId = p.Value<int>("elementId");
            var id = Autodesk.Revit.DB.ElementIdCompat.From(elementId);

            var wall = doc.GetElement(id) as Autodesk.Revit.DB.Wall;
            if (wall == null)
            {
                return new { ok = false, msg = $"Wall with ElementId {elementId} not found." };
            }

            using var tx = new Transaction(doc, "Delete Wall");
            tx.Start();
            try
            {
                var deleted = doc.Delete(id);
                if (!deleted.Contains(id))
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Failed to delete Wall {elementId}." };
                }

                tx.Commit();
                return new { ok = true, inputUnits = UnitHelper.InputUnitsMeta(), internalUnits = UnitHelper.InternalUnitsMeta() };
            }
            catch (Exception ex)
            {
                tx.RollBack();
                return new { ok = false, msg = $"Error deleting Wall {elementId}: {ex.Message}" };
            }
        }
    }
}

