using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexGui;

internal sealed class A2AHubService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly HttpListener _listener = new();
    private readonly A2ATaskStore _taskStore = new();
    private readonly A2AAuditLogService _audit = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task? _resultLoop;
    private readonly int _port;

    public A2AHubService(int port = 5260)
    {
        _port = port;
    }

    public void Start()
    {
        if (_cts != null) return;
        try
        {
            _cts = new CancellationTokenSource();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
            _loop = Task.Run(() => RunAsync(_cts.Token));
            _resultLoop = Task.Run(() => ResultWatcherAsync(_cts.Token));
            CodexGuiLog.Info($"A2AHubService started on 127.0.0.1:{_port}");
        }
        catch (Exception ex)
        {
            CodexGuiLog.Exception("A2AHubService start failed", ex);
            try { _listener.Close(); } catch { }
            _cts = null;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(ctx, ct), ct);
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                CodexGuiLog.Exception("A2AHubService loop error", ex);
                if (ctx != null) await WriteJsonAsync(ctx.Response, 500, new { ok = false, msg = ex.Message }).ConfigureAwait(false);
            }
        }
    }

    private async Task ResultWatcherAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                ScanLocalA2AResults();
                _taskStore.MarkTimeouts(TimeSpan.FromMinutes(5), _audit);
            }
            catch (Exception ex)
            {
                CodexGuiLog.Exception("A2AHubService result watcher failed", ex);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod ?? "GET";

            if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    ok = true,
                    name = "CodexGUI A2A Hub",
                    port = _port,
                    stage = "stage-1-routing",
                    networkScope = "loopback",
                    timeUtc = DateTimeOffset.UtcNow.ToString("o")
                }).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/.well-known/agent-card.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/a2a/agent-card", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(ctx.Response, 200, BuildAgentCard()).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/a2a/agents", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(ctx.Response, 200, new { ok = true, agents = DiscoverAgents() }).ConfigureAwait(false);
                return;
            }

            var agentMatch = Regex.Match(path, "^/a2a/agents/(?<id>.+)$", RegexOptions.IgnoreCase);
            if (agentMatch.Success && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                var id = Uri.UnescapeDataString(agentMatch.Groups["id"].Value);
                var agent = DiscoverAgents().FirstOrDefault(a => string.Equals(a.AgentId, id, StringComparison.OrdinalIgnoreCase));
                if (agent == null)
                {
                    await WriteJsonAsync(ctx.Response, 404, new { ok = false, errorCode = "agent_not_found", agentId = id }).ConfigureAwait(false);
                    return;
                }
                await WriteJsonAsync(ctx.Response, 200, new { ok = true, agent }).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/a2a/rpc", StringComparison.OrdinalIgnoreCase) && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRpcAsync(ctx, ct).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/a2a/route", StringComparison.OrdinalIgnoreCase) && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRouteAsync(ctx, ct).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/a2a/tasks", StringComparison.OrdinalIgnoreCase) && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                ScanLocalA2AResults();
                await WriteJsonAsync(ctx.Response, 200, new { ok = true, tasks = _taskStore.List() }).ConfigureAwait(false);
                return;
            }

            var taskMatch = Regex.Match(path, "^/a2a/tasks/(?<id>[^/]+)(?<cancel>/cancel)?$", RegexOptions.IgnoreCase);
            if (taskMatch.Success)
            {
                var taskId = Uri.UnescapeDataString(taskMatch.Groups["id"].Value);
                if (taskMatch.Groups["cancel"].Success && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    var task = _taskStore.Cancel(taskId);
                    if (task == null)
                    {
                        await WriteJsonAsync(ctx.Response, 404, new { ok = false, errorCode = "task_not_found", taskId }).ConfigureAwait(false);
                        return;
                    }
                    _audit.Write("task_canceled", task, "canceled", "not_required");
                    await WriteJsonAsync(ctx.Response, 200, new { ok = true, task = task.ToPublic() }).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    ScanLocalA2AResults();
                    var task = _taskStore.Get(taskId);
                    if (task == null)
                    {
                        await WriteJsonAsync(ctx.Response, 404, new { ok = false, errorCode = "task_not_found", taskId }).ConfigureAwait(false);
                        return;
                    }
                    await WriteJsonAsync(ctx.Response, 200, new { ok = true, task = task.ToPublic(includePaths: true) }).ConfigureAwait(false);
                    return;
                }
            }

            await WriteJsonAsync(ctx.Response, 404, new { ok = false, msg = "Not found." }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CodexGuiLog.Exception("A2AHubService request error", ex);
            await WriteJsonAsync(ctx.Response, 500, new { ok = false, msg = ex.Message }).ConfigureAwait(false);
        }
    }

    private static object BuildAgentCard() => new
    {
        name = "CodexGUI A2A Hub",
        description = "Local loopback A2A hub for Revit MCP, DesignDocsAI, ExcelMCP, AutoCadMCP, and local knowledge tools.",
        url = "http://127.0.0.1:5260/a2a/rpc",
        version = "2026.04.28",
        capabilities = new { streaming = false, pushNotifications = false },
        capabilitiesList = new[] { "agents/list", "message/send", "tasks/get", "tasks/list", "tasks/cancel", "revit.context.read", "docs.search" },
        skills = new object[]
        {
            new { id = "hub.agent.discovery", name = "Discover local agents", tags = new[] { "hub", "agents", "discovery" } },
            new { id = "hub.route.stage1", name = "Route Stage 1 read-only requests", tags = new[] { "hub", "routing", "stage1" } },
            new { id = "revit.context.read", name = "Read Revit context", tags = new[] { "revit", "mcp", "read" } },
            new { id = "docs.search", name = "Submit DesignDocsAI search", tags = new[] { "documents", "search", "citations" } }
        }
    };

    private static List<A2AHubAgent> DiscoverAgents()
    {
        var agents = new List<A2AHubAgent>();
        agents.AddRange(DiscoverRevitAgents());
        agents.AddRange(DiscoverDesignDocsAgents());
        if (!agents.Any(a => a.Kind == "designdocsai"))
        {
            agents.Add(new A2AHubAgent
            {
                AgentId = "designdocsai:local",
                Kind = "designdocsai",
                DisplayName = "DesignDocsAI",
                Endpoint = "",
                Status = "configured",
                ReadOnly = true,
                Capabilities = new[] { "docs.search", "docs.lookup", "docs.extract.evidence", "docs.answer.with_citations" }
            });
        }

        agents.Add(new A2AHubAgent
        {
            AgentId = "excelmcp:5215",
            Kind = "excel-mcp",
            DisplayName = "ExcelMCP",
            Endpoint = "http://127.0.0.1:5215/mcp",
            Port = 5215,
            Status = IsTcpListening(5215) ? "online" : "offline",
            ReadOnly = false,
            Capabilities = new[] { "excel.read", "excel.write", "excel.format", "excel.save" }
        });
        agents.Add(new A2AHubAgent
        {
            AgentId = "autocadmcp:5251",
            Kind = "autocad-mcp",
            DisplayName = "AutoCadMCP",
            Endpoint = "http://127.0.0.1:5251/rpc",
            Port = 5251,
            Status = IsTcpListening(5251) ? "online" : "offline",
            ReadOnly = false,
            Capabilities = new[] { "autocad.read", "autocad.write" }
        });
        return agents;
    }

    private static IEnumerable<A2AHubAgent> DiscoverRevitAgents()
    {
        var states = ReadRevitServerState();
        var seen = new HashSet<int>();
        foreach (var s in states)
        {
            if (s.Port <= 0 || !seen.Add(s.Port)) continue;
            yield return ToRevitAgent(s, IsTcpListening(s.Port) ? "online" : "stale");
        }

        for (var port = 5210; port <= 5230; port++)
        {
            if (IsKnownNonRevitPort(port)) continue;
            if (seen.Contains(port) || !IsTcpListening(port)) continue;
            yield return ToRevitAgent(new RevitState { Port = port, Endpoint = $"http://127.0.0.1:{port}/rpc" }, "online");
        }
    }

    private static IReadOnlyList<A2AHubAgent> DiscoverDesignDocsAgents()
    {
        var agents = new List<A2AHubAgent>();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalA2A", "registry", "instances");
        if (!Directory.Exists(root)) return agents;
        foreach (var path in Directory.GetFiles(root, "*.json"))
        {
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                var r = doc.RootElement;
                var appId = GetString(r, "app_id");
                var kind = GetString(r, "app_kind");
                var instanceId = GetString(r, "instance_id");
                if (string.IsNullOrWhiteSpace(instanceId)) continue;
                if (!IsDesignDocsInstance(appId, kind, instanceId)) continue;
                var caps = ReadStringArray(r, "capability_names").ToList();
                foreach (var c in new[] { "docs.search", "docs.lookup", "docs.extract.evidence", "docs.answer.with_citations" })
                    if (!caps.Contains(c, StringComparer.OrdinalIgnoreCase)) caps.Add(c);

                agents.Add(new A2AHubAgent
                {
                    AgentId = "designdocsai:" + instanceId,
                    InstanceId = instanceId,
                    Kind = "designdocsai",
                    DisplayName = GetString(r, "display_name", "DesignDocsAI"),
                    Endpoint = "",
                    Status = GetString(r, "status", "configured"),
                    ReadOnly = true,
                    LocalA2AInboxPath = GetString(r, "inbox_path"),
                    LocalA2AResultsPath = GetString(r, "results_path"),
                    LocalA2AEventsPath = GetString(r, "events_path"),
                    Capabilities = caps.ToArray()
                });
            }
            catch (Exception ex)
            {
                CodexGuiLog.Exception("DiscoverDesignDocsAgents failed: " + path, ex);
            }
            finally
            {
                doc?.Dispose();
            }
        }
        return agents;
    }

    private static bool IsDesignDocsInstance(string appId, string kind, string instanceId)
    {
        return appId.Contains("designdocsai", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("design_docs", StringComparison.OrdinalIgnoreCase)
            || instanceId.Contains("designdocsai", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownNonRevitPort(int port)
    {
        return port == 5215
            || port == 5220
            || port == 5251
            || port == 5260;
    }

    private static A2AHubAgent ToRevitAgent(RevitState s, string status)
    {
        var docGuid = string.IsNullOrWhiteSpace(s.DocGuid) ? "unknown" : s.DocGuid;
        var agentId = $"revit:{docGuid}:{s.ProcessId}:{s.Port}";
        return new A2AHubAgent
        {
            AgentId = agentId,
            Kind = "revit-mcp",
            DisplayName = string.IsNullOrWhiteSpace(s.DocTitle) ? $"Revit MCP {s.Port}" : $"Revit {s.RevitVersion} - {s.DocTitle}",
            Endpoint = string.IsNullOrWhiteSpace(s.Endpoint) ? $"http://127.0.0.1:{s.Port}/rpc" : s.Endpoint,
            Port = s.Port,
            ProcessId = s.ProcessId,
            RevitVersion = s.RevitVersion,
            DocTitle = s.DocTitle,
            DocGuid = s.DocGuid,
            DocPath = s.DocPath,
            Status = status,
            ReadOnly = string.IsNullOrWhiteSpace(s.DocGuid),
            Capabilities = new[] { "revit.context.read", "revit.selection.read", "revit.schedule.read", "revit.view.read", "revit.element.read" }
        };
    }

    private async Task HandleRpcAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var id = root.TryGetProperty("id", out var idEl) ? idEl.Clone() : default;
        var method = root.TryGetProperty("method", out var mEl) ? NormalizeRpcMethod(mEl.GetString() ?? "") : "";

        try
        {
            if (method == "agents/list")
            {
                await WriteJsonAsync(ctx.Response, 200, RpcResult(id, new { ok = true, agents = DiscoverAgents() })).ConfigureAwait(false);
                return;
            }
            if (method == "agents/get")
            {
                var p = root.TryGetProperty("params", out var pEl) ? pEl : default;
                var agentId = GetString(p, "agentId");
                var agent = DiscoverAgents().FirstOrDefault(a => string.Equals(a.AgentId, agentId, StringComparison.OrdinalIgnoreCase));
                if (agent == null)
                {
                    await WriteJsonAsync(ctx.Response, 200, RpcError(id, -32011, "Agent not found.", "agent_not_found")).ConfigureAwait(false);
                    return;
                }
                await WriteJsonAsync(ctx.Response, 200, RpcResult(id, new { ok = true, agent })).ConfigureAwait(false);
                return;
            }
            if (method == "tasks/list")
            {
                ScanLocalA2AResults();
                await WriteJsonAsync(ctx.Response, 200, RpcResult(id, new { tasks = _taskStore.List() })).ConfigureAwait(false);
                return;
            }
            if (method == "tasks/get")
            {
                ScanLocalA2AResults();
                var p = root.TryGetProperty("params", out var pEl) ? pEl : default;
                var taskId = GetString(p, "id", GetString(p, "taskId"));
                var task = _taskStore.Get(taskId);
                if (task == null)
                {
                    await WriteJsonAsync(ctx.Response, 200, RpcError(id, -32012, "Task not found.", "task_not_found")).ConfigureAwait(false);
                    return;
                }
                await WriteJsonAsync(ctx.Response, 200, RpcResult(id, new { task = task.ToPublic(includePaths: true) })).ConfigureAwait(false);
                return;
            }
            if (method == "tasks/cancel")
            {
                var p = root.TryGetProperty("params", out var pEl) ? pEl : default;
                var taskId = GetString(p, "id", GetString(p, "taskId"));
                var task = _taskStore.Cancel(taskId);
                if (task == null)
                {
                    await WriteJsonAsync(ctx.Response, 200, RpcError(id, -32012, "Task not found.", "task_not_found")).ConfigureAwait(false);
                    return;
                }
                _audit.Write("task_canceled", task, "canceled", "not_required");
                await WriteJsonAsync(ctx.Response, 200, RpcResult(id, new { task = task.ToPublic() })).ConfigureAwait(false);
                return;
            }
            if (method == "message/send")
            {
                var req = NormalizeRpcRouteRequest(root);
                var route = await RouteAsync(req, ct).ConfigureAwait(false);
                if (route.Error != null)
                    await WriteJsonAsync(ctx.Response, 200, RpcError(id, route.Error.Code, route.Error.Message, route.Error.ErrorCode, route.Error.Data)).ConfigureAwait(false);
                else
                    await WriteJsonAsync(ctx.Response, 200, RpcResult(id, new { task = route.Task?.ToPublic(includePaths: false) })).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(ctx.Response, 200, RpcError(id, -32601, "Method not found.", "capability_not_supported")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CodexGuiLog.Exception("A2AHubService HandleRpcAsync failed", ex);
            await WriteJsonAsync(ctx.Response, 200, RpcError(id, -32000, ex.Message, "transport_failed")).ConfigureAwait(false);
        }
    }

    private async Task HandleRouteAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var req = NormalizeShorthandRouteRequest(doc.RootElement);
        var route = await RouteAsync(req, ct).ConfigureAwait(false);
        if (route.Error != null)
        {
            await WriteJsonAsync(ctx.Response, 400, new { ok = false, error = route.Error }).ConfigureAwait(false);
            return;
        }
        await WriteJsonAsync(ctx.Response, 200, new { ok = true, task = route.Task?.ToPublic(includePaths: false) }).ConfigureAwait(false);
    }

    private async Task<RouteResult> RouteAsync(A2AHubRouteRequest request, CancellationToken ct)
    {
        request = request.WithDefaults();
        var risk = ClassifyRisk(request.Capability);
        var agents = DiscoverAgents();
        var resolved = ResolveTarget(request.Target, request.Capability, risk, agents);
        if (resolved.Error != null) return RouteResult.Fail(resolved.Error);

        if (risk == "write")
        {
            var task = _taskStore.Create(request, resolved.Agent, risk, "approval_required", "Write routing is not implemented in Stage 1.");
            _audit.Write("route_rejected", task, "approval_required", "required");
            return RouteResult.Ok(task);
        }

        if (resolved.Agent == null)
            return RouteResult.Fail(new HubError(-32011, "Agent not found.", "agent_not_found"));

        if (resolved.Agent.Kind == "revit-mcp")
            return await RouteRevitAsync(request, resolved.Agent, risk, ct).ConfigureAwait(false);

        if (resolved.Agent.Kind == "designdocsai")
            return RouteDesignDocsAI(request, resolved.Agent, risk);

        var unsupported = _taskStore.Create(request, resolved.Agent, risk, "unsupported_in_stage_1", "This agent route is not implemented in Stage 1.");
        _audit.Write("route_rejected", unsupported, "unsupported_in_stage_1", "not_required");
        return RouteResult.Ok(unsupported);
    }

    private async Task<RouteResult> RouteRevitAsync(A2AHubRouteRequest request, A2AHubAgent agent, string risk, CancellationToken ct)
    {
        if (!string.Equals(request.Capability, "revit.context.read", StringComparison.OrdinalIgnoreCase))
        {
            var task = _taskStore.Create(request, agent, risk, "unsupported_in_stage_1", "Only revit.context.read is supported in Stage 1.");
            _audit.Write("route_rejected", task, "unsupported_in_stage_1", "not_required");
            return RouteResult.Ok(task);
        }
        if (!string.Equals(agent.Status, "online", StringComparison.OrdinalIgnoreCase))
            return RouteResult.Fail(new HubError(-32012, "Agent is offline.", "agent_offline"));

        var running = _taskStore.Create(request, agent, risk, "submitted", "Revit get_context submitted.");
        _audit.Write("route_requested", running, "submitted", "not_required");
        try
        {
            var endpoint = string.IsNullOrWhiteSpace(agent.Endpoint) ? $"http://127.0.0.1:{agent.Port}/rpc" : agent.Endpoint;
            var rpc = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = running.TaskId,
                ["method"] = "get_context",
                ["params"] = CloneJsonObject(request.Arguments)
            };
            var result = await PostJsonAsync(endpoint, rpc, ct).ConfigureAwait(false);
            var finalResult = await ResolveRevitJobIfNeeded(agent, result, ct).ConfigureAwait(false);
            _taskStore.Complete(running.TaskId, finalResult, "Revit context read completed.");
            var completed = _taskStore.Get(running.TaskId) ?? running;
            _audit.Write("route_completed", completed, "completed", "not_required");
            return RouteResult.Ok(completed);
        }
        catch (Exception ex)
        {
            _taskStore.Fail(running.TaskId, "transport_failed", ex.Message);
            var failed = _taskStore.Get(running.TaskId) ?? running;
            _audit.Write("route_failed", failed, "failed", "not_required");
            return RouteResult.Ok(failed);
        }
    }

    private RouteResult RouteDesignDocsAI(A2AHubRouteRequest request, A2AHubAgent agent, string risk)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "docs.search", "docs.lookup", "docs.extract.evidence", "docs.answer.with_citations",
            "document.get_status", "core.get_status"
        };
        if (!allowed.Contains(request.Capability))
        {
            var unsupported = _taskStore.Create(request, agent, risk, "unsupported_in_stage_1", "DesignDocsAI capability is not supported in Stage 1.");
            _audit.Write("route_rejected", unsupported, "unsupported_in_stage_1", "not_required");
            return RouteResult.Ok(unsupported);
        }

        var inbox = agent.LocalA2AInboxPath;
        if (string.IsNullOrWhiteSpace(inbox))
            return RouteResult.Fail(new HubError(-32013, "DesignDocsAI inbox is not configured.", "agent_not_found"));

        var existing = _taskStore.FindByRequestId(request.RequestId);
        var payloadHash = request.PayloadHash();
        if (existing != null)
        {
            if (string.Equals(existing.RequestPayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
                return RouteResult.Ok(existing);
            return RouteResult.Fail(new HubError(-32015, "Duplicate request_id with different payload.", "duplicate_request_id_conflict"));
        }

        var task = _taskStore.Create(request, agent, risk, "accepted", "DesignDocsAI task accepted.");
        task.RequestPayloadHash = payloadHash;
        _taskStore.Save(task);
        _audit.Write("route_requested", task, "accepted", "not_required");

        try
        {
            Directory.CreateDirectory(inbox);
            var envelope = new JsonObject
            {
                ["envelope_version"] = "A2A_REQUEST_V1",
                ["trace_id"] = request.TraceId,
                ["request_id"] = request.RequestId,
                ["task_id"] = task.TaskId,
                ["from"] = "codexgui-hub",
                ["to"] = string.IsNullOrWhiteSpace(agent.InstanceId) ? "designdocsai-local" : agent.InstanceId,
                ["capability"] = request.Capability,
                ["mode"] = risk == "read" ? "read" : risk,
                ["arguments"] = CloneJsonObject(request.Arguments),
                ["policy"] = request.Policy != null ? CloneJsonObject(request.Policy) : new JsonObject { ["allow_agent_task"] = true },
                ["created_utc"] = DateTimeOffset.UtcNow.ToString("o"),
                ["deadline_utc"] = DateTimeOffset.UtcNow.AddMinutes(5).ToString("o"),
                ["hop_count"] = 0,
                ["route_history"] = new JsonArray("codexgui-hub")
            };
            var finalPath = AtomicWriteJson(inbox, envelope, request);
            task.State = "submitted";
            task.Transport = "LocalA2A";
            task.SubmittedPath = finalPath;
            task.UpdatedUtc = DateTimeOffset.UtcNow;
            task.ResultSummary = "Submitted to DesignDocsAI LocalA2A inbox.";
            _taskStore.Save(task);
            _audit.Write("route_submitted", task, "submitted", "not_required");
            return RouteResult.Ok(task);
        }
        catch (Exception ex)
        {
            _taskStore.Fail(task.TaskId, "transport_failed", ex.Message);
            var failed = _taskStore.Get(task.TaskId) ?? task;
            _audit.Write("route_failed", failed, "failed", "not_required");
            return RouteResult.Ok(failed);
        }
    }

    private async Task<JsonNode?> ResolveRevitJobIfNeeded(A2AHubAgent agent, JsonNode? result, CancellationToken ct)
    {
        var obj = UnwrapResult(result) as JsonObject;
        if (obj == null) return result;
        var queued = ReadBool(obj, "queued");
        var jobId = ReadString(obj, "jobId");
        if (string.IsNullOrWhiteSpace(jobId)) jobId = ReadString(obj, "job_id");
        if (!queued || string.IsNullOrWhiteSpace(jobId)) return result;

        var baseUrl = agent.Endpoint;
        if (baseUrl.EndsWith("/rpc", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl.Substring(0, baseUrl.Length - 4);
        for (var i = 0; i < 120; i++)
        {
            ct.ThrowIfCancellationRequested();
            var json = await _http.GetStringAsync($"{baseUrl.TrimEnd('/')}/job/{Uri.EscapeDataString(jobId)}", ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            var state = ReadString(node as JsonObject, "state").ToUpperInvariant();
            if (state == "SUCCEEDED")
            {
                var resultJson = ReadString(node as JsonObject, "result_json");
                if (!string.IsNullOrWhiteSpace(resultJson))
                    return JsonNode.Parse(resultJson);
                return node;
            }
            if (state == "FAILED" || state == "TIMEOUT" || state == "DEAD")
                return node;
            await Task.Delay(i < 10 ? 500 : 2000, ct).ConfigureAwait(false);
        }
        return new JsonObject { ["ok"] = false, ["state"] = "TIMEOUT", ["msg"] = "Revit job polling timed out." };
    }

    private static JsonNode? UnwrapResult(JsonNode? node)
    {
        var cur = node;
        for (var i = 0; i < 5; i++)
        {
            if (cur is JsonObject obj && obj.TryGetPropertyValue("result", out var next) && next != null)
                cur = next;
            else
                break;
        }
        return cur;
    }

    private async Task<JsonNode?> PostJsonAsync(string url, JsonObject payload, CancellationToken ct)
    {
        using var content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {text}");
        return JsonNode.Parse(text);
    }

    private void ScanLocalA2AResults()
    {
        var pending = _taskStore.ListInternal()
            .Where(t => string.Equals(t.Transport, "LocalA2A", StringComparison.OrdinalIgnoreCase)
                     && (t.State == "accepted" || t.State == "submitted" || t.State == "working" || t.State == "approval_pending"))
            .ToList();
        if (pending.Count == 0) return;

        var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalA2A");
        foreach (var d in new[]
        {
            Path.Combine(localRoot, "hub", "incoming", "results"),
            Path.Combine(localRoot, "hub", "incoming", "events")
        })
            if (Directory.Exists(d)) searchDirs.Add(d);
        foreach (var a in DiscoverAgents().Where(a => a.Kind == "designdocsai"))
        {
            if (!string.IsNullOrWhiteSpace(a.LocalA2AResultsPath) && Directory.Exists(a.LocalA2AResultsPath)) searchDirs.Add(a.LocalA2AResultsPath);
            if (!string.IsNullOrWhiteSpace(a.LocalA2AEventsPath) && Directory.Exists(a.LocalA2AEventsPath)) searchDirs.Add(a.LocalA2AEventsPath);
        }

        foreach (var dir in searchDirs)
        {
            foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(File.GetLastWriteTimeUtc))
            {
                TryApplyLocalA2AResult(file, pending);
            }
        }
    }

    private void TryApplyLocalA2AResult(string file, List<A2AHubTask> pending)
    {
        try
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var requestId = GetString(root, "request_id", GetString(root, "requestId"));
            var traceId = GetString(root, "trace_id", GetString(root, "traceId"));
            var taskId = GetString(root, "task_id", GetString(root, "taskId"));
            var match = pending.FirstOrDefault(t =>
                (!string.IsNullOrWhiteSpace(taskId) && string.Equals(t.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(requestId) && string.Equals(t.RequestId, requestId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(traceId) && string.Equals(t.TraceId, traceId, StringComparison.OrdinalIgnoreCase)));
            if (match == null) return;

            var state = GetString(root, "state", GetString(root, "status"));
            if (string.IsNullOrWhiteSpace(state)) state = file.Contains("\\events\\", StringComparison.OrdinalIgnoreCase) ? "working" : "completed";
            state = NormalizeTaskState(state);
            if (state == "completed")
            {
                var payloadPath = _taskStore.WritePayload(match.TaskId, JsonNode.Parse(text));
                _taskStore.Complete(match.TaskId, JsonNode.Parse(text), "DesignDocsAI result received.", payloadPath);
                var completed = _taskStore.Get(match.TaskId) ?? match;
                _audit.Write("route_completed", completed, "completed", "not_required");
            }
            else if (state == "failed" || state == "rejected" || state == "canceled" || state == "timeout")
            {
                _taskStore.SetState(match.TaskId, state, GetString(root, "msg", GetString(root, "message", state)), file);
                var updated = _taskStore.Get(match.TaskId) ?? match;
                _audit.Write("route_finished", updated, state, "not_required");
            }
            else
            {
                _taskStore.SetState(match.TaskId, "working", "DesignDocsAI is working.", file);
            }
        }
        catch (Exception ex)
        {
            CodexGuiLog.Exception("TryApplyLocalA2AResult failed: " + file, ex);
        }
    }

    private static string NormalizeTaskState(string state)
    {
        state = (state ?? string.Empty).Trim().ToLowerInvariant();
        return state switch
        {
            "succeeded" or "success" or "done" => "completed",
            "error" => "failed",
            "cancelled" => "canceled",
            "submitted" => "submitted",
            "accepted" => "accepted",
            "working" or "running" => "working",
            "rejected" => "rejected",
            "timeout" or "timedout" => "timeout",
            _ => string.IsNullOrWhiteSpace(state) ? "working" : state
        };
    }

    private static A2AHubRouteRequest NormalizeRpcRouteRequest(JsonElement root)
    {
        var p = root.TryGetProperty("params", out var pEl) ? pEl : default;
        var data = ExtractMessageData(p);
        var target = GetString(p, "targetAgentId", GetString(p, "target"));
        var capability = GetString(p, "capability");
        JsonNode? args = null;
        JsonNode? policy = null;

        if (data.ValueKind == JsonValueKind.Object)
        {
            if (string.IsNullOrWhiteSpace(target)) target = GetString(data, "targetAgentId", GetString(data, "target"));
            if (string.IsNullOrWhiteSpace(capability)) capability = GetString(data, "capability");
            if (data.TryGetProperty("arguments", out var a)) args = JsonNode.Parse(a.GetRawText());
            if (data.TryGetProperty("policy", out var pol)) policy = JsonNode.Parse(pol.GetRawText());
        }
        if (args == null && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("arguments", out var pa)) args = JsonNode.Parse(pa.GetRawText());
        if (policy == null && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("policy", out var pp)) policy = JsonNode.Parse(pp.GetRawText());

        return new A2AHubRouteRequest
        {
            RequestId = GetString(p, "requestId", GetString(p, "request_id")),
            TraceId = GetString(p, "traceId", GetString(p, "trace_id")),
            Sender = GetString(p, "sender", "unknown"),
            Target = target,
            Capability = capability,
            Arguments = args as JsonObject ?? new JsonObject(),
            Policy = policy as JsonObject,
            ReturnImmediately = ReadBool(p, "returnImmediately")
        };
    }

    private static JsonElement ExtractMessageData(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object) return default;
        if (p.TryGetProperty("message", out var msg)
            && msg.ValueKind == JsonValueKind.Object
            && msg.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    return data;
            }
        }
        return default;
    }

    private static A2AHubRouteRequest NormalizeShorthandRouteRequest(JsonElement root)
    {
        JsonNode? args = null;
        JsonNode? policy = null;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("arguments", out var a)) args = JsonNode.Parse(a.GetRawText());
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("policy", out var p)) policy = JsonNode.Parse(p.GetRawText());
        return new A2AHubRouteRequest
        {
            RequestId = GetString(root, "requestId", GetString(root, "request_id")),
            TraceId = GetString(root, "traceId", GetString(root, "trace_id")),
            Sender = GetString(root, "sender", "codexgui"),
            Target = GetString(root, "targetAgentId", GetString(root, "target")),
            Capability = GetString(root, "capability"),
            Arguments = args as JsonObject ?? new JsonObject(),
            Policy = policy as JsonObject,
            ReturnImmediately = ReadBool(root, "returnImmediately")
        };
    }

    private static string NormalizeRpcMethod(string method)
    {
        if (string.Equals(method, "SendMessage", StringComparison.OrdinalIgnoreCase)) return "message/send";
        if (string.Equals(method, "GetTask", StringComparison.OrdinalIgnoreCase)) return "tasks/get";
        if (string.Equals(method, "ListTasks", StringComparison.OrdinalIgnoreCase)) return "tasks/list";
        if (string.Equals(method, "CancelTask", StringComparison.OrdinalIgnoreCase)) return "tasks/cancel";
        if (string.Equals(method, "agent/list", StringComparison.OrdinalIgnoreCase)) return "agents/list";
        if (string.Equals(method, "agent/get", StringComparison.OrdinalIgnoreCase)) return "agents/get";
        if (string.Equals(method, "hub.agents", StringComparison.OrdinalIgnoreCase)) return "agents/list";
        return method ?? string.Empty;
    }

    private static string ClassifyRisk(string capability)
    {
        capability = capability ?? string.Empty;
        if (capability.EndsWith(".write", StringComparison.OrdinalIgnoreCase)
            || capability.Contains(".delete", StringComparison.OrdinalIgnoreCase)
            || capability.Contains(".modify", StringComparison.OrdinalIgnoreCase)
            || capability.Contains(".apply", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability, "codex.send_prompt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability, "codex.stop", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability, "docs.index.write", StringComparison.OrdinalIgnoreCase))
            return "write";
        if (capability.Contains(".diff", StringComparison.OrdinalIgnoreCase)
            || capability.Contains(".preview", StringComparison.OrdinalIgnoreCase)
            || capability.Contains(".compare", StringComparison.OrdinalIgnoreCase))
            return "preview";
        return "read";
    }

    private static TargetResolution ResolveTarget(string target, string capability, string risk, List<A2AHubAgent> agents)
    {
        target = (target ?? string.Empty).Trim();
        if (risk == "write" && (string.IsNullOrWhiteSpace(target)
            || target.Equals("current-revit", StringComparison.OrdinalIgnoreCase)
            || target.Equals("any-revit", StringComparison.OrdinalIgnoreCase)
            || target.Equals("all-revit", StringComparison.OrdinalIgnoreCase)))
            return TargetResolution.Fail(new HubError(-32010, "Exact target is required for writes.", "target_selection_required"));

        if (string.IsNullOrWhiteSpace(target))
            target = capability.StartsWith("docs.", StringComparison.OrdinalIgnoreCase) ? "designdocsai:active" : "current-revit";

        if (target.Equals("designdocsai:active", StringComparison.OrdinalIgnoreCase) || target.Equals("designdocsai:local", StringComparison.OrdinalIgnoreCase))
        {
            var agent = agents
                .Where(a => a.Kind == "designdocsai")
                .OrderByDescending(a => string.Equals(a.Status, "running", StringComparison.OrdinalIgnoreCase) || string.Equals(a.Status, "online", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(a => !string.IsNullOrWhiteSpace(a.LocalA2AInboxPath))
                .FirstOrDefault();
            return agent == null ? TargetResolution.Fail(new HubError(-32011, "DesignDocsAI agent not found.", "agent_not_found")) : TargetResolution.Ok(agent);
        }

        if (target.Equals("current-revit", StringComparison.OrdinalIgnoreCase))
        {
            var revits = agents.Where(a => a.Kind == "revit-mcp" && a.Status == "online").ToList();
            if (revits.Count == 1) return TargetResolution.Ok(revits[0]);
            return TargetResolution.Fail(new HubError(-32010, "Target selection is required.", "target_selection_required", new { candidates = revits.Select(a => a.AgentId).ToArray() }));
        }

        if (target.Equals("all-revit", StringComparison.OrdinalIgnoreCase))
            return TargetResolution.Fail(new HubError(-32016, "all-revit fan-out is not implemented in Stage 1.", "unsupported_in_stage_1"));

        if (target.Equals("excelmcp:active", StringComparison.OrdinalIgnoreCase))
            target = "excelmcp:5215";
        if (target.Equals("autocadmcp:active", StringComparison.OrdinalIgnoreCase))
            target = "autocadmcp:5251";

        var exact = agents.FirstOrDefault(a => string.Equals(a.AgentId, target, StringComparison.OrdinalIgnoreCase));
        return exact == null ? TargetResolution.Fail(new HubError(-32011, "Agent not found.", "agent_not_found")) : TargetResolution.Ok(exact);
    }

    private static string AtomicWriteJson(string inbox, JsonObject envelope, A2AHubRouteRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var guid = Guid.NewGuid().ToString("N");
        var finalName = $"{now:yyyyMMddHHmmssfff}_{SafeFilePart(request.RequestId)}_{SafeFilePart(request.Capability)}_{guid}.json";
        var finalPath = Path.Combine(inbox, finalName);
        var tempPath = Path.Combine(inbox, finalName + ".tmp." + guid);
        File.WriteAllText(tempPath, envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(tempPath, finalPath);
        return finalPath;
    }

    private static string SafeFilePart(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "request" : value.Trim();
        value = Regex.Replace(value, @"[^A-Za-z0-9_.-]+", "_");
        return value.Length > 80 ? value.Substring(0, 80) : value;
    }

    private static List<RevitState> ReadRevitServerState()
    {
        var result = new List<RevitState>();
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP", "server_state.json");
            if (!File.Exists(path)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            if (root.TryGetProperty("instances", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray()) AddRevitState(result, item);
            }
            else
            {
                AddRevitState(result, root);
            }
        }
        catch (Exception ex)
        {
            CodexGuiLog.Exception("ReadRevitServerState failed", ex);
        }
        return result;
    }

    private static void AddRevitState(List<RevitState> result, JsonElement item)
    {
        var port = GetInt(item, "port");
        if (port <= 0) return;
        result.Add(new RevitState
        {
            Port = port,
            Endpoint = GetString(item, "endpoint"),
            ProcessId = Math.Max(GetInt(item, "processId"), GetInt(item, "pid")),
            RevitVersion = GetString(item, "revitVersion"),
            DocTitle = GetString(item, "docTitle"),
            DocGuid = GetString(item, "docGuid"),
            DocPath = GetString(item, "docPath")
        });
    }

    private static bool IsTcpListening(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var ar = client.BeginConnect(IPAddress.Loopback, port, null, null);
            var ok = ar.AsyncWaitHandle.WaitOne(120);
            if (!ok) return false;
            client.EndConnect(ar);
            return true;
        }
        catch { return false; }
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return null;
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var n) ? n : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => JsonNode.Parse(element.GetRawText())
        };
    }

    private static object RpcResult(JsonElement id, object result) => new { jsonrpc = "2.0", id = JsonElementToObject(id), result };

    private static object RpcError(JsonElement id, int code, string message, string errorCode, object? data = null)
    {
        var errorData = new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode
        };
        if (data != null) errorData["detail"] = data;

        return new
        {
            jsonrpc = "2.0",
            id = JsonElementToObject(id),
            error = new
            {
                code,
                message,
                data = errorData
            }
        };
    }

    private static JsonObject CloneJsonObject(JsonObject? node)
    {
        if (node == null) return new JsonObject();
        return JsonNode.Parse(node.ToJsonString()) as JsonObject ?? new JsonObject();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        response.Close();
    }

    private static string GetString(JsonElement element, string name, string fallback = "")
    {
        if (element.ValueKind != JsonValueKind.Object) return fallback;
        if (element.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? fallback;
            if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) return value.ToString();
        }
        return fallback;
    }

    private static int GetInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)) return i;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return bool.TryParse(value.ToString(), out var b) && b;
    }

    private static bool ReadBool(JsonObject? obj, string name)
    {
        if (obj == null || !obj.TryGetPropertyValue(name, out var value) || value == null) return false;
        if (value is JsonValue jv && jv.TryGetValue<bool>(out var b)) return b;
        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static string ReadString(JsonObject? obj, string name)
    {
        if (obj == null || !obj.TryGetPropertyValue(name, out var value) || value == null) return string.Empty;
        if (value is JsonValue jv && jv.TryGetValue<string>(out var s)) return s ?? string.Empty;
        return value.ToString();
    }

    private static IEnumerable<string> ReadStringArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in arr.EnumerateArray())
        {
            var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
            if (!string.IsNullOrWhiteSpace(s)) yield return s!;
        }
    }

    private sealed class RevitState
    {
        public int Port { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string RevitVersion { get; set; } = string.Empty;
        public string DocTitle { get; set; } = string.Empty;
        public string DocGuid { get; set; } = string.Empty;
        public string DocPath { get; set; } = string.Empty;
    }

    private sealed class A2AHubAgent
    {
        public string AgentId { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public int Port { get; set; }
        public int ProcessId { get; set; }
        public string RevitVersion { get; set; } = string.Empty;
        public string DocTitle { get; set; } = string.Empty;
        public string DocGuid { get; set; } = string.Empty;
        public string DocPath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool ReadOnly { get; set; }
        public string[] Capabilities { get; set; } = Array.Empty<string>();
        public string LocalA2AInboxPath { get; set; } = string.Empty;
        public string LocalA2AResultsPath { get; set; } = string.Empty;
        public string LocalA2AEventsPath { get; set; } = string.Empty;
    }

    private sealed class A2AHubRouteRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Capability { get; set; } = string.Empty;
        public JsonObject Arguments { get; set; } = new();
        public JsonObject? Policy { get; set; }
        public bool ReturnImmediately { get; set; }

        public A2AHubRouteRequest WithDefaults()
        {
            RequestId = string.IsNullOrWhiteSpace(RequestId) ? "req-" + Guid.NewGuid().ToString("N") : RequestId;
            TraceId = string.IsNullOrWhiteSpace(TraceId) ? "trace-" + Guid.NewGuid().ToString("N") : TraceId;
            Sender = string.IsNullOrWhiteSpace(Sender) ? "unknown" : Sender;
            Capability = Capability?.Trim() ?? string.Empty;
            Target = Target?.Trim() ?? string.Empty;
            Arguments ??= new JsonObject();
            return this;
        }

        public string PayloadHash()
        {
            var payload = new JsonObject
            {
                ["target"] = Target,
                ["capability"] = Capability,
                ["arguments"] = CloneJsonObject(Arguments),
                ["policy"] = Policy != null ? CloneJsonObject(Policy) : null
            };
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString(JsonOptions));
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
    }

    private sealed class HubError
    {
        public int Code { get; }
        public string Message { get; }
        public string ErrorCode { get; }
        public object? Data { get; }

        public HubError(int code, string message, string errorCode, object? data = null)
        {
            Code = code;
            Message = message;
            ErrorCode = errorCode;
            Data = data;
        }
    }

    private sealed class TargetResolution
    {
        public A2AHubAgent? Agent { get; private set; }
        public HubError? Error { get; private set; }
        public static TargetResolution Ok(A2AHubAgent agent) => new() { Agent = agent };
        public static TargetResolution Fail(HubError error) => new() { Error = error };
    }

    private sealed class RouteResult
    {
        public A2AHubTask? Task { get; private set; }
        public HubError? Error { get; private set; }
        public static RouteResult Ok(A2AHubTask task) => new() { Task = task };
        public static RouteResult Fail(HubError error) => new() { Error = error };
    }

    private sealed class A2AHubTask
    {
        public string TaskId { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string TargetAgentId { get; set; } = string.Empty;
        public string Capability { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string RiskClass { get; set; } = string.Empty;
        public string Transport { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public string RequestSummary { get; set; } = string.Empty;
        public string ResultSummary { get; set; } = string.Empty;
        public string ResultPayloadPath { get; set; } = string.Empty;
        public string SubmittedPath { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string RequestPayloadHash { get; set; } = string.Empty;

        public object ToPublic(bool includePaths = false) => new
        {
            id = TaskId,
            state = State,
            traceId = TraceId,
            requestId = RequestId,
            sender = Sender,
            targetAgentId = TargetAgentId,
            capability = Capability,
            riskClass = RiskClass,
            transport = Transport,
            createdUtc = CreatedUtc.ToString("o"),
            updatedUtc = UpdatedUtc.ToString("o"),
            requestSummary = RequestSummary,
            resultSummary = ResultSummary,
            errorCode = ErrorCode,
            error = Error,
            resultPayloadPath = includePaths ? ResultPayloadPath : null,
            submittedPath = includePaths ? SubmittedPath : null
        };
    }

    private sealed class A2ATaskStore
    {
        private readonly object _sync = new();
        private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP", "A2AHub", "tasks");
        private readonly string _payloadRoot;

        public A2ATaskStore()
        {
            _payloadRoot = Path.Combine(_root, "payloads");
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_payloadRoot);
        }

        public A2AHubTask Create(A2AHubRouteRequest request, A2AHubAgent? agent, string risk, string state, string summary)
        {
            var now = DateTimeOffset.UtcNow;
            var task = new A2AHubTask
            {
                TaskId = "task-" + Guid.NewGuid().ToString("N"),
                TraceId = request.TraceId,
                RequestId = request.RequestId,
                Sender = request.Sender,
                TargetAgentId = agent?.AgentId ?? request.Target,
                Capability = request.Capability,
                State = state,
                RiskClass = risk,
                Transport = agent?.Kind == "designdocsai" ? "LocalA2A" : agent?.Kind == "revit-mcp" ? "RevitMCP" : "none",
                CreatedUtc = now,
                UpdatedUtc = now,
                RequestSummary = SummarizeRequest(request),
                ResultSummary = summary
            };
            Save(task);
            return task;
        }

        public void Save(A2AHubTask task)
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_root);
                var path = TaskPath(task.TaskId);
                var tmp = path + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(tmp, JsonSerializer.Serialize(task, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
        }

        public A2AHubTask? Get(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return null;
            var path = TaskPath(taskId);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<A2AHubTask>(File.ReadAllText(path, Encoding.UTF8));
        }

        public A2AHubTask? FindByRequestId(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return null;
            return ListInternal().FirstOrDefault(t => string.Equals(t.RequestId, requestId, StringComparison.OrdinalIgnoreCase));
        }

        public List<object> List()
        {
            return ListInternal().OrderByDescending(t => t.UpdatedUtc).Select(t => t.ToPublic()).ToList();
        }

        public List<A2AHubTask> ListInternal()
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_root);
                var result = new List<A2AHubTask>();
                foreach (var file in Directory.GetFiles(_root, "task-*.json"))
                {
                    try
                    {
                        var task = JsonSerializer.Deserialize<A2AHubTask>(File.ReadAllText(file, Encoding.UTF8));
                        if (task != null) result.Add(task);
                    }
                    catch { }
                }
                return result;
            }
        }

        public void Complete(string taskId, JsonNode? result, string summary, string? payloadPath = null)
        {
            var task = Get(taskId);
            if (task == null) return;
            task.State = "completed";
            task.UpdatedUtc = DateTimeOffset.UtcNow;
            task.ResultSummary = summary;
            task.ResultPayloadPath = payloadPath ?? WritePayload(taskId, result);
            Save(task);
        }

        public void Fail(string taskId, string code, string message)
        {
            var task = Get(taskId);
            if (task == null) return;
            task.State = "failed";
            task.UpdatedUtc = DateTimeOffset.UtcNow;
            task.ErrorCode = code;
            task.Error = message;
            task.ResultSummary = message;
            Save(task);
        }

        public void SetState(string taskId, string state, string summary, string? payloadPath = null)
        {
            var task = Get(taskId);
            if (task == null) return;
            task.State = state;
            task.UpdatedUtc = DateTimeOffset.UtcNow;
            task.ResultSummary = summary;
            if (!string.IsNullOrWhiteSpace(payloadPath)) task.ResultPayloadPath = payloadPath;
            Save(task);
        }

        public A2AHubTask? Cancel(string taskId)
        {
            var task = Get(taskId);
            if (task == null) return null;
            task.State = "canceled";
            task.UpdatedUtc = DateTimeOffset.UtcNow;
            task.ResultSummary = "Canceled by request.";
            Save(task);
            return task;
        }

        public string WritePayload(string taskId, JsonNode? payload)
        {
            Directory.CreateDirectory(_payloadRoot);
            var path = Path.Combine(_payloadRoot, taskId + ".result.json");
            File.WriteAllText(path, payload?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null", new UTF8Encoding(false));
            return path;
        }

        public void MarkTimeouts(TimeSpan timeout, A2AAuditLogService audit)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var task in ListInternal())
            {
                if ((task.State == "accepted" || task.State == "submitted" || task.State == "working")
                    && now - task.UpdatedUtc > timeout)
                {
                    task.State = "timeout";
                    task.UpdatedUtc = now;
                    task.ResultSummary = "Task timed out.";
                    Save(task);
                    audit.Write("task_timeout", task, "timeout", "not_required");
                }
            }
        }

        private string TaskPath(string taskId) => Path.Combine(_root, taskId + ".json");

        private static string SummarizeRequest(A2AHubRouteRequest request)
        {
            var q = string.Empty;
            if (request.Arguments.TryGetPropertyValue("query", out var query) && query != null)
                q = query.ToString();
            if (q.Length > 80) q = q.Substring(0, 80) + "...";
            return string.IsNullOrWhiteSpace(q) ? request.Capability : request.Capability + ": " + q;
        }
    }

    private sealed class A2AAuditLogService
    {
        private readonly object _sync = new();
        private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP", "A2AHub", "audit");

        public void Write(string eventType, A2AHubTask task, string status, string approvalState)
        {
            try
            {
                lock (_sync)
                {
                    Directory.CreateDirectory(_root);
                    var path = Path.Combine(_root, "audit-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd") + ".ndjson");
                    var obj = new
                    {
                        timestampUtc = DateTimeOffset.UtcNow.ToString("o"),
                        eventType,
                        taskId = task.TaskId,
                        traceId = task.TraceId,
                        requestId = task.RequestId,
                        sender = task.Sender,
                        targetAgentId = task.TargetAgentId,
                        capability = task.Capability,
                        riskClass = task.RiskClass,
                        approvalState,
                        status
                    };
                    File.AppendAllText(path, JsonSerializer.Serialize(obj, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                CodexGuiLog.Exception("A2AAuditLogService.Write failed", ex);
            }
        }
    }
}
