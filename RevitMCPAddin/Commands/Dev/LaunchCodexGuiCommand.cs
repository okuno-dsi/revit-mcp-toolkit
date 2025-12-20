#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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
                if (string.IsNullOrWhiteSpace(docs))
                {
                    message = "ユーザーの Documents フォルダが取得できません。";
                    return Result.Failed;
                }

                var root = Path.Combine(docs, "Codex_MCP");
                var guiDir = Path.Combine(root, "CodexGui");
                var exePath = Path.Combine(guiDir, "CodexGui.exe");
                var workRoot = Path.Combine(root, "Codex");

                if (!File.Exists(exePath))
                {
                    message = $"Codex GUI の実行ファイルが見つかりません。\n{exePath}\n\n" +
                              "dotnet publish で出力した CodexGui フォルダをここにコピーしてください。";
                    return Result.Failed;
                }

                Directory.CreateDirectory(workRoot);

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = workRoot,
                    UseShellExecute = false
                };

                // Codex GUI 側の run_codex_prompt.ps1 が参照する作業ルート
                psi.EnvironmentVariables["CODEX_MCP_ROOT"] = workRoot;

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

