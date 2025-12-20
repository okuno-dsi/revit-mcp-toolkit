// RevitMcpServer/Rpc/SmokeTestCommand.cs  （A案の基底に合致）
using System;
using System.Threading.Tasks;
using System.Text.Json;
using RevitMCP.Abstractions.Rpc;
using RevitMcpServer.Core;

namespace RevitMcpServer.Rpc
{
    public sealed class SmokeTestCommand : RevitMCP.Abstractions.Rpc.RpcCommandBase
    {
        public override string Name => "smoke_test";
        public override RpcCommandKind Kind => RpcCommandKind.Read;

        public override Type ParamsType => null;  // 型メタ不要なら null でOK（C#7.3では警告出ない）
        public override Type ResultType => null;

        protected override Task<object> ProcessAsync(JsonElement? param)
        {
            string method = null;
            JsonElement? innerParams = null;

            if (param.HasValue && param.Value.ValueKind == JsonValueKind.Object)
            {
                var root = param.Value;
                if (root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String)
                    method = m.GetString();
                if (root.TryGetProperty("params", out var p))
                    innerParams = p;
            }
            if (string.IsNullOrWhiteSpace(method))
                return Task.FromResult<object>(new { ok = false, error = "invalid_method", msg = "method is required" });

            if (!CommandRegistry.TryGet(method, out var meta))
                return Task.FromResult<object>(new { ok = false, error = "unknown_command", msg = "Unknown command: " + method });

            // 軽量チェック
            if (string.Equals(meta.kind, "write", StringComparison.OrdinalIgnoreCase))
            {
                bool mayNeedIds =
                    method.StartsWith("update_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("delete_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("move_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("set_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("apply_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("remove_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("rename_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("replace_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("swap_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("merge_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("trim_", StringComparison.OrdinalIgnoreCase) ||
                    method.StartsWith("create_", StringComparison.OrdinalIgnoreCase);

                if (mayNeedIds && !HasAnyId(innerParams))
                    return Task.FromResult<object>(new { ok = false, error = "missing_id", msg = "Likely requires one of elementId/elementIds/viewId/roomId/spaceId/..." });

                if (string.Equals(meta.importance, "high", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult<object>(new { ok = true, msg = "Command '" + method + "' is high-impact write. Confirm before execution.", severity = "warn", meta });
            }

            return Task.FromResult<object>(new { ok = true, msg = "'" + method + "' looks valid.", meta });
        }

        private static bool HasAnyId(JsonElement? p)
        {
            if (!p.HasValue || p.Value.ValueKind != JsonValueKind.Object) return false;
            var obj = p.Value;
            return obj.TryGetProperty("elementId", out _) ||
                   obj.TryGetProperty("elementIds", out _) ||
                   obj.TryGetProperty("viewId", out _) ||
                   obj.TryGetProperty("roomId", out _) ||
                   obj.TryGetProperty("spaceId", out _) ||
                   obj.TryGetProperty("wallId", out _) ||
                   obj.TryGetProperty("floorId", out _) ||
                   obj.TryGetProperty("gridId", out _);
        }
    }
}
