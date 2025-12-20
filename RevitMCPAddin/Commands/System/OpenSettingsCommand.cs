#nullable enable
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SystemOps
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class OpenSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            SettingsHelper.OpenInNotepad();
            TaskDialog.Show("Revit MCP", "settings.json を開きました。");
            return Result.Succeeded;
        }
    }
}

