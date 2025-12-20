// ======================================================================
// File: Commands/WorksetOps/WorksetCommands.cs
// 機能: ワークセット関連コマンド集（CreateWorksetCommand を 2 引数版に修正）
// ======================================================================
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.WorksetOps
{
    /// <summary>
    /// 全ワークセット一覧と各要素の割当情報を取得
    /// </summary>
    public class GetWorksetsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_worksets";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            if (!doc.IsWorkshared)
                return new { ok = false, msg = "Document is not workshared." };

            var worksets = new FilteredWorksetCollector(doc).ToWorksets();
            var list = worksets.Select(ws =>
            {
                var ids = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.WorksetId.IntegerValue == ws.Id.IntegerValue)
                    .Select(e => e.Id.IntegerValue)
                    .ToList();

                return new
                {
                    worksetId = ws.Id.IntegerValue,
                    name = ws.Name,
                    kind = ws.Kind.ToString(),
                    isEditable = ws.IsEditable,
                    elementCount = ids.Count,
                    elementIds = ids
                };
            }).ToList();

            return new { ok = true, worksets = list };
        }
    }

    /// <summary>
    /// 指定要素のワークセット情報を取得
    /// </summary>
    public class GetElementWorksetCommand : IRevitCommandHandler
    {
        public string CommandName => "get_element_workset";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int eid = ((JObject)cmd.Params).Value<int>("elementId");
            var elem = doc.GetElement(new ElementId(eid));
            if (elem == null)
                return new { ok = false, msg = $"Element {eid} not found." };

            var wsId = elem.WorksetId;
            var ws = new FilteredWorksetCollector(doc)
                        .ToWorksets()
                        .FirstOrDefault(w => w.Id.IntegerValue == wsId.IntegerValue);
            if (ws == null)
                return new { ok = false, msg = $"Workset {wsId.IntegerValue} not found." };

            return new
            {
                ok = true,
                worksetId = ws.Id.IntegerValue,
                worksetName = ws.Name
            };
        }
    }

    /// <summary>
    /// 新しいユーザーワークセットを作成（2 引数オーバーロードを使用）
    /// </summary>
    public class CreateWorksetCommand : IRevitCommandHandler
    {
        public string CommandName => "create_workset";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var name = ((JObject)cmd.Params).Value<string>("name");
            if (string.IsNullOrWhiteSpace(name))
                return new { ok = false, msg = "Workset name is required." };
            if (!doc.IsWorkshared)
                return new { ok = false, msg = "Document is not workshared." };

            Workset ws;
            using (var tx = new Transaction(doc, "Create Workset"))
            {
                tx.Start();
                // ← ここを 2 引数オーバーロードに修正
                ws = Workset.Create(doc, name);
                tx.Commit();
            }

            return new { ok = true, worksetId = ws.Id.IntegerValue };
        }
    }

    /// <summary>
    /// 要素を指定ワークセットへ割り当て
    /// </summary>
    public class SetElementWorksetCommand : IRevitCommandHandler
    {
        public string CommandName => "set_element_workset";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int eid = p.Value<int>("elementId");
            int wsid = p.Value<int>("worksetId");

            var elem = doc.GetElement(new ElementId(eid));
            if (elem == null)
                return new { ok = false, msg = $"Element {eid} not found." };
            if (!doc.IsWorkshared)
                return new { ok = false, msg = "Document is not workshared." };

            using (var tx = new Transaction(doc, "Set Element Workset"))
            {
                tx.Start();
                var param = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (param == null || param.IsReadOnly)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Cannot set workset on this element." };
                }
                param.Set(wsid);
                tx.Commit();
            }

            return new { ok = true };
        }
    }
}
