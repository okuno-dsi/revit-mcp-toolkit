#nullable enable
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.SystemOps
{
    [RpcCommand(
        "cleanup_revitmcp_cache",
        Aliases = new[] { "cleanup_local_cache" },
        Category = "System",
        Tags = new[] { "cache", "cleanup", "localappdata" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Summary = "Cleanup stale cache files under %LOCALAPPDATA%\\RevitMCP (best-effort).",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"cleanup_revitmcp_cache\", \"params\":{ \"dryRun\": true, \"retentionDays\": 7, \"maxDeletedPaths\": 200 } }"
    )]
    public sealed class CleanupRevitMcpCacheCommand : IRevitCommandHandler
    {
        public string CommandName => "cleanup_revitmcp_cache";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();

            bool dryRun = p.Value<bool?>("dryRun") ?? true;
            int retentionDays = p.Value<int?>("retentionDays") ?? CacheCleanupService.DefaultRetentionDays;
            int maxDeletedPaths = p.Value<int?>("maxDeletedPaths") ?? 200;
            if (maxDeletedPaths < 0) maxDeletedPaths = 0;

            int currentPort = 0;
            try { currentPort = PortLocator.GetCurrentPortOrDefault(0); } catch { currentPort = 0; }

            var rep = CacheCleanupService.CleanupLocalCache(currentPort: currentPort, retentionDays: retentionDays, dryRun: dryRun);

            // Truncate deletedPaths for agent-friendliness.
            bool truncated = false;
            if (rep.deletedPaths != null && rep.deletedPaths.Count > maxDeletedPaths)
            {
                truncated = true;
                rep.deletedPaths = rep.deletedPaths.GetRange(0, maxDeletedPaths);
            }

            var data = JObject.FromObject(rep);
            data["deletedPathsTruncated"] = truncated;
            data["localRoot"] = Paths.LocalRoot;

            return new JObject
            {
                ["ok"] = rep.ok,
                ["code"] = rep.ok ? "OK" : "ERROR",
                ["msg"] = rep.msg ?? (rep.ok ? "OK" : "ERROR"),
                ["data"] = data
            };
        }
    }
}
