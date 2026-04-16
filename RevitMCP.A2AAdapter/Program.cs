using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using RevitMCP.A2AAdapter;

var builder = WebApplication.CreateBuilder(args);

var options = ResolveOptions(builder.Configuration, args);
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
};

builder.WebHost.UseUrls($"http://{options.BindHost}:{options.Port}");
builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 10_000_000; });

builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = jsonOptions.PropertyNamingPolicy;
    o.SerializerOptions.DefaultIgnoreCondition = jsonOptions.DefaultIgnoreCondition;
});
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(jsonOptions);
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton<RevitMcpClient>();
builder.Services.AddSingleton<TaskStore>();
builder.Services.AddSingleton<A2AService>();

var app = builder.Build();
var startedUtc = DateTimeOffset.UtcNow;

app.MapGet("/", (HttpRequest req) => Results.Json(new
{
    ok = true,
    name = "RevitMCP.A2AAdapter",
    startedUtc = startedUtc.ToString("o"),
    agentCard = "/.well-known/agent-card.json",
    a2aRpc = "/a2a/rpc",
    target = options.RevitMcpServerUrl
}));

app.MapGet("/health", async (RevitMcpClient revit, CancellationToken ct) =>
{
    JsonNode? targetHealth;
    try { targetHealth = await revit.GetHealthAsync(ct).ConfigureAwait(false); }
    catch (Exception ex) { targetHealth = new JsonObject { ["ok"] = false, ["msg"] = ex.Message }; }

    return Results.Json(new
    {
        ok = true,
        name = "RevitMCP.A2AAdapter",
        protocolVersion = options.ProtocolVersion,
        startedUtc = startedUtc.ToString("o"),
        target = options.RevitMcpServerUrl,
        targetHealth
    });
});

app.MapGet("/.well-known/agent-card.json", (HttpRequest req) =>
    Results.Json(AgentCardFactory.Build(req, options), jsonOptions));

app.MapGet("/a2a/agent-card", (HttpRequest req) =>
    Results.Json(AgentCardFactory.Build(req, options), jsonOptions));

app.MapPost("/a2a/rpc", async (HttpContext ctx, A2AService service) =>
{
    JsonNode? id = null;
    try
    {
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return Results.Json(JsonRpcUtil.Error(null, -32600, "JSON-RPC request object is required."), jsonOptions, statusCode: 400);

        id = JsonRpcUtil.CloneId(doc.RootElement);
        var response = await service.HandleAsync(doc.RootElement, ctx.Request, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Json(response, jsonOptions);
    }
    catch (JsonException ex)
    {
        return Results.Json(JsonRpcUtil.Error(id, -32700, "Invalid JSON payload.", JsonValue.Create(ex.Message)), jsonOptions, statusCode: 400);
    }
});

app.Run();

static A2AOptions ResolveOptions(IConfiguration configuration, string[] args)
{
    var section = configuration.GetSection("A2AAdapter");
    var port = ReadIntArg(args, "--port=")
            ?? ReadIntEnv("REVIT_MCP_A2A_PORT")
            ?? section.GetValue<int?>("Port")
            ?? 5220;

    var bindHost = ReadStringArg(args, "--bind-host=")
                ?? Environment.GetEnvironmentVariable("REVIT_MCP_A2A_BIND_HOST")
                ?? section.GetValue<string>("BindHost")
                ?? "127.0.0.1";

    var target = ReadStringArg(args, "--target=")
              ?? Environment.GetEnvironmentVariable("REVIT_MCP_A2A_TARGET_URL")
              ?? section.GetValue<string>("RevitMcpServerUrl")
              ?? "http://127.0.0.1:5210";

    var protocolVersion = Environment.GetEnvironmentVariable("REVIT_MCP_A2A_PROTOCOL_VERSION")
                       ?? section.GetValue<string>("ProtocolVersion")
                       ?? "0.3";

    var agentVersion = Environment.GetEnvironmentVariable("REVIT_MCP_A2A_AGENT_VERSION")
                    ?? section.GetValue<string>("AgentVersion")
                    ?? "0.1.0";

    var blockingTimeoutSeconds = ReadIntEnv("REVIT_MCP_A2A_BLOCKING_TIMEOUT_SECONDS")
                              ?? section.GetValue<int?>("BlockingTimeoutSeconds")
                              ?? 60;

    if (port <= 0 || port > 65535)
        port = 5220;

    bindHost = NormalizeBindHost(bindHost);

    return new A2AOptions
    {
        Port = port,
        BindHost = bindHost,
        RevitMcpServerUrl = NormalizeTargetUrl(target),
        ProtocolVersion = protocolVersion.Trim(),
        AgentVersion = agentVersion.Trim(),
        BlockingTimeoutSeconds = Math.Clamp(blockingTimeoutSeconds, 1, 3600)
    };
}

static string NormalizeBindHost(string value)
{
    value = (value ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(value))
        return "127.0.0.1";
    if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
        return "127.0.0.1";
    if (IPAddress.TryParse(value, out _))
        return value;
    return "127.0.0.1";
}

static string NormalizeTargetUrl(string value)
{
    value = (value ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(value))
        value = "http://127.0.0.1:5210";
    return value.TrimEnd('/');
}

static string? ReadStringArg(string[] args, string prefix)
{
    foreach (var arg in args ?? Array.Empty<string>())
    {
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return arg.Substring(prefix.Length).Trim();
    }
    return null;
}

static int? ReadIntArg(string[] args, string prefix)
{
    var s = ReadStringArg(args, prefix);
    return int.TryParse(s, out var value) ? value : null;
}

static int? ReadIntEnv(string name)
{
    var s = Environment.GetEnvironmentVariable(name);
    return int.TryParse(s, out var value) ? value : null;
}
