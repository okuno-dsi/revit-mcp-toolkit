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
    public class LaunchCodexGuiCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var root = Paths.ResolveRoot();
                var appsRoot = Paths.ResolveAppsRoot();
                var codexRoot = Paths.ResolveCodexRoot();
                var workRoot = Paths.ResolveWorkRoot();

                var guiDir = !string.IsNullOrWhiteSpace(appsRoot)
                    ? Path.Combine(appsRoot, "CodexGui")
                    : (!string.IsNullOrWhiteSpace(root) ? Path.Combine(root, "Apps", "CodexGui") : string.Empty);
                if (string.IsNullOrWhiteSpace(guiDir) && !string.IsNullOrWhiteSpace(docs))
                {
                    var fallback = Path.Combine(docs, "Revit_MCP", "Apps", "CodexGui");
                    if (Directory.Exists(fallback)) guiDir = fallback;
                }

                var exePath = string.IsNullOrWhiteSpace(guiDir) ? string.Empty : Path.Combine(guiDir, "CodexGui.exe");

                if (!File.Exists(exePath))
                {
                    message = $"Codex GUI の実行ファイルが見つかりません。\n{exePath}\n\n" +
                              "dotnet publish で出力した CodexGui フォルダを Apps\\CodexGui にコピーしてください。";
                    return Result.Failed;
                }

                if (!string.IsNullOrWhiteSpace(workRoot))
                    Directory.CreateDirectory(workRoot);
                if (string.IsNullOrWhiteSpace(codexRoot) && !string.IsNullOrWhiteSpace(root))
                {
                    var c2 = Path.Combine(root, "Codex");
                    if (Directory.Exists(c2)) codexRoot = c2;
                    else
                    {
                        var c1 = Path.Combine(root, "Docs", "Codex");
                        if (Directory.Exists(c1)) codexRoot = c1;
                    }
                }
                if (string.IsNullOrWhiteSpace(codexRoot) && !string.IsNullOrWhiteSpace(docs))
                {
                    var c1 = Path.Combine(docs, "Revit_MCP", "Codex");
                    if (Directory.Exists(c1)) codexRoot = c1;
                    else
                    {
                        var c0 = Path.Combine(docs, "Revit_MCP", "Docs", "Codex");
                        if (Directory.Exists(c0)) codexRoot = c0;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = !string.IsNullOrWhiteSpace(codexRoot) ? codexRoot : (workRoot ?? guiDir),
                    UseShellExecute = false
                };

                if (!string.IsNullOrWhiteSpace(root))
                    psi.EnvironmentVariables["REVIT_MCP_ROOT"] = root;
                if (!string.IsNullOrWhiteSpace(workRoot))
                    psi.EnvironmentVariables["REVIT_MCP_WORK_ROOT"] = workRoot;
                if (!string.IsNullOrWhiteSpace(codexRoot))
                    psi.EnvironmentVariables["CODEX_MCP_ROOT"] = codexRoot;

                Process.Start(psi);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = "Codex GUI 起動に失敗しました: " + ex.Message;
                return Result.Failed;
            }
        }
    }
}
