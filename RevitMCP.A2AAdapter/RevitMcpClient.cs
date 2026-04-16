using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitMCP.A2AAdapter;

public sealed class RevitMcpClient
{
    private readonly HttpClient _http;
    private readonly A2AOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public RevitMcpClient(HttpClient http, A2AOptions options, JsonSerializerOptions jsonOptions)
    {
        _http = http;
        _options = options;
        _jsonOptions = jsonOptions;
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.BlockingTimeoutSeconds + 10));
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonNode?> GetHealthAsync(CancellationToken cancellationToken)
    {
        var url = Combine(_options.RevitMcpServerUrl, "health");
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new JsonObject { ["ok"] = false, ["statusCode"] = (int)response.StatusCode, ["body"] = text };
        return ParseOrText(text);
    }

    public async Task<JsonNode?> PostRpcAsync(string method, JsonNode? parameters, string rpcId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = rpcId,
            ["method"] = method,
            ["params"] = parameters?.DeepClone() ?? new JsonObject()
        };

        var url = Combine(_options.RevitMcpServerUrl, "rpc");
        using var content = new StringContent(payload.ToJsonString(_jsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = rpcId,
                ["error"] = new JsonObject
                {
                    ["code"] = -32080,
                    ["message"] = "RevitMCPServer RPC call failed.",
                    ["data"] = new JsonObject
                    {
                        ["statusCode"] = (int)response.StatusCode,
                        ["body"] = text
                    }
                }
            };
        }

        return ParseOrText(text);
    }

    public async Task<JsonNode?> GetJobAsync(string jobId, CancellationToken cancellationToken)
    {
        var url = Combine(_options.RevitMcpServerUrl, "job/" + Uri.EscapeDataString(jobId));
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new JsonObject { ["ok"] = false, ["statusCode"] = (int)response.StatusCode, ["body"] = text };
        return ParseOrText(text);
    }

    public static JsonNode? UnwrapJsonRpcPayload(JsonNode? node)
    {
        var cur = node;
        for (var i = 0; i < 4; i++)
        {
            if (cur is JsonObject obj && obj.TryGetPropertyValue("result", out var result) && result != null)
            {
                cur = result;
                continue;
            }
            break;
        }
        return cur;
    }

    public static string? TryGetString(JsonNode? node, string name)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(name, out var value) || value == null)
            return null;
        try { return value.GetValue<string>(); }
        catch { return value.ToString(); }
    }

    public static JsonNode? TryGetNode(JsonNode? node, string name)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(name, out var value))
            return null;
        return value?.DeepClone();
    }

    private static JsonNode? ParseOrText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try { return JsonNode.Parse(text); }
        catch { return JsonValue.Create(text); }
    }

    private static string Combine(string baseUrl, string relative)
    {
        baseUrl = (baseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://127.0.0.1:5210";
        return baseUrl.TrimEnd('/') + "/" + relative.TrimStart('/');
    }
}
