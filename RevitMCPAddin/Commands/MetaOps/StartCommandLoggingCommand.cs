// ================================================================
// File: Commands/MetaOps/StartCommandLoggingCommand.cs
// Purpose: コマンドログの記録開始（出力先と任意のprefixを設定）
// ================================================================
#nullable enable
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    public class StartCommandLoggingCommand : IRevitCommandHandler
    {
        public string CommandName => "start_command_logging";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();

            // dir: 必須。prefix: 任意
            var dir = p.Value<string>("dir");
            var prefix = p.Value<string>("prefix");

            if (string.IsNullOrWhiteSpace(dir))
                return new { ok = false, msg = "出力先 'dir' を指定してください。" };

            try
            {
                System.IO.Directory.CreateDirectory(dir);
                CommandLogWriter.Start(dir, prefix);
                RevitLogger.Info($"Command logging started. dir={dir}, prefix={prefix ?? ""}");
                return new
                {
                    ok = true,
                    msg = "Command logging started.",
                    paths = new
                    {
                        jsonl = CommandLogWriter.CurrentJsonlPath,
                        journal = CommandLogWriter.CurrentJournalPath
                    }
                };
            }
            catch (System.Exception ex)
            {
                RevitLogger.Error("Start logging failed: " + ex.Message);
                return new { ok = false, msg = "記録開始に失敗: " + ex.Message };
            }
        }
    }
}

