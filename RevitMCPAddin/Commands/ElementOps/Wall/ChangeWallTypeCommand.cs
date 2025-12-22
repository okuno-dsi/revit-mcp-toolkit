// RevitMCPAddin/Commands/ElementOps/Wall/ChangeWallTypeCommand.cs
// UnitHelper化: 例外時は {ok:false,msg}、成功時は input/internal units を付加
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class ChangeWallTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_wall_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("elementId", out var eidToken))
                return new { ok = false, msg = "Parameter 'elementId' is required." };

            int elementId = eidToken.Value<int>();
            var wall = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId)) as Autodesk.Revit.DB.Wall;
            if (wall == null)
                return new { ok = false, msg = $"Wall not found: {elementId}" };

            WallType newType = null;
            if (p.TryGetValue("typeId", out var tidToken))
                newType = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tidToken.Value<int>())) as WallType;
            else if (p.TryGetValue("typeName", out var tnameToken))
                newType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                           .FirstOrDefault(wt => string.Equals(wt.Name, tnameToken.Value<string>(), StringComparison.OrdinalIgnoreCase));
            if (newType == null)
                return new { ok = false, msg = "WallType not found by typeId or typeName." };

            try
            {
                using (var tx = new Transaction(doc, "Change Wall Type"))
                {
                    tx.Start();
                    wall.ChangeTypeId(newType.Id);
                    tx.Commit();
                }
                return new
                {
                    ok = true,
                    elementId,
                    newTypeId = newType.Id.IntValue(),
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}


