using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ExcelMCP.IntegrationTests;

[Collection(nameof(ExcelMcpServerCollection))]
public sealed class McpProtocolTests
{
    private readonly ExcelMcpServerFixture _fixture;

    public McpProtocolTests(ExcelMcpServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Initialize_NegotiatesUnsupportedProtocolVersion()
    {
        using var response = await PostJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = "2099-01-01",
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "tests",
                    ["version"] = "1.0.0",
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2025-11-25", response.Headers.GetValues("MCP-Protocol-Version").Single());
        Assert.True(response.Headers.TryGetValues("MCP-Session-Id", out var sessionHeaders));
        Assert.False(string.IsNullOrWhiteSpace(sessionHeaders.Single()));

        var payload = await ReadJsonAsync(response);
        Assert.Equal("2025-11-25", payload["result"]?["protocolVersion"]?.GetValue<string>());
    }

    [Fact]
    public async Task NotificationsInitialized_ReturnsAcceptedWithoutBody()
    {
        var (sessionId, protocolVersion) = await InitializeSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { }
            })
        };
        request.Headers.Add("MCP-Session-Id", sessionId);
        request.Headers.Add("MCP-Protocol-Version", protocolVersion);

        using var response = await _fixture.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(string.IsNullOrWhiteSpace(body));
    }

    [Fact]
    public async Task ToolsList_ExposesFirstClassTools()
    {
        var (sessionId, protocolVersion) = await InitializeAndReadySessionAsync();

        using var response = await PostJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["method"] = "tools/list",
            ["params"] = new JsonObject(),
        }, sessionId, protocolVersion);

        var payload = await ReadJsonAsync(response);
        var toolNames = payload["result"]?["tools"]?.AsArray().Select(x => x?["name"]?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(toolNames);
        Assert.Contains("excel.sheet_info", toolNames!);
        Assert.Contains("excel.read_cells", toolNames);
        Assert.Contains("excel.write_cells", toolNames);
        Assert.Contains("excel.append_rows", toolNames);
        Assert.Contains("excel.set_formula", toolNames);
        Assert.Contains("excel.format_sheet", toolNames);
        Assert.Contains("excel.to_csv", toolNames);
        Assert.Contains("excel.to_json", toolNames);
        Assert.Contains("excel.list_charts", toolNames);
        Assert.Contains("excel.list_open_workbooks", toolNames);
        Assert.Contains("excel.preview_write_cells", toolNames);
        Assert.Contains("excel.api_call", toolNames);
        Assert.Contains("mcp.status", toolNames);
    }

    [Fact]
    public async Task ToolsCall_Health_ReturnsStructuredToolResult()
    {
        var (sessionId, protocolVersion) = await InitializeAndReadySessionAsync();

        using var response = await PostJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 3,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "excel.health",
                ["arguments"] = new JsonObject(),
            }
        }, sessionId, protocolVersion);

        var payload = await ReadJsonAsync(response);
        Assert.False(payload["result"]?["isError"]?.GetValue<bool>() ?? true);
        Assert.Equal(true, payload["result"]?["structuredContent"]?["ok"]?.GetValue<bool>());
        Assert.Equal("excel.health", payload["result"]?["structuredContent"]?["tool"]?.GetValue<string>());
        Assert.Equal("/health", payload["result"]?["structuredContent"]?["path"]?.GetValue<string>());
    }

    [Fact]
    public async Task InvalidSession_IsRejected()
    {
        using var response = await PostJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 4,
            ["method"] = "tools/list",
            ["params"] = new JsonObject(),
        }, "invalid-session", "2025-11-25");

        var payload = await ReadJsonAsync(response);
        Assert.Equal(-32001, payload["error"]?["code"]?.GetValue<int>());
    }

    [Fact]
    public async Task Batch_SupportsMixedNotificationsAndRequests()
    {
        var (sessionId, protocolVersion) = await InitializeAndReadySessionAsync();
        var batch = new JsonArray
        {
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "ping"
            },
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 5,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "mcp.status",
                    ["arguments"] = new JsonObject(),
                }
            }
        };

        using var response = await PostRawJsonAsync(batch.ToJsonString(), sessionId, protocolVersion);
        var payload = await ReadJsonAsync(response);

        Assert.True(payload is JsonArray);
        var responses = payload.AsArray();
        Assert.Single(responses);
        Assert.Equal(5, responses[0]?["id"]?.GetValue<int>());
    }

    [Fact]
    public async Task GetOptionsAndDelete_FollowTransportRules()
    {
        var (sessionId, protocolVersion) = await InitializeAndReadySessionAsync();

        using var optionsRequest = new HttpRequestMessage(HttpMethod.Options, "/mcp");
        using var optionsResponse = await _fixture.Client.SendAsync(optionsRequest);
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        Assert.Equal("2025-11-25", optionsResponse.Headers.GetValues("MCP-Protocol-Version").Single());
        Assert.Contains("GET", string.Join(',', optionsResponse.Content.Headers.Allow));

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        getRequest.Headers.Add("MCP-Session-Id", sessionId);
        getRequest.Headers.Add("MCP-Protocol-Version", protocolVersion);
        using var getResponse = await _fixture.Client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.StartsWith("text/event-stream", getResponse.Content.Headers.ContentType?.ToString(), StringComparison.OrdinalIgnoreCase);
        using (var stream = await getResponse.Content.ReadAsStreamAsync())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            var firstChunk = await reader.ReadLineAsync();
            Assert.Contains("ExcelMCP MCP stream established", firstChunk ?? string.Empty);
        }

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/mcp");
        deleteRequest.Headers.Add("MCP-Session-Id", sessionId);
        using var deleteResponse = await _fixture.Client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var postAfterDelete = await PostJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 6,
            ["method"] = "tools/list",
            ["params"] = new JsonObject(),
        }, sessionId, protocolVersion);
        var payload = await ReadJsonAsync(postAfterDelete);
        Assert.Equal(-32001, payload["error"]?["code"]?.GetValue<int>());
    }

    [Fact]
    public async Task PreviewWriteCells_ValidatesRequiredArguments()
    {
        var (sessionId, protocolVersion) = await InitializeAndReadySessionAsync();

        using var response = await PostJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 7,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "excel.preview_write_cells",
                ["arguments"] = new JsonObject
                {
                    ["excelPath"] = "C:/tmp/test.xlsx",
                    ["startCell"] = "A1"
                }
            }
        }, sessionId, protocolVersion);

        var payload = await ReadJsonAsync(response);
        Assert.True(payload["result"]?["isError"]?.GetValue<bool>() ?? false);
        Assert.Contains("Missing required argument: values", payload["result"]?["structuredContent"]?["msg"]?.GetValue<string>() ?? string.Empty);
    }

    private async Task<(string SessionId, string ProtocolVersion)> InitializeSessionAsync()
    {
        using var response = await PostJsonAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 100,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = "2025-11-25",
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "tests",
                    ["version"] = "1.0.0",
                }
            }
        });

        return (
            response.Headers.GetValues("MCP-Session-Id").Single(),
            response.Headers.GetValues("MCP-Protocol-Version").Single());
    }

    private async Task<(string SessionId, string ProtocolVersion)> InitializeAndReadySessionAsync()
    {
        var (sessionId, protocolVersion) = await InitializeSessionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", method = "notifications/initialized", @params = new { } })
        };
        request.Headers.Add("MCP-Session-Id", sessionId);
        request.Headers.Add("MCP-Protocol-Version", protocolVersion);
        using var response = await _fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return (sessionId, protocolVersion);
    }

    private Task<HttpResponseMessage> PostJsonAsync(JsonNode payload, string? sessionId = null, string? protocolVersion = null)
    {
        return PostRawJsonAsync(payload.ToJsonString(), sessionId, protocolVersion);
    }

    private Task<HttpResponseMessage> PostRawJsonAsync(string payload, string? sessionId = null, string? protocolVersion = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
            request.Headers.Add("MCP-Session-Id", sessionId);
        if (!string.IsNullOrWhiteSpace(protocolVersion))
            request.Headers.Add("MCP-Protocol-Version", protocolVersion);
        return _fixture.Client.SendAsync(request);
    }

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(text));
        return JsonNode.Parse(text)!;
    }
}
