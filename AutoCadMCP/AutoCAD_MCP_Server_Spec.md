# CAD MCP Server — AutoCAD Core Console Post-Processor
**Purpose:** Merge many Revit-exported DWGs into **one** clean DWG and apply company CAD standards (layer consolidation, LayTrans, purge/audit), via **AutoCAD Core Console**. Designed as a thin **MCP** (Model Context Protocol) style JSON-RPC HTTP service so agents (Codex/Gemini/…​) can call it.

---

## 0. Quick Start (TL;DR for Agents)

```bash
# 1) Create solution
dotnet new web -n AutoCadMcpServer
cd AutoCadMcpServer

# 2) Add folders
mkdir -p Router/Methods Core Scripts wwwroot Logs

# 3) Drop the provided Program.cs, Router, Core/* (see below spec)
# 4) Configure appsettings.json (paths, limits)
# 5) Build/Run
dotnet build
dotnet run --urls=http://127.0.0.1:5251

# 6) Probe
curl -s http://127.0.0.1:5251/health

# 7) Merge DWGs (async)
curl -s http://127.0.0.1:5251/rpc -H "Content-Type: application/json" -d @merge.json
```

`merge.json`
```json
{
  "jsonrpc":"2.0","id":1,"method":"merge_dwgs",
  "params":{
    "inputs":[
      "C:/Exports/DWG_ByComment/Comments_A.dwg",
      "C:/Exports/DWG_ByComment/Comments_B.dwg"
    ],
    "output":"C:/Exports/Merged/plan_merged.dwg",
    "layerStrategy":{
      "mode":"map",
      "map":{"WS_A-WALL":"A-WALL","WS_B-WALL":"A-WALL"},
      "deleteEmptyLayers":true
    },
    "units":"mm",
    "origin":"0,0,0",
    "postProcess":{"purge":true,"audit":true,"layTransDws":"C:/Standards/laytrans.dws"},
    "accore":{
      "path":"C:/Program Files/Autodesk/AutoCAD 2024/accoreconsole.exe",
      "locale":"ja-JP",
      "seed":"C:/Company/seed_mm.dwg",
      "timeoutMs":600000,
      "maxParallel":1
    },
    "stagingPolicy":{
      "root":"C:/CadJobs/Staging",
      "atomicWrite":true,
      "keepTempOnError":true
    },
    "opTimeoutMs":900000
  }
}
```

---

## 1. Architecture

- **Client** (Revit MCP / Agent) → **HTTP JSON-RPC** → **AutoCadMcpServer** → **AutoCAD Core Console** (`accoreconsole.exe`)  
- OS: Windows only (AutoCAD required; LT不可).  
- Server: .NET 6/8 Kestrel self-host.  
- Execution model: **async enqueue** → process **off-thread** → **/post_result** (internal) or **/get_result**.

### Project Layout

```
AutoCadMcpServer/
├─ Program.cs
├─ appsettings.json
├─ Router/
│  ├─ RpcRouter.cs
│  └─ Methods/
│     ├─ MergeDwgsHandler.cs
│     ├─ ConsolidateLayersHandler.cs
│     ├─ PurgeAuditHandler.cs
│     └─ ProbeAccoreHandler.cs
├─ Core/
│  ├─ JobQueue.cs
│  ├─ AccoreRunner.cs
│  ├─ ScriptBuilder.cs
│  ├─ AtomicFile.cs
│  ├─ PathGuard.cs
│  └─ ResultStore.cs
├─ Scripts/
│  └─ merge_template.scr
├─ wwwroot/
│  └─ openrpc.json (optional)
└─ Logs/
```

---

## 2. API (JSON-RPC over HTTP)

### 2.1 Methods

| method                 | purpose                                  |
|------------------------|-------------------------------------------|
| `merge_dwgs`           | Merge many DWGs into a single DWG         |
| `consolidate_layers`   | Layer rename/map/merge (LayTrans/laymrgs) |
| `purge_audit`          | PURGE/AUDIT/Regapps cleanup               |
| `bind_xrefs`           | Bind external references                   |
| `probe_accoreconsole`  | Verify accore path/launch/locale           |
| `health` / `version`   | Health check / version                     |

> In MVP, `merge_dwgs` can internally do consolidate+purge/audit.

### 2.2 Common parameters & result

**Request envelope**
```json
{"jsonrpc":"2.0","id":<number|string>,"method":"<name>","params":{ ... }}
```

**Common params**
- `opTimeoutMs` (int, optional): upper wait hint for upstream; server is async and does not block.
- `stagingPolicy.root` (string): staging path (server-side allowlist).
- `stagingPolicy.atomicWrite` (bool): use atomic replace+retry at final write.
- `stagingPolicy.keepTempOnError` (bool): keep staging on failures for forensics.

**Async response**
```json
{"jsonrpc":"2.0","id":1,"result":{"ok":true,"done":false,"jobId":"<uuid>","msg":"Accepted."}}
```

**Result push/pull**
```json
{"ok":true,"done":true,"jobId":"<uuid>","outputs":[{"path":".../merged.dwg","size":1234567}],
 "logs":{"stdoutTail":"...","stderrTail":"..."}, "stats":{"elapsedMs":14500}}
```

### 2.3 `merge_dwgs` parameters (detailed)
- `inputs`: array of absolute DWG paths (server allowlisted).  
- `output`: absolute DWG path to write.  
- `layerStrategy`:
  - `mode`: `"map" | "prefix" | "unify"`
  - `map`: from→to layer names (when `mode:"map"`).
  - `prefix`: string (when `mode:"prefix"`).
  - `deleteEmptyLayers`: bool.
- `units`: "mm" | "inch" … (seed INSUNITS alignment).  
- `origin`: "x,y,z" insertion origin (string).  
- `postProcess`:
  - `purge`: bool, `audit`: bool
  - `layTransDws`: path to DWS (optional)
  - `plotStyle`: CTB/STB path (optional)
- `accore`:
  - `path`: accoreconsole.exe
  - `locale`: "ja-JP"
  - `seed`: seed dwg (company standard)
  - `timeoutMs`: child process hard timeout
  - `maxParallel`: >1 not recommended (Core Console is not great with high parallelism)

---

## 3. Security / Governance

- **PathGuard**: normalize absolute paths, deny `..`, UNC, unapproved drives, enforce extensions `.dwg`.  
- **Limits**: max input count/size (e.g., 100 files / 200MB each).  
- **Atomic write**: temp → replace or delete+move with retries/backoff.  
- **User**: run under a dedicated least-privileged Windows account.  
- **Logging**: redact paths if needed; keep stdout/stderr tails in Logs by jobId.  
- **Parallelism**: global semaphore (default 1–2).  
- **Timeouts**: accore timeout; server opTimeoutMs only guides clients.

---

## 4. Processing Flow

1. **Validate** inputs (PathGuard).  
2. **Stage**: copy inputs to `Staging/<jobId>/in/`, seed → `Staging/<jobId>/out/seed.dwg`.  
3. **Build Script** (.scr): resolve tokens (`$IN_0...`, map blocks, LayTrans).  
4. **Run accore** (AccoreRunner): `/i <seed> /s <script> /l ja-JP`, capture stdout/stderr.  
5. **Post-process**: verify output, optional extra `-AUDIT`.  
6. **Atomic move** to final `output`.  
7. **Emit result** with paths/stats/log tails.  
8. **Cleanup** staging (or keep on error if configured).

---

## 5. Script Template (`Scripts/merge_template.scr`)

```scr
FILEDIA 0
._-LAYER _THAW _ALL _ON _ALL _UNLOCK _ALL _

; Units/INSBASE are defined in seed.dwg
; === INSERT ===
$FOR_EACH_INPUT
._-INSERT "$IN_PATH" 0,0,0 1 1 0
._EXPLODE L
$END_FOR_EACH

; === LAYER MAP / MERGE ===
$FOR_EACH_LAYMAP
._-LAYMRG _N "$FROM" "$TO" _
$END_FOR_EACH

; === LAYTRANS ===
$IF_LAYTRANS
._-LAYTRANS
"$DWS_PATH"
*.* 
$END_IF

; === CLEANUP ===
._-PURGE _A _* _N
._-PURGE _R _* _N
._-AUDIT _Y
._QSAVE
FILEDIA 1
```

`ScriptBuilder` replaces tokens:  
- `$FOR_EACH_INPUT ... $END_FOR_EACH` (with `$IN_PATH`)  
- `$FOR_EACH_LAYMAP ...` (`$FROM`, `$TO`)  
- `$IF_LAYTRANS ... $END_IF` only when `layTransDws` set.

---

## 6. Code Skeletons (for Agents)

### 6.1 Program.cs (Kestrel/JSON-RPC entry)
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { ok = true, ts = DateTimeOffset.Now }));
app.MapPost("/rpc", async (HttpContext ctx) =>
{
    using var sr = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var body = await sr.ReadToEndAsync();
    var req = System.Text.Json.JsonSerializer.Deserialize<JsonRpcReq>(body);

    try
    {
        var result = await RpcRouter.Dispatch(req);
        return Results.Json(new { jsonrpc = "2.0", id = req.id, result });
    }
    catch (RpcError ex)
    {
        return Results.Json(new { jsonrpc = "2.0", id = req.id, error = new { code = ex.Code, message = ex.Message, data = ex.Data } });
    }
});

app.Run("http://127.0.0.1:5251");

record JsonRpcReq(string jsonrpc, object id, string method, System.Text.Json.Nodes.JsonObject @params);
class RpcError : Exception { public int Code; public object Data; public RpcError(int c, string m, object d=null):base(m){Code=c;Data=d;} }
```

### 6.2 Router/Methods/MergeDwgsHandler.cs (enqueue async job)
```csharp
public static class MergeDwgsHandler
{
    public static async Task<object> Handle(JObject p)
    {
        // 1) validate & stage
        var inputs = p["inputs"]?.Values<string>()?.ToList() ?? new List<string>();
        if (inputs.Count == 0) throw new RpcError(400, "No inputs.");
        foreach (var f in inputs) PathGuard.EnsureAllowedDwG(f);
        PathGuard.EnsureAllowedOutput((string)p["output"]);

        var job = JobFactory.FromMergeParams(p);  // create Job + staging
        JobQueue.Enqueue(job);
        return new { ok = true, done = false, jobId = job.Id, msg = "Accepted." };
    }
}
```

### 6.3 Core/AccoreRunner.cs (child process)
```csharp
public static class AccoreRunner
{
    public static AccoreResult Run(string accorePath, string seed, string script, string locale, int timeoutMs)
    {
        var psi = new ProcessStartInfo(accorePath, $"/i \"{seed}\" /s \"{script}\" /l {locale}")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(seed)
        };
        var p = Process.Start(psi);
        var stdout = new StringBuilder(); var stderr = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();

        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch {}
            return new AccoreResult(false, "timeout", Tail(stdout.ToString()), Tail(stderr.ToString()));
        }
        return new AccoreResult(p.ExitCode == 0, null, Tail(stdout.ToString()), Tail(stderr.ToString()));
    }

    private static string Tail(string s, int n=4000) => s == null ? "" : (s.Length<=n?s:s.Substring(s.Length-n));
}
public record AccoreResult(bool Ok, string Error, string StdoutTail, string StderrTail);
```

### 6.4 Core/AtomicFile.cs (atomic write with retry)
```csharp
public static class AtomicFile
{
    public static void WriteReplace(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tmp, bytes);

        int delay = 100;
        for (int i=0;i<6;i++)
        {
            try
            {
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, null, true); return; }
                    catch { TryDelete(path); File.Move(tmp, path); return; }
                }
                else { File.Move(tmp, path); return; }
            }
            catch { Thread.Sleep(delay); delay = Math.min(delay*2, 2000); }
        }
        TryDelete(tmp);
        throw new IOException("Atomic replace failed: " + path);
    }
    private static void TryDelete(string p){ try{ if (File.Exists(p)) File.Delete(p);}catch{} }
}
```

### 6.5 Core/ScriptBuilder.cs (token expansion)
```csharp
public static class ScriptBuilder
{
    public static string Build(string template, IEnumerable<string> inputs, LayerStrategy ls, string dwsPath=null)
    {
        var sb = new StringBuilder(template);
        // inputs
        var ins = new StringBuilder();
        foreach (var path in inputs)
        {
            ins.AppendLine("._-INSERT \"" + path + "\" 0,0,0 1 1 0");
            ins.AppendLine("._EXPLODE L");
        }
        sb.Replace("$FOR_EACH_INPUT", ins.ToString()).Replace("$END_FOR_EACH", "");

        // layer map
        if (ls != null && ls.Mode == "map" && ls.Map != null && ls.Map.Count > 0)
        {
            var lm = new StringBuilder();
            foreach (var kv in ls.Map)
                lm.AppendLine("._-LAYMRG _N \"" + kv.Key + "\" \"" + kv.Value + "\" _");
            sb.Replace("$FOR_EACH_LAYMAP", lm.ToString()).Replace("$END_FOR_EACH", "");
        }
        else sb.Replace("$FOR_EACH_LAYMAP", "").Replace("$END_FOR_EACH", "");

        // laytrans
        if (!string.IsNullOrEmpty(dwsPath))
            sb.Replace("$IF_LAYTRANS", "").Replace("$DWS_PATH", dwsPath).Replace("$END_IF", "");
        else
            sb.Replace("$IF_LAYTRANS", "").Replace("$DWS_PATH", "").Replace("$END_IF", "");

        return sb.ToString();
    }
}
public class LayerStrategy { public string Mode; public Dictionary<string,string> Map; public string Prefix; public bool DeleteEmptyLayers; }
```

---

## 7. Error Model

| code              | cause                          | http |
|-------------------|--------------------------------|------|
| `E_PATH_DENY`     | path not allowed               | 400  |
| `E_NO_INPUT`      | empty inputs                   | 400  |
| `E_SIZE_LIMIT`    | size/count exceeded            | 400  |
| `E_ACCORE_START`  | cannot start core console      | 500  |
| `E_ACCORE_TIMEOUT`| child process timed out        | 504  |
| `E_SCRIPT_FAIL`   | script error / non-zero exit   | 500  |
| `E_ATOMIC_WRITE`  | final move/replace failed      | 500  |

All error responses include `{ ok:false, code, msg, detail:{ jobId, stdoutTail, stderrTail } }`.

---

## 8. Build / Run / Test

```bash
# Build
dotnet build -c Release

# Run
dotnet run --urls=http://127.0.0.1:5251

# Health
curl -s http://127.0.0.1:5251/health

# Probe accoreconsole
curl -s http://127.0.0.1:5251/rpc -H "Content-Type: application/json" -d \
'{"jsonrpc":"2.0","id":1,"method":"probe_accoreconsole","params":{"path":"C:/Program Files/Autodesk/AutoCAD 2024/accoreconsole.exe"}}'
```

**Checklist**
- ✅ accore path exists & launches
- ✅ seed dwg exists / INSUNITS correct
- ✅ laytrans DWS exists (if used)
- ✅ output dir writable
- ✅ job staging path exists and is whitelisted

---

## 9. Roadmap (Optional Enhancements)

- AutoCAD .NET plugin (ObjectARX .NET) with custom `MERGE_RUN` for DB-level robust merge (fewer explode side-effects)
- ODA Drawings SDK backend (AutoCAD不要) behind same MCP API
- Xref policy: attach vs bind vs overlay
- CAD Standards enforcement (DWS diff report -> return JSON)
- ZIP packaging & checksum
- Metrics: per-job timings, layer counts before/after, purge counts

---

## 10. Appendix: Security Notes

- Always sanitize/allowlist paths. Never allow arbitrary system paths from clients.
- Run accore under a service account with limited rights; keep staging on fast SSD but not user profile.
- Enforce concurrency: `maxParallel` <= 2 unless verified stable.
- Keep logs but truncate to safe tails to avoid PII leakage.
