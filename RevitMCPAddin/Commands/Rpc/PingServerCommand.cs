// ================================================================
// File: Commands/Rpc/PingServerCommand.cs
// Purpose : JSON-RPC "ping_server" – サーバー/アドインの往復疎通を確認
// Notes   : トランザクション不要。Revit未保存/無文書でも動作OK。
// ================================================================
using System;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rpc
{
    public class PingServerCommand : IRevitCommandHandler
    {
        public string CommandName => "ping_server";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            // 可能であれば軽い情報を返す（何もなければ null 安全）
            var app = uiapp?.Application;
            var activeDoc = uiapp?.ActiveUIDocument?.Document;

            // Revitプロセスの起動時刻と簡易Uptime（※サーバーUptimeではなくRevitのUptime）
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            var now = DateTime.Now;
            var uptimeSec = (int)Math.Max(0, (now - proc.StartTime).TotalSeconds);

            return new
            {
                ok = true,
                msg = "MCP Server round-trip OK (Revit Add-in reachable)",
                // 簡易メタ
                product = app?.VersionName,                 // e.g. "Autodesk Revit 2024"
                build = app?.SubVersionNumber,             // e.g. "24.1.0.123"
                activeDocument = activeDoc?.Title,         // null可
                time = now.ToString("yyyy-MM-dd HH:mm:ss"),
                process = new { pid = proc.Id, uptimeSec },

                // プロジェクト全体の慣例に合わせたユニット付与（機械可読）
                inputUnits = new { length = "mm", angle = "deg" },
                internalUnits = new { length = "ft", angle = "rad" },

                // issues 互換（常に空で返却）
                issues = new { failures = Array.Empty<object>(), dialogs = Array.Empty<object>() }
            };
        }
    }
}
