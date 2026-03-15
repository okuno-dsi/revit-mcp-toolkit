using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ExcelMcpEndpointExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static void MapExcelMcpEndpoints(this WebApplication app)
    {
        app.MapMethods("/mcp", new[] { "OPTIONS" }, HandleOptions);
        app.MapGet("/mcp", HandleGetAsync);
        app.MapDelete("/mcp", HandleDelete);
        app.MapPost("/mcp", HandlePostAsync);
    }

    private static IResult HandleOptions(HttpContext context)
    {
        SetTransportHeaders(context, ExcelMcpProtocol.DefaultProtocolVersion, sessionId: null);
        context.Response.Headers["Allow"] = "OPTIONS, GET, POST, DELETE";
        context.Response.Headers["Accept-Post"] = "application/json";
        return Results.Ok(new
        {
            ok = true,
            transport = "streamable-http",
            supportedProtocolVersions = ExcelMcpProtocol.SupportedProtocolVersions,
        });
    }

    private static async Task HandleGetAsync(HttpContext context, ExcelMcpSessionStore sessions)
    {
        if (!TryValidateOrigin(context, out var originError))
        {
            await WriteJsonAsync(context, ExcelMcpProtocol.Error(null, -32099, originError), StatusCodes.Status403Forbidden);
            return;
        }

        if (!TryGetSession(context, sessions, out var session, out var sessionError))
        {
            await WriteJsonAsync(context, ExcelMcpProtocol.Error(null, -32001, sessionError), StatusCodes.Status400BadRequest);
            return;
        }

        SetTransportHeaders(context, session.ProtocolVersion, session.SessionId);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";

        await context.Response.WriteAsync(": ExcelMCP MCP stream established\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync(": keepalive\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
    }

    private static IResult HandleDelete(HttpContext context, ExcelMcpSessionStore sessions)
    {
        if (!TryValidateOrigin(context, out var originError))
            return Results.Json(ExcelMcpProtocol.Error(null, -32099, originError), JsonOptions, statusCode: StatusCodes.Status403Forbidden);

        var sessionId = context.Request.Headers[ExcelMcpProtocol.SessionHeader].ToString();
        if (!sessions.Delete(sessionId))
            return Results.Json(ExcelMcpProtocol.Error(null, -32001, "Missing or invalid MCP session."), JsonOptions, statusCode: StatusCodes.Status404NotFound);

        return Results.NoContent();
    }

    private static async Task<IResult> HandlePostAsync(
        HttpContext context,
        ExcelMcpSessionStore sessions,
        ExcelMcpToolRegistry registry,
        ExcelMcpToolExecutor executor)
    {
        if (!TryValidateOrigin(context, out var originError))
            return Results.Json(ExcelMcpProtocol.Error(null, -32099, originError), JsonOptions, statusCode: StatusCodes.Status403Forbidden);

        JsonNode payload;
        try
        {
            using var sr = new StreamReader(context.Request.Body);
            var raw = await sr.ReadToEndAsync(context.RequestAborted);
            payload = JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw)
                ?? throw new Exception("JSON payload is required.");
        }
        catch (Exception ex)
        {
            return Results.Json(ExcelMcpProtocol.Error(null, -32700, "Invalid JSON.", new { detail = ex.Message }), JsonOptions, statusCode: StatusCodes.Status400BadRequest);
        }

        var contextState = new McpRequestContext
        {
            HttpContext = context,
            Sessions = sessions,
            Registry = registry,
            Executor = executor,
        };

        if (payload is JsonArray batch)
            return await HandleBatchAsync(contextState, batch);

        if (payload is JsonObject request)
            return await HandleSingleAsync(contextState, request, allowAcceptedForNotification: true);

        return Results.Json(ExcelMcpProtocol.InvalidRequest(null, "JSON-RPC request object or batch array is required."), JsonOptions, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> HandleBatchAsync(McpRequestContext contextState, JsonArray batch)
    {
        if (batch.Count == 0)
            return Results.Json(ExcelMcpProtocol.InvalidRequest(null, "Batch request must contain at least one request object."), JsonOptions, statusCode: StatusCodes.Status400BadRequest);

        var responses = new List<object>();
        foreach (var item in batch)
        {
            if (item is not JsonObject request)
            {
                responses.Add(ExcelMcpProtocol.InvalidRequest(null, "Each batch item must be a JSON-RPC object."));
                continue;
            }

            var response = await ProcessRequestAsync(contextState, request);
            if (response is not null)
                responses.Add(response);
        }

        if (responses.Count == 0)
            return Results.Accepted();

        return Results.Json(responses, JsonOptions);
    }

    private static async Task<IResult> HandleSingleAsync(McpRequestContext contextState, JsonObject request, bool allowAcceptedForNotification)
    {
        var response = await ProcessRequestAsync(contextState, request);
        if (response is null && allowAcceptedForNotification)
            return Results.Accepted();
        if (response is null)
            return Results.NoContent();
        return Results.Json(response, JsonOptions);
    }

    private static async Task<object?> ProcessRequestAsync(McpRequestContext contextState, JsonObject request)
    {
        var idNode = request["id"];
        var method = request["method"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(method))
            return ExcelMcpProtocol.InvalidRequest(idNode, "JSON-RPC method is required.");

        if (request["params"] is not null and not JsonObject)
            return ExcelMcpProtocol.InvalidParams(idNode, "params must be an object when present.");
        var parameters = request["params"] as JsonObject ?? new JsonObject();
        var isNotification = ExcelMcpProtocol.IsNotification(request);

        if (string.Equals(method, "initialize", StringComparison.OrdinalIgnoreCase))
        {
            var requestedProtocolVersion = parameters["protocolVersion"]?.GetValue<string>()
                ?? contextState.HttpContext.Request.Headers[ExcelMcpProtocol.ProtocolHeader].ToString()
                ?? ExcelMcpProtocol.LegacyFallbackProtocolVersion;
            var negotiatedProtocolVersion = ExcelMcpProtocol.NegotiateProtocolVersion(requestedProtocolVersion);
            var initializedSession = contextState.Sessions.Create(negotiatedProtocolVersion);
            contextState.CurrentSession = initializedSession;
            SetTransportHeaders(contextState.HttpContext, initializedSession.ProtocolVersion, initializedSession.SessionId);
            return ExcelMcpProtocol.Success(idNode, ExcelMcpProtocol.InitializeResult(initializedSession.ProtocolVersion));
        }

        if (!EnsureSession(contextState, idNode, out var sessionError))
            return sessionError;
        var session = contextState.CurrentSession!;
        SetTransportHeaders(contextState.HttpContext, session.ProtocolVersion, session.SessionId);

        if (string.Equals(method, "notifications/initialized", StringComparison.OrdinalIgnoreCase))
        {
            contextState.Sessions.MarkInitialized(session.SessionId);
            return null;
        }

        if (!session.IsInitialized)
            return ExcelMcpProtocol.Error(idNode, -32002, "Session not initialized. Send notifications/initialized after initialize.");

        if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase))
        {
            if (isNotification)
                return null;
            return ExcelMcpProtocol.Success(idNode, new { });
        }

        if (string.Equals(method, "tools/list", StringComparison.OrdinalIgnoreCase))
        {
            var cursor = parameters["cursor"]?.GetValue<string>();
            var tools = contextState.Registry.ListTools(cursor, out var nextCursor);
            return ExcelMcpProtocol.Success(idNode, new { tools, nextCursor });
        }

        if (string.Equals(method, "tools/call", StringComparison.OrdinalIgnoreCase))
        {
            var toolName = parameters["name"]?.GetValue<string>();
            var toolArgs = parameters["arguments"] as JsonObject ?? new JsonObject();
            if (string.IsNullOrWhiteSpace(toolName))
                return ExcelMcpProtocol.InvalidParams(idNode, "tools/call requires params.name.");

            ExcelMcpToolExecutionResult execution;
            try
            {
                execution = await ExecuteToolAsync(contextState, session, toolName, toolArgs);
            }
            catch (Exception ex)
            {
                execution = new ExcelMcpToolExecutionResult
                {
                    Payload = JsonSerializer.SerializeToNode(new { ok = false, tool = toolName, msg = ex.Message })!,
                    IsError = true,
                };
            }

            if (isNotification)
                return null;

            return ExcelMcpProtocol.Success(idNode, ExcelMcpProtocol.ToolResult(execution.Payload, execution.IsError));
        }

        if (method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase))
            return null;

        return ExcelMcpProtocol.Error(idNode, -32601, $"Method '{method}' not found.");
    }

    private static async Task<ExcelMcpToolExecutionResult> ExecuteToolAsync(
        McpRequestContext contextState,
        ExcelMcpSessionState session,
        string toolName,
        JsonObject toolArgs)
    {
        if (string.Equals(toolName, "mcp.status", StringComparison.OrdinalIgnoreCase))
        {
            return new ExcelMcpToolExecutionResult
            {
                Payload = JsonSerializer.SerializeToNode(new
                {
                    ok = true,
                    service = "ExcelMCP",
                    protocol = new
                    {
                        negotiated = session.ProtocolVersion,
                        supported = ExcelMcpProtocol.SupportedProtocolVersions,
                    },
                    session = new
                    {
                        session.SessionId,
                        session.IsInitialized,
                        session.CreatedAtUtc,
                        session.LastSeenAtUtc,
                    },
                })!,
                IsError = false,
            };
        }

        if (string.Equals(toolName, "excel.api_call", StringComparison.OrdinalIgnoreCase))
        {
            var path = toolArgs["path"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ExcelMcpToolExecutionResult
                {
                    Payload = JsonSerializer.SerializeToNode(new { ok = false, msg = "excel.api_call requires arguments.path." })!,
                    IsError = true,
                };
            }

            var method = (toolArgs["method"]?.GetValue<string>() ?? "GET").ToUpperInvariant();
            var body = toolArgs["body"] as JsonObject ?? new JsonObject();
            var definition = new ExcelMcpToolDefinition
            {
                Name = toolName,
                Description = "Fallback HTTP endpoint call.",
                HttpMethod = method,
                Path = path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path,
                InputSchema = new JsonObject(),
            };
            if (definition.Path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            {
                return new ExcelMcpToolExecutionResult
                {
                    Payload = JsonSerializer.SerializeToNode(new { ok = false, msg = "excel.api_call cannot call /mcp recursively." })!,
                    IsError = true,
                };
            }

            return await contextState.Executor.ExecuteAsync(definition, new ExcelMcpToolInvocationContext
            {
                HttpContext = contextState.HttpContext,
                Session = session,
                Arguments = body,
            }, contextState.HttpContext.RequestAborted);
        }

        if (!contextState.Registry.TryGet(toolName, out var tool))
        {
            return new ExcelMcpToolExecutionResult
            {
                Payload = JsonSerializer.SerializeToNode(new { ok = false, msg = $"Unknown tool: {toolName}" })!,
                IsError = true,
            };
        }

        var validationError = contextState.Registry.ValidateArguments(tool, toolArgs);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return new ExcelMcpToolExecutionResult
            {
                Payload = JsonSerializer.SerializeToNode(new { ok = false, tool = toolName, msg = validationError })!,
                IsError = true,
            };
        }

        return await contextState.Executor.ExecuteAsync(tool, new ExcelMcpToolInvocationContext
        {
            HttpContext = contextState.HttpContext,
            Session = session,
            Arguments = toolArgs,
        }, contextState.HttpContext.RequestAborted);
    }

    private static bool EnsureSession(McpRequestContext contextState, JsonNode? idNode, out object? error)
    {
        if (contextState.CurrentSession is not null)
        {
            error = null;
            return true;
        }

        var sessionId = contextState.HttpContext.Request.Headers[ExcelMcpProtocol.SessionHeader].ToString();
        if (!contextState.Sessions.TryGet(sessionId, out var session))
        {
            error = ExcelMcpProtocol.Error(idNode, -32001, "Missing or invalid MCP session. Call initialize first.");
            return false;
        }

        var requestedProtocolVersion = contextState.HttpContext.Request.Headers[ExcelMcpProtocol.ProtocolHeader].ToString();
        if (!string.IsNullOrWhiteSpace(requestedProtocolVersion)
            && !string.Equals(requestedProtocolVersion, session.ProtocolVersion, StringComparison.OrdinalIgnoreCase))
        {
            error = ExcelMcpProtocol.Error(idNode, -32003, "Protocol version does not match the active session.", new { requested = requestedProtocolVersion, session = session.ProtocolVersion });
            return false;
        }

        contextState.CurrentSession = session;
        error = null;
        return true;
    }

    private static bool TryGetSession(HttpContext context, ExcelMcpSessionStore sessions, out ExcelMcpSessionState session, out string error)
    {
        var sessionId = context.Request.Headers[ExcelMcpProtocol.SessionHeader].ToString();
        if (!sessions.TryGet(sessionId, out session!))
        {
            error = "Missing or invalid MCP session. Call initialize first.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void SetTransportHeaders(HttpContext context, string protocolVersion, string? sessionId)
    {
        context.Response.Headers[ExcelMcpProtocol.ProtocolHeader] = protocolVersion;
        if (!string.IsNullOrWhiteSpace(sessionId))
            context.Response.Headers[ExcelMcpProtocol.SessionHeader] = sessionId;
        context.Response.Headers["Cache-Control"] = "no-store";
    }

    private static bool TryValidateOrigin(HttpContext context, out string error)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin) || string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            error = "Invalid Origin header.";
            return false;
        }

        var host = originUri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        var requestHost = context.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(requestHost)
            && string.Equals(host, requestHost, StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        error = $"Origin '{origin}' is not allowed for local MCP transport.";
        return false;
    }

    private static async Task WriteJsonAsync(HttpContext context, object payload, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private sealed class McpRequestContext
    {
        public required HttpContext HttpContext { get; init; }
        public required ExcelMcpSessionStore Sessions { get; init; }
        public required ExcelMcpToolRegistry Registry { get; init; }
        public required ExcelMcpToolExecutor Executor { get; init; }
        public ExcelMcpSessionState? CurrentSession { get; set; }
    }
}
