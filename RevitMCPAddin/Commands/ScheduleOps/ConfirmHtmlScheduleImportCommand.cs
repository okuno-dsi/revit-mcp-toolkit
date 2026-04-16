#nullable enable
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Failures;

namespace RevitMCPAddin.Commands.ScheduleOps
{
    internal sealed class ConfirmHtmlScheduleImportCommand : IRevitCommandHandler
    {
        public string CommandName => "confirm_html_schedule_import";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params ?? new JObject();
            var scheduleName = (p.Value<string>("scheduleName") ?? string.Empty).Trim();
            var uploadedFileName = (p.Value<string>("uploadedFileName") ?? string.Empty).Trim();
            var requestedBy = (p.Value<string>("requestedBy") ?? string.Empty).Trim();
            var changedCellCount = p.Value<int?>("changedCellCount") ?? 0;
            var docTitle = (p.Value<string>("docTitle") ?? string.Empty).Trim();

            var title = "HTML Import Approval";
            var instruction = string.IsNullOrWhiteSpace(scheduleName)
                ? "HTML 経由の集計表変更リクエストを許可しますか？"
                : $"集計表「{scheduleName}」のHTML経由変更リクエストを許可しますか？";
            var content =
                $"Document: {docTitle}\n" +
                $"Requested by: {(string.IsNullOrWhiteSpace(requestedBy) ? "(unknown)" : requestedBy)}\n" +
                $"Workbook: {uploadedFileName}\n" +
                $"Changed cells (preview): {changedCellCount}\n\n" +
                "「許可」で反映を実行します。\n" +
                "「拒否」で今回は実行しません。必要なら HTML 側でキューに入れて後で再確認できます。";

            var td = new TaskDialog(title)
            {
                MainInstruction = instruction,
                MainContent = content,
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
                TitleAutoPrefix = false
            };

            TaskDialogResult result;
            using (FailureHandlingContext.PushDialogDismissOverride(dismissDialogs: false))
            {
                result = td.Show();
            }
            var decision = result == TaskDialogResult.Yes ? "approved" : "rejected";
            return new
            {
                ok = true,
                decision,
                scheduleName,
                requestedBy,
                uploadedFileName,
                changedCellCount
            };
        }
    }
}
