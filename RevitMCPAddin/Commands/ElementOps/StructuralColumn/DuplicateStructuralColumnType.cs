using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.StructuralColumn
{
    /// <summary>
    /// Duplicates an existing structural column FamilySymbol under a new name.
    /// </summary>
    public class DuplicateStructuralColumnTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_structural_column_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // 既存タイプの取得（typeId または typeName）
            FamilySymbol original = null;
            if (p.TryGetValue("typeId", out var tid))
            {
                original = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>())) as FamilySymbol;
            }
            else if (p.TryGetValue("typeName", out var tn))
            {
                original = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(s => s.Family.Name == tn.Value<string>() || s.Name == tn.Value<string>());
            }

            if (original == null)
                throw new System.InvalidOperationException("Original structural column type not found.");

            // 新タイプ名
            if (!p.TryGetValue("newTypeName", out var ntn) || string.IsNullOrWhiteSpace(ntn.Value<string>()))
                throw new System.InvalidOperationException("New type name must be provided.");

            string newName = ntn.Value<string>();

            using var tx = new Transaction(doc, "Duplicate Structural Column Type");
            tx.Start();

            // 複製
            FamilySymbol duplicated = original.Duplicate(newName) as FamilySymbol;
            if (duplicated == null)
                throw new System.InvalidOperationException("Failed to duplicate structural column type.");

            tx.Commit();

            return new
            {
                ok = true,
                originalId = original.Id.IntValue(),
                newTypeId = duplicated.Id.IntValue(),
                newTypeName = duplicated.Name
            };
        }
    }
}


