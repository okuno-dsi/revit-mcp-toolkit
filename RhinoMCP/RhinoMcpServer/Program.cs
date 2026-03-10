using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
// Default to 5200 if no URL is provided
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(urls))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5200");
}
builder.Services.AddControllers().AddNewtonsoftJson();
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { ok = true, name = "RhinoMcpServer" }));
app.MapPost("/echo", async (HttpContext ctx) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(body);
});

// Global error translator for /rpc to always return 200 with JSON-RPC error
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path == "/rpc")
    {
        try
        {
            await next();
        }
        catch (RhinoMcpServer.Rpc.JsonRpcException jex)
        {
            var err = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JValue.CreateNull(),
                ["error"] = new JObject{ ["code"] = jex.Code, ["message"] = jex.Message }
            };
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(err.ToString(Formatting.None));
            return;
        }
        catch (Exception ex)
        {
            var err = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JValue.CreateNull(),
                ["error"] = new JObject{ ["code"] = -32000, ["message"] = ex.Message }
            };
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(err.ToString(Formatting.None));
            return;
        }
    }
    else
    {
        await next();
    }
});

app.MapPost("/rpc", async (HttpContext ctx) =>
{
    try { System.IO.File.AppendAllText("rpc_trace.txt", DateTime.UtcNow.ToString("o") + " ENTER /rpc" + Environment.NewLine); } catch {}
    try
    {
        var req = ctx.Request;
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();
        try { System.IO.File.AppendAllText("rpc_trace.txt", DateTime.UtcNow.ToString("o") + " BODY " + body + Environment.NewLine); } catch {}
        var call = JObject.Parse(body);
        string method = call.Value<string>("method") ?? "";
        var idToken = call["id"];
        object? id = idToken == null ? null : idToken.Type switch
        {
            JTokenType.Integer => (object)idToken.Value<long>(),
            JTokenType.Float => (object)idToken.Value<double>(),
            JTokenType.String => (object)idToken.Value<string>()!,
            JTokenType.Boolean => (object)idToken.Value<bool>(),
            _ => idToken.ToString()
        };

        try
        {
            var result = await RhinoMcpServer.Rpc.RpcRouter.RouteAsync(method, call["params"] as JObject ?? new JObject());
            RhinoMcpServer.ServerLogger.Log(new RhinoMcpServer.ServerLogEntry
            {
                time = DateTime.UtcNow,
                id = id,
                method = method,
                ok = true,
                msg = "ok"
            });
            var resp = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = idToken ?? JValue.CreateNull(),
                ["result"] = result is JToken jt ? jt : JToken.FromObject(result)
            };
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(resp.ToString(Formatting.None));
            return;
        }
        catch (RhinoMcpServer.Rpc.JsonRpcException jex)
        {
            RhinoMcpServer.ServerLogger.Log(new RhinoMcpServer.ServerLogEntry
            {
                time = DateTime.UtcNow,
                id = id,
                method = method,
                ok = false,
                msg = jex.Message
            });
            var err = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = idToken ?? JValue.CreateNull(),
                ["error"] = new JObject{ ["code"] = jex.Code, ["message"] = jex.Message }
            };
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(err.ToString(Formatting.None));
            return;
        }
        catch (Exception ex)
        {
            RhinoMcpServer.ServerLogger.Log(new RhinoMcpServer.ServerLogEntry
            {
                time = DateTime.UtcNow,
                id = id,
                method = method,
                ok = false,
                msg = ex.Message
            });
            var err = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = idToken ?? JValue.CreateNull(),
                ["error"] = new JObject{ ["code"] = -32000, ["message"] = ex.Message }
            };
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(err.ToString(Formatting.None));
            return;
        }
    }
    catch (Exception outer)
    {
        try
        {
            var err = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JValue.CreateNull(),
                ["error"] = new JObject{ ["code"] = -32001, ["message"] = outer.Message }
            };
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(err.ToString(Formatting.None));
        }
        catch { }
        return;
    }
});

var rhinoMcpSessions = new RhinoMcpSessionStore();

app.MapMethods("/mcp", new[] { "OPTIONS" }, (HttpContext ctx) =>
{
    ctx.Response.Headers[RhinoMcpProtocol.ProtocolHeader] = RhinoMcpProtocol.DefaultProtocolVersion;
    return Results.Ok();
});

app.MapPost("/mcp", async (HttpContext ctx) =>
{
    JObject reqObj;
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        reqObj = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }
    catch (Exception ex)
    {
        return Results.Json(RhinoMcpProtocol.Error(null, -32700, "Invalid JSON.", new { detail = ex.Message }));
    }

    var idToken = reqObj["id"];
    var method = reqObj.Value<string>("method") ?? string.Empty;
    var prm = reqObj["params"] as JObject ?? new JObject();
    var sessionId = ctx.Request.Headers[RhinoMcpProtocol.SessionHeader].ToString();

    if (string.Equals(method, "initialize", StringComparison.OrdinalIgnoreCase))
    {
        var protocolVersion = prm.Value<string>("protocolVersion")
            ?? ctx.Request.Headers[RhinoMcpProtocol.ProtocolHeader].ToString()
            ?? RhinoMcpProtocol.DefaultProtocolVersion;
        var session = rhinoMcpSessions.Create(protocolVersion);
        ctx.Response.Headers[RhinoMcpProtocol.SessionHeader] = session.SessionId;
        ctx.Response.Headers[RhinoMcpProtocol.ProtocolHeader] = session.ProtocolVersion;
        return Results.Json(RhinoMcpProtocol.Success(idToken, RhinoMcpProtocol.InitializeResult(session.ProtocolVersion)));
    }

    if (!rhinoMcpSessions.TryGet(sessionId, out var state))
    {
        return Results.Json(RhinoMcpProtocol.Error(idToken, -32001, "Missing or invalid MCP session. Call initialize first."));
    }

    ctx.Response.Headers[RhinoMcpProtocol.SessionHeader] = state.SessionId;
    ctx.Response.Headers[RhinoMcpProtocol.ProtocolHeader] = state.ProtocolVersion;

    if (string.Equals(method, "notifications/initialized", StringComparison.OrdinalIgnoreCase))
    {
        rhinoMcpSessions.MarkInitialized(state.SessionId);
        return Results.Json(RhinoMcpProtocol.Success(idToken, new { }));
    }

    if (!state.IsInitialized)
    {
        return Results.Json(RhinoMcpProtocol.Error(idToken, -32002, "Session not initialized. Send notifications/initialized after initialize."));
    }

    if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(RhinoMcpProtocol.Success(idToken, new { }));
    }

    if (string.Equals(method, "tools/list", StringComparison.OrdinalIgnoreCase))
    {
        var tools = RhinoMcpServer.Rpc.RpcRouter.ListMethods()
            .Select(name => new
            {
                name,
                description = $"Rhino RPC method '{name}'",
                inputSchema = new { type = "object", additionalProperties = true }
            })
            .Append(new
            {
                name = "mcp.status",
                description = "Return Rhino MCP status.",
                inputSchema = new { type = "object", additionalProperties = false }
            })
            .ToArray();

        return Results.Json(RhinoMcpProtocol.Success(idToken, new { tools }));
    }

    if (string.Equals(method, "tools/call", StringComparison.OrdinalIgnoreCase))
    {
        var toolName = prm.Value<string>("name");
        var argsObj = prm["arguments"] as JObject ?? new JObject();
        if (string.IsNullOrWhiteSpace(toolName))
            return Results.Json(RhinoMcpProtocol.Error(idToken, -32602, "tools/call requires params.name."));

        JToken payload;
        var isError = false;
        try
        {
            if (string.Equals(toolName, "mcp.status", StringComparison.OrdinalIgnoreCase))
            {
                payload = JToken.FromObject(new
                {
                    ok = true,
                    service = "RhinoMcpServer",
                    mcp = new
                    {
                        sessionId = state.SessionId,
                        protocolVersion = state.ProtocolVersion,
                        initialized = state.IsInitialized
                    }
                });
            }
            else
            {
                var rs = await RhinoMcpServer.Rpc.RpcRouter.RouteAsync(toolName!, argsObj);
                payload = rs is JToken jt ? jt : JToken.FromObject(rs);
            }
        }
        catch (RhinoMcpServer.Rpc.JsonRpcException ex)
        {
            isError = true;
            payload = JToken.FromObject(new { ok = false, code = ex.Code, msg = ex.Message });
        }
        catch (Exception ex)
        {
            isError = true;
            payload = JToken.FromObject(new { ok = false, code = 500, msg = ex.Message });
        }

        return Results.Json(RhinoMcpProtocol.Success(idToken, RhinoMcpProtocol.ToolResult(payload, isError)));
    }

    return Results.Json(RhinoMcpProtocol.Error(idToken, -32601, $"Method '{method}' not found."), statusCode: 404);
});

app.Run();

internal static class RhinoMcpProtocol
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
        serverInfo = new { name = "RhinoMcpServer", version = "1.0.0" },
        instructions = "Use tools/list then tools/call with Rhino RPC method names."
    };

    public static object ToolResult(JToken payload, bool isError) => new
    {
        content = new object[]
        {
            new
            {
                type = "text",
                text = payload.ToString(Formatting.Indented)
            }
        },
        structuredContent = payload,
        isError
    };

    public static object Success(JToken? id, object? result) => new
    {
        jsonrpc = "2.0",
        id = NormalizeId(id),
        result
    };

    public static object Error(JToken? id, int code, string message, object? data = null) => new
    {
        jsonrpc = "2.0",
        id = NormalizeId(id),
        error = new { code, message, data }
    };

    private static object? NormalizeId(JToken? id)
    {
        if (id == null) return null;
        return id.Type switch
        {
            JTokenType.Integer => id.Value<long>(),
            JTokenType.Float => id.Value<double>(),
            JTokenType.String => id.Value<string>(),
            JTokenType.Boolean => id.Value<bool>(),
            _ => id.ToString(Formatting.None)
        };
    }
}

internal sealed class RhinoMcpSessionStore
{
    private readonly ConcurrentDictionary<string, RhinoMcpSessionState> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public RhinoMcpSessionState Create(string protocolVersion)
    {
        var s = new RhinoMcpSessionState
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ProtocolVersion = string.IsNullOrWhiteSpace(protocolVersion)
                ? RhinoMcpProtocol.DefaultProtocolVersion
                : protocolVersion,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow,
            IsInitialized = false
        };
        _sessions[s.SessionId] = s;
        return s;
    }

    public bool TryGet(string? sessionId, out RhinoMcpSessionState state)
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

internal sealed class RhinoMcpSessionState
{
    public string SessionId { get; set; } = "";
    public string ProtocolVersion { get; set; } = RhinoMcpProtocol.DefaultProtocolVersion;
    public bool IsInitialized { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
