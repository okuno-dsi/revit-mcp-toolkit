// Commands/BridgeControl/StartBridgeCommand.cs
#nullable enable
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core.Net; // BridgeProcessManager

namespace RevitMCPAddin.Commands.BridgeControl
{
    [Transaction(TransactionMode.Manual)]
    public class StartBridgeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var (ok, msg) = BridgeProcessManager.Start();
                TaskDialog.Show("MCP Bridge", ok ? $"Bridge Started.\n{msg}" : $"Failed.\n{msg}");
                return ok ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
