using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    /// <summary>
    /// ドキュメント末尾に新しい Revision を作成し、その ID を返します
    /// </summary>
    public class CreateDefaultRevisionCommand : IRevitCommandHandler
    {
        // JSON-RPC の method 名
        public string CommandName => "create_default_revision";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            // トランザクション開始
            using (var tx = new Transaction(doc, "Create Default Revision"))
            {
                tx.Start();

                // ドキュメントの末尾に新規 Revision を追加
                Autodesk.Revit.DB.Revision rev = Autodesk.Revit.DB.Revision.Create(doc);  // ← API 呼び出し :contentReference[oaicite:0]{index=0}

                tx.Commit();

                // 作成した Revision の ElementId を返却
                return new
                {
                    ok = true,
                    revisionId = rev.Id.IntegerValue
                };
            }
        }
    }
}
