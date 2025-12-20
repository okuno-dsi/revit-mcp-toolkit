// ================================================================
// File: Commands/Export/ExportDwgByParamGroupsCommand.cs
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Export
{
    public class ExportDwgByParamGroupsCommand : IRevitCommandHandler
    {
        public string CommandName => "export_dwg_by_param_groups";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = (uidoc != null) ? uidoc.Document : null;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)cmd.Params;
            bool asyncMode = p.Value<bool?>("asyncMode") ?? true;

            // UiEventPump は OnStartup で Initialize 済み推奨（未済ならここで念のため）
            try { UiEventPump.Initialize(); } catch { }

            // LongOpEngine: BaseAddress 初期化は Worker 側で実施済み想定（null 許容）
            try { LongOpEngine.Initialize(uiapp, null); } catch { }

            if (asyncMode)
            {
                string sessionKey = p.Value<string>("sessionKey")
                    ?? (doc.Title + ":" + (p.Value<int?>("viewId") ?? 0).ToString() + ":" + (p.Value<string>("category") ?? "OST_Walls") + ":" + (p.Value<string>("paramName") ?? "Comments"));
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);

                long rpcId = GetRpcIdAsLong(cmd.Id);
                string jobId = LongOpEngine.Enqueue(rpcId, CommandName, p, sessionKey, startIndex);
                return new
                {
                    ok = true,
                    done = false,
                    phase = "queued",
                    nextIndex = startIndex,
                    msg = "Accepted. Processing asynchronously via Idling.",
                    jobId
                };
            }
            else
            {
                int maxMs = Math.Max(10000, p.Value<int?>("maxMillisPerPass") ?? 20000);
                var job = new LongOpEngine.Job
                {
                    RpcId = GetRpcIdAsLong(cmd.Id),
                    Method = CommandName,
                    Params = p,
                    NextIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0)
                };
                var r = ExportDwgByParamGroupsTick.Run(uiapp, job, maxMs);
                return new
                {
                    ok = r.ok,
                    outputs = r.outputs,
                    skipped = r.skipped,
                    done = r.done,
                    nextIndex = r.nextIndex,
                    totalGroups = r.totalGroups,
                    processed = r.processed,
                    elapsedMs = r.elapsedMs,
                    phase = r.phase,
                    msg = r.msg
                };
            }
        }

        // RequestCommand.Id が long / int / string / JToken 等でも安全に long 化
        private static long GetRpcIdAsLong(object id)
        {
            try
            {
                if (id == null) return 0;
                if (id is long l) return l;
                if (id is int i) return i;
                if (id is string s && long.TryParse(s, out var lv)) return lv;
                var tok = id as Newtonsoft.Json.Linq.JToken;
                if (tok != null)
                {
                    if (tok.Type == Newtonsoft.Json.Linq.JTokenType.Integer) return tok.Value<long>();
                    if (tok.Type == Newtonsoft.Json.Linq.JTokenType.String && long.TryParse(tok.Value<string>(), out var lv2)) return lv2;
                }
                return Convert.ToInt64(id);
            }
            catch { return 0; }
        }
    }
}
