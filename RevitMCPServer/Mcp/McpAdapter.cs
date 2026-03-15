// File: RevitMcpServer/Mcp/McpAdapter.cs
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using RevitMCP.Abstractions.Rpc;
using RevitMcpServer.Docs;

namespace RevitMcpServer.Mcp
{
    internal static class McpAdapter
    {
        public const string SessionHeader = "MCP-Session-Id";
        public const string ProtocolHeader = "MCP-Protocol-Version";
        public const string DefaultProtocolVersion = "2025-11-25";
        public const string ServerName = "RevitMCPServer";
        public const string ServerVersion = "1.0.0";
        public static readonly string[] SupportedProtocolVersions = new[]
        {
            "2025-11-25",
            "2025-11-05",
            "2025-03-26"
        };

        public static object CreateInitializeResult(string protocolVersion)
        {
            return new
            {
                protocolVersion,
                capabilities = new
                {
                    tools = new
                    {
                        listChanged = false
                    },
                    resources = new
                    {
                        subscribe = false,
                        listChanged = false
                    },
                    prompts = new
                    {
                        listChanged = false
                    },
                    logging = new
                    {
                    }
                },
                serverInfo = new
                {
                    name = ServerName,
                    version = ServerVersion
                },
                instructions = "Use tools/list to discover available Revit commands. Legacy /rpc and /job endpoints remain available for existing clients."
            };
        }

        public static bool IsSupportedProtocolVersion(string? protocolVersion)
        {
            if (string.IsNullOrWhiteSpace(protocolVersion))
                return true;

            return SupportedProtocolVersions.Contains(protocolVersion.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        public static string NegotiateProtocolVersion(string? requestedProtocolVersion)
        {
            if (!string.IsNullOrWhiteSpace(requestedProtocolVersion)
                && IsSupportedProtocolVersion(requestedProtocolVersion))
            {
                return requestedProtocolVersion.Trim();
            }

            return DefaultProtocolVersion;
        }

        public static bool IsOriginAllowed(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return true;

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;

            if (uri.IsLoopback)
                return true;

            if (IPAddress.TryParse(uri.Host, out var ip))
                return IPAddress.IsLoopback(ip);

            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        public static object CreateToolCallResult(JsonNode? payload, bool isError)
        {
            string text;
            try
            {
                text = payload?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
            }
            catch
            {
                text = payload?.ToJsonString() ?? "{}";
            }

            return new
            {
                content = new object[]
                {
                    new
                    {
                        type = "text",
                        text
                    }
                },
                structuredContent = payload,
                isError
            };
        }

        public static object CreateJsonRpcSuccess(string? id, object? result)
            => new
            {
                jsonrpc = "2.0",
                id = NormalizeId(id),
                result
            };

        public static object CreateJsonRpcError(string? id, int code, string message, object? data = null)
            => new
            {
                jsonrpc = "2.0",
                id = NormalizeId(id),
                error = new
                {
                    code,
                    message,
                    data
                }
            };

        public static JsonNode? UnwrapRpcResult(string? resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
                return null;

            try
            {
                var parsed = JsonNode.Parse(resultJson);
                if (parsed is not JsonObject root)
                    return parsed;

                if (root["result"] is JsonObject level1)
                {
                    if (level1["result"] is JsonNode level2)
                        return level2.DeepClone();
                    return level1.DeepClone();
                }

                return root.DeepClone();
            }
            catch
            {
                return JsonValue.Create(resultJson);
            }
        }

        private static object? NormalizeId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            if (long.TryParse(id, out var n))
                return n;
            return id;
        }
    }

    internal sealed class McpSessionStore
    {
        private readonly ConcurrentDictionary<string, McpSessionState> _sessions =
            new ConcurrentDictionary<string, McpSessionState>(StringComparer.OrdinalIgnoreCase);

        public McpSessionState Create(string protocolVersion)
        {
            var session = new McpSessionState
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ProtocolVersion = string.IsNullOrWhiteSpace(protocolVersion) ? McpAdapter.DefaultProtocolVersion : protocolVersion,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastSeenAtUtc = DateTimeOffset.UtcNow,
                IsInitialized = false
            };
            _sessions[session.SessionId] = session;
            return session;
        }

        public bool TryGet(string? sessionId, out McpSessionState session)
        {
            if (!string.IsNullOrWhiteSpace(sessionId) && _sessions.TryGetValue(sessionId, out session!))
            {
                session.LastSeenAtUtc = DateTimeOffset.UtcNow;
                return true;
            }

            session = null!;
            return false;
        }

        public bool MarkInitialized(string? sessionId)
        {
            if (!TryGet(sessionId, out var session))
                return false;

            session.IsInitialized = true;
            session.LastSeenAtUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    internal sealed class McpSessionState
    {
        public string SessionId { get; set; } = "";
        public string ProtocolVersion { get; set; } = McpAdapter.DefaultProtocolVersion;
        public bool IsInitialized { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset LastSeenAtUtc { get; set; }
    }

    internal static class McpToolCatalog
    {
        public static List<McpToolDefinition> Build(RpcRouter router, IEnumerable<DocMethod> extras)
        {
            var tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (var method in extras ?? Enumerable.Empty<DocMethod>())
            {
                if (string.IsNullOrWhiteSpace(method?.Name))
                    continue;

                tools[method.Name] = new McpToolDefinition
                {
                    Name = method.Name,
                    Description = BuildDescription(method.Summary, method.Source, method.Tags),
                    InputSchema = EnsureObjectSchema(method.ParamsSchema)
                };
            }

            tools["revit.status"] = new McpToolDefinition
            {
                Name = "revit.status",
                Description = "Server-local status probe for queue, port, and health information.",
                InputSchema = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>(),
                    ["additionalProperties"] = false
                }
            };
            foreach (var kv in router.GetAllCommands())
            {
                if (tools.ContainsKey(kv.Key))
                    continue;

                tools[kv.Key] = new McpToolDefinition
                {
                    Name = kv.Key,
                    Description = $"{kv.Value.Kind} RPC command",
                    InputSchema = EnsureObjectSchema(SchemaUtils.ToJsonSchema(kv.Value.ParamsType ?? typeof(object)))
                };
            }

            return tools.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildDescription(string? summary, string? source, string[]? tags)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(summary))
                parts.Add(summary!.Trim());
            if (!string.IsNullOrWhiteSpace(source))
                parts.Add($"source: {source!.Trim()}");
            if (tags != null && tags.Length > 0)
                parts.Add("tags: " + string.Join(", ", tags.Where(x => !string.IsNullOrWhiteSpace(x))));
            return string.Join(" | ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static Dictionary<string, object?> EnsureObjectSchema(Dictionary<string, object?>? schema)
        {
            var clone = schema != null
                ? new Dictionary<string, object?>(schema, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (!clone.ContainsKey("type"))
                clone["type"] = "object";

            if (string.Equals(Convert.ToString(clone["type"]), "object", StringComparison.OrdinalIgnoreCase)
                && !clone.ContainsKey("properties")
                && !clone.ContainsKey("additionalProperties"))
            {
                clone["properties"] = new Dictionary<string, object?>();
                clone["additionalProperties"] = true;
            }

            return clone;
        }
    }

    internal sealed class McpToolDefinition
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, object?> InputSchema { get; set; } = new Dictionary<string, object?>();
    }
}
