# View Isolation + DWG Output Plan (Reproducible)

Scope
- Create Seed (no walls) and type-specific views reliably across sessions, then export DWG. No fragile manual toggling; flows are scriptable and idempotent.

Prerequisites
- Revit running with Revit MCP add‑in on 5210 (health: `GET http://127.0.0.1:5210/debug`).
- This repo on disk; PowerShell 7 recommended.
- Durable sender: `Codex/Scripts/Reference/send_revit_command_durable.py`.

Command Inventory (server)
- `duplicate_view` → returns `{ ok, viewId, name }` to create a view copy.
- `show_all_in_view` → detach template + unhide + clear overrides (batch with `startIndex/nextIndex`).
- `get_elements_in_view` → supports `_filter` (include/excludeCategoryIds, modelOnly, excludeImports, include/excludeClasses, includeLevelIds) and `_shape` paging.
- `hide_elements_in_view` → hides elementIds; skips non-hideable elements safely.
- `export_dwg` → duplicates internally and exports; respects keepTempView/annotation safety.
- `isolate_by_filter_in_view` (added) → one-shot isolation by categories/classes/parameters, annotations preserved by default.

1) Choose Base View (auto)
- Prefer a view named `1FL`. If absent, choose an open view with the most walls (categoryId = -2000011) visible.

PowerShell snippet
```
$py='Codex/Scripts/Reference/send_revit_command_durable.py'
function Call($m,$p,$w=240,$t=360){ $pjson=($p|ConvertTo-Json -Depth 60 -Compress); & python -X utf8 $py --port 5210 --command $m --params $pjson --wait-seconds $w --timeout-sec $t }
$lov = Call 'list_open_views' @{} | ConvertFrom-Json
$open=@($lov.result.result.views)
$base=$open | Where-Object { $_.name -eq '1FL' } | Select-Object -First 1
if(-not $base){
  $best=$null; $bestC=-1
  foreach($v in $open){ $wr=Call 'get_elements_in_view' @{ viewId=[int]$v.viewId; _shape=@{ idsOnly=$true }; categoryIds=@(-2000011) } | ConvertFrom-Json; $ids=$wr.result.result.elementIds; $c=($ids|Measure-Object).Count; if($c -gt $bestC){ $best=$v; $bestC=$c } }
  $base=$best
}
$baseViewId=[int]$base.viewId; $baseName=[string]$base.name
```

2) Seed (no walls) View
- Duplicate → Reset (detach template + unhide + clear overrides) → Hide all walls → Validate.

```
$dup=Call 'duplicate_view' @{ viewId=$baseViewId; withDetailing=$true; namePrefix=($baseName+' SEED ') } | ConvertFrom-Json
$seedId=[int]($dup.result.result.viewId)
# Reset
$idx=0; do{ $r=Call 'show_all_in_view' @{ viewId=$seedId; detachViewTemplate=$true; includeTempReset=$true; unhideElements=$true; clearElementOverrides=$true; batchSize=800; startIndex=$idx; refreshView=$true } | ConvertFrom-Json; try{ $idx=[int]$r.result.result.nextIndex }catch{ $idx=0 } } while($idx -gt 0)
# Hide walls
$wr=Call 'get_elements_in_view' @{ viewId=$seedId; _shape=@{ idsOnly=$true }; categoryIds=@(-2000011) } | ConvertFrom-Json
$wallIds=@($wr.result.result.elementIds)
if($wallIds.Count){ $chunk=800; for($i=0;$i -lt $wallIds.Count;$i+=$chunk){ $hi=[Math]::Min($i+$chunk-1,$wallIds.Count-1); $batch=@($wallIds[$i..$hi]); $null=Call 'hide_elements_in_view' @{ viewId=$seedId; elementIds=$batch; detachViewTemplate=$true; refreshView=$true; batchSize=800 } } }
# Validate
$chk=Call 'get_elements_in_view' @{ viewId=$seedId; _shape=@{ idsOnly=$true }; categoryIds=@(-2000011) } | ConvertFrom-Json
```
- Expected: `chk.result.result.elementIds` count = 0

3) Type‑Specific Views (robust)
Option A – Generic one‑shot (recommended)
- Use `isolate_by_filter_in_view` with rules for each type.
```
# Example for ALC100 walls only
$dup=Call 'duplicate_view' @{ viewId=$baseViewId; withDetailing=$true; namePrefix=($baseName+' ALC100 ') } | ConvertFrom-Json
$vid=[int]$dup.result.result.viewId
$iso=Call 'isolate_by_filter_in_view' @{ viewId=$vid; detachViewTemplate=$true; keepAnnotations=$true; filter=@{ includeClasses=@('Wall'); parameterRules=@(@{ target='type'; builtInName='SYMBOL_NAME_PARAM'; op='eq'; value='ALC100 複合壁' }) } } | ConvertFrom-Json
```

Option B – 手動分離（ビュー側で再計測→非対象をHide）
- 複製→リセット→複製ビュー内で
  - モデル非壁（_filter: modelOnly=true, excludeImports=true, excludeCategoryIds=[-2000011]）をHide
  - 壁（categoryIds=[-2000011]）を取得→element_info でタイプ別→当該タイプ以外をHide
  - 検証: 残る壁のタイプ distinct が当該タイプのみ

4) DWG Export
- Seed: `export_dwg { viewId: <seedId>, outputFolder: '.../DWG', fileName: 'seed', dwgVersion: 'ACAD2018' }`
- Type: `export_dwg { viewId: <typeViewId>, fileName: 'walls_<TYPE>' }`
- 事前に Play/Proxy 不要。Revit MCP 直呼びでOK（ローカル）。

4.1) Batch Export multiple views (items[])
- Use export_dwg with items[] to export multiple views in a single batch call. For very large batches, combine with startIndex/batchSize/maxMillisPerTx to time-slice.

PowerShell snippet
```
$py='Codex/Scripts/Reference/send_revit_command_durable.py'
function Call($m,$p,$w=600,$t=1200){ $pjson=($p|ConvertTo-Json -Depth 60 -Compress); & python -X utf8 $py --port 5210 --command $m --params $pjson --wait-seconds $w --timeout-sec $t }

# Prepare view list (example)
$views = @(
  @{ viewId = 401; outputFolder = "C:\\Exports"; fileName = "Plan_1F" },
  @{ viewId = 402; outputFolder = "C:\\Exports"; fileName = "Plan_2F" }
)

# One-shot batch (small set)
$req = @{ items = $views; dwgVersion = 'ACAD2018'; useExportSetup = 'JIS-A-ByLayer-2024'; startIndex = 0; batchSize = 10 }
$res = Call 'export_dwg' $req | ConvertFrom-Json

# Time-sliced loop (large set)
$idx = 0
do {
  $req.startIndex = $idx
  $res = Call 'export_dwg' $req | ConvertFrom-Json
  try { $idx = [int]$res.result.result.nextIndex } catch { $idx = 0 }
} while ($idx -gt 0)
```

Notes
- Each item inherits top-level options (dwgVersion/useExportSetup) unless overridden in the item.
- The server duplicates the source view internally; annotations remain visible while non-target model elements can be hidden.

5) 安定化のコツ
- 常に「複製ビュー内で」要素を再計測してから非表示にする（ベースからの持ち越し集合は使わない）。
- アノテーションは保持（注釈が消えると空の出力になりやすい）。
- HideElements は 800 前後のバッチで実行（ビューが大きいほど安全）。
- 長時間のリセット (show_all_in_view) は nextIndex でループ完走必須。

6) 差異が出た場合のチェック
- テンプレートで可視性がロックされていないか（detachViewTemplate=true）。
- get_elements_in_view に `_filter` が効いているか（include/excludeCategoryIds, modelOnly, excludeImports）。
- hide_elements_in_view の skipped/hiddenCount を監視（非表示不可要素は残って正常）。
- export_dwg は複製側で注釈保持のまま非対象Hideしたビューを使う。

7) フル自動バッチ（概念）
- 繰り返し：types[] = element_info(get_elements_in_view(walls)).GroupBy(typeName)
- For each typeName: duplicate_view → isolate_by_filter_in_view (rules by typeName) → export_dwg

This plan is safe to rerun across sessions: views are duplicated per run, state is reset per duplicate, and annotations are preserved while model non-targets are hidden.



