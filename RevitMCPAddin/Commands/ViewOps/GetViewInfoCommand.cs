using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;      // IRevitCommandHandler の定義を参照 

namespace RevitMCPAddin.Commands.ViewOps
{
    public class GetViewInfoCommand : IRevitCommandHandler
    {
        // RevitMcpWorker で登録するコマンド名
        public string CommandName => "get_view_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            int viewId = p.Value<int>("viewId");
            var doc = uiapp.ActiveUIDocument.Document;
            var viewEl = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            if (viewEl == null)
            {
                return new { ok = false, msg = $"ビュー {viewId} が見つかりません" };
            }

            return new
            {
                ok = true,
                viewId = viewId,
                viewName = viewEl.Name,
                viewType = viewEl.ViewType.ToString()
            };
        }
    }
}

