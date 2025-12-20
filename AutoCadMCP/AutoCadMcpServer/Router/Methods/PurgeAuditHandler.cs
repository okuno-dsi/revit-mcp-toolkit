using System.Text.Json.Nodes;
using AutoCadMcpServer.Router;

namespace AutoCadMcpServer.Router.Methods
{
    public static class PurgeAuditHandler
    {
        public static Task<object> Handle(JsonObject p, ILogger logger, IConfiguration config)
        {
            throw new RpcError(501, "purge_audit not implemented as standalone. Use merge_dwgs with postProcess.");
        }
    }
}

