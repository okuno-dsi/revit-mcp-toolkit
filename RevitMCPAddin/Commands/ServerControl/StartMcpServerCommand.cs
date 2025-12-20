// Commands/ServerControl/StartMcpServerCommand.cs
#nullable enable
using System;
using System.Diagnostics;                 
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core.Net;             // ServerProcessManager
using RevitMCPAddin.UI;                   // RibbonPortUi

namespace RevitMCPAddin.Commands.ServerControl
{
    [Transaction(TransactionMode.Manual)]
    public class StartMcpServerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                // Revit 側の PID を ownerPid として渡す
                int ownerPid = Process.GetCurrentProcess().Id;
                var (ok, port, msg) = ServerProcessManager.StartOrAttach(ownerPid);

                // リボン表示のポートを更新
                RibbonPortUi.UpdatePort(data.Application, port);

                TaskDialog.Show("MCP Server", ok
                    ? $"Server Started/Attached.\nPort: {port}\n{msg}"
                    : $"Failed to start server.\n{msg}");

                return ok ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

