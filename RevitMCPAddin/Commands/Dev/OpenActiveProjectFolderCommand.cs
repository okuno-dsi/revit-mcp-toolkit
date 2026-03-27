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
    public class OpenActiveProjectFolderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var doc = data.Application?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    message = "アクティブな文書がありません。";
                    return Result.Failed;
                }

                var docGuid = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out _);
                var docTitle = string.IsNullOrWhiteSpace(doc.Title) ? "Untitled" : doc.Title;
                var folder = GetProjectFolder(docTitle, docGuid);
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = "プロジェクトフォルダを開けませんでした: " + ex.Message;
                return Result.Failed;
            }
        }

        private static string GetProjectFolder(string docTitle, string docGuid)
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Revit_MCP",
                "Projects");
            return Path.Combine(root, SanitizeFileName($"{docTitle}_{docGuid}"));
        }

        private static string SanitizeFileName(string? value)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "project" : value!;
            foreach (var ch in Path.GetInvalidFileNameChars())
                text = text.Replace(ch, '_');
            return text;
        }
    }
}
