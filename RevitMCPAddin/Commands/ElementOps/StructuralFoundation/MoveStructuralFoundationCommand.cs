// ================================================================
// File: Commands/ElementOps/Foundation/MoveStructuralFoundationCommand.cs (UnitHelper対応版)
// Revit 2023 / .NET Framework 4.8 / C# 8
// ================================================================
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    public class MoveStructuralFoundationCommand : IRevitCommandHandler
    {
        public string CommandName => "move_structural_foundation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = (JObject)cmd.Params;

            // 対象: elementId / uniqueId
            Element e = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) e = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) e = doc.GetElement(uid);
            if (e == null) return ResultUtil.Err("要素が見つかりません（elementId/uniqueId）。");

            // offset(mm)
            var off = p["offset"] as JObject;
            if (off == null) return ResultUtil.Err("offset {x,y,z} が必要です。");
            var offset = new XYZ(
                UnitHelper.ToInternalBySpec(off.Value<double>("x"), SpecTypeId.Length),
                UnitHelper.ToInternalBySpec(off.Value<double>("y"), SpecTypeId.Length),
                UnitHelper.ToInternalBySpec(off.Value<double>("z"), SpecTypeId.Length)
            );

            using (var tx = new Transaction(doc, "Move Structural Foundation"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, e.Id, offset);
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = e.Id.IntegerValue,
                uniqueId = e.UniqueId,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }
}
