AutoCad MCP Server ? English Manual

Overview

- Purpose: Headless AutoCAD merge automation with a tiny JSON?RPC server.
- Key flow: Accepts a request  stages a job  generates a `.scr`  runs `accoreconsole.exe` (optional, headless)  writes merged DWG.
- Typical use case: Bind XREFs, then merge layers from each source file into a deterministic per?file destination naming.

Whatfs Included

- Minimal ASP.NET server with endpoints for health, JSON?RPC, and result lookup.
- Script builder that emits AutoLISP/ActiveX helpers and a merge template.
- PowerShell helpers to run latest staged job interactively (with pause and logs).

Key Paths

- Server: `AutoCadMcpServer`
- Template: `AutoCadMcpServer/Scripts/merge_template.scr`
- Tools: `AutoCadMcpServer/tools`
- Sample inputs: `Projects/AutoCadOut`
- Default staging roots: `C:/CadJobs/Staging`, `C:/Temp/CadJobs/Staging`

Run the Server

1) Build and run
- `dotnet build AutoCadMcpServer -c Release`
- `dotnet run -c Release --project AutoCadMcpServer`

2) Health check
- `GET http://127.0.0.1:5251/health`  `{ "ok": true }`

3) Port note
- If `5251` is busy, run with: `dotnet run -c Release --project AutoCadMcpServer --urls http://127.0.0.1:5252`

JSON?RPC Endpoint

- URL: `POST /rpc`
- Body: either
  - a file path string to a JSON file (comments allowed: `//` and `/* */`), or
  - raw JSON text (comments allowed).

Example (via file path body)

- Body: `%USERPROFILE%\Documents\Revit_MCP\Projects\AutoCadOut\command.txt`

Example (raw JSON body)

```
{
  "jsonrpc": "2.0", "id": "dxfmap-001", "method": "merge_dwgs_dxf_textmap",
  "params": {
    "inputs": [
      "C:/.../Projects/AutoCadOut/walls_A.dwg",
      "C:/.../Projects/AutoCadOut/walls_B.dwg",
      "C:/.../Projects/AutoCadOut/walls_C.dwg",
      "C:/.../Projects/AutoCadOut/walls_D.dwg"
    ],
    "output": "C:/.../Projects/AutoCadOut/Merged/merged.dwg",
    "rename": { "include": ["A-WALL-____-MCUT"], "format": "{old}_{stem}" },
    "accore": {
      "path": "C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe",
      "seed": "C:/Seed/empty_seed.dwg",
      "locale": "en-US",
      "timeoutMs": 900000
    },
    "stagingPolicy": { "root": "C:/Temp/CadJobs/Staging", "keepTempOnError": true, "atomicWrite": true }
  }
}
```

Request Parameters

- `inputs`: array of strings or objects `{ path, stem }`.
  - If object is used, `stem` overrides the file?base used when constructing layer names.
- `output`: absolute path to the merged DWG. If omitted, defaults to `<jobDir>/out/merged.dwg`.
- `rename`:
  - `include`: list of source layer names to merge (e.g., `A-WALL-____-MCUT`).
  - `format`: destination layer format; placeholders: `{old}`, `{stem}`.
    - Example: `{old}_{stem}`  `A-WALL-____-MCUT_walls_A`.
- `accore` (optional to run headless):
  - `path`: full path to `accoreconsole.exe`.
  - `seed`: seed DWG to open; if missing/unavailable, the server uses `Projects/AutoCadOut/seed.dwg` or the first input DWG.
  - `locale`: e.g., `en-US`.
  - `timeoutMs`: run timeout; default 180000.
- `stagingPolicy`:
  - `root`: staging root override; default is `C:/CadJobs/Staging` then `C:/Temp/CadJobs/Staging`.
  - `keepTempOnError` / `atomicWrite`: reserved for future behavior.

What the Server Does

1) Staging
- Creates `<root>/<jobId>/{in,out,logs}` plus `run.scr`.
- Copies all input DWGs into `<jobId>/in`.

2) Script generation
- Attaches each input as XREF at origin.
- Binds all XREFs (`_.-XREF BIND *`).
- Defines ActiveX helpers:
  - `_ensure-layer`, `_foreach-entity-in-all-blocks`, `merge-one-layer-AX`.
- For each `include` layer in each file, merges `<stem>$0$<old>` into `format({old},{stem})`.
- PURGE/AUDIT, then SAVEAS 2018 to `output`.

> Note: the newer `merge_dwgs_perfile_rename` command uses an INSERT+EXPLODE-based script with a LISP helper
> (`relayer`) instead of XREF+BIND. See the reference script at the end of this document.

3) Headless run (optional)
- If `accore` is present, the server runs `accoreconsole.exe` without opening another console.
- Logs go to `<jobId>/logs/accore_console.txt` and `Projects/AutoCadOut/console_out.txt`.
- Response includes `run` with `exitCode`, `timedOut`, and `outputExists`.

Result Lookup

- `GET /result/{jobId}`  reports `<jobId>/out/merged.dwg` existence and paths.

Manual Run Helper (optional)

- Use `AutoCadMcpServer/Tools/Run-AccoreLatestJob.ps1` to launch the newest staged job interactively.
  - Keeps a separate PowerShell window open, writes the same logs, and waits for Enter.

Configuration

- `AutoCadMcpServer/appsettings.json` provides default staging roots.
- The server binds to `http://127.0.0.1:5251` by default; override with `--urls` at run.

Troubleshooting

- Port in use: stop the other listener on 5251 or run with `--urls`.
- Accore path invalid: set `accore.path` to the installed version (e.g., AutoCAD 2026 path).
- Seed missing: provide a valid `seed` or place `Projects/AutoCadOut/seed.dwg`.
- Timeout: increase `accore.timeoutMs` for large models.
- Logs encoding: logs are UTF?8 with BOM; open in a Unicode?capable editor.

Lingering Core Console (accoreconsole.exe)
- If a previous headless run did not exit, you may need to kill `accoreconsole.exe` before starting a new job.
- Quick kill:
  - `pwsh -File Codex/Scripts/Reference/kill_accoreconsole.ps1`
  - Or: `Get-Process accoreconsole -ErrorAction SilentlyContinue | Stop-Process -Force`
- Consider adding a job timeout (`accore.timeoutMs`) and ensuring scripts use non?interactive commands (e.g., `._` prefixes, `EXPERT 5`).

Lingering AutoCAD (acad.exe)
- If GUI AutoCAD was launched for diagnostics and remained open or hung, kill it before running headless jobs.
- Quick kill:
  - `pwsh -File Codex/Scripts/Reference/kill_cad_processes.ps1 -IncludeAcad`
  - Or: `Get-Process acad -ErrorAction SilentlyContinue | Stop-Process -Force`

Collecting Logs (support bundle)
- To gather recent Core Console logs (.scr and console text) next to your export folder:
  - `pwsh -File Codex/Scripts/Reference/collect_accore_logs.ps1 -ExportDir ".../Export_YYYYMMDD_HHMMSS" -IncludeLayerList`
- Attach the created `AccoreLogs_*` folder when reporting issues.

Operational Notes

- Accore path consistency
  - Ensure `accore.path` points to the actual `accoreconsole.exe` installed (e.g., `C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe`).
  - If multiple AutoCAD versions exist, prefer an explicit path in requests to avoid ambiguity.

- Layer name exact match
  - `rename.include` matches layer names exactly. Wildcards are not expanded by the server.
  - Mitigation: use `Tools/AutoCad/DumpLayersViaDXF.ps1` to list layers first, then enumerate needed names under `include`.

- Stem collisions (duplicate destination layers)
  - Destination layers derive from `format({old},{stem})`. If two inputs share the same `stem`, they can map into identical destination layers.
  - Mitigation: pass inputs as objects and set a unique `stem` per file, e.g. `{ "path": ".../walls.dwg", "stem": "walls_A" }`. Alternatively, change `rename.format` to include more uniqueness.

- Large models and timeouts
  - Heavier drawings can exceed the default 180 seconds.
  - Mitigation: set `accore.timeoutMs` (e.g., `900000` = 15 minutes), or split inputs into smaller batches.

- Seed DWG
  - If `accore.seed` is missing, the server attempts `Projects/AutoCadOut/seed.dwg`, then falls back to the first input DWG.
  - Mitigation: keep a small, trusted seed DWG at a known path (e.g., `C:/Seed/empty_seed.dwg`) and reference it explicitly.

- Output path and permissions
  - The server creates the output directory if needed, but the directory must be writable by the service user.
  - Mitigation: target user?writable locations (e.g., under `Projects/AutoCadOut`) and avoid protected folders.

- Parallel servers and port conflicts
  - Default bind: `http://127.0.0.1:5251`. Running multiple instances on the same port causes conflicts.
  - Mitigation: run with a different URL via `--urls` or stop the previous process.

- Filename normalization (non-ASCII)
  - When driving Core Console with DWGs whose names contain Japanese or other non-ASCII characters, prefer copying them to an ASCII-only temp folder and renaming with a deterministic scheme before passing them to `accoreconsole.exe`.
  - Avoid naive replacement of all non-ASCII characters with `_` (for example, `-replace '[^A-Za-z0-9_-]','_'`), because distinct names such as `大梁.dwg` and `小梁.dwg` can both collapse to `__.dwg` and overwrite each other.
  - A safe pattern is to encode each non-ASCII character as its Unicode code point, e.g.:

    ```powershell
    function ToAsciiName([string]$name){
      $base = [IO.Path]::GetFileNameWithoutExtension($name)
      $ext  = [IO.Path]::GetExtension($name)
      $sb   = New-Object System.Text.StringBuilder
      foreach($ch in $base.ToCharArray()){
        $code = [int][char]$ch
        if( ($code -ge 48 -and $code -le 57) -or     # 0-9
            ($code -ge 65 -and $code -le 90) -or     # A-Z
            ($code -ge 97 -and $code -le 122) -or    # a-z
            $code -eq 45 -or $code -eq 95 ) {        # - _
          [void]$sb.Append($ch)
        } else {
          [void]$sb.AppendFormat('u{0:X4}', $code)   # e.g. 大 -> u5927
        }
      }
      $san = $sb.ToString()
      if([string]::IsNullOrWhiteSpace($san)){ $san = 'dwg' }
      return $san + $ext
    }
    ```

  - When you derive the per-file `stem` used in `rename.format`, align it with this ASCII-safe filename to keep layer mappings unique and readable.

Mapping Preview

- Response includes a `mapping` array summarizing the planned renames before/after run.
- Item format: `{ stem, old, src, dst }`
  - Example: `{"stem":"walls_A","old":"A-WALL-____-MCUT","src":"walls_A$0$A-WALL-____-MCUT","dst":"A-WALL-____-MCUT_walls_A"}`
- Use this for pre?run sanity checks and logging.

Layer Verification Tools

- `Tools/AutoCad/DumpLayersViaDXF.ps1` ? exports DXF and parses LAYER names (ASCII), useful for automation.
- `Tools/AutoCad/ListDWGLayers.ps1` ? writes a `.layers.txt` by running a short LISP in Core Console.
- Expect that post?merge the source layers like `<stem>$0$<old>` are removed and only the destination layers remain.

Security Notes

- The server executes an external process (`accoreconsole.exe`) when `accore` is specified; keep it bound to localhost and limit access.
- Request bodies can come from file paths; ensure only trusted paths are used in production.

Fallback (No Server)

- If you only need to merge two files like `B.dwg` and `G.dwg` onto layers `B` and `G` inside a seed drawing, you may skip the server and use the Core Console helper script:
  - `Scripts/Reference/merge_bg_from_seed.ps1`
  - Example:
    - `pwsh -File Codex/Scripts/Reference/merge_bg_from_seed.ps1 -ExportDir "Codex/Projects/AutoCadOut/Export_YYYYMMDD_HHMMSS" -Locale en-US`
  - This uses INSERT+EXPLODE and entity?level layer reassignment (entlast?based) to avoid XREF+BIND fragility in some environments.

Reference Core Console Script (per-file rename)

- The `merge_dwgs_perfile_rename` handler ultimately emits a `.scr` script for Core Console. The exact content depends on the request, but a representative example for two inputs (seed + one extra DWG) is:

```text
._CMDECHO 0
._FILEDIA 0
._CMDDIA 0
._ATTDIA 0
._EXPERT 5

(defun relayer (stem include exclude prefix suffix / tbl rec name newname up)
  (setq tbl (tblnext "LAYER" T))
  (while tbl
    (setq name (cdr (assoc 2 tbl)))
    (setq up (if name (strcase name) "" ))
    (if (and name
             (not (= up "0"))
             (not (= up "DEFPOINTS"))
             (or (= include "") (wcmatch name include))
             (or (= exclude "") (not (wcmatch name exclude))))
      (progn
        (setq newname (strcat prefix name suffix))
        (if (not (= name newname))
          (command "_.-RENAME" "Layer" name newname)
        )
      )
    )
    (setq tbl (tblnext "LAYER"))
  )
  (princ)
)

.__-INSERT "C:/temp/CadJobs/Job/in/seed_beam.dwg" 0,0,0 1 1 0
._EXPLODE L
(relayer "seed_beam" "*" "" "" "_seed_beam")

.__-INSERT "C:/temp/CadJobs/Job/in/wall.dwg" 0,0,0 1 1 0
._EXPLODE L
(relayer "wall" "*" "" "" "_wall")

._-PURGE A * N
._-PURGE R * N
._-PURGE A * N
._AUDIT Y
.__SAVEAS 2018 "C:/temp/CadJobs/Job/out/merged.dwg"
._QUIT Y
(princ)
```

- This script shows the key patterns:
  - Global/English-safe commands (`._CMDECHO`, `._FILEDIA`, etc.).
  - A `relayer` LISP that renames layers by wildcard while skipping `0` and `DEFPOINTS`.
  - `INSERT`+`EXPLODE` per input DWG at the origin, followed by a per-file suffix (e.g. `_seed_beam`, `_wall`) on matching layer names.
  - PURGE/AUDIT and a final `SAVEAS` to the requested output path.





