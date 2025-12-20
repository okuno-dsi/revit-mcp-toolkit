// RevitMCPAddin/Commands/ElementOps/Wall/DuplicateWallTypeCommand.cs
// UnitHelper化: エラー整形 & units 付加
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Linq;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class DuplicateWallTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_wall_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            WallType sourceType = null;
            if (p.TryGetValue("sourceTypeName", out var sn))
            {
                var name = sn.Value<string>();
                sourceType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                              .FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            else if (p.TryGetValue("sourceTypeId", out var sid))
            {
                sourceType = doc.GetElement(new ElementId(sid.Value<int>())) as WallType;
            }
            if (sourceType == null)
                return new { ok = false, msg = "Source WallType not found." };

            var newName = p.Value<string>("newName");
            if (string.IsNullOrWhiteSpace(newName))
                return new { ok = false, msg = "newName is required." };

            using var tx = new Transaction(doc, "Duplicate WallType");
            tx.Start();
            var dup = sourceType.Duplicate(newName) as WallType;
            if (dup == null)
            {
                tx.RollBack();
                return new { ok = false, msg = "Duplicate failed." };
            }
            tx.Commit();

            return new
            {
                ok = true,
                newTypeId = dup.Id.IntegerValue,
                newTypeName = dup.Name,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }
}
