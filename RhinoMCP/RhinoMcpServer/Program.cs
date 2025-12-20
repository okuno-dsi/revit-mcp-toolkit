using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

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

app.Run();
