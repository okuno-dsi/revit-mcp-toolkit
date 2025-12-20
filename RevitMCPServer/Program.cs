// ================================================================
// Revit MCP Server - Minimal Host (JSON-RPC queue bridge)
// ================================================================

#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RevitMcpServer.Engine; // DurableQueue, JobIndex
// SSR dependencies removed
using RevitMcpServer.Infra; // Logging
using RevitMcpServer.Persistence; // SqliteConnectionFactory

// ----------------------------- Bootstrap -----------------------------
var builder = WebApplication.CreateBuilder(args);

int startPort = 5210;
try
{
    var envPort = Environment.GetEnvironmentVariable("PORT") ?? Environment.GetEnvironmentVariable("MCP_SERVER_PORT");
    if (int.TryParse(envPort, out var p) && p > 0 && p < 65536) startPort = p;
    foreach (var a in args)
    {
        if (a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(a.Substring("--port=".Length), out var cli) && cli > 0 && cli < 65536) startPort = cli;
        }
    }
}
catch { }

var chosenPort = RevitMcpServer.Infra.PortLocker.AcquireAvailablePort(startPort);
builder.WebHost.UseUrls($"http://127.0.0.1:{chosenPort}");
builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 10_000_000; });

// Services
builder.Services.AddSingleton<DurableQueue>();
builder.Services.AddSingleton<JobIndex>();

var app = builder.Build();

// Initialize logging
try { RevitMcpServer.Infra.Logging.Init(chosenPort); } catch { }

// Expose server port to engine/infra and configure per-port queue path
try
{
    ServerContext.Port = chosenPort;
    var qOverride = Environment.GetEnvironmentVariable("REVIT_MCP_QUEUE_DIR") ?? Environment.GetEnvironmentVariable("MCP_QUEUE_DIR");
    SqliteConnectionFactory.Configure(chosenPort, qOverride);
}
catch { /* best-effort */ }

// Configure docs/manifest cache path away from wwwroot (SSR removed)
try { RevitMcpServer.Docs.ManifestRegistry.ConfigureCachePath(Path.Combine(AppContext.BaseDirectory, "Results", "manifest-cache.json")); } catch { }

// ----------------------------- Root -----------------------------
app.MapGet("/", () => Results.Json(new { ok = true, port = chosenPort, message = "Revit MCP Server (SSR disabled)" }));

// ----------------------------- Health -----------------------------
app.MapGet("/health", () => Results.Json(new { ok = true, port = chosenPort, time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }));

// Debug (lightweight)
app.MapGet("/debug", () => Results.Json(new { ok = true, port = chosenPort, ssr = "disabled" }));

// ----------------------------- JSON-RPC Bridge -----------------------------
var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

// Enqueue via /rpc/{method}
app.MapPost("/rpc/{method}", async (HttpContext ctx, string method, DurableQueue durable, JobIndex index) =>
{
    string raw; using (var r = new StreamReader(ctx.Request.Body, Encoding.UTF8)) raw = await r.ReadToEndAsync();
    string id = "1"; JsonElement? prm = null;
    try
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("id", out var idEl)) id = idEl.ToString();
                if (root.TryGetProperty("params", out var p)) prm = p.Clone();
            }
        }
    }
    catch { }

    string paramsJson = (prm.HasValue && prm.Value.ValueKind != JsonValueKind.Undefined) ? prm.Value.ToString() : "{}";
    var jobId = await durable.EnqueueAsync(method, paramsJson, null, id, 100, 60);
    index.Put(id, jobId);
    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] REQUEST: POST /rpc/{method} -> jobId={jobId}");
    var hint = new { ok = true, queued = true, jobId = jobId };
    return Results.Json(new { jsonrpc = "2.0", id = id, result = hint }, jsonOpts);
});

// Enqueue via /rpc (body must contain method)
app.MapPost("/rpc", async (HttpContext ctx, DurableQueue durable, JobIndex index) =>
{
    string body; using (var r = new StreamReader(ctx.Request.Body, Encoding.UTF8)) body = await r.ReadToEndAsync();
    string id = "1", method = ""; JsonElement? prm = null;
    try
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String)
        {
            method = m.GetString() ?? "";
            if (root.TryGetProperty("id", out var idEl)) id = idEl.ToString();
            if (root.TryGetProperty("params", out var p)) prm = p.Clone();
        }
        else return Results.Json(new { ok = false, code = "INVALID_JSONRPC", msg = "Root object with 'method' required." }, statusCode: 400);
    }
    catch (Exception ex) { return Results.Json(new { ok = false, code = "INVALID_JSON", msg = ex.Message }, statusCode: 400); }

    string paramsJson = (prm.HasValue && prm.Value.ValueKind != JsonValueKind.Undefined) ? prm.Value.ToString() : "{}";
    var jobId = await durable.EnqueueAsync(method, paramsJson, null, id, 100, 60);
    index.Put(id, jobId);
    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] REQUEST: POST /rpc (method={method}) -> jobId={jobId}");
    var hint = new { ok = true, queued = true, jobId = jobId };
    return Results.Json(new { jsonrpc = "2.0", id = id, result = hint }, jsonOpts);
});

// Manual: enqueue baseline snapshot jobs (disabled with SSR removal)
app.MapPost("/enqueue_baseline", () => Results.Json(new { ok = true, note = "baseline enqueue disabled (SSR removed)" }));

// Client -> Add-in: pending request
app.MapGet("/pending_request", async (HttpContext httpCtx, DurableQueue durable) =>
{
    int waitMs = 0; int.TryParse(httpCtx.Request.Query["waitMs"], out waitMs);
    dynamic? job = null; var start = DateTime.UtcNow;
    do { job = await durable.ClaimAsync(); if (job != null || waitMs <= 0) break; await Task.Delay(100); } while ((DateTime.UtcNow - start).TotalMilliseconds < waitMs);
    if (job == null) return Results.NoContent();
    string jobId = (string)job["job_id"]; string method = (string)job["method"]; string pjson = (string)job["params_json"]; string rpcId = job["rpc_id"] != null ? Convert.ToString(job["rpc_id"])! : "1";
    await durable.StartRunningAsync(jobId);
    var rpc = new JsonObject { ["jsonrpc"] = JsonValue.Create("2.0"), ["method"] = JsonValue.Create(method), ["params"] = SafeParseOrEmpty(pjson), ["id"] = JsonValue.Create(rpcId) };
    var text = rpc.ToJsonString();
    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RESPONSE: pending_request -> {method}");
    return Results.Text(text, "application/json", Encoding.UTF8);
});

// Add-in -> Server: post final result
app.MapPost("/post_result", async (HttpContext ctx, DurableQueue durable, JobIndex index) =>
{
    string result; using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8)) result = await reader.ReadToEndAsync();
    // Try resolve rpcId -> jobId and complete
    try
    {
        string rpcId = "";
        try { using var d2 = JsonDocument.Parse(result); if (d2.RootElement.TryGetProperty("id", out var idEl2)) rpcId = idEl2.ToString(); } catch { }
        if (!string.IsNullOrEmpty(rpcId))
        {
            var jobId2 = index.TryGet(rpcId, out var mapped) ? mapped : await durable.FindRunningJobIdByRpcIdAsync(rpcId);
            if (!string.IsNullOrEmpty(jobId2)) await durable.CompleteAsync(jobId2!, result);
        }
    }
    catch { }

    // SSR ingest removed

    return Results.Ok(new { ok = true });
});

// Get result (client UI poll)
app.MapGet("/get_result", async (HttpRequest req, DurableQueue durable) =>
{
    var jobId = req.Query["jobId"].ToString();
    if (!string.IsNullOrWhiteSpace(jobId))
    {
        try
        {
            var row = await durable.GetAsync(jobId);
            if (row != null && row.ContainsKey("result_json") && row["result_json"] != null)
            {
                var s = Convert.ToString(row["result_json"]);
                if (!string.IsNullOrEmpty(s)) return Results.Text(s, "application/json", Encoding.UTF8);
            }
        }
        catch { }
        return Results.NoContent();
    }
    return Results.NoContent();
});

// ----------------------------- Durable helpers (compat) -----------------------------
app.MapPost("/enqueue", async (HttpContext ctx, DurableQueue durable, JobIndex index) =>
{
    string body; using (var r = new StreamReader(ctx.Request.Body, Encoding.UTF8)) body = await r.ReadToEndAsync();
    string method = ""; string id = Guid.NewGuid().ToString("N"); int priority = 100; int timeoutSec = 60; JsonElement? prm = null; string? idemKey = null;
    try
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String) method = m.GetString() ?? "";
            if (root.TryGetProperty("id", out var idEl)) id = idEl.ToString();
            if (root.TryGetProperty("params", out var p)) prm = p.Clone();
            if (root.TryGetProperty("priority", out var pr) && pr.TryGetInt32(out var pi)) priority = pi;
            if (root.TryGetProperty("timeoutSec", out var to) && to.TryGetInt32(out var ti)) timeoutSec = ti;
            if (root.TryGetProperty("idempotencyKey", out var ik)) idemKey = ik.ToString();
        }
    }
    catch (Exception ex) { return Results.Json(new { ok = false, code = "INVALID_JSON", msg = ex.Message }, statusCode: 400); }
    if (string.IsNullOrWhiteSpace(method)) return Results.Json(new { ok = false, code = "E_NO_METHOD" }, statusCode: 400);
    string paramsJson = (prm.HasValue && prm.Value.ValueKind != JsonValueKind.Undefined) ? prm.Value.ToString() : "{}";
    var jobId = await durable.EnqueueAsync(method, paramsJson, idemKey, id, priority, timeoutSec);
    index.Put(id, jobId);
    Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ENQUEUE: method={method} jobId={jobId}");
    // Stamp server identity for diagnostics (non-breaking additional fields)
    int srvPid = System.Diagnostics.Process.GetCurrentProcess().Id;
    int srvPort = TryResolveServerPort();
    return Results.Json(new { ok = true, jobId, serverPid = srvPid, serverPort = srvPort });
});

// /cache removed with SSR

app.MapGet("/job/{id}", async (string id, DurableQueue durable) =>
{
    try
    {
        var row = await durable.GetAsync(id);
        if (row != null) return Results.Json(row);
        return Results.Json(new { ok = false, code = "E_NOT_FOUND" }, statusCode: 404);
    }
    catch (Exception ex) { return Results.Json(new { ok = false, code = "EXCEPTION", msg = ex.Message }, statusCode: 500); }
});

// Graceful shutdown endpoint (local only)
app.MapPost("/shutdown", (HttpContext ctx) =>
{
    try
    {
        // respond first, then exit
        _ = Task.Run(async () => { await Task.Delay(200); Environment.Exit(0); });
        int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        return Results.Json(new { ok = true, msg = "Shutting down", serverPid = pid });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, code = "SHUTDOWN_FAIL", msg = ex.Message }, statusCode: 500);
    }
});

app.MapGet("/jobs", async (HttpRequest req, DurableQueue durable) =>
{
    try
    {
        var state = req.Query["state"].ToString();
        if (string.IsNullOrWhiteSpace(state)) state = "ENQUEUED";
        int limit = 50; int.TryParse(req.Query["limit"], out limit); if (limit <= 0) limit = 50;
        var list = await durable.ListAsync(state, limit);
        return Results.Json(new { ok = true, items = list });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, code = "EXCEPTION", msg = ex.Message }, statusCode: 500); }
});

// Snapshot ingest endpoints removed with SSR

app.Run();

// ----------------------------- Helpers -----------------------------
static JsonNode SafeParseOrEmpty(string? json)
{
    try { if (!string.IsNullOrWhiteSpace(json)) return JsonNode.Parse(json!) ?? new JsonObject(); }
    catch { }
    return new JsonObject();
}

// Baseline auto-enqueue removed with SSR

static int TryResolveServerPort()
{
    try
    {
        // Prefer explicit REVIT_MCP_PORT
        var env = Environment.GetEnvironmentVariable("REVIT_MCP_PORT");
        if (int.TryParse(env, out var p) && p > 0 && p < 65536) return p;

        // Fallback: parse ASPNETCORE_URLS (e.g., http://localhost:5210)
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var first = urls.Split(';', ',', ' ', '\t', '\n')[0];
            if (Uri.TryCreate(first, UriKind.Absolute, out var uri))
            {
                if (uri.Port > 0) return uri.Port;
            }
        }
    }
    catch { }
    return 0;
}


