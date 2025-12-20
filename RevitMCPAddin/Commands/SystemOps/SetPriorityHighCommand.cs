// Commands/SystemOps/SetPriorityHighCommand.cs
#nullable enable
using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMCPAddin.Commands.SystemOps
{
    [Transaction(TransactionMode.Manual)]
    public class SetPriorityHighCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var p = Process.GetCurrentProcess();
                p.PriorityClass = ProcessPriorityClass.High;
                TaskDialog.Show("Revit MCP", $"CPU priority set to: {p.PriorityClass}.");
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

