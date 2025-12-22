// RevitMCPAddin/Commands/ViewOps/GetCurrentViewCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// 現在のアクティブビュー情報を返すコマンド
    /// </summary>
    public class GetCurrentViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_current_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var view = uiapp.ActiveUIDocument.ActiveView;
            return new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                uniqueId = view.UniqueId,
                name = view.Name,
                viewType = view.ViewType.ToString()
            };
        }
    }
}

