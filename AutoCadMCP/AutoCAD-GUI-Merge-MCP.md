
# AutoCAD MCP – **GUI Merge Commands** (Design + Sample Code)

**Version:** 1.0 (2025‑10‑28)  
**Scope:** Add a set of *GUI‑driven* AutoCAD commands (Insert → Explode → Per‑file layer rename → Purge/Audit → Save) that can be triggered by **AI agents** through the **MCP server**.  
**Audience:** AI agent developers and AutoCAD .NET engineers.  
**Target:** AutoCAD 2021+ / .NET Framework 4.8 (reference assemblies from your installed AutoCAD).

---

## 1) Problem & Goals

- Console automation via `accoreconsole.exe` is fast, but diagnosing script errors is hard in some cases.
- For **maximum reliability**, run the merge **inside the GUI AutoCAD** using the official .NET API, while still being callable by an **AI agent** through your MCP server.
- Preserve the host drawing’s layer system; only rename the **layers added by each imported file** (e.g., `WALL` → `WALL_A`, `WALL_B`, …).

### What you get
- A **GUI command** `MERGE_DWGS_PERFILE_RENAME` for manual operation.
- An **MCP bridge** that polls `/pending_request` and executes the same logic **automatically** on the UI thread.
- Clean JSON‑RPC **method**: `merge_dwgs_perfile_rename` with flexible parameters.

---

## 2) High‑Level Architecture

```
+-------------------+         JSON-RPC          +--------------------+
|  AI Agent / LLM   |  enqueue / get_result     |   MCP Server       |
|  (Python, etc.)   +-------------------------->+ (HTTP / JSON-RPC)  |
+-------------------+                           +---------+----------+
                                                         |
                                                         | polling: /pending_request
                                                         v
                                                +--------+---------+
                                                | AutoCAD GUI      |
                                                |  .NET Add-in     |
                                                |  (this repo)     |
                                                +--------+---------+
                                                         |
                                                         | Insert→Explode→Rename→Purge/Audit→Save
                                                         v
                                                +--------+---------+
                                                |  Output DWG      |
                                                +------------------+
```

- **Why polling?** It keeps AutoCAD in control of its UI thread and avoids remote code execution races.
- **Threading model:** Execute inside `DocumentLock` on the main UI thread; each phase wrapped in transactions.

---

## 3) JSON‑RPC Method Contract

### Method
`merge_dwgs_perfile_rename`

### Params (JSON)
```jsonc
{
  "inputs": [                 // required
    { "path": "C:/Cad/in/A.dwg", "stem": "A" },
    { "path": "C:/Cad/in/B.dwg", "stem": "B" }
  ],
  "output": "C:/Cad/out/merged_gui.dwg",       // required
  "rename": {                                  // optional – default shown below
    "include": ["*"],                          // ["*"] means all new layers; otherwise exact names
    "exclude": ["0", "DEFPOINTS"],             // always respected
    "format": "{old}_{stem}"                   // placeholders: {old}, {stem}
  },
  "mode": "gui"                                // required by the bridge; ignored elsewhere
}
```

### Result
```json
{ "ok": true, "output": "C:/Cad/out/merged_gui.dwg", "elapsedMs": 12345, "logPath": "C:/Cad/logs/merge_20251028T111200.txt" }
```
### Error
```json
{ "ok": false, "error": { "code": "E_IO", "message": "ReadDwgFile failed ...", "detail": "stacktrace or context" } }
```

---

## 4) MCP Server Endpoints (Minimal)

- `POST /enqueue` — queue a job (store JSON‑RPC request as-is)
- `GET /pending_request?agent=acad&accept=merge_dwgs_perfile_rename` — AutoCAD add‑in polls and claims a job
- `POST /post_result` — add‑in posts the JSON‑RPC result
- `GET /get_result?id=<jobId>` — agent polls for the result

> Your existing MCP server likely already implements these. Only the **method name** and **params** here are new.

### Example (Agent → Server)
```bash
curl -s -X POST http://localhost:5251/enqueue -H "content-type: application/json" -d @- <<'JSON'
{
  "jsonrpc": "2.0",
  "id": "job-1001",
  "method": "merge_dwgs_perfile_rename",
  "params": {
    "inputs": [
      {"path":"C:/Cad/in/A.dwg","stem":"A"},
      {"path":"C:/Cad/in/B.dwg","stem":"B"}
    ],
    "output": "C:/Cad/out/merged_gui.dwg",
    "rename": { "include": ["*"], "exclude": ["0","DEFPOINTS"], "format": "{old}_{stem}" },
    "mode": "gui"
  }
}
JSON
```
