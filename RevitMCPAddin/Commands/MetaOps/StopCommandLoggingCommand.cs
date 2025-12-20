// ================================================================
// File: Commands/MetaOps/StopCommandLoggingCommand.cs
// Purpose: コマンドログの記録停止（ファイルは残す）
// ================================================================
#nullable enable
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    public class StopCommandLoggingCommand : IRevitCommandHandler
    {
        public string CommandName => "stop_command_logging";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var wasEnabled = CommandLogWriter.Enabled;
            CommandLogWriter.Stop();
            RevitLogger.Info("Command logging stopped.");
            return new
            {
                ok = true,
                wasEnabled,
                msg = "Command logging stopped."
            };
        }
    }
}
