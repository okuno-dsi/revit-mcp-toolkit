# RevitMCP Command: generate_dwg_merge_script

Purpose
- Generate an AutoCAD Core Console `.scr` script that consolidates multiple DWG files into a single DWG, with optional layer merge rules.
- This command only creates the script; execution is delegated to AutoCadMCP or run externally via accoreconsole.

Command Names
- `generate_dwg_merge_script`
- Alias: `gen_dwg_script`

Inputs (params)
- `inputDir` (string, required)
  - Folder containing input DWGs.
- `pattern` (string, optional, default: `walls_*.dwg`)
  - File pattern to select DWGs under `inputDir`.
- `bindType` (string, optional, default: `Bind`)
  - `Bind` or `Insert`. Internally mapped to system variable `BINDTYPE` (0=Bind, 1=Insert).
- `refPathType` (string, optional, default: `2`)
  - `0`, `1`, or `2` → passed to `REFPATHTYPE`.
- `mergeMode` (string, optional, default: `None`)
  - `None`: no layer consolidation after bind.
  - `ByFile`: for each input base name, merges bound layers `<base>$0$*` into a single target layer `<base>`.
  - `Map`: layer consolidation driven by a CSV map (see `layerMapCsv`).
- `layerMapCsv` (string, optional)
  - CSV file path for `Map` mode. Format: `pattern,targetLayer` (header optional). Patterns are wildcards that match bound layer names (e.g., `walls_A$0$*`).
- `outputDwg` (string, optional, default: `C:/temp/CadOut/merged.dwg`)
  - Output DWG path used by `SAVEAS`.
- `saveAsVersion` (string, optional, default: `2018`)
  - `2013`, `2018`, or `2024`.
- `purgeTimes` (int, optional, default: 2)
  - Number of `PURGE` cycles before save.
- `audit` (bool, optional, default: false)
  - Whether to run `AUDIT` before save.
- `trustedPaths` (string, optional, default: `C:/Temp;C:/Temp/CadOut`)
  - Passed to `TRUSTEDPATHS`. Use `;` to separate multiple paths.
- `outScript` (string, optional, default: `<inputDir>/run_merge.scr`)
  - Output `.scr` path. The file is saved using ANSI/Default encoding, CRLF line endings.

Output (result)
- `{ ok: true, scriptPath, matched, pattern, bindType, refPathType, mergeMode, outputDwg, saveAsVersion }`
- On error: `{ ok: false, error, msg }`

Script Behavior
- Header: sets `CMDECHO=0`, `FILEDIA=0`, `CMDDIA=0`, `ATTDIA=0`, `EXPERT=5`, `QAFLAGS=2`, `NOMUTT=1`, `REFPATHTYPE`, `BINDTYPE`, and `TRUSTEDPATHS`.
- For each selected DWG: issues `-XREF ATTACH` at origin with scale 1.
- `-XREF RELOAD *` then `-XREF BIND *` (no interactive `B`/`I`; `BINDTYPE` controls behavior).
- Layer merge (optional):
  - `ByFile`: Emits a small LISP to merge bound layers `<base>$0$*` into layer `<base>` for each input base name.
  - `Map`: Emits a LISP that merges layers by `pattern -> targetLayer` rows from CSV.
- Tail: `PURGE` (N times), optional `AUDIT`, then `SAVEAS` (multiline prompts) and `_QUIT`.

Conventions and Notes
- One input per line: do not concatenate inputs (e.g., `._SAVEAS2018` is invalid). Use newlines.
- Use forward slashes (`/`) for paths inside `.scr` to avoid escaping issues; the generator normalizes `\` to `/`.
- Do not send `B`/`I` after `-XREF BIND`; use `BINDTYPE` instead.
- `SAVEAS` is emitted as multi-line prompts: `SAVEAS` → `<version>` → `"<path>"`. Overwrite confirmation `Y` is not sent (omit unless necessary).
- Write output to a writable folder (e.g., `C:/temp/CadOut`).

Example: Generate Script (JSON‑RPC via helper)
- Using `Manuals/Scripts/send_revit_command_durable.py` (port 5210 as example):
```
python Manuals/Scripts/send_revit_command_durable.py \
  --port 5210 \
  --command generate_dwg_merge_script \
  --params '{
    "inputDir": "C:/temp/CadOut",
    "pattern": "walls_*.dwg",
    "bindType": "Bind",
    "refPathType": "2",
    "mergeMode": "ByFile",
    "saveAsVersion": "2018",
    "outputDwg": "C:/temp/CadOut/walls_merged_job.dwg",
    "trustedPaths": "C:/Temp/CadOut;C:/Temp",
    "purgeTimes": 2,
    "audit": true,
    "outScript": "C:/temp/CadOut/run_merge_job.scr"
  }'
```

Example: Merge by Map (CSV)
- CSV (`pattern,targetLayer`):
```
pattern,targetLayer
walls_A$0$*,A
walls_B$0$*,B
walls_C$0$*,C
```
- Params (diff only): `"mergeMode":"Map", "layerMapCsv":"C:/temp/CadOut/layermap.csv"`

Executing the Script
- AutoCadMCP server: call its run‑script API (if available) and pass `scriptPath` (and optional `seed` DWG).
- External: run accoreconsole directly (pause付きの .cmd など)。
```
"C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe" \
  /l ja-JP \
  /i "C:/temp/CadOut/seed.dwg" \
  /s "C:/temp/CadOut/run_merge_job.scr"
```

Troubleshooting
- 「そのようなコマンド 'B' はありません」
  - 余分な `B`/`I` 入力は禁止。`BINDTYPE` で制御します（本コマンドは自動適用）。
- 「ファイル名が無効です」
  - 書込み権限のあるフォルダを指定（例 `C:/temp/CadOut`）。SAVEAS は改行式で入力します。
- 「そのようなコマンド 'Y' はありません」
  - SAVEAS がエラーで失敗し、次行の `Y` が独立コマンドになっている可能性。原則 `Y` は送っていません。
- 出力DWGの内容が足りない
  - `pattern` に未包含の DWG がある可能性。`inputDir` と `pattern` を確認。

Versioning / Environment
- RevitMCP Add-in (Revit 2023+ / .NET Fx 4.8)
- AutoCAD Core Console 2026 で検証（他バージョンは SAVEAS の `saveAsVersion` に合わせる）

