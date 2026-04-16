using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitMCP.A2AAdapter;

public sealed class A2AService
{
    private readonly RevitMcpClient _revit;
    private readonly TaskStore _tasks;
    private readonly A2AOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public A2AService(RevitMcpClient revit, TaskStore tasks, A2AOptions options, JsonSerializerOptions jsonOptions)
    {
        _revit = revit;
        _tasks = tasks;
        _options = options;
        _jsonOptions = jsonOptions;
    }

    public async Task<JsonObject> HandleAsync(JsonElement root, HttpRequest request, CancellationToken cancellationToken)
    {
        var id = JsonRpcUtil.CloneId(root);
        var method = JsonRpcUtil.GetMethod(root);
        var parameters = JsonRpcUtil.ParamsAsNode(root);

        if (string.IsNullOrWhiteSpace(method))
            return JsonRpcUtil.Error(id, -32600, "JSON-RPC method is required.");

        try
        {
            return method switch
            {
                "SendMessage" => JsonRpcUtil.Success(id, await SendMessageAsync(parameters, cancellationToken).ConfigureAwait(false)),
                "GetTask" => JsonRpcUtil.Success(id, await GetTaskAsync(parameters, cancellationToken).ConfigureAwait(false)),
                "ListTasks" => JsonRpcUtil.Success(id, await ListTasksAsync(parameters, cancellationToken).ConfigureAwait(false)),
                "CancelTask" => JsonRpcUtil.Success(id, CancelTask(parameters)),
                "GetExtendedAgentCard" => JsonRpcUtil.Success(id, JsonRpcUtil.ToNode(AgentCardFactory.Build(request, _options), _jsonOptions)),
                _ => JsonRpcUtil.Error(id, -32601, $"A2A method is not supported by this adapter: {method}")
            };
        }
        catch (JsonException ex)
        {
            return JsonRpcUtil.Error(id, -32602, "Invalid A2A parameters.", JsonValue.Create(ex.Message));
        }
        catch (Exception ex)
        {
            return JsonRpcUtil.Error(id, -32603, "A2A adapter error.", JsonValue.Create(ex.Message));
        }
    }

    private async Task<JsonNode> SendMessageAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = ExtractRevitRequest(parameters);
        if (string.IsNullOrWhiteSpace(request.Method))
        {
            return new JsonObject
            {
                ["message"] = A2AObjectFactory.TextMessage(
                    "agent",
                    "This adapter requires an explicit Revit MCP command. Provide metadata.revitMethod/revitParams or a data part containing revitMethod/revitParams.")
            };
        }

        var contextId = request.ContextId ?? Guid.NewGuid().ToString("N");
        var task = _tasks.Add(new A2ATaskRecord
        {
            ContextId = contextId,
            RevitMethod = request.Method,
            State = A2AStates.Submitted
        });
        task.History.Add(A2AObjectFactory.TextMessage("user", request.RequestSummary, task.Id, task.ContextId));

        var rpc = await _revit.PostRpcAsync(request.Method, request.Parameters, task.Id, cancellationToken).ConfigureAwait(false);
        ApplyInitialRpcResult(task, rpc);

        if (!request.ReturnImmediately && !A2AStates.IsTerminal(task.State))
            await WaitForTerminalAsync(task, TimeSpan.FromSeconds(Math.Max(1, _options.BlockingTimeoutSeconds)), cancellationToken).ConfigureAwait(false);

        return new JsonObject
        {
            ["task"] = A2AObjectFactory.ToTask(task)
        };
    }

    private async Task<JsonNode> GetTaskAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var id = RevitMcpClient.TryGetString(parameters, "id")
              ?? RevitMcpClient.TryGetString(parameters, "taskId");
        if (string.IsNullOrWhiteSpace(id) || !_tasks.TryGet(id, out var task))
        {
            return new JsonObject
            {
                ["task"] = A2AObjectFactory.ToTask(new A2ATaskRecord
                {
                    Id = id ?? Guid.NewGuid().ToString("N"),
                    State = A2AStates.Rejected,
                    ErrorMessage = "Task not found."
                })
            };
        }

        await RefreshTaskAsync(task, cancellationToken).ConfigureAwait(false);
        return A2AObjectFactory.ToTask(task);
    }

    private async Task<JsonNode> ListTasksAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var contextId = RevitMcpClient.TryGetString(parameters, "contextId");
        var state = RevitMcpClient.TryGetString(parameters, "status")
                 ?? RevitMcpClient.TryGetString(parameters, "state");
        var pageSize = ReadInt(parameters, "pageSize") ?? 50;
        var tasks = _tasks.List(contextId, state, pageSize);

        var arr = new JsonArray();
        foreach (var task in tasks)
        {
            await RefreshTaskAsync(task, cancellationToken).ConfigureAwait(false);
            arr.Add(A2AObjectFactory.ToTask(task, includeArtifacts: false, includeHistory: false));
        }

        return new JsonObject
        {
            ["tasks"] = arr,
            ["nextPageToken"] = null
        };
    }

    private JsonNode CancelTask(JsonNode? parameters)
    {
        var id = RevitMcpClient.TryGetString(parameters, "id")
              ?? RevitMcpClient.TryGetString(parameters, "taskId");
        if (string.IsNullOrWhiteSpace(id) || !_tasks.TryGet(id, out var task))
        {
            var rejected = new A2ATaskRecord
            {
                Id = id ?? Guid.NewGuid().ToString("N"),
                State = A2AStates.Rejected,
                ErrorMessage = "Task not found."
            };
            return A2AObjectFactory.ToTask(rejected);
        }

        if (!A2AStates.IsTerminal(task.State))
        {
            task.State = A2AStates.Canceled;
            task.UpdatedUtc = DateTimeOffset.UtcNow.ToString("o");
        }
        return A2AObjectFactory.ToTask(task);
    }

    private void ApplyInitialRpcResult(A2ATaskRecord task, JsonNode? rpc)
    {
        var payload = RevitMcpClient.UnwrapJsonRpcPayload(rpc);
        if (rpc is JsonObject root && root["error"] != null)
        {
            task.State = A2AStates.Failed;
            task.ErrorMessage = root["error"]?["message"]?.ToString() ?? "Revit MCP RPC call failed.";
            task.ResultPayload = rpc.DeepClone();
            task.UpdatedUtc = DateTimeOffset.UtcNow.ToString("o");
            return;
        }

        var queued = payload?["queued"]?.GetValue<bool?>() ?? false;
        var jobId = RevitMcpClient.TryGetString(payload, "jobId")
                 ?? RevitMcpClient.TryGetString(payload, "job_id");
        if (queued && !string.IsNullOrWhiteSpace(jobId))
        {
            task.State = A2AStates.Working;
            task.RevitJobId = jobId;
            task.ResultPayload = payload?.DeepClone();
            task.UpdatedUtc = DateTimeOffset.UtcNow.ToString("o");
            return;
        }

        task.State = IsPayloadOk(payload) ? A2AStates.Completed : A2AStates.Failed;
        task.ErrorMessage = task.State == A2AStates.Failed ? "Revit MCP command returned ok=false." : null;
        task.ResultPayload = payload?.DeepClone() ?? rpc?.DeepClone();
        task.UpdatedUtc = DateTimeOffset.UtcNow.ToString("o");
    }

    private async Task WaitForTerminalAsync(A2ATaskRecord task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (!A2AStates.IsTerminal(task.State) && DateTimeOffset.UtcNow < deadline)
        {
            await RefreshTaskAsync(task, cancellationToken).ConfigureAwait(false);
            if (A2AStates.IsTerminal(task.State))
                break;
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshTaskAsync(A2ATaskRecord task, CancellationToken cancellationToken)
    {
        if (A2AStates.IsTerminal(task.State) || string.IsNullOrWhiteSpace(task.RevitJobId))
            return;

        var job = await _revit.GetJobAsync(task.RevitJobId, cancellationToken).ConfigureAwait(false);
        if (job is not JsonObject jobObj)
            return;

        var state = RevitMcpClient.TryGetString(jobObj, "state") ?? string.Empty;
        if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            task.State = A2AStates.Completed;
            var resultText = RevitMcpClient.TryGetString(jobObj, "result_json");
            task.ResultPayload = ParseJobResult(resultText) ?? jobObj.DeepClone();
            task.UpdatedUtc = DateTimeOffset.UtcNow.ToString("o");
            return;
        }

        if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "TIMEOUT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "DEAD", StringComparison.OrdinalIgnoreCase))
        {
            task.State = string.Equals(state, "TIMEOUT", StringComparison.OrdinalIgnoreCase) ? A2AStates.Failed : A2AStates.Failed;
            task.ErrorMessage = RevitMcpClient.TryGetString(jobObj, "error_msg") ?? state;
            task.ResultPayload = jobObj.DeepClone();
            task.UpdatedUtc = DateTimeOffset.UtcNow.ToString("o");
            return;
        }

        if (string.Equals(state, "ENQUEUED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "RUNNING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "DISPATCHING", StringComparison.OrdinalIgnoreCase))
        {
            task.State = A2AStates.Working;
            task.ResultPayload = jobObj.DeepClone();
            task.UpdatedUtc = DateTimeOffset.UtcNow.ToString("o");
        }
    }

    private static JsonNode? ParseJobResult(string? resultText)
    {
        if (string.IsNullOrWhiteSpace(resultText))
            return null;
        try
        {
            var node = JsonNode.Parse(resultText);
            return RevitMcpClient.UnwrapJsonRpcPayload(node) ?? node;
        }
        catch
        {
            return JsonValue.Create(resultText);
        }
    }

    private static bool IsPayloadOk(JsonNode? payload)
    {
        if (payload is not JsonObject obj)
            return true;
        if (obj.TryGetPropertyValue("ok", out var okNode) && okNode != null)
        {
            try { return okNode.GetValue<bool>(); }
            catch { return false; }
        }
        return obj["error"] == null;
    }

    private static int? ReadInt(JsonNode? node, string name)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(name, out var value) || value == null)
            return null;
        try { return value.GetValue<int>(); }
        catch
        {
            if (int.TryParse(value.ToString(), out var parsed))
                return parsed;
        }
        return null;
    }

    private static ExtractedRevitRequest ExtractRevitRequest(JsonNode? parameters)
    {
        var method = RevitMcpClient.TryGetString(parameters, "revitMethod")
                  ?? RevitMcpClient.TryGetString(parameters, "method");
        var revitParams = RevitMcpClient.TryGetNode(parameters, "revitParams")
                       ?? RevitMcpClient.TryGetNode(parameters, "params");

        var metadata = RevitMcpClient.TryGetNode(parameters, "metadata");
        method ??= RevitMcpClient.TryGetString(metadata, "revitMethod")
                ?? RevitMcpClient.TryGetString(metadata, "method");
        revitParams ??= RevitMcpClient.TryGetNode(metadata, "revitParams")
                     ?? RevitMcpClient.TryGetNode(metadata, "params");

        var message = RevitMcpClient.TryGetNode(parameters, "message");
        method ??= RevitMcpClient.TryGetString(message, "revitMethod")
                ?? RevitMcpClient.TryGetString(message, "method");
        revitParams ??= RevitMcpClient.TryGetNode(message, "revitParams")
                     ?? RevitMcpClient.TryGetNode(message, "params");

        var messageMetadata = RevitMcpClient.TryGetNode(message, "metadata");
        method ??= RevitMcpClient.TryGetString(messageMetadata, "revitMethod")
                ?? RevitMcpClient.TryGetString(messageMetadata, "method");
        revitParams ??= RevitMcpClient.TryGetNode(messageMetadata, "revitParams")
                     ?? RevitMcpClient.TryGetNode(messageMetadata, "params");

        if (message is JsonObject msgObj && msgObj.TryGetPropertyValue("parts", out var partsNode) && partsNode is JsonArray parts)
        {
            foreach (var part in parts)
            {
                var data = RevitMcpClient.TryGetNode(part, "data");
                method ??= RevitMcpClient.TryGetString(data, "revitMethod")
                        ?? RevitMcpClient.TryGetString(data, "method");
                revitParams ??= RevitMcpClient.TryGetNode(data, "revitParams")
                             ?? RevitMcpClient.TryGetNode(data, "params");

                var text = RevitMcpClient.TryGetString(part, "text");
                if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        var textObj = JsonNode.Parse(text);
                        method ??= RevitMcpClient.TryGetString(textObj, "revitMethod")
                                ?? RevitMcpClient.TryGetString(textObj, "method");
                        revitParams ??= RevitMcpClient.TryGetNode(textObj, "revitParams")
                                     ?? RevitMcpClient.TryGetNode(textObj, "params");
                    }
                    catch
                    {
                        // Ignore non-JSON text.
                    }
                }
            }
        }

        var configuration = RevitMcpClient.TryGetNode(parameters, "configuration");
        var returnImmediately = TryGetBool(configuration, "returnImmediately") ?? false;
        var contextId = RevitMcpClient.TryGetString(message, "contextId")
                     ?? RevitMcpClient.TryGetString(parameters, "contextId");

        return new ExtractedRevitRequest
        {
            Method = method?.Trim(),
            Parameters = revitParams ?? new JsonObject(),
            ContextId = contextId,
            ReturnImmediately = returnImmediately,
            RequestSummary = string.IsNullOrWhiteSpace(method) ? "Unresolved Revit MCP request." : $"Revit MCP method: {method}"
        };
    }

    private static bool? TryGetBool(JsonNode? node, string name)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(name, out var value) || value == null)
            return null;
        try { return value.GetValue<bool>(); }
        catch
        {
            var s = value.ToString();
            if (bool.TryParse(s, out var b))
                return b;
        }
        return null;
    }

    private sealed class ExtractedRevitRequest
    {
        public string? Method { get; init; }
        public JsonNode? Parameters { get; init; }
        public string? ContextId { get; init; }
        public bool ReturnImmediately { get; init; }
        public string RequestSummary { get; init; } = "";
    }
}
