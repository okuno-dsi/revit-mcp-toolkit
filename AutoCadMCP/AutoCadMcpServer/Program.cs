using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

