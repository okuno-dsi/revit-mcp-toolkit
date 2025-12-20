# AutoCad MCP — Agent Guide

This document explains how to use AutoCad MCP (AutoCAD Micro‑Control Point) via a simple JSON‑RPC over HTTP interface. It focuses on agent‑friendly usage, including the newly added `merge_dwgs_perfile_rename` command.

## Overview
- Server: ASP.NET Core app exposing HTTP endpoints.
- Default URL: `http://127.0.0.1:5251`
- Endpoints:
  - `GET /health` → `{ ok: true, ts }`
  - `GET /version` → `{ ok: true, version }`
  - `POST /rpc` → JSON‑RPC 2.0 request/response
  - `GET /result/{jobId}` → Optional result store (when used by handlers)

## JSON‑RPC Envelope
POST `/_rpc` is not used — use `POST /rpc`.

Request body:
```
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "<command_name>",
  "params": { ... }
}
```

Response (success):
```
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": { ... }  // command‑specific payload
}
```

Response (error):
```
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": { "code": <int>, "message": "...", "data": { ... } }
}
```

## New Command: `merge_dwgs_perfile_rename`
Merges multiple DWG files by inserting → exploding each file in order, then renames only the specified layer names per input file using a format string before post‑processing and saving.

Method: `merge_dwgs_perfile_rename`

Params (example):
```
{
  "inputs": [
    { "path": "C:/work/A.dwg", "stem": "A" },
    { "path": "C:/work/B.dwg", "stem": "B" }
  ],
  "output": "C:/out/merged.dwg",

  "rename": {
    "include": ["WALL", "WINDOW", "DOOR"],   // only these layers get renamed per file
    "exclude": ["GRID", "CENTER", "TEXT"],   // never renamed (kept common)
    "format":  "{old}_{stem}"                 // e.g., WALL + A → WALL_A
  },

  "accore": {
    "path":     "C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe",
    "seed":     "C:/std/seed.dwg",
    "locale":   "ja-JP",
    "timeoutMs": 600000
  },

  "postProcess": {
    "layTransDws": null,  // or path to .dws
    "purge": true,
    "audit": true
  },

  "stagingPolicy": {
    "root":           "C:/temp/CadJobs/Staging",
    "keepTempOnError": false,
    "atomicWrite":     true
  }
}
```

Notes:
- `inputs[]`: each DWG is `INSERT`ed at origin then `EXPLODE`d before the next. Set `stem` per file; defaults to file name when omitted.
- Renaming: for every name in `rename.include`, if not in `rename.exclude`, the layer is renamed using `format` with tokens `{old}` and `{stem}` (both tokens are required).
- Post‑process: optional `LAYTRANS` (when `layTransDws` is provided), `PURGE`, and `AUDIT` are applied.
- Save: the output is written as 2018 DWG. With `atomicWrite=true`, a staged file is atomically moved to the final path.
- Accoreconsole: AutoCAD Core Console must be installed. Provide `accore.path` and a valid `seed` drawing.
- Paths: The server enforces path guards; only allowed roots/patterns are accepted (configure via app settings).

Response (success):
```
{
  "ok": true,
  "output": "C:/out/merged.dwg",
  "stdoutTail": "...",   // last lines from accore console
  "stderrTail": "...",
  "exitCode": 0,
  "staging": "C:/temp/CadJobs/Staging/<jobId>"
}
```

Response (failure):
```
{
  "ok": false,
  "error": "...",
  "stdoutTail": "...",
  "stderrTail": "...",
  "exitCode": 1,
  "staging": "C:/temp/CadJobs/Staging/<jobId>"  // may be deleted depending on policy
}
```

Possible errors (error.message):
- `E_NO_INPUTS` — `inputs[]` missing/empty.
- `E_NO_OUTPUT` — `output` missing/empty.
- `E_BAD_FORMAT` — `rename.format` must contain both `{old}` and `{stem}`.
- `E_NO_ACCORE` — `accore.path` not provided and not configured.
- `E_NO_SEED` — `seed` not provided and not configured.
- Path guard violations for input/output locations.

## Other Methods (router overview)
- `merge_dwgs` — Base merging without per‑file renaming (see code).
- `probe_accoreconsole` — Check accoreconsole availability.
- `purge_audit` — Purge/Audit for a target DWG.
- `consolidate_layers` — Layer cleanup/normalization.
- `health` — Health ping.
- `version` — Server version.

## Quick Start
1) Start server
   - `AutoCadMcpServer.exe` (default binds to `http://127.0.0.1:5251`)
   - Or: `AutoCadMcpServer.exe --urls http://localhost:5251`
2) Health check
- `GET http://127.0.0.1:5251/health`
3) Run a job (PowerShell example)
```
$body = @{ jsonrpc='2.0'; id=1; method='merge_dwgs_perfile_rename'; params= @{ 
  inputs=@(@{path='C:/work/A.dwg';stem='A'},@{path='C:/work/B.dwg';stem='B'});
  output='C:/out/merged.dwg';
  rename=@{ include=@('WALL','WINDOW','DOOR'); exclude=@('GRID','CENTER','TEXT'); format='{old}_{stem}' };
  accore=@{ path='C:/Program Files/Autodesk/AutoCAD 2025/accoreconsole.exe'; seed='C:/std/seed.dwg'; locale='ja-JP'; timeoutMs=600000 };
  postProcess=@{ layTransDws=$null; purge=$true; audit=$true };
  stagingPolicy=@{ root='C:/temp/CadJobs/Staging'; keepTempOnError=$false; atomicWrite=$true } } } | ConvertTo-Json -Depth 10
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5251/rpc' -Body $body -ContentType 'application/json; charset=utf-8'
```

## Tips & Best Practices
- Use absolute paths and ensure the service account has access.
- Prefer `atomicWrite=true` to avoid partially written outputs on failures.
- Increase `timeoutMs` for large inputs or heavy LAYTRANS operations.
- For debugging, set `keepTempOnError=true` to inspect staged scripts/logs.
- Layer name casing is compared case‑insensitively; avoid near‑duplicates.

## Troubleshooting
- `E_NO_ACCORE` or accore failures → verify accoreconsole path and that version matches DWG compatibility.
- `E_NO_SEED` → provide a valid seed DWG; it defines file format and environment.
- `PathGuard` rejection → adjust allowed roots in configuration or change input/output paths accordingly.
- Empty/invalid output → check `stderrTail`/`stdoutTail` in the response and review the staged `run.scr`.
