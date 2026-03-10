using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using AutoCadMcpServer.Router;
using AutoCadMcpServer.Core;

var builder = WebApplication.CreateBuilder(args);

// Enable code pages (e.g., Shift-JIS cp932) for script encoding
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Initialize result store (for persisted result logs)
await AutoCadMcpServer.Core.ResultStore.Instance.InitAsync(app.Configuration);

app.MapGet("/health", () => Results.Json(new { ok = true, ts = DateTimeOffset.Now }));
app.MapGet("/version", () => Results.Json(new { ok = true, version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0" }));

// --- Minimal queue endpoints for GUI bridge (AutoCAD-GUI-Merge-MCP.md) ---
// POST /enqueue -> store JSON-RPC job for GUI add-in to claim
app.MapPost("/enqueue", async (HttpContext ctx) =>
{
    using var sr = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var raw = await sr.ReadToEndAsync();
    string bodyText;
    try
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("{") && !trimmed.StartsWith("["))
        {
            var path = trimmed.Trim('"', '\'', ' ', '\\');
            if (!File.Exists(path))
                return Results.BadRequest(new { error = $"File not found: {path}" });
            bodyText = await File.ReadAllTextAsync(path, Encoding.UTF8);
        }
        else
        {
            bodyText = raw ?? string.Empty;
        }
        bodyText = JsonCommentStripper.Strip(bodyText);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = new { code = -32700, message = "Parse error", detail = ex.Message } });
    }

    JsonRpcReq? req = null;
    try
    {
        req = JsonSerializer.Deserialize<JsonRpcReq>(bodyText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (req == null) throw new Exception("Invalid JSON-RPC request");
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = new { code = -32600, message = "Invalid request", detail = ex.Message } });
    }

    // PathGuard validation for known methods
    try
    {
        if (string.Equals(req.method, "merge_dwgs_perfile_rename", StringComparison.OrdinalIgnoreCase))
        {
            var p = req.@params;
            if (p is null) throw new RpcError(400, "E_INVALID_PARAMS");
            var arr = p["inputs"] as JsonArray ?? throw new RpcError(400, "E_NO_INPUTS");
            foreach (var x in arr)
            {
                if (x is JsonObject jo)
                {
                    var path = jo["path"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(path)) PathGuard.EnsureAllowedDwg(path!, app.Configuration);
                }
                else
                {
                    var s = x?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(s)) PathGuard.EnsureAllowedDwg(s!, app.Configuration);
                }
            }
            var output = p["output"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(output)) throw new RpcError(400, "E_NO_OUTPUT");
            PathGuard.EnsureAllowedOutput(output, app.Configuration);
        }
    }
    catch (RpcError ex)
    {
        return Results.Json(new { ok = false, error = new { code = ex.Code, message = ex.Message } });
    }

    var id = PendingRequestStore.Instance.Enqueue(req, bodyText);
    return Results.Json(new { ok = true, enqueued = true, id });
});

// GET /pending_request?agent=acad&accept=merge_dwgs_perfile_rename -> claim next job
app.MapGet("/pending_request", (HttpRequest req) =>
{
    var agent = req.Query["agent"].ToString();
    var accept = req.Query["accept"].ToString();
    if (string.IsNullOrWhiteSpace(accept)) return Results.BadRequest(new { error = "accept is required" });

    var job = PendingRequestStore.Instance.TryClaim(agent, accept);
    if (job == null) return Results.NoContent(); // client treats empty body as no job
    return Results.Text(job.Body, "application/json", Encoding.UTF8);
});

// POST /post_result -> add-in posts back result for a claimed id
app.MapPost("/post_result", async (HttpContext ctx) =>
{
    using var sr = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var raw = await sr.ReadToEndAsync();
    string bodyText = JsonCommentStripper.Strip(raw ?? string.Empty);

    try
    {
        var jo = JsonNode.Parse(bodyText) as JsonObject ?? throw new Exception("Invalid JSON");
        var id = jo["id"]?.GetValue<string>() ?? jo["jobId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id)) return Results.BadRequest(new { ok = false, error = "Missing id" });
        var resultNode = jo["result"] ?? jo["data"] ?? jo["payload"];
        object result = (object?)resultNode ?? new { ok = true };
        PendingRequestStore.Instance.PostResult(id!, result);
        return Results.Json(new { ok = true, id });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = new { code = 500, message = ex.Message } });
    }
});

// GET /get_result?id=... -> agent polls for result
app.MapGet("/get_result", (HttpRequest req) =>
{
    var id = req.Query["id"].ToString();
    if (string.IsNullOrWhiteSpace(id)) return Results.BadRequest(new { ok = false, error = "Missing id" });
    var (found, res) = PendingRequestStore.Instance.GetResult(id);
    if (!found || res == null) return Results.NotFound(new { ok = false, error = "Not ready" });
    return Results.Json(res);
});

app.MapPost("/rpc", async (HttpContext ctx) =>
{
    using var sr = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var raw = await sr.ReadToEndAsync();

    // Support: request body can be a file path to a JSON file (may include comments),
    // or raw JSON (also may include comments). Aligns with Codex variant behavior.
    string bodyText;
    try
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("{") && !trimmed.StartsWith("["))
        {
            var path = trimmed.Trim('"', '\'', ' ', '\\');
            if (!File.Exists(path))
                return Results.BadRequest(new { error = $"File not found: {path}" });
            bodyText = await File.ReadAllTextAsync(path, Encoding.UTF8);
        }
        else
        {
            bodyText = raw ?? string.Empty;
        }
        // Allow JSON with comments (// and /* */) by stripping prior to parsing
        bodyText = JsonCommentStripper.Strip(bodyText);
    }
    catch (Exception ex)
    {
        return Results.Json(new { jsonrpc = "2.0", id = (object?)null, error = new { code = -32700, message = "Parse error", data = ex.Message } });
    }

    JsonRpcReq? req = null;
    try
    {
        req = JsonSerializer.Deserialize<JsonRpcReq>(bodyText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (req == null) throw new Exception("Invalid JSON-RPC request");
    }
    catch (Exception ex)
    {
        return Results.Json(new { jsonrpc = "2.0", id = (object?)null, error = new { code = -32700, message = "Parse error", data = ex.Message } });
    }

    try
    {
        var result = await RpcRouter.Dispatch(req, app.Logger, app.Configuration);
        return Results.Json(new { jsonrpc = "2.0", id = req.id, result });
    }
    catch (RpcError ex)
    {
        return Results.Json(new { jsonrpc = "2.0", id = req.id, error = new { code = ex.Code, message = ex.Message, data = ex.Data } });
    }
    catch (Exception ex)
    {
        return Results.Json(new { jsonrpc = "2.0", id = req?.id, error = new { code = 500, message = ex.Message, data = ex.ToString() } });
    }
});

app.MapGet("/result/{jobId}", (string jobId) =>
{
    if (AutoCadMcpServer.Core.ResultStore.Instance.TryGet(jobId, out var res) && res != null)
        return Results.Json(res);

    // Fallback: scan common staging roots (Codex variant behavior)
    var roots = new[] { "C:/CadJobs/Staging", "C:/Temp/CadJobs/Staging" };
    foreach (var r in roots)
    {
        try
        {
            var jobDir = Path.Combine(r, jobId);
            if (Directory.Exists(jobDir))
            {
                var outDir = Path.Combine(jobDir, "out");
                var merged = Path.Combine(outDir, "merged.dwg");
                var exists = File.Exists(merged);
                return Results.Json(new { jobId, jobDir, outDir, mergedPath = merged, exists });
            }
        }
        catch { /* ignore */ }
    }
    return Results.NotFound(new { ok = false, msg = "Job not found" });
});

var mcpSessions = new AutoCadMcpSessionStore();
var mcpJsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapMethods("/mcp", new[] { "OPTIONS" }, (HttpContext ctx) =>
{
    ctx.Response.Headers[AutoCadMcpProtocol.ProtocolHeader] = AutoCadMcpProtocol.DefaultProtocolVersion;
    return Results.Ok();
});

app.MapPost("/mcp", async (HttpContext ctx) =>
{
    JsonObject request;
    try
    {
        using var sr = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        var raw = await sr.ReadToEndAsync();
        var stripped = JsonCommentStripper.Strip(raw ?? string.Empty);
        request = JsonNode.Parse(stripped) as JsonObject
            ?? throw new Exception("JSON-RPC request object is required.");
    }
    catch (Exception ex)
    {
        return Results.Json(AutoCadMcpProtocol.Error(null, -32700, "Invalid JSON.", new { detail = ex.Message }), mcpJsonOpts);
    }

    var idNode = request["id"];
    var method = request["method"]?.GetValue<string>() ?? string.Empty;
    var prm = request["params"] as JsonObject ?? new JsonObject();
    var sessionId = ctx.Request.Headers[AutoCadMcpProtocol.SessionHeader].ToString();

    if (string.Equals(method, "initialize", StringComparison.OrdinalIgnoreCase))
    {
        var protocolVersion = prm["protocolVersion"]?.GetValue<string>()
            ?? ctx.Request.Headers[AutoCadMcpProtocol.ProtocolHeader].ToString()
            ?? AutoCadMcpProtocol.DefaultProtocolVersion;
        var session = mcpSessions.Create(protocolVersion);
        ctx.Response.Headers[AutoCadMcpProtocol.SessionHeader] = session.SessionId;
        ctx.Response.Headers[AutoCadMcpProtocol.ProtocolHeader] = session.ProtocolVersion;
        return Results.Json(
            AutoCadMcpProtocol.Success(idNode, AutoCadMcpProtocol.InitializeResult(session.ProtocolVersion)),
            mcpJsonOpts);
    }

    if (!mcpSessions.TryGet(sessionId, out var state))
    {
        return Results.Json(
            AutoCadMcpProtocol.Error(idNode, -32001, "Missing or invalid MCP session. Call initialize first."),
            mcpJsonOpts);
    }

    ctx.Response.Headers[AutoCadMcpProtocol.SessionHeader] = state.SessionId;
    ctx.Response.Headers[AutoCadMcpProtocol.ProtocolHeader] = state.ProtocolVersion;

    if (string.Equals(method, "notifications/initialized", StringComparison.OrdinalIgnoreCase))
    {
        mcpSessions.MarkInitialized(state.SessionId);
        return Results.Json(AutoCadMcpProtocol.Success(idNode, new { }), mcpJsonOpts);
    }

    if (!state.IsInitialized)
    {
        return Results.Json(
            AutoCadMcpProtocol.Error(idNode, -32002, "Session not initialized. Send notifications/initialized after initialize."),
            mcpJsonOpts);
    }

    if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(AutoCadMcpProtocol.Success(idNode, new { }), mcpJsonOpts);
    }

    if (string.Equals(method, "tools/list", StringComparison.OrdinalIgnoreCase))
    {
        var tools = AutoCadRpcMethodCatalog.Methods
            .Select(name => new
            {
                name,
                description = $"AutoCAD RPC method '{name}'",
                inputSchema = new
                {
                    type = "object",
                    additionalProperties = true
                }
            })
            .Append(new
            {
                name = "mcp.status",
                description = "Return server and MCP session status.",
                inputSchema = new
                {
                    type = "object",
                    additionalProperties = false
                }
            })
            .ToArray();

        return Results.Json(AutoCadMcpProtocol.Success(idNode, new { tools }), mcpJsonOpts);
    }

    if (string.Equals(method, "tools/call", StringComparison.OrdinalIgnoreCase))
    {
        var toolName = prm["name"]?.GetValue<string>();
        var toolArgs = prm["arguments"] as JsonObject ?? new JsonObject();
        if (string.IsNullOrWhiteSpace(toolName))
            return Results.Json(AutoCadMcpProtocol.Error(idNode, -32602, "tools/call requires params.name."), mcpJsonOpts);

        JsonNode payload;
        var isError = false;
        try
        {
            if (string.Equals(toolName, "mcp.status", StringComparison.OrdinalIgnoreCase))
            {
                payload = JsonSerializer.SerializeToNode(new
                {
                    ok = true,
                    service = "AutoCadMcpServer",
                    mcp = new
                    {
                        sessionId = state.SessionId,
                        protocolVersion = state.ProtocolVersion,
                        initialized = state.IsInitialized
                    }
                })!;
            }
            else
            {
                var rpcReq = new AutoCadMcpServer.Router.JsonRpcReq(
                    "2.0",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    toolName!,
                    toolArgs);
                var result = await RpcRouter.Dispatch(rpcReq, app.Logger, app.Configuration);
                payload = JsonSerializer.SerializeToNode(result) ?? new JsonObject();
            }
        }
        catch (RpcError ex)
        {
            isError = true;
            payload = JsonSerializer.SerializeToNode(new { ok = false, code = ex.Code, msg = ex.Message, data = ex.Data })!;
        }
        catch (Exception ex)
        {
            isError = true;
            payload = JsonSerializer.SerializeToNode(new { ok = false, code = 500, msg = ex.Message })!;
        }

        return Results.Json(
            AutoCadMcpProtocol.Success(idNode, AutoCadMcpProtocol.ToolResult(payload, isError)),
            mcpJsonOpts);
    }

    return Results.Json(
        AutoCadMcpProtocol.Error(idNode, -32601, $"Method '{method}' not found."),
        mcpJsonOpts,
        statusCode: 404);
});

app.Run("http://127.0.0.1:5251");

namespace AutoCadMcpServer.Router
{
    public record JsonRpcReq(string jsonrpc, object id, string method, JsonObject @params);
    public class RpcError : Exception { public int Code; public object? Data; public RpcError(int c, string m, object? d = null) : base(m) { Code = c; Data = d; } }
}

// Minimal comment stripper for JSON bodies
internal static class JsonCommentStripper
{
    public static string Strip(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        bool inStr = false; bool inBlock = false; bool inLine = false; bool esc = false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';

            if (inLine)
            {
                if (c == '\n' || c == '\r') { inLine = false; sb.Append(c); }
                continue;
            }
            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                continue;
            }

            if (!inStr)
            {
                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }
            }

            sb.Append(c);
            if (c == '"' && !esc) inStr = !inStr;
            esc = (!esc && c == '\\');
        }
        return sb.ToString();
    }
}

internal static class AutoCadRpcMethodCatalog
{
    public static readonly string[] Methods = new[]
    {
        "merge_dwgs",
        "merge_dwgs_perfile_rename",
        "merge_dwgs_dxf_textmap",
        "probe_accoreconsole",
        "purge_audit",
        "consolidate_layers",
        "health",
        "version"
    };
}

internal static class AutoCadMcpProtocol
{
    public const string SessionHeader = "MCP-Session-Id";
    public const string ProtocolHeader = "MCP-Protocol-Version";
    public const string DefaultProtocolVersion = "2025-11-05";

    public static object InitializeResult(string protocolVersion) => new
    {
        protocolVersion,
        capabilities = new
        {
            tools = new { listChanged = false },
            resources = new { subscribe = false, listChanged = false },
            prompts = new { listChanged = false }
        },
        serverInfo = new { name = "AutoCadMcpServer", version = "1.0.0" },
        instructions = "Use tools/list then tools/call with AutoCAD RPC method names."
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

internal sealed class AutoCadMcpSessionStore
{
    private readonly ConcurrentDictionary<string, AutoCadMcpSessionState> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public AutoCadMcpSessionState Create(string protocolVersion)
    {
        var s = new AutoCadMcpSessionState
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ProtocolVersion = string.IsNullOrWhiteSpace(protocolVersion)
                ? AutoCadMcpProtocol.DefaultProtocolVersion
                : protocolVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow,
            IsInitialized = false
        };
        _sessions[s.SessionId] = s;
        return s;
    }

    public bool TryGet(string? sessionId, out AutoCadMcpSessionState state)
    {
        if (!string.IsNullOrWhiteSpace(sessionId) && _sessions.TryGetValue(sessionId, out state!))
        {
            state.LastSeenAtUtc = DateTimeOffset.UtcNow;
            return true;
        }
        state = null!;
        return false;
    }

    public bool MarkInitialized(string sessionId)
    {
        if (!TryGet(sessionId, out var s))
            return false;
        s.IsInitialized = true;
        s.LastSeenAtUtc = DateTimeOffset.UtcNow;
        return true;
    }
}

internal sealed class AutoCadMcpSessionState
{
    public string SessionId { get; set; } = "";
    public string ProtocolVersion { get; set; } = AutoCadMcpProtocol.DefaultProtocolVersion;
    public bool IsInitialized { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}

