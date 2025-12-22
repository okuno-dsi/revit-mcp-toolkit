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

            var snap = ContextTokenService.Capture(uiapp, includeSelectionIds: includeSelectionIds, maxSelectionIds: maxSelectionIds);

            return new JObject
            {
                ["ok"] = true,
                ["code"] = "OK",
                ["msg"] = "Context",
                ["data"] = JObject.FromObject(snap)
            };
        }
    }
}

