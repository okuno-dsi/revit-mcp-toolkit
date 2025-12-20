#nullable enable
using System.Text.Json.Nodes;
using AutoCadMcpServer.Router.Methods;
using AutoCadMcpServer.Core;

namespace AutoCadMcpServer.Router
{
    public static class RpcRouter
    {
        public static Task<object> Dispatch(JsonRpcReq req, ILogger logger, IConfiguration config)
        {
            switch (req.method)
            {
                case "merge_dwgs":
                    return MergeDwgsHandler.Handle(req.@params, logger, config);

                case "merge_dwgs_perfile_rename":
                    return MergeDwgsPerFileRenameHandler.Handle(req.@params, logger, config);

                // Router/RpcRouter.cs
                case "merge_dwgs_dxf_textmap":
                    // Fallback to per-file rename merge implementation which supports include/format
                    return MergeDwgsDxfTextMapHandler.Handle(req.@params, logger, config);

                case "probe_accoreconsole":
                    return ProbeAccoreHandler.Handle(req.@params, logger, config);

                case "purge_audit":
                    return PurgeAuditHandler.Handle(req.@params, logger, config);

                case "consolidate_layers":
                    return ConsolidateLayersHandler.Handle(req.@params, logger, config);

                case "health":
                    return Task.FromResult<object>(new { ok = true, ts = DateTimeOffset.Now });

                case "version":
                    return Task.FromResult<object>(new { ok = true, version = typeof(RpcRouter).Assembly.GetName().Version?.ToString() ?? "0.0.0" });

                default:
                    throw new RpcError(404, $"Unknown method: {req.method}");
            }
        }
    }
}
