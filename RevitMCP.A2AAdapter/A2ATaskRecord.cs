using System.Text.Json.Nodes;

namespace RevitMCP.A2AAdapter;

public sealed class A2ATaskRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ContextId { get; init; } = Guid.NewGuid().ToString("N");
    public string State { get; set; } = A2AStates.Submitted;
    public string? RevitMethod { get; set; }
    public string? RevitJobId { get; set; }
    public string? ErrorMessage { get; set; }
    public string CreatedUtc { get; init; } = DateTimeOffset.UtcNow.ToString("o");
    public string UpdatedUtc { get; set; } = DateTimeOffset.UtcNow.ToString("o");
    public JsonNode? ResultPayload { get; set; }
    public List<JsonNode> History { get; } = new();
}

public static class A2AStates
{
    public const string Submitted = "submitted";
    public const string Working = "working";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Rejected = "rejected";

    public static bool IsTerminal(string? state)
    {
        return string.Equals(state, Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, Failed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, Canceled, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, Rejected, StringComparison.OrdinalIgnoreCase);
    }
}
