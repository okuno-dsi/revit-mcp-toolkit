Purpose
- Generate flexible AutoCAD Core Console (.scr) scripts and run them with pause-enabled wrappers.
- Handle typical Revit-to-DWG consolidation: XREF attach many DWGs, bind, optional layer merge, purge/audit, and save.

Files
- Tools/Generate-AccoreScript.ps1: Builds a .scr from inputs (folder, pattern, bind/insert, merge rules).
- Tools/Run-AccCoreJob.ps1: Runs accoreconsole with a script (and optional seed) and shows ExitCode; optional -Pause.
- Jobs/sample-merge.json: Example job config for folder-based merge-by-file.
- Jobs/sample-layermap.csv: Example layer-map for MergeMode=Map.

Quick Start (folder merge by file)
1) Generate script (.scr):
   pwsh -NoLogo -NoProfile -File Tools/Generate-AccoreScript.ps1 \
     -InputDir 'C:/temp/CadOut' \
     -Pattern 'walls_*.dwg' \
     -BindType Bind \
     -RefPathType 2 \
     -SaveAsVersion 2018 \
     -OutputDwg 'C:/temp/CadOut/walls_merged_job.dwg' \
     -TrustedPaths 'C:/Temp/CadOut;C:/Temp' \
     -MergeMode ByFile \
     -PurgeTimes 2 \
     -Audit \
     -OutScript 'run_job.scr'

2) Run with Core Console (pause enabled):
   pwsh -NoLogo -NoProfile -File Tools/Run-AccCoreJob.ps1 \
     -ScriptPath 'run_job.scr' \
     -SeedDwg 'seed.dwg' \
     -Locale 'ja-JP' \
     -Pause

Merge modes
- None: No layer consolidation after bind.
- ByFile: For each DWG base name, merge bound layers "<base>$0$*" into a target layer named <base>.
- Map: Use Jobs/sample-layermap.csv (pattern,targetLayer). Patterns are wildcards over bound layer names.

Notes
- Use forward slashes in paths inside .scr to avoid escaping issues; the tools already normalize this.
- BindType is controlled by system variable BINDTYPE (0=Bind, 1=Insert); no interactive 'B'/'I' input needed.
- SAVEAS is issued as multi-line prompts: SAVEAS -> version -> "path".
- FILEDIA/CMDDIA are forced to 0 to avoid dialogs.

Examples
- Generate from job JSON (PowerShell 7+):
   $job = Get-Content Jobs/sample-merge.json | ConvertFrom-Json \
   ; pwsh Tools/Generate-AccoreScript.ps1 @{
        InputDir=$job.inputDir; Pattern=$job.pattern; BindType=$job.bindType; RefPathType=$job.refPathType;
        SaveAsVersion=$job.saveAsVersion; OutputDwg=$job.outputDwg; TrustedPaths=$job.trustedPaths;
        MergeMode=$job.mergeMode; PurgeTimes=$job.purgeTimes; Audit=$job.audit; OutScript='run_job.scr'
     }

Troubleshooting
- "そのようなコマンド … はありません": Most often missing spaces/line breaks between inputs.
- SAVEAS fails with invalid filename: write to a writable folder (e.g., C:/temp/CadOut) and ensure quotes.
- BIND prompts for type: do not send 'B'/'I'; set BINDTYPE instead.
- ExitCode=0 but output is incomplete: ensure all input DWGs matched Pattern; increase Purge/Audit if needed.

