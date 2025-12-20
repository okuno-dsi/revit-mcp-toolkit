// Commands/Dev/OpenFoldersCommands.cs
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Dev
{
    [Transaction(TransactionMode.Manual)]
    public class OpenAddinFolderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var folder = Paths.AddinFolder;
                if (!Directory.Exists(folder)) { message = "アドインフォルダが見つかりません。"; return Result.Failed; }
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                return Result.Succeeded;
            }
            catch (Exception ex) { message = "エクスプローラ起動に失敗: " + ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class OpenLogFolderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                // ★ ここを Local に固定
                var folder = Paths.EnsureLocalLogs(); // => C:\Users\<user>\AppData\Local\RevitMCP\logs
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                return Result.Succeeded;
            }
            catch (Exception ex) { message = "エクスプローラ起動に失敗: " + ex.Message; return Result.Failed; }
        }
    }
}
