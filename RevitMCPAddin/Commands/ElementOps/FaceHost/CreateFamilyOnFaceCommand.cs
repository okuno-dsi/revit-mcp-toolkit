using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FaceHost
{
    public class CreateFamilyOnFaceCommand : IRevitCommandHandler
    {
        public string CommandName => "create_family_on_face";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // パラメータ取得
            int elemId = p.Value<int>("elementId");
            int faceIndex = p.Value<int>("faceIndex");
            var ptJson = p["insertionPoint"].ToObject<RevitMCP.Abstractions.Models.Point3D>();
            int symbolId = p.Value<int>("familySymbolId");

            // 要素／シンボル取得
            var elem = doc.GetElement(new ElementId(elemId));
            var symbol = doc.GetElement(new ElementId(symbolId)) as FamilySymbol;
            if (elem == null || symbol == null)
                return new { ok = false, msg = "要素またはファミリタイプが見つかりません" };

            // フェイス取得
            var faces = FaceHostHelper.GetPlanarFaces(elem);
            if (faceIndex < 0 || faceIndex >= faces.Count)
                return new { ok = false, msg = "faceIndex が範囲外です" };

            // 挿入点
            var pt = new XYZ(ptJson.X, ptJson.Y, ptJson.Z);

            // トランザクション内でインスタンス生成
            using (var tx = new Transaction(doc, "Host Family on Face"))
            {
                tx.Start();
                if (!symbol.IsActive)
                    symbol.Activate();
                var inst = FaceHostHelper.CreateOnFace(doc, faces[faceIndex], pt, symbol);
                tx.Commit();
                return new { ok = true, elementId = inst.Id.IntegerValue };
            }
        }
    }
}
