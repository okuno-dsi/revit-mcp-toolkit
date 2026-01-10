// ================================================================
// Revit MCP Server - Minimal Host (JSON-RPC queue bridge)
// ================================================================

#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;
using System.Text;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RevitMcpServer.Engine; // DurableQueue, JobIndex
// SSR dependencies removed
using RevitMcpServer.Infra; // Logging
using RevitMcpServer.Persistence; // SqliteConnectionFactory

// ----------------------------- Bootstrap -----------------------------
var builder = WebApplication.CreateBuilder(args);
var serverStartedUtc = DateTimeOffset.UtcNow;

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

// Step 11: docs router (server-local commands) + load cached add-in manifest (best-effort)
RevitMCP.Abstractions.Rpc.RpcRouter? docsRouter = null;
try { docsRouter = RevitMcpServer.Docs.RouterBuilder.Build(); } catch { docsRouter = new RevitMCP.Abstractions.Rpc.RpcRouter(); }

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
try { RevitMcpServer.Docs.ManifestRegistry.LoadFromDisk(); } catch { }
try { RevitMcpServer.Docs.CapabilitiesGenerator.TryWriteDefault(RevitMcpServer.Docs.ManifestRegistry.GetAll()); } catch { }

// ----------------------------- Root -----------------------------
app.MapGet("/", () => Results.Json(new { ok = true, port = chosenPort, message = "Revit MCP Server (SSR disabled)" }));

// ----------------------------- Health -----------------------------
app.MapGet("/health", () => Results.Json(new { ok = true, port = chosenPort, time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }));

// Debug (lightweight)
app.MapGet("/debug", () => Results.Json(new { ok = true, port = chosenPort, ssr = "disabled" }));

// Debug: capabilities (machine-readable command implementation status)
app.MapGet("/debug/capabilities", (HttpRequest req) =>
{
    try
    {
        var methods = RevitMcpServer.Docs.ManifestRegistry.GetAll();
        var caps = RevitMcpServer.Docs.CapabilitiesGenerator.Build(methods);
        try { RevitMcpServer.Docs.CapabilitiesGenerator.WriteJsonl(RevitMcpServer.Docs.CapabilitiesGenerator.GetDefaultJsonlPath(), caps); } catch { }

        bool canonicalOnly = QueryFlag(req, "canonicalOnly");
        if (!QueryFlag(req, "includeDeprecated")) canonicalOnly = true;

        var filtered = canonicalOnly ? caps.Where(x => x != null && x.Deprecated == false).ToList() : caps;

        if (QueryFlag(req, "grouped"))
        {
            var groups = filtered
                .GroupBy(x => x.Canonical ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var canon = g.Key;
                    var primary =
                        g.FirstOrDefault(x => string.Equals(x.Method, canon, StringComparison.OrdinalIgnoreCase))
                        ?? g.OrderBy(x => x.Deprecated ? 1 : 0).ThenBy(x => x.Method, StringComparer.OrdinalIgnoreCase).First();

                    var aliases = g
                        .Where(x => !string.Equals(x.Method, canon, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.Method, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new { method = x.Method, deprecated = x.Deprecated, summary = x.Summary })
                        .ToList();

                    return new
                    {
                        canonical = canon,
                        summary = primary.Summary,
                        transaction = primary.Transaction,
                        revitHandler = primary.RevitHandler,
                        since = primary.Since,
                        aliases
                    };
                })
                .ToList();

            return Results.Json(new
            {
                ok = true,
                canonicalOnly = canonicalOnly,
                canonicalCount = groups.Count,
                aliasCount = groups.Sum(x => x.aliases.Count),
                groups
            });
        }

        return Results.Json(new { ok = true, canonicalOnly = canonicalOnly, count = filtered.Count, capabilities = filtered });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, code = "CAPABILITIES_FAIL", msg = ex.Message }, statusCode: 500);
    }
});

// ----------------------------- Step 11: Docs/Manifest -----------------------------
app.MapPost("/manifest/register", async (HttpContext ctx) =>
{
    try
    {
        string body;
        using (var r = new StreamReader(ctx.Request.Body, Encoding.UTF8))
            body = await r.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(body))
            return Results.Json(new { ok = false, code = "INVALID_MANIFEST", msg = "Empty body." }, statusCode: 400);

        var manifest = JsonSerializer.Deserialize<RevitMcpServer.Docs.DocManifest>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (manifest == null)
            return Results.Json(new { ok = false, code = "INVALID_MANIFEST", msg = "Failed to parse manifest JSON." }, statusCode: 400);

        RevitMcpServer.Docs.ManifestRegistry.Upsert(manifest);
        RevitMcpServer.Docs.ManifestRegistry.SaveToDisk();
        try { RevitMcpServer.Docs.CapabilitiesGenerator.TryWriteDefault(RevitMcpServer.Docs.ManifestRegistry.GetAll()); } catch { }

        return Results.Json(new { ok = true, source = manifest.Source, count = manifest.Commands?.Count ?? 0 });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, code = "MANIFEST_REGISTER_FAIL", msg = ex.Message }, statusCode: 500);
    }
});

app.MapGet("/docs/manifest.json", (HttpRequest req) =>
{
    try
    {
        var all = RevitMcpServer.Docs.ManifestRegistry.GetAll();
        bool includeDeprecated = QueryFlag(req, "includeDeprecated");
        var filtered = includeDeprecated ? all : all.Where(m => (m?.Deprecated ?? false) == false).ToList();
        return Results.Json(new { ok = true, includeDeprecated, count = filtered.Count, methods = filtered });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, code = "DOCS_FAIL", msg = ex.Message }, statusCode: 500);
    }
});

app.MapGet("/docs/openrpc.json", () =>
{
    try
    {
        var router = docsRouter ?? new RevitMCP.Abstractions.Rpc.RpcRouter();
        var extras = RevitMcpServer.Docs.ManifestRegistry.GetAll();
        var json = RevitMcpServer.Docs.OpenRpcGenerator.Generate(router, extras);
        return Results.Text(json, "application/json", Encoding.UTF8);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, code = "DOCS_FAIL", msg = ex.Message }, statusCode: 500);
    }
});

app.MapGet("/docs/openapi.json", () =>
{
    try
    {
        var router = docsRouter ?? new RevitMCP.Abstractions.Rpc.RpcRouter();
        var extras = RevitMcpServer.Docs.ManifestRegistry.GetAll();
        var json = RevitMcpServer.Docs.OpenApiGenerator.Generate(router, extras);
        return Results.Text(json, "application/json", Encoding.UTF8);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, code = "DOCS_FAIL", msg = ex.Message }, statusCode: 500);
    }
});

app.MapGet("/docs/commands.md", () =>
{
    try
    {
        var methods = RevitMcpServer.Docs.ManifestRegistry.GetAll()
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Revit MCP Commands (Auto-generated)");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:o}");
        sb.AppendLine($"Count: {methods.Count}");
        sb.AppendLine();

        foreach (var m in methods)
        {
            var tags = (m.Tags != null && m.Tags.Length > 0) ? string.Join(", ", m.Tags) : "";
            var summary = (m.Summary ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            sb.Append("- `").Append(m.Name).Append("`");
            if (!string.IsNullOrWhiteSpace(summary)) sb.Append(" — ").Append(summary);
            if (!string.IsNullOrWhiteSpace(tags)) sb.Append("  (tags: ").Append(tags).Append(")");
            if (!string.IsNullOrWhiteSpace(m.Source)) sb.Append("  [").Append(m.Source).Append("]");
            sb.AppendLine();
        }

        return Results.Text(sb.ToString(), "text/markdown", Encoding.UTF8);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, code = "DOCS_FAIL", msg = ex.Message }, statusCode: 500);
    }
});

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

    // Step 10: server-only immediate status (no queue; works even if Revit is busy)
    if (IsRevitStatusMethod(method))
    {
        var status = await BuildRevitStatusAsync(durable, serverStartedUtc);
        return Results.Json(new { jsonrpc = "2.0", id = id, result = status }, jsonOpts);
    }

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

    // Step 10: server-only immediate status (no queue; works even if Revit is busy)
    if (IsRevitStatusMethod(method))
    {
        var status = await BuildRevitStatusAsync(durable, serverStartedUtc);
        return Results.Json(new { jsonrpc = "2.0", id = id, result = status }, jsonOpts);
    }

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
            if (!string.IsNullOrEmpty(jobId2))
            {
                // Step 1: server-side timing augmentation (queueWait/total/revit fallback)
                var augmented = result;
                try
                {
                    var row = await durable.GetAsync(jobId2!);
                    if (row is IDictionary<string, object?> dict)
                        augmented = TryAugmentResultJsonWithTimings(result, dict, DateTimeOffset.UtcNow);
                }
                catch { /* best-effort */ }

                await durable.CompleteAsync(jobId2!, augmented);
            }
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
            if (row != null)
            {
                object obj = null!;
                if (row.TryGetValue("result_json", out obj) && obj != null)
                {
                    var s = Convert.ToString(obj);
                    if (!string.IsNullOrEmpty(s)) return Results.Text(s, "application/json", Encoding.UTF8);
                }
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

    // Step 10: server-only immediate status (no queue; works even if Revit is busy)
    if (IsRevitStatusMethod(method))
    {
        var status = await BuildRevitStatusAsync(durable, serverStartedUtc);
        return Results.Json(status);
    }

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

// Background safety: periodically reclaim extremely old in-progress jobs (e.g., after crashes).
try
{
    var durableForSweep = app.Services.GetService(typeof(DurableQueue)) as DurableQueue;
    if (durableForSweep != null)
    {
        var stopping = app.Lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            // Run immediately, then periodically.
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    var staleAfterSec = ResolveStaleInProgressSeconds();
                    var reclaimed = await durableForSweep.ReclaimStaleInProgressJobsAsync(staleAfterSec);
                    if (reclaimed > 0)
                        try { Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RECLAIM(bg): reclaimed={reclaimed} staleAfterSec={staleAfterSec}"); } catch { }
                }
                catch { /* best-effort */ }

                try { await Task.Delay(TimeSpan.FromMinutes(10), stopping); }
                catch { /* ignore cancellation */ }
            }
        }, stopping);
    }
}
catch { /* best-effort */ }

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

static string TryAugmentResultJsonWithTimings(string resultJson, IDictionary<string, object?> jobRow, DateTimeOffset finishUtc)
{
    try
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return resultJson;

        DateTimeOffset enqueueUtc, startUtc;
        var hasEnqueue = TryParseSqliteTimestamp(jobRow.TryGetValue("enqueue_ts", out var enq) ? enq : null, out enqueueUtc);
        var hasStart = TryParseSqliteTimestamp(jobRow.TryGetValue("start_ts", out var st) ? st : null, out startUtc);
        if (!hasEnqueue) return resultJson;

        long queueWaitMs = 0;
        long totalMs = (long)Math.Max(0, (finishUtc - enqueueUtc).TotalMilliseconds);
        long revitMs = 0;
        if (hasStart)
        {
            queueWaitMs = (long)Math.Max(0, (startUtc - enqueueUtc).TotalMilliseconds);
            revitMs = (long)Math.Max(0, (finishUtc - startUtc).TotalMilliseconds);
        }

        JsonNode? rootNode;
        try { rootNode = JsonNode.Parse(resultJson); }
        catch { return resultJson; }
        if (rootNode is not JsonObject rootObj) return resultJson;

        // Locate payload node (best-effort):
        // - Common shape: { jsonrpc, id, result: { ..., result: { ok, ...timings... }, ledger?... } }
        // - Async/other shape: { jsonrpc, id, result: { ok, ...timings... } }
        JsonObject? payloadObj = null;
        if (rootObj["result"] is JsonObject r1)
        {
            if (r1["result"] is JsonObject r2 && r2["ok"] != null)
                payloadObj = r2;
            else if (r1["ok"] != null)
                payloadObj = r1;
        }
        if (payloadObj == null) return resultJson;

        var timings = payloadObj["timings"] as JsonObject ?? new JsonObject();
        // Always set server-derived totals (non-breaking additive fields)
        timings["queueWaitMs"] = queueWaitMs;
        timings["totalMs"] = totalMs;
        // Only fill revitMs if missing/zero (addin may have a more accurate measurement)
        if (timings["revitMs"] == null || timings["revitMs"]?.GetValue<long>() == 0)
            timings["revitMs"] = revitMs;
        payloadObj["timings"] = timings;

        return rootObj.ToJsonString(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
    catch
    {
        return resultJson;
    }
}

static bool TryParseSqliteTimestamp(object? v, out DateTimeOffset dto)
{
    dto = default;
    try
    {
        if (v == null || v is DBNull) return false;
        if (v is DateTime dt)
        {
            dto = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            return true;
        }

        var s = Convert.ToString(v);
        if (string.IsNullOrWhiteSpace(s)) return false;

        // SQLite CURRENT_TIMESTAMP is "YYYY-MM-DD HH:MM:SS" (UTC)
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dto))
            return true;
        if (DateTimeOffset.TryParse(s, out dto))
        {
            dto = dto.ToUniversalTime();
            return true;
        }
    }
    catch { /* ignore */ }
    return false;
}

static bool IsRevitStatusMethod(string? method)
{
    var m = (method ?? string.Empty).Trim();
    if (m.Length == 0) return false;
    return string.Equals(m, "revit.status", StringComparison.OrdinalIgnoreCase)
        || string.Equals(m, "revit_status", StringComparison.OrdinalIgnoreCase)
        || string.Equals(m, "status", StringComparison.OrdinalIgnoreCase);
}

static int ResolveStaleInProgressSeconds()
{
    // Conservative default: 6 hours. Stale in-progress jobs older than this are almost certainly orphaned.
    int sec = 6 * 60 * 60;
    try
    {
        var env = Environment.GetEnvironmentVariable("REVIT_MCP_STALE_INPROGRESS_SEC")
                  ?? Environment.GetEnvironmentVariable("MCP_STALE_INPROGRESS_SEC");
        if (int.TryParse(env, out var v) && v > 0) sec = v;
    }
    catch { /* ignore */ }
    return sec;
}

static bool QueryFlag(HttpRequest req, string key)
{
    try
    {
        var v = req.Query[key].ToString();
        if (string.IsNullOrWhiteSpace(v)) return false;
        v = v.Trim();
        return v == "1"
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
        return false;
    }
}

static async Task<object> BuildRevitStatusAsync(DurableQueue durable, DateTimeOffset serverStartedUtc)
{
    try
    {
        int srvPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        int srvPort = TryResolveServerPort();
        var targetPort = durable.ResolveCurrentPort();

        int staleAfterSec = ResolveStaleInProgressSeconds();
        int reclaimed = 0;
        int reclaimedAggressive = 0;
        int? aggressiveStaleAfterSec = null;
        try
        {
            // Only reclaim extremely old in-progress jobs to avoid impacting legitimate long-running commands.
            reclaimed = await durable.ReclaimStaleInProgressJobsAsync(staleAfterSec);
            if (reclaimed > 0)
                try { Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RECLAIM: reclaimed={reclaimed} staleAfterSec={staleAfterSec}"); } catch { }
        }
        catch { /* best-effort */ }

        var counts = await durable.CountByStateAsync();
        long queued = counts.TryGetValue("ENQUEUED", out var q) ? q : 0;
        long running = counts.TryGetValue("RUNNING", out var r) ? r : 0;
        long dispatching = counts.TryGetValue("DISPATCHING", out var d) ? d : 0;

        // If multiple jobs are simultaneously RUNNING/DISPATCHING, it usually indicates orphaned state
        // (e.g., Revit/add-in crash before posting /post_result). In that case, run an additional
        // more aggressive reclaim pass (still bounded by time) and recompute counts.
        try
        {
            var inProgress = running + dispatching;
            if (inProgress >= 2)
            {
                // Heuristic: more in-progress → more aggressive.
                var cap = (inProgress >= 5) ? (15 * 60) : (30 * 60);
                var thr = Math.Min(staleAfterSec, cap);
                if (thr > 0 && thr < staleAfterSec)
                {
                    aggressiveStaleAfterSec = thr;
                    reclaimedAggressive = await durable.ReclaimStaleInProgressJobsAsync(thr);
                    if (reclaimedAggressive > 0)
                        try { Logging.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RECLAIM(aggressive): reclaimed={reclaimedAggressive} staleAfterSec={thr} inProgress={inProgress}"); } catch { }

                    // refresh counts
                    counts = await durable.CountByStateAsync();
                    queued = counts.TryGetValue("ENQUEUED", out q) ? q : 0;
                    running = counts.TryGetValue("RUNNING", out r) ? r : 0;
                    dispatching = counts.TryGetValue("DISPATCHING", out d) ? d : 0;
                }
            }
        }
        catch { /* best-effort */ }

        var activeRow = await durable.GetLatestJobByStatesAsync(new[] { "RUNNING", "DISPATCHING" }, "start_ts");
        var lastErrRow = await durable.GetLatestJobByStatesAsync(new[] { "FAILED", "TIMEOUT", "DEAD" }, "finish_ts");

        return new
        {
            ok = true,
            serverPid = srvPid,
            serverPort = srvPort,
            targetPort = targetPort,
            nowUtc = DateTimeOffset.UtcNow.ToString("o"),
            startedAtUtc = serverStartedUtc.ToString("o"),
            uptimeSec = Math.Max(0, (DateTimeOffset.UtcNow - serverStartedUtc).TotalSeconds),
            staleCleanup = new
            {
                staleAfterSec,
                reclaimedCount = reclaimed,
                aggressiveStaleAfterSec,
                reclaimedAggressiveCount = reclaimedAggressive
            },
            queue = new
            {
                queuedCount = queued,
                runningCount = running,
                dispatchingCount = dispatching,
                countsByState = counts
            },
            activeJob = TrimJobRow(activeRow),
            lastError = TrimJobRow(lastErrRow)
        };
    }
    catch (Exception ex)
    {
        return new { ok = false, code = "STATUS_ERROR", msg = ex.Message };
    }
}

static object? TrimJobRow(dynamic? row)
{
    try
    {
        if (row is not IDictionary<string, object?> dict) return null;

        // Avoid large payload fields in status responses.
        string[] keep = new[]
        {
            "job_id","rpc_id","method","state","priority","timeout_sec","attempts","target_port",
            "enqueue_ts","start_ts","heartbeat_ts","finish_ts","error_code","error_msg"
        };

        var trimmed = new Dictionary<string, object?>();
        foreach (var k in keep)
        {
            if (dict.TryGetValue(k, out var v) && v != null && v is not DBNull)
                trimmed[k] = v;
        }

        return trimmed;
    }
    catch
    {
        return null;
    }
}


