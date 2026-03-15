using System.Text.Json.Nodes;

internal sealed class ExcelMcpToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonObject InputSchema { get; init; }
    public JsonObject? OutputExample { get; init; }
    public string? HttpMethod { get; init; }
    public string? Path { get; init; }
    public JsonObject? Annotations { get; init; }
    public Func<ExcelMcpToolInvocationContext, CancellationToken, Task<ExcelMcpToolExecutionResult>>? Handler { get; init; }

    public bool IsCustom => Handler is not null;
}

internal sealed class ExcelMcpToolInvocationContext
{
    public required HttpContext HttpContext { get; init; }
    public required ExcelMcpSessionState Session { get; init; }
    public required JsonObject Arguments { get; init; }
}

internal sealed class ExcelMcpToolExecutionResult
{
    public required JsonNode Payload { get; init; }
    public bool IsError { get; init; }
}
