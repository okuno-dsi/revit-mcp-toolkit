namespace RevitMCP.A2AAdapter;

public static class AgentCardFactory
{
    public static object Build(HttpRequest request, A2AOptions options)
    {
        var baseUrl = ResolvePublicBaseUrl(request);
        return new
        {
            name = "Revit MCP A2A Adapter",
            description = "Official A2A-facing adapter for deterministic Revit MCP requests. The adapter exposes A2A JSON-RPC and bridges requests to a local RevitMCPServer.",
            supportedInterfaces = new[]
            {
                new
                {
                    url = $"{baseUrl}/a2a/rpc",
                    protocolBinding = "JSONRPC",
                    protocolVersion = options.ProtocolVersion
                }
            },
            provider = new
            {
                organization = "Daiken",
                url = baseUrl
            },
            version = options.AgentVersion,
            documentationUrl = $"{baseUrl}/",
            capabilities = new
            {
                streaming = false,
                pushNotifications = false,
                extendedAgentCard = false,
                stateTransitionHistory = true
            },
            defaultInputModes = new[] { "application/json", "text/plain" },
            defaultOutputModes = new[] { "application/json", "text/plain" },
            skills = new object[]
            {
                new
                {
                    id = "revit.rpc",
                    name = "Revit MCP deterministic RPC bridge",
                    description = "Executes an explicit Revit MCP command supplied as metadata.revitMethod or a data part with revitMethod and revitParams.",
                    tags = new[] { "revit", "bim", "mcp", "json-rpc" },
                    examples = new[]
                    {
                        "Send a data part: { \"revitMethod\": \"get_context\", \"revitParams\": {} }",
                        "Send metadata: { \"revitMethod\": \"get_project_info\", \"revitParams\": {} }"
                    },
                    inputModes = new[] { "application/json" },
                    outputModes = new[] { "application/json", "text/plain" }
                },
                new
                {
                    id = "revit.status",
                    name = "Revit MCP status",
                    description = "Reports adapter and target Revit MCP server health.",
                    tags = new[] { "revit", "status", "health" },
                    examples = new[] { "Check whether the adapter can reach RevitMCPServer." },
                    inputModes = new[] { "application/json", "text/plain" },
                    outputModes = new[] { "application/json" }
                }
            }
        };
    }

    private static string ResolvePublicBaseUrl(HttpRequest request)
    {
        var scheme = request.Scheme;
        var host = request.Host.HasValue ? request.Host.Value : "127.0.0.1";
        return $"{scheme}://{host}".TrimEnd('/');
    }
}
