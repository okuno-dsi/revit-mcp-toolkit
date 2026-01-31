#nullable enable
// ================================================================
// File   : Commands/System/GetContextCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary:
//   Step 7: get_context / help.get_context
//   Returns contextToken + revision + active doc/view/selection snapshot.
// ================================================================
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    [RpcCommand(
        "help.get_context",
        Aliases = new[] { "get_context" },
        Category = "MetaOps",
        Tags = new[] { "context", "help", "diag" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Importance = "high",
        Summary = "Get current contextToken/revision and a snapshot of active doc/view/selection.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"help.get_context\", \"params\":{ \"includeSelectionIds\": true, \"maxSelectionIds\": 200 } }"
    )]
    public sealed class GetContextCommand : IRevitCommandHandler
    {
        public string CommandName => "get_context";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();

            bool includeSelectionIds = p.Value<bool?>("includeSelectionIds") ?? true;
            int maxSelectionIds = p.Value<int?>("maxSelectionIds") ?? 200;
            if (maxSelectionIds < 0) maxSelectionIds = 0;

            // Selection robustness options (optional)
            int selectionRetryMaxWaitMs = p.SelectToken("selection.retry.maxWaitMs")?.ToObject<int?>()
                                          ?? p.SelectToken("retry.maxWaitMs")?.ToObject<int?>()
                                          ?? 0;
            int selectionRetryPollMs = p.SelectToken("selection.retry.pollMs")?.ToObject<int?>()
                                       ?? p.SelectToken("retry.pollMs")?.ToObject<int?>()
                                       ?? 150;
            bool selectionFallbackToStash = p.SelectToken("selection.fallbackToStash")?.ToObject<bool?>()
                                            ?? p.SelectToken("fallbackToStash")?.ToObject<bool?>()
                                            ?? true;
            int selectionStashMaxAgeMs = p.SelectToken("selection.maxAgeMs")?.ToObject<int?>()
                                         ?? p.SelectToken("maxAgeMs")?.ToObject<int?>()
                                         ?? 2000;

            var snap = ContextTokenService.Capture(
                uiapp,
                includeSelectionIds: includeSelectionIds,
                maxSelectionIds: maxSelectionIds,
                selectionRetryMaxWaitMs: selectionRetryMaxWaitMs,
                selectionRetryPollMs: selectionRetryPollMs,
                selectionFallbackToStash: selectionFallbackToStash,
                selectionStashMaxAgeMs: selectionStashMaxAgeMs);

            var data = JObject.FromObject(snap);
            data["terminology"] = TermMapService.BuildTerminologyContextBlock();

            return new JObject
            {
                ["ok"] = true,
                ["code"] = "OK",
                ["msg"] = "Context",
                ["data"] = data
            };
        }
    }
}
