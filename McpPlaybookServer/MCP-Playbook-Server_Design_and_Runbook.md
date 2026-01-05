# MCP Playbook Server — Design Spec & Operations Runbook
**Version:** 1.2 (2025-10-20)  
**Context:** RevitMCP Project (external recorder/replay proxy)  
**Audience:** AI agent developers, BIM automation engineers, RevitMCP maintainers

---

## 0) Purpose
Repeat common Revit workflows without re-thinking steps every time, while **avoiding accidental replays** (e.g., placing 8 rebars when you wanted 5). The **MCP Playbook Server** is a small **external HTTP proxy** placed between your AI agent and the `RevitMcpServer`. It forwards JSON‑RPC calls, **records** them to structured logs (JSONL), builds **recipes** with variables, and **replays** them with **safety gates** (dry‑run, expectation bounds, confirm).

**Key advantages**
- No extra load or dependencies inside the Revit add‑in (.NET Framework 4.8 remains unchanged).
- Runs as a separate .NET 8 process.
- Enforces safe, intention‑driven automation (variables, bounds, dry‑run).

---

## 1) High‑Level Architecture
```
[AI Agent / MCP Client / CLI]
    │  JSON-RPC over HTTP
    ▼
[MCP Playbook Server]  (this project)
  • POST /teach/start    — begin capture
  • POST /teach/stop     — end capture
  • POST /rpc            — forward + record
  • POST /replay         — dry-run or execute (with variables & expectations)
    │  JSON-RPC over HTTP (forward)
    ▼
[RevitMcpServer (external process)]
    │
[Revit Add-in (.NET 4.8)]
```
**Design principle:** Capture **intent**, not fixed IDs. Normalize hard `ElementId`s into **selectors** (category/type/level/family/mark), and replay with **variables** and **guardrails**.

---

## 2) Artifacts & Storage Layout
Captured sessions are written under the current user’s profile:
```
%LOCALAPPDATA%\RevitMCP\Playbooks\
  YYYYMMDD_HHMMSS_<session-name>\
    capture.jsonl          # one normalized JSON per RPC
    recipe.json            # replay template with vars & expectations
    playbook.md            # human-readable summary (optional)
    summary.yaml           # session metadata (versions, ports, etc.)
```

### 2.1 JSONL entry (example)
```json
{"t":1760930000001,"method":"get_walls","params":{"level":"1FL"},"result":{"ok":true,"count":8}}
{"t":1760930000020,"method":"update_wall_parameter",
 "params":{"selector":{"category":"OST_Walls","typeName":"RC150","level":"1FL"},
           "param":"Comments","value":"Model A"},
 "result":{"ok":true,"updated":8}}
```

### 2.2 Recipe template (`recipe.json`)
```json
{
  "name": "rename-walls-on-level",
  "vars": {
    "target_wall_type": "RC150",
    "level_name": "1FL",
    "param_name": "Comments",
    "param_value": "Model B"
  },
  "steps": [
    {"method":"get_walls","params":{"level":"{{level_name}}"}},
    {"method":"update_wall_parameter",
     "params":{"selector":{"category":"OST_Walls","typeName":"{{target_wall_type}}","level":"{{level_name}}"},
               "param":"{{param_name}}","value":"{{param_value}}"},
     "expect":{"minUpdated":1,"maxUpdated":500}}
  ]
}
```

---

## 3) Safety Model (to prevent “same as last time” mistakes)
1. **Variables required** for volatile inputs (e.g., rebar `count`, `spacing`, `diameter`). If a required placeholder like `{{bar_count}}` remains unresolved at replay time → **reject**.
2. **Dry‑run first** to preview targets and planned effects (no writes).
3. **Expectation bounds** (per step) such as `minUpdated/maxUpdated`, `minCreated/maxCreated`, `minCandidates/maxCandidates`. If violated → **stop early**.
4. **Confirm flag** for create/mutate operations (`"confirm": true`), enforced by the proxy unless in dry‑run.
5. **Target caps** to avoid exploding operations (e.g., `maxCreated: 200`).
6. Prefer **intent‑based spec** (e.g., “spacing = 200mm across host length”) over “place exactly 8,” which is brittle across models.

### 3.1 Rebar examples
**A) Fixed count (5 bars this time, not 8)**
```json
{
  "name": "place-rebar-linear",
  "vars": {
    "host_selector": { "category": "OST_StructuralFraming", "typeName": "RC-Beam-450x600", "level": "2FL" },
    "bar_type": "D16",
    "bar_count": null,          // REQUIRED at runtime
    "cover_top": "25mm",
    "cover_side": "25mm"
  },
  "steps": [
    {
      "method": "preview_rebar_linear",
      "params": { "host":"{{host_selector}}","barType":"{{bar_type}}","count":"{{bar_count}}","coverTop":"{{cover_top}}","coverSide":"{{cover_side}}" },
      "expect": { "minCandidates": 1, "maxCandidates": 50 }
    },
    {
      "method": "create_rebar_linear",
      "params": { "host":"{{host_selector}}","barType":"{{bar_type}}","count":"{{bar_count}}","coverTop":"{{cover_top}}","coverSide":"{{cover_side}}","confirm":true },
      "expect": { "minCreated": 1, "maxCreated": 6 }   // 6 max — 8 would be rejected
    }
  ]
}
```

**B) Spacing‑based (model length changes → count adjusts naturally)**
```json
{
  "name": "place-rebar-by-spacing",
  "vars": {
    "spacing_mm": 200,
    "bar_type": "D16",
    "host_selector": { "category":"OST_StructuralFraming", "level":"2FL" }
  },
  "steps": [
    { "method":"preview_rebar_by_spacing",
      "params":{"host":"{{host_selector}}","barType":"{{bar_type}}","spacingMm":"{{spacing_mm}}"},
      "expect":{"minCandidates":1,"maxCandidates":1000}
    },
    { "method":"create_rebar_by_spacing",
      "params":{"host":"{{host_selector}}","barType":"{{bar_type}}","spacingMm":"{{spacing_mm}}","confirm":true},
      "expect":{"minCreated":1,"maxCreated":200}
    }
  ]
}
```

---

## 4) Minimal HTTP API
- `POST /teach/start?name={optional}` → `{ ok:true, dir }`  
- `POST /teach/stop` → `{ ok:true }`  
- `POST /rpc` → forward to real `/rpc` on Revit MCP and **record** normalized entry to `capture.jsonl`.  
- `POST /replay` body:
  ```json
  { "SessionId":"20251020_120000_rename", "RecipePath":null, "DryRun":true, "Args":{"target_wall_type":"RC150"} }
  ```
  → Returns `{ ok, steps:{ total, succeeded, failed }, details:[...] }`.

---

## 5) Sample Implementation (C# / .NET 8 Minimal API)
> Compact baseline; extend as your selectors and checks grow.

```csharp
// Program.cs — MCP Playbook Server (baseline + safety)
#nullable enable
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Options
string forwardBase = GetArg(args, "--forward") ?? "http://127.0.0.1:5210";
string portStr     = GetArg(args, "--port")    ?? "5201";

builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(int.Parse(portStr)));
var app  = builder.Build();
var http = new HttpClient(){ Timeout = TimeSpan.FromMinutes(5) };

// In-memory state
var teach = new TeachState();

app.MapPost("/teach/start", (HttpRequest req) => {
    var name = req.Query["name"].ToString();
    return teach.Start(name);
});

app.MapPost("/teach/stop", () => teach.Stop());

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

app.MapPost("/replay", async (ReplayRequest rr) =>
{
    var plan = await RecipeLoader.LoadAsync(rr);
    var executor = new Replayer(http, $"{forwardBase}/rpc");
    return await executor.RunAsync(plan, rr.DryRun, rr.Args ?? new());
});

app.Run();

// --------------- helpers & types ---------------
static string? GetArg(string[] args, string key){
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase)) return args[i+1];
    return null;
}

record ReplayRequest(string? SessionId, string? RecipePath, bool DryRun=false, Dictionary<string,object>? Args=null);

sealed class TeachState {
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

static class Recorder {
    public static object Normalize(string reqJson, string resJson) {
        var req = JsonSerializer.Deserialize<JsonElement>(reqJson);
        string method = req.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
        var @params = req.TryGetProperty("params", out var p) ? p : default;

        object? resultObj = null;
        try {
            var res = JsonSerializer.Deserialize<JsonElement>(resJson);
            resultObj = res.TryGetProperty("result", out var r) ? r : res;
        } catch { resultObj = new { raw = resJson }; }

        return new { t = DateTimeOffset.Now.ToUnixTimeMilliseconds(), method, @params, result = resultObj };
    }
}

sealed class ReplayPlan {
    public string Name { get; set; } = "untitled";
    public Dictionary<string,object> Vars { get; set; } = new();
    public List<ReplayStep> Steps { get; set; } = new();
}
sealed class ReplayStep {
    public string Method { get; set; } = "";
    public JsonElement Params { get; set; }
    public Expectation? Expect { get; set; }
}
sealed class Expectation {
    public int? MinUpdated { get; set; }
    public int? MaxUpdated { get; set; }
    public int? MinCreated { get; set; }
    public int? MaxCreated { get; set; }
    public int? MinCandidates { get; set; }
    public int? MaxCandidates { get; set; }
    public bool? RequireConfirm { get; set; }
}

static class RecipeLoader {
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
        return await JsonSerializer.DeserializeAsync<ReplayPlan>(fs) ?? new ReplayPlan();
    }
}

sealed class Replayer {
    private readonly HttpClient _http;
    private readonly string _rpcUrl;
    public Replayer(HttpClient http, string rpcUrl){ _http=http; _rpcUrl=rpcUrl; }

    public async Task<object> RunAsync(ReplayPlan plan, bool dryRun, Dictionary<string,object> args){
        int ok=0, ng=0;
        var details = new List<object>();

        var vars = new Dictionary<string,object>(plan.Vars, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in args) vars[kv.Key] = kv.Value;

        foreach (var step in plan.Steps){
            var ptext = Substitute(step.Params, vars);
            EnsureRequiredVars(ptext);
            if (IsCreate(step.Method) && !dryRun && (step.Expect?.RequireConfirm ?? true))
                EnforceConfirm(ptext);

            var body = BuildJsonRpc(step.Method, ptext);

            if (dryRun){
                details.Add(new { method = step.Method, dryRun = true, body });
                ok++;
                continue;
            }

            var resp = await _http.PostAsync(_rpcUrl, new StringContent(body, Encoding.UTF8, "application/json"));
            var txt  = await resp.Content.ReadAsStringAsync();
            bool stepOk = resp.IsSuccessStatusCode && CheckExpectations(step.Expect, txt);

            if(stepOk) ok++; else ng++;
            details.Add(new { method = step.Method, request = body, response = txt, status = stepOk ? "ok" : "error" });
            if(!stepOk) break;
        }
        return new { ok = ng==0, steps = new { total = ok+ng, succeeded = ok, failed = ng }, details };
    }

    private static string BuildJsonRpc(string method, string paramsJson){
        var id = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        return $"{{"jsonrpc":"2.0","id":{id},"method":"{Escape(method)}","params":{paramsJson}}}";
    }
    private static string Substitute(JsonElement rawParams, Dictionary<string,object> vars){
        var text = rawParams.GetRawText();
        foreach (var kv in vars)
            text = text.Replace("{{"+kv.Key+"}}", JsonSerializer.Serialize(kv.Value));
        return text;
    }
    private static void EnsureRequiredVars(string paramsJson){
        if (paramsJson.Contains("{{"))
            throw new InvalidOperationException("Required variables not provided. Use DryRun to preview placeholders and pass Args.");
    }
    private static void EnforceConfirm(string paramsJson){
        if (!paramsJson.Contains(""confirm":true"))
            throw new InvalidOperationException("Create operation requires confirm:true in params.");
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
    private static string Escape(string s) => s.Replace("\","\\").Replace(""","\"");
}
```

---

## 6) Operations Runbook

### 6.1 Installation & Build
1. **Create solution** `McpPlaybookServer` (.NET 8, Console/Empty).  
2. Add the **Program.cs** from section 5.  
3. Build/publish (optionally single file):
   ```bash
   dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true
   ```

### 6.2 Startup & Wiring
1. Launch `RevitMcpServer` (e.g., `http://127.0.0.1:5210`).  
2. Start **Playbook Server**:
   ```bash
   McpPlaybookServer.exe --forward http://127.0.0.1:5209 --port 5209
   ```
3. Point your agent (Gemini CLI / Codex / etc.) to **`http://127.0.0.1:5209/rpc`**.

### 6.3 Capturing a Session (Teach Mode)
```bash
curl -X POST "http://127.0.0.1:5209/teach/start?name=walls-rename-1FL"
# … perform your usual RPC operations via the agent …
curl -X POST "http://127.0.0.1:5209/teach/stop"
```
Outputs go to `%LOCALAPPDATA%\RevitMCP\Playbooks\<session>\`.

### 6.4 Authoring a Recipe
- Start from `capture.jsonl` and extract the **essential steps**.  
- Replace literals with **`{{variables}}`**.  
- Add **`expect`** bounds to each step.  
- Save as `recipe.json` in the same session folder (or anywhere).

### 6.5 Dry‑Run (Mandatory First)
```bash
curl -X POST "http://127.0.0.1:5209/replay" ^
  -H "Content-Type: application/json" ^
  -d "{"RecipePath":"C:/.../recipe.json","DryRun":true,
       "Args":{"target_wall_type":"RC150","level_name":"1FL"}}"
```
Verify `details[]` shows the **targets** and **planned counts** match expectations.

### 6.6 Execute (With Confirm)
```bash
curl -X POST "http://127.0.0.1:5209/replay" ^
  -H "Content-Type: application/json" ^
  -d "{"RecipePath":"C:/.../recipe.json","DryRun":false,
       "Args":{"target_wall_type":"RC150","level_name":"1FL","param_value":"Model C"}}"
```
- Ensure create/mutate steps include `"confirm": true` in `params` and suitable `expect`.

### 6.7 Operational Safeguards (Checklist)
- [ ] First run is **always** dry‑run.  
- [ ] **Variables provided** for all placeholders.  
- [ ] **Bounds** set (`min/max*`) and are tight enough to catch model drift.  
- [ ] **Confirm** present on create/mutate steps.  
- [ ] **Log** (JSONL) retained for audit & rollback planning.

### 6.8 Troubleshooting
- **`Required variables not provided`** → Some `{{var}}` was not passed in `Args`. Dry‑run to see placeholders.  
- **Bounds violation** → The model changed; adjust variables or recipe expectations.  
- **No elements match selector** → Selector too strict or wrong level/type; use a read command to inspect.  
- **HTTP 5xx from RevitMcpServer** → Check Revit is open, server port, antivirus/firewall, long‑running transactions.

### 6.9 Versioning & Governance
- Include Revit version, RevitMCP server version, and Playbook server version in `summary.yaml`.  
- Use semantic versioning for recipes (`name@major.minor.patch`).  
- Keep a **reviewed** folder for approved recipes; require code review for changes.

### 6.10 Security
- Bind to `localhost` only unless you explicitly need remote access.  
- Recipes/logs may reference project metadata—store under restricted folders as needed.  
- Avoid personal names in logs; prefer element selectors and type names.

---

## 7) Optional: Tiny Python Replayer
```python
# replay.py
import json, sys, requests
BASE = "http://127.0.0.1:5209"
recipe_path = sys.argv[1]
payload = {"RecipePath": recipe_path, "DryRun": False, "Args": {}}
r = requests.post(f"{BASE}/replay", json=payload, timeout=120)
print(r.json())
```

---

## 8) Quick Start (Copy/Paste)
```bash
# 1) Start RevitMcpServer (assumed at port 5210)

# 2) Start Playbook Server
McpPlaybookServer.exe --forward http://127.0.0.1:5210 --port 5209

# 3) Teach (capture)
curl -X POST "http://127.0.0.1:5209/teach/start?name=my-first-session"
# ... operate via agent ...
curl -X POST "http://127.0.0.1:5209/teach/stop"

# 4) Dry-run a recipe
curl -X POST "http://127.0.0.1:5209/replay" -H "Content-Type: application/json" ^
  -d "{"SessionId":"my-first-session","DryRun":true}"

# 5) Execute with args
curl -X POST "http://127.0.0.1:5209/replay" -H "Content-Type: application/json" ^
  -d "{"SessionId":"my-first-session","DryRun":false,
       "Args":{"target_wall_type":"RC150","param_value":"Model D"}}"
```

---

## 9) License
Apache-2.0 for the Playbook Server sample, unless your organization specifies otherwise.
