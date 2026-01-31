// Commands/SystemOps/ShowBuildInfoCommand.cs
#nullable enable
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SystemOps
{
    [Transaction(TransactionMode.Manual)]
    public class ShowBuildInfoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var ver = BuildInfo.GetDisplayVersion();
                TaskDialog.Show("Revit MCP", $"Build: {ver}");
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
