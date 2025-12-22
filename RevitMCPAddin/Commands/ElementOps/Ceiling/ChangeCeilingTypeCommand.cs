using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Ceiling
{
    /// <summary>
    /// Change the type of an existing Ceiling element.
    /// </summary>
    public class ChangeCeilingTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_ceiling_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            // ドキュメント取得
            Document doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // 要素IDと新タイプIDを取得
            ElementId elemId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("elementId"));
            Element elem = doc.GetElement(elemId);
            ElementId newTypeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("newTypeId"));

            // 存在チェック
            if (elem == null)
                return new { ok = false, message = $"ElementId {elemId.IntValue()} が見つかりません。" };

            // トランザクションでタイプ変更
            using (var tx = new Transaction(doc, "Change Ceiling Type"))
            {
                tx.Start();
                elem.ChangeTypeId(newTypeId);
                tx.Commit();

                if (elem.GetTypeId().IntValue() != newTypeId.IntValue())
                    return new { ok = false, message = $"ElementId {elemId.IntValue()} のタイプ変更に失敗しました。" };
            }

            return new { ok = true };
        }
    }
}


