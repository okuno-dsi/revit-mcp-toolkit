// MCP Playbook Server - Minimal API (safer templating version)
#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Options
string forwardBase = Cli.GetArg(args, "--forward") ?? "http://127.0.0.1:5210";
string portStr     = Cli.GetArg(args, "--port")    ?? "5209";

builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(int.Parse(portStr)));
var app  = builder.Build();
var http = new HttpClient(){ Timeout = TimeSpan.FromMinutes(5) };
http.DefaultRequestHeaders.ExpectContinue = false;

// In-memory state
var teach = new TeachState();

app.MapPost("/teach/start", (HttpRequest req) => {
    var name = req.Query["name"].ToString();
    return teach.Start(name);
});

app.MapPost("/teach/stop", () => teach.Stop());

// Proxy RPC to RevitMcpServer and record normalized entry
app.MapPost("/rpc", async (HttpRequest req, HttpResponse res) =>
{
    using var sr = new StreamReader(req.Body, Encoding.UTF8);
    var body = await sr.ReadToEndAsync();

    var fwd = await http.PostAsync($"{forwardBase}/rpc", new StringContent(body, Encoding.UTF8, "application/json"));
    var text = await fwd.Content.ReadAsStringAsync();

    try { teach.Record(Recorder.Normalize(body, text)); } catch {}

    res.StatusCode = (int)fwd.StatusCode;
    res.ContentType = "application/json; charset=utf-8";
    await res.WriteAsync(text);
});

// Durable queue passthroughs (Revit MCP): /enqueue, /get_result, /job/{id}
app.MapPost("/enqueue", async (HttpRequest req, HttpResponse res) =>
{
    var forwardUrl = $"{forwardBase}/enqueue{req.QueryString}";
    await Proxy.ForwardPostAsync(http, req, res, forwardUrl, teach);
});

app.MapGet("/get_result", async (HttpRequest req, HttpResponse res) =>
{
    var forwardUrl = $"{forwardBase}/get_result{req.QueryString}";
    await Proxy.ForwardGetAsync(http, req, res, forwardUrl);
});

app.MapGet("/job/{id}", async (HttpRequest req, HttpResponse res, string id) =>
{
    var forwardUrl = $"{forwardBase}/job/{Uri.EscapeDataString(id)}{req.QueryString}";
    await Proxy.ForwardGetAsync(http, req, res, forwardUrl);
});

// ---------- Dynamic target routing: /t/{port}/... ----------
app.MapPost("/t/{port}/rpc", async (HttpRequest req, HttpResponse res, string port) =>
{
    if (!NetUtil.TryParseLocalPort(port, out var p)) { res.StatusCode = 400; await res.WriteAsync("{\"ok\":false,\"error\":\"invalid port\"}"); return; }
    var url = $"http://127.0.0.1:{p}/rpc";
    await Proxy.ForwardPostAsync(http, req, res, url, teach);
});

app.MapPost("/t/{port}/enqueue", async (HttpRequest req, HttpResponse res, string port) =>
{
    if (!NetUtil.TryParseLocalPort(port, out var p)) { res.StatusCode = 400; await res.WriteAsync("{\"ok\":false,\"error\":\"invalid port\"}"); return; }
    var url = $"http://127.0.0.1:{p}/enqueue{req.QueryString}";
    await Proxy.ForwardPostAsync(http, req, res, url, teach);
});

app.MapGet("/t/{port}/get_result", async (HttpRequest req, HttpResponse res, string port) =>
{
    if (!NetUtil.TryParseLocalPort(port, out var p)) { res.StatusCode = 400; await res.WriteAsync("{\"ok\":false,\"error\":\"invalid port\"}"); return; }
    var url = $"http://127.0.0.1:{p}/get_result{req.QueryString}";
    await Proxy.ForwardGetAsync(http, req, res, url);
});

app.MapGet("/t/{port}/job/{id}", async (HttpRequest req, HttpResponse res, string port, string id) =>
{
    if (!NetUtil.TryParseLocalPort(port, out var p)) { res.StatusCode = 400; await res.WriteAsync("{\"ok\":false,\"error\":\"invalid port\"}"); return; }
    var url = $"http://127.0.0.1:{p}/job/{Uri.EscapeDataString(id)}{req.QueryString}";
    await Proxy.ForwardGetAsync(http, req, res, url);
});

app.MapPost("/t/{port}/replay", async (string port, ReplayRequest rr) =>
{
    if (!NetUtil.TryParseLocalPort(port, out var p)) return Results.BadRequest(new { ok=false, error="invalid port" });
    var plan = await RecipeLoader.LoadAsync(rr);
    var executor = new Replayer(http, $"http://127.0.0.1:{p}/rpc");
    return await executor.RunAsync(plan, rr.DryRun, rr.Args ?? new());
});

// Debug endpoints (optional): verify forwarding independently of client bodies
app.MapGet("/debug/enqueue_direct", async () =>
{
    var payload = new
    {
        jsonrpc = "2.0",
        id = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
        method = "ping_server",
        @params = new { }
    };
    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    using var fwd = await http.PostAsync($"{forwardBase}/enqueue", content);
    var text = await fwd.Content.ReadAsStringAsync();
    return Results.Text(text, "application/json; charset=utf-8");
});
app.MapGet("/debug/job_test", async () =>
{
    using var fwd = await http.GetAsync($"{forwardBase}/job/test");
    var text = await fwd.Content.ReadAsStringAsync();
    return Results.Text(((int)fwd.StatusCode)+": "+text, "text/plain; charset=utf-8");
});

// Replay endpoint
app.MapPost("/replay", async (ReplayRequest rr) =>
{
    var plan = await RecipeLoader.LoadAsync(rr);
    var executor = new Replayer(http, $"{forwardBase}/rpc");
    return await executor.RunAsync(plan, rr.DryRun, rr.Args ?? new());
});

app.Run();

static class Proxy
{
    public static async Task ForwardPostAsync(HttpClient http, HttpRequest req, HttpResponse res, string url, TeachState? teach = null)
    {
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);
        var bytes = ms.ToArray();
        using var content = new ByteArrayContent(bytes);
        var contentType = req.ContentType;
        try {
            if (!string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)){
                var mt = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                mt.CharSet = "utf-8";
                content.Headers.ContentType = mt;
            } else {
                var mt = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                mt.CharSet = "utf-8";
                content.Headers.ContentType = mt;
            }
        } catch {
            var mt = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            mt.CharSet = "utf-8";
            content.Headers.ContentType = mt;
        }
        CopyHeaders(req.Headers, content.Headers);

        using var fwd = await http.PostAsync(url, content);
        await WriteResponseAsync(res, fwd);

        if (teach != null){
            try { var bodyText = Encoding.UTF8.GetString(bytes); teach.Record(Recorder.Normalize(bodyText, await fwd.Content.ReadAsStringAsync())); } catch {}
        }
    }

    public static async Task ForwardGetAsync(HttpClient http, HttpRequest req, HttpResponse res, string url)
    {
        using var forward = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var h in req.Headers)
        {
            if (string.Equals(h.Key, "Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(h.Key, "Connection", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            try { forward.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray()); } catch {}
        }
        using var fwd = await http.SendAsync(forward, HttpCompletionOption.ResponseContentRead);
        await WriteResponseAsync(res, fwd);
    }

    private static async Task WriteResponseAsync(HttpResponse res, HttpResponseMessage fwd)
    {
        res.StatusCode = (int)fwd.StatusCode;
        if (fwd.Headers.ETag != null) res.Headers["ETag"] = fwd.Headers.ETag.ToString();
        if (fwd.Headers.RetryAfter != null) res.Headers["Retry-After"] = fwd.Headers.RetryAfter.ToString();
        res.ContentType = fwd.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
        var text = await fwd.Content.ReadAsStringAsync();
        await res.WriteAsync(text);
    }

    private static void CopyHeaders(IHeaderDictionary src, System.Net.Http.Headers.HttpContentHeaders dst)
    {
        foreach (var h in src)
        {
            var key = h.Key;
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            try { dst.TryAddWithoutValidation(key, h.Value.ToArray()); } catch {}
        }
    }
}

// --------------- helpers & types ---------------
internal static class Cli
{
    public static string? GetArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}

internal static class NetUtil
{
    public static bool TryParseLocalPort(string s, out int port)
    {
        if (int.TryParse(s, out port))
        {
            // Allow registered/ephemeral ports; optionally narrow (e.g., 5000-6000) if必要
            return port >= 1 && port <= 65535;
        }
        return false;
    }
}

// Replay request body
public sealed record ReplayRequest(string? SessionId, string? RecipePath, bool DryRun = false, Dictionary<string,object>? Args = null);

// Teaching/capture state
public sealed class TeachState {
    string? _dir;
    StreamWriter? _jsonl;
    public IResult Start(string? sessionName){
        var name = string.IsNullOrWhiteSpace(sessionName) ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : sessionName.Trim();
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP","Playbooks", name);
        Directory.CreateDirectory(_dir);
        _jsonl = new StreamWriter(Path.Combine(_dir, "capture.jsonl"), append:true, Encoding.UTF8);
        File.WriteAllText(Path.Combine(_dir, "playbook.md"), $"# Playbook: {name}\nCreated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_dir, "summary.yaml"), $"session: {name}\ncreated: {DateTime.Now:O}\n", Encoding.UTF8);
        return Results.Ok(new{ ok=true, dir=_dir });
    }
    public IResult Stop(){ _jsonl?.Dispose(); _jsonl=null; return Results.Ok(new{ok=true}); }
    public void Record(object entry){
        if(_jsonl!=null){
            _jsonl.WriteLine(JsonSerializer.Serialize(entry));
            _jsonl.Flush();
        }
    }
}

// Recorder: normalize JSON-RPC into concise JSONL entry
public static class Recorder {
    public static object Normalize(string reqJson, string resJson) {
        try{
            using var reqDoc = JsonDocument.Parse(reqJson);
            string method = reqDoc.RootElement.TryGetProperty("method", out var m) ? m.GetString() ?? string.Empty : string.Empty;
            var @params = reqDoc.RootElement.TryGetProperty("params", out var p) ? p : default;

            object? resultObj = null;
            try {
                using var resDoc = JsonDocument.Parse(resJson);
                var root = resDoc.RootElement;
                resultObj = root.TryGetProperty("result", out var r) ? (object)r : root;
            } catch { resultObj = new { raw = resJson }; }

            return new { t = DateTimeOffset.Now.ToUnixTimeMilliseconds(), method, @params, result = resultObj };
        } catch {
            return new { t = DateTimeOffset.Now.ToUnixTimeMilliseconds(), rawReq = reqJson, rawRes = resJson };
        }
    }
}

// Recipe model
public sealed class ReplayPlan {
    public string Name { get; set; } = "untitled";
    public Dictionary<string,object> Vars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ReplayStep> Steps { get; set; } = new();
}
public sealed class ReplayStep {
    public string Method { get; set; } = string.Empty;
    public JsonElement Params { get; set; }
    public Expectation? Expect { get; set; }
}
public sealed class Expectation {
    public int? MinUpdated { get; set; }
    public int? MaxUpdated { get; set; }
    public int? MinCreated { get; set; }
    public int? MaxCreated { get; set; }
    public int? MinCandidates { get; set; }
    public int? MaxCandidates { get; set; }
    public bool? RequireConfirm { get; set; }
}

public static class RecipeLoader {
    public static async Task<ReplayPlan> LoadAsync(ReplayRequest rr){
        if (!string.IsNullOrWhiteSpace(rr.RecipePath))
            return await LoadFromFileAsync(rr.RecipePath!);
        if (!string.IsNullOrWhiteSpace(rr.SessionId)){
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitMCP","Playbooks", rr.SessionId!);
            var path = Path.Combine(dir, "recipe.json");
            return await LoadFromFileAsync(path);
        }
        throw new InvalidOperationException("RecipePath or SessionId is required.");
    }
    static async Task<ReplayPlan> LoadFromFileAsync(string path){
        using var fs = File.OpenRead(path);
        var plan = await JsonSerializer.DeserializeAsync<ReplayPlan>(fs, new JsonSerializerOptions{ PropertyNameCaseInsensitive = true });
        return plan ?? new ReplayPlan();
    }
}

public sealed class Replayer {
    private readonly HttpClient _http;
    private readonly string _rpcUrl;
    public Replayer(HttpClient http, string rpcUrl){ _http=http; _rpcUrl=rpcUrl; }

    public async Task<object> RunAsync(ReplayPlan plan, bool dryRun, Dictionary<string,object> args){
        int ok=0, ng=0;
        var details = new List<object>();

        var vars = new Dictionary<string,object>(plan.Vars, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in args) vars[kv.Key] = kv.Value;

        foreach (var step in plan.Steps){
            // Substitute placeholders safely (JSON-aware)
            var substituted = Substitute(step.Params, vars);
            EnsureNoPlaceholdersRemain(substituted);

            if (IsCreate(step.Method) && !dryRun && (step.Expect?.RequireConfirm ?? true))
                EnforceConfirm(substituted);

            var rpcBody = BuildJsonRpc(step.Method, substituted);

            if (dryRun){
                details.Add(new { method = step.Method, dryRun = true, body = rpcBody });
                ok++;
                continue;
            }

            var resp = await _http.PostAsync(_rpcUrl, new StringContent(rpcBody, Encoding.UTF8, "application/json"));
            var txt  = await resp.Content.ReadAsStringAsync();
            bool stepOk = resp.IsSuccessStatusCode && CheckExpectations(step.Expect, txt);

            if(stepOk) ok++; else ng++;
            details.Add(new { method = step.Method, request = rpcBody, response = txt, status = stepOk ? "ok" : "error" });
            if(!stepOk) break;
        }
        return new { ok = ng==0, steps = new { total = ok+ng, succeeded = ok, failed = ng }, details };
    }

    private static string BuildJsonRpc(string method, JsonNode @params){
        var obj = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            ["method"] = method,
            ["params"] = @params
        };
        return JsonSerializer.Serialize(obj);
    }

    private static JsonNode Substitute(JsonElement rawParams, IDictionary<string,object> vars){
        JsonNode? node = JsonNode.Parse(rawParams.GetRawText());
        return SubstituteNode(node, vars) ?? new JsonObject();
    }

    private static JsonNode? SubstituteNode(JsonNode? node, IDictionary<string,object> vars){
        switch (node){
            case null: return null;
            case JsonValue jv:
                if (jv.TryGetValue<string>(out var s) && IsPlaceholder(s)){
                    var key = UnwrapPlaceholder(s);
                    if (!vars.TryGetValue(key, out var value) || value is null)
                        return jv; // leave as-is; will be caught by EnsureNoPlaceholdersRemain
                    return JsonSerializer.SerializeToNode(value) ?? JsonValue.Create(value?.ToString());
                }
                return jv;
            case JsonObject obj:
                var keys = obj.Select(kv => kv.Key).ToArray();
                foreach (var k in keys) obj[k] = SubstituteNode(obj[k], vars);
                return obj;
            case JsonArray arr:
                for (int i=0;i<arr.Count;i++) arr[i] = SubstituteNode(arr[i], vars);
                return arr;
            default: return node;
        }
    }

    private static void EnsureNoPlaceholdersRemain(JsonNode node){
        if (ContainsPlaceholder(node))
            throw new InvalidOperationException("Required variables not provided. Use DryRun to preview placeholders and pass Args.");
    }
    private static bool ContainsPlaceholder(JsonNode? node){
        switch (node){
            case null: return false;
            case JsonValue jv:
                return jv.TryGetValue<string>(out var s) && IsPlaceholder(s);
            case JsonObject obj:
                foreach (var kv in obj)
                    if (ContainsPlaceholder(kv.Value)) return true;
                return false;
            case JsonArray arr:
                foreach (var el in arr)
                    if (ContainsPlaceholder(el)) return true;
                return false;
            default: return false;
        }
    }
    private static bool IsPlaceholder(string s) => s.StartsWith("{{") && s.EndsWith("}}");
    private static string UnwrapPlaceholder(string s) => s.Substring(2, s.Length-4).Trim();

    private static void EnforceConfirm(JsonNode node){
        if (!HasConfirmTrue(node))
            throw new InvalidOperationException("Create operation requires confirm:true in params.");
    }
    private static bool HasConfirmTrue(JsonNode? node){
        switch (node){
            case null: return false;
            case JsonObject obj:
                foreach (var kv in obj){
                    if (string.Equals(kv.Key, "confirm", StringComparison.OrdinalIgnoreCase) && kv.Value is JsonValue jv && jv.TryGetValue<bool>(out var b) && b)
                        return true;
                    if (HasConfirmTrue(kv.Value)) return true;
                }
                return false;
            case JsonArray arr:
                foreach (var el in arr)
                    if (HasConfirmTrue(el)) return true;
                return false;
            default: return false;
        }
    }

    private static bool CheckExpectations(Expectation? exp, string responseJson){
        if (exp == null) return true;
        try{
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            var res  = root.TryGetProperty("result", out var r) ? r : root;
            int Get(string n){ return res.TryGetProperty(n, out var v) && v.ValueKind==JsonValueKind.Number ? v.GetInt32() : -1; }
            int updated = Get("updated"), created = Get("created"), candidates = Get("candidates");

            if (exp.MinUpdated.HasValue && updated >= 0 && updated < exp.MinUpdated) return false;
            if (exp.MaxUpdated.HasValue && updated >= 0 && updated > exp.MaxUpdated) return false;
            if (exp.MinCreated.HasValue && created >= 0 && created < exp.MinCreated) return false;
            if (exp.MaxCreated.HasValue && created >= 0 && created > exp.MaxCreated) return false;
            if (exp.MinCandidates.HasValue && candidates >= 0 && candidates < exp.MinCandidates) return false;
            if (exp.MaxCandidates.HasValue && candidates >= 0 && candidates > exp.MaxCandidates) return false;
            return true;
        } catch { return false; }
    }
    private static bool IsCreate(string method) => method.StartsWith("create_", StringComparison.OrdinalIgnoreCase);
}
