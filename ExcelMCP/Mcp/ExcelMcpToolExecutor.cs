using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class ExcelMcpToolExecutor
{
    public const string SelfClientName = "ExcelMcpSelf";

    private readonly IHttpClientFactory _httpClientFactory;

    public ExcelMcpToolExecutor(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ExcelMcpToolExecutionResult> ExecuteAsync(
        ExcelMcpToolDefinition tool,
        ExcelMcpToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (tool.Handler is not null)
            return await tool.Handler(context, cancellationToken);

        if (string.IsNullOrWhiteSpace(tool.Path) || string.IsNullOrWhiteSpace(tool.HttpMethod))
        {
            return new ExcelMcpToolExecutionResult
            {
                Payload = JsonSerializer.SerializeToNode(new { ok = false, msg = $"Tool '{tool.Name}' is not wired to an endpoint." })!,
                IsError = true,
            };
        }

        var client = _httpClientFactory.CreateClient(SelfClientName);
        var baseUrl = $"{context.HttpContext.Request.Scheme}://{context.HttpContext.Request.Host}{context.HttpContext.Request.PathBase}";
        var url = $"{baseUrl}{tool.Path}";

        using var request = new HttpRequestMessage(new HttpMethod(tool.HttpMethod), url);
        if (!IsBodylessMethod(tool.HttpMethod))
        {
            var bodyText = context.Arguments.ToJsonString();
            request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? parsed;
        try
        {
            parsed = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch
        {
            parsed = JsonValue.Create(text);
        }

        var endpointOk = response.IsSuccessStatusCode;
        if (parsed is JsonObject parsedObject && parsedObject["ok"] is JsonNode okNode)
        {
            try
            {
                endpointOk = okNode.GetValue<bool>() && response.IsSuccessStatusCode;
            }
            catch
            {
                endpointOk = response.IsSuccessStatusCode;
            }
        }

        var payload = JsonSerializer.SerializeToNode(new
        {
            ok = endpointOk,
            tool = tool.Name,
            method = tool.HttpMethod,
            path = tool.Path,
            statusCode = (int)response.StatusCode,
            response = parsed,
        })!;

        return new ExcelMcpToolExecutionResult
        {
            Payload = payload,
            IsError = !endpointOk,
        };
    }

    private static bool IsBodylessMethod(string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);
}
