// Commands/BridgeControl/StopBridgeCommand.cs
#nullable enable
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core.Net; // BridgeProcessManager

namespace RevitMCPAddin.Commands.BridgeControl
{
    [Transaction(TransactionMode.Manual)]
    public class StopBridgeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var (ok, msg) = BridgeProcessManager.Stop();
                TaskDialog.Show("MCP Bridge", ok ? $"Bridge Stopped.\n{msg}" : $"Failed?\n{msg}");
                return Result.Succeeded; // 停止済みでも成功扱い
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}
