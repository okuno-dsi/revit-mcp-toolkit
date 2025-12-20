#nullable enable
using System.Net.Http;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SystemOps
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReloadServerSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var port = PortLocator.GetCurrentPortOrDefault(5210);
            var url = $"http://127.0.0.1:{port}/rpc/reload_config";

            try
            {
                using (var hc = new HttpClient())
                {
                    var resp = hc.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                    var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    TaskDialog.Show("Revit MCP", $"Reload Settings: {(int)resp.StatusCode}\n{text}");
                }
                return Result.Succeeded;
            }
            catch (global::System.Exception ex) // ★ 念のため global:: を付けておくと安心
            {
                TaskDialog.Show("Revit MCP", "設定再読込に失敗しました。\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}

