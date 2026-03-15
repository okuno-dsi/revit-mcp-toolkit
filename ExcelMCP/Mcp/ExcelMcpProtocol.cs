using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ExcelMcpProtocol
{
    public const string SessionHeader = "MCP-Session-Id";
    public const string ProtocolHeader = "MCP-Protocol-Version";
    public const string DefaultProtocolVersion = "2025-11-25";
    public const string LegacyFallbackProtocolVersion = "2025-03-26";

    public static readonly string[] SupportedProtocolVersions =
    {
        DefaultProtocolVersion,
        "2025-11-05",
        LegacyFallbackProtocolVersion,
    };

    public static bool IsNotification(JsonObject request) =>
        request["id"] is null;

    public static bool IsSupportedProtocolVersion(string? protocolVersion) =>
        !string.IsNullOrWhiteSpace(protocolVersion)
        && SupportedProtocolVersions.Contains(protocolVersion, StringComparer.OrdinalIgnoreCase);

    public static string NegotiateProtocolVersion(string? requestedProtocolVersion)
    {
        if (IsSupportedProtocolVersion(requestedProtocolVersion))
            return requestedProtocolVersion!;
        return DefaultProtocolVersion;
    }

    public static object InitializeResult(string protocolVersion) => new
    {
        protocolVersion,
        capabilities = new
        {
            tools = new { listChanged = false }
        },
        serverInfo = new { name = "ExcelMCP", version = "1.1.0" },
        instructions = "Call initialize, then notifications/initialized, then tools/list and tools/call. This server supports tools over Streamable HTTP-style POST/GET/DELETE transport.",
        supportedProtocolVersions = SupportedProtocolVersions
    };

    public static object ToolResult(JsonNode payload, bool isError) => new
    {
        content = new object[]
        {
            new
            {
                type = "text",
                text = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            }
        },
        structuredContent = payload,
        isError
    };

    public static object Success(JsonNode? idNode, object? result) => new
    {
        jsonrpc = "2.0",
        id = NormalizeId(idNode),
        result
    };

    public static object Error(JsonNode? idNode, int code, string message, object? data = null) => new
    {
        jsonrpc = "2.0",
        id = NormalizeId(idNode),
        error = new { code, message, data }
    };

    public static object InvalidRequest(JsonNode? idNode, string message, object? data = null) =>
        Error(idNode, -32600, message, data);

    public static object InvalidParams(JsonNode? idNode, string message, object? data = null) =>
        Error(idNode, -32602, message, data);

    private static object? NormalizeId(JsonNode? idNode)
    {
        if (idNode is null) return null;
        if (idNode is JsonValue v)
        {
            if (v.TryGetValue<long>(out var n)) return n;
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<string>(out var s))
            {
                if (long.TryParse(s, out var n2)) return n2;
                return s;
            }
        }

        return idNode.ToJsonString();
    }
}
