// ================================================================
// File: Core/CommandRunner.cs
// Purpose: すべてのコマンド実行の共通入口。スナップショット前後処理を注入。
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    //public interface IRevitCommandHandler
    //{
    //    string CommandName { get; }
    //    object Execute(UIApplication uiapp, RequestCommand cmd);
    //}

    public sealed class CommandRunner
    {
        private readonly Dictionary<string, IRevitCommandHandler> _handlers;

        public CommandRunner(IEnumerable<IRevitCommandHandler> handlers)
        {
            _handlers = new Dictionary<string, IRevitCommandHandler>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in handlers) _handlers[h.CommandName] = h;
        }

        public object Run(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            if (!_handlers.TryGetValue(cmd.Method, out var handler))
                return new { ok = false, msg = $"未知のコマンド: {cmd.Method}" };

            // --- 前スナップショット（任意） ---
            SnapshotResultMeta? pre = null;
            if (cmd.Meta != null && cmd.Meta.ExportSnapshot && (cmd.Meta.SnapshotOptions?.SavePreAndPost ?? true))
            {
                try { pre = SnapshotManager.Run(doc, cmd.Meta, "pre"); } catch { /* ignore */ }
            }

            // --- 実処理 ---
            var result = handler.Execute(uiapp, cmd);

            // 結果オブジェクトを JObject に寄せて meta 付加
            var jo = JObject.FromObject(result ?? new { ok = true });
            var meta = new JObject();

            // --- 後スナップショット（本命） ---
            SnapshotResultMeta? post = null;
            if (cmd.Meta != null && cmd.Meta.ExportSnapshot)
            {
                try { post = SnapshotManager.Run(doc, cmd.Meta, "post"); } catch { /* ignore */ }
            }

            // --- マニフェスト情報を必要に応じてレスポンスへ添付 ---
            if (cmd.Meta != null && (cmd.Meta.ExportSnapshot || cmd.Meta.IncludeManifest))
            {
                var metaObj = new
                {
                    requestId = cmd.Meta.RequestId,
                    snapshot = post ?? pre, // post優先（なければpre）
                };
                meta = JObject.FromObject(metaObj);
            }

            jo["meta"] = meta;
            return jo;
        }
    }
}
