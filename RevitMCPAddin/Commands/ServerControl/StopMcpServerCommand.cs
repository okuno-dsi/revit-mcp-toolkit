// Commands/ServerControl/StopMcpServerCommand.cs
#nullable enable
using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core.Net;             // ServerProcessManager
using RevitMCPAddin.UI;                   // PortSettings
using System.Net.Http;
using System.Text;

namespace RevitMCPAddin.Commands.ServerControl
{
    [Transaction(TransactionMode.Manual)]
    public class StopMcpServerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                int ownerPid = Process.GetCurrentProcess().Id;
                int port = PortSettings.GetPort();

                var (ok, msg) = ServerProcessManager.StopByLock(ownerPid, port);

                bool notOwner = ok && (msg?.StartsWith("skip stop:", StringComparison.OrdinalIgnoreCase) ?? false);
                bool noLock = ok && (msg?.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0);

                if (notOwner)
                {
                    var td = new TaskDialog("MCP Server");
                    td.MainInstruction = "Server stop requires force";
                    td.MainContent = $"Port: {port}\n{msg}\n\nForce stop this port's server?";
                    td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    td.DefaultButton = TaskDialogResult.No;
                    var ans = td.Show();
                    if (ans == TaskDialogResult.Yes)
                    {
                        var (fok, fmsg) = ServerProcessManager.ForceStopByPort(port);
                        TaskDialog.Show("MCP Server", fok
                            ? $"Force stop OK.\nPort: {port}\n{fmsg}"
                            : $"Force stop failed.\nPort: {port}\n{fmsg}");
                        return fok ? Result.Succeeded : Result.Failed;
                    }
                    else
                    {
                        TaskDialog.Show("MCP Server", $"Cancelled.\nPort: {port}\n{msg}");
                        return Result.Cancelled;
                    }
                }

                // When lock is missing or stopByLock failed, try graceful HTTP shutdown
                if (!ok || noLock)
                {
                    try
                    {
                        using (var hc = new HttpClient())
                        {
                            hc.Timeout = TimeSpan.FromMilliseconds(800);
                            var url = $"http://127.0.0.1:{port}/shutdown";
                            var resp = hc.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                            var txt = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            if ((int)resp.StatusCode < 300)
                            {
                                TaskDialog.Show("MCP Server", $"Shutdown requested.\nPort: {port}\n{txt}");
                                return Result.Succeeded;
                            }
                            else
                            {
                                TaskDialog.Show("MCP Server", $"Shutdown HTTP failed ({(int)resp.StatusCode}).\nPort: {port}\n{txt}");
                                return Result.Failed;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("MCP Server", $"Shutdown attempt failed.\nPort: {port}\n{ex.Message}\nOriginal: {msg}");
                        return Result.Failed;
                    }
                }

                TaskDialog.Show("MCP Server", $"Server Stopped.\nPort: {port}\n{msg}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
