# 選択要素のパラメータを GUID・値・表示付きでエクセル出力する手順

目的
- Revit MCP サーバーに接続し、現在選択中の要素について「共有パラメータの GUID」「値（SI/プロジェクト）」「表示文字列（AsValueString）」を収集し、Excel 形式で保存します。

前提
- Revit が起動し、MCP Add-in が有効（既定ポート `5210`）。
- PowerShell 5+/7+ と Python 3.x が使用可能。
- 実行ポリシーでブロックされる場合はプロセス限定 Bypass を使用: 例
  - `pwsh -ExecutionPolicy Bypass -File Codex/Scripts/Reference/test_connection.ps1 -Port 5210`

出力先（既定）
- `Projects/<ProjectName>_<Port>/Logs/`
- 代表ファイル名（例）:
  - `selected_parameters_with_values_guid.csv`（UTF-8 BOM）
  - `selected_parameters_with_values_guid.xlsx`

手順 A: 接続確認（必須）
```powershell
pwsh -ExecutionPolicy Bypass -File Codex/Scripts/Reference/test_connection.ps1 -Port 5210
```

手順 B: 選択中要素 ID を取得
```powershell
$PORT = 5210
$PY = "Codex/Scripts/Reference/send_revit_command_durable.py"
$LOGS = (Resolve-Path "Codex/Work").Path | ForEach-Object { Get-ChildItem $_ -Directory | Where-Object { $_.Name -like "*_$PORT" -and $_.Name -notlike 'Project_*' } | Select-Object -First 1 } | ForEach-Object { Join-Path $_.FullName 'Logs' }
if(-not (Test-Path $LOGS)){ New-Item -ItemType Directory -Path $LOGS | Out-Null }
python $PY --port $PORT --command get_selected_element_ids --params '{}' --output-file (Join-Path $LOGS 'selected_element_ids.json')
```
- 空の場合は Revit 側で要素を選択し直してください。

手順 C: 要素情報（UniqueId/TypeId など）を保存
```powershell
$sel = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $LOGS 'selected_element_ids.json') | ConvertFrom-Json
$EID = [int]$sel.result.result.elementIds[0]
python $PY --port $PORT --command get_element_info --params (ConvertTo-Json @{ elementIds=@($EID); rich=$true } -Compress) --output-file (Join-Path $LOGS 'elem_current_info.json')
```

手順 D: パラメータメタデータ（instance/type 両方）
```powershell
python $PY --port $PORT --command get_param_meta --params '{"target":{"by":"elementId","value":'+$EID+'},"include":{"instance":true,"type":true},"maxCount":0}' --output-file (Join-Path $LOGS 'param_meta_current.json')
```
- メタの `id` が 0 未満なら BuiltInParameter、`kind` は `instance`/`type`、`isShared` が共有パラメータの目安です。

手順 E: 値を高速に取得（Bulk）
- インスタンス値
```powershell
$metaObj = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $LOGS 'param_meta_current.json') | ConvertFrom-Json
$plist = $metaObj.result.result.parameters
$keys = @(); foreach($pm in $plist){ if([int]$pm.id -lt 0){ $keys += @{ builtInId=[int]$pm.id } elseif($pm.guid){ $keys += @{ guid=$pm.guid } } elseif($pm.name){ $keys += @{ name=$pm.name } } }
$bulkInst = Join-Path $LOGS 'bulk_params_current.json'
python $PY --port $PORT --command get_instance_parameters_bulk --params (ConvertTo-Json @{ elementIds=@($EID); paramKeys=$keys; page=@{ startIndex=0; batchSize=1 } } -Compress) --output-file $bulkInst
```
- タイプ値
```powershell
$info = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $LOGS 'elem_current_info.json') | ConvertFrom-Json
$typeId = [int]$info.result.result.elements[0].typeId
$bulkType = Join-Path $LOGS 'bulk_type_params_current.json'
python $PY --port $PORT --command get_type_parameters_bulk --params (ConvertTo-Json @{ typeIds=@($typeId); paramKeys=$keys; page=@{ startIndex=0; batchSize=1 } } -Compress) --output-file $bulkType
```
- 備考: Bulk の `params/display` は BuiltIn 名キー（例: `DOOR_WIDTH`）で返ることがあります。表示名（日本語）との不一致に注意してください（後述のマージで解決）。

手順 F: 共有パラメータの GUID を解決
- `isShared:true` または `origin:'shared'` な項目について、`get_parameter_identity { fields:['guid'] }` をパラメータ名単位で呼び出します。タイムアウト回避のため 20 件程度ずつ分割推奨。
```powershell
$shared = @($plist | Where-Object { $_.isShared -eq $true -or $_.origin -eq 'shared' })
$guidMap = @{}
for($i=0; $i -lt $shared.Count; $i+=20){
  $end=[Math]::Min($i+19,$shared.Count-1)
  for($j=$i; $j -le $end; $j++){
    $pm=$shared[$j]; if(-not $pm.name){ continue }
    $att = (($(""+$pm.kind) -eq 'type') ? 'type' : 'instance')
    $req = @{ target=@{ by='elementId'; value=$EID }; paramName=$pm.name; attachedToOverride=$att; fields=@('guid') } | ConvertTo-Json -Compress
    $tmp = Join-Path $LOGS ("guid_{0}.json" -f $j)
    python $PY --port $PORT --command get_parameter_identity --params $req --output-file $tmp | Out-Null
    try { $o=Get-Content -Raw -Encoding UTF8 -Path $tmp | ConvertFrom-Json; $g=$o.result.result.parameter.guid; if($g){ $guidMap[$pm.name]=$g } } catch {}
  }
}
$guidMap | ConvertTo-Json -Depth 5 | Out-File -FilePath (Join-Path $LOGS 'guid_map_current.json') -Encoding utf8
```

手順 G: マージして CSV/Excel へ
- 収集方針（推奨の優先順位）
  1) Built-in/幾何系: `get_parameter_identity` で解決した表示・単位・値（例: 幅/高さ/面積など）
  2) Bulk（インスタンス/タイプ）: `display` → `params` の順で補完
  3) GUID: `guid_map_current.json` → メタの `guid`
- 出力（UTF-8 BOM CSV → Excel 変換）
```powershell
# マップ展開
$inst = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $LOGS 'bulk_params_current.json') | ConvertFrom-Json
$type = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $LOGS 'bulk_type_params_current.json') | ConvertFrom-Json
$instItem = $inst.result.result.items[0]
$typeItem = $type.result.result.items[0]
$instP=@{}; $instD=@{}; try{ foreach($k in $instItem.params.PSObject.Properties.Name){ $instP[$k]=$instItem.params.$k } }catch{}
try{ foreach($k in $instItem.display.PSObject.Properties.Name){ $instD[$k]=$instItem.display.$k } }catch{}
$typeP=@{}; $typeD=@{}; try{ foreach($k in $typeItem.params.PSObject.Properties.Name){ $typeP[$k]=$typeItem.params.$k } }catch{}
try{ foreach($k in $typeItem.display.PSObject.Properties.Name){ $typeD[$k]=$typeItem.display.$k } }catch{}
$biVal = @{}
$biPath = Join-Path $LOGS 'bi_value_map_current.json'
if(Test-Path $biPath){ $biTmp = Get-Content -Raw -Encoding UTF8 -Path $biPath | ConvertFrom-Json; foreach($n in $biTmp.PSObject.Properties.Name){ $biVal[$n]=$biTmp.$n } }
$gmap = @{}
$gPath = Join-Path $LOGS 'guid_map_current.json'
if(Test-Path $gPath){ $gTmp = Get-Content -Raw -Encoding UTF8 -Path $gPath | ConvertFrom-Json; foreach($n in $gTmp.PSObject.Properties.Name){ $gmap[$n]=$gTmp.$n } }

# 行生成
$info = Get-Content -Raw -Encoding UTF8 -Path (Join-Path $LOGS 'elem_current_info.json') | ConvertFrom-Json
$uniq = $info.result.result.elements[0].uniqueId
$rows=@()
foreach($pm in $plist){
  $n=$pm.name; if([string]::IsNullOrWhiteSpace($n)){ continue }
  $k = (""+$pm.kind)
  $val=''; $disp=''; $units=''
  if($biVal.ContainsKey($n)){ $b=$biVal[$n]; $disp=$b.display; if($b.unitSi){ $units=$b.unitSi }; if($b.valueProject -ne $null){ $val=$b.valueProject } elseif($b.valueSi -ne $null){ $val=$b.valueSi } }
  if([string]::IsNullOrWhiteSpace($disp)){
    if($k -eq 'instance'){ if($instD.ContainsKey($n)){ $disp=$instD[$n] } elseif($instP.ContainsKey($n)){ $val=$instP[$n] } }
    else { if($typeD.ContainsKey($n)){ $disp=$typeD[$n] } elseif($typeP.ContainsKey($n)){ $val=$typeP[$n] } }
  }
  $guid = if($gmap.ContainsKey($n)){ $gmap[$n] } elseif($pm.guid){ $pm.guid } else { '' }
  $rows += [pscustomobject]([ordered]@{
    elementId=$EID; elementUniqueId=$uniq; paramKind=$pm.kind; name=$pm.name; paramId=$pm.id; storageType=$pm.storageType; units=$units; value=$val; display=$disp; isShared=$pm.isShared; isBuiltIn=([int]$pm.id -lt 0); guid=$guid; origin=$pm.origin; groupEnum=$pm.projectGroup.enum; groupUi=$pm.projectGroup.uiLabel; resolvedBy='bulk+bi+guidmap'
  })
}

$csv = Join-Path $LOGS 'selected_parameters_with_values_guid.csv'
$xlsx = [IO.Path]::ChangeExtension($csv,'.xlsx')
$rows | Export-Csv -Path $csv -NoTypeInformation -Encoding utf8BOM
$excel = New-Object -ComObject Excel.Application; try{ $excel.Visible=$false; $excel.DisplayAlerts=$false; $wb=$excel.Workbooks.Open((Resolve-Path $csv).Path); try{ $wb.SaveAs($xlsx,51) } finally { $wb.Close($true) | Out-Null } } finally { $excel.Quit() | Out-Null; [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel) } 
```

手順 H: 検証（isShared=True かつ GUID 空の行を抽出）
- 収集直後に、共有パラメータなのに GUID が空の行を確認し、後述の再照会に回します。
```powershell
$CSV_MAIN = Join-Path $LOGS 'selected_parameters_with_values_guid.csv'
$rows = Import-Csv -Path $CSV_MAIN
$filtered = $rows | Where-Object {
  $isShared = $_.isShared
  $guid = '' + $_.guid
  (($isShared -eq $true) -or ($isShared -eq 'True') -or ($isShared -eq 'true')) -and ([string]::IsNullOrWhiteSpace($guid))
}
$CSV_TODO = Join-Path $LOGS 'shared_without_guid.csv'
$JSON_TODO = Join-Path $LOGS 'shared_without_guid.json'
$filtered | Export-Csv -Path $CSV_TODO -NoTypeInformation -Encoding utf8BOM
$filtered | ConvertTo-Json -Depth 5 | Out-File -FilePath $JSON_TODO -Encoding utf8
```

手順 I: 再照会（shared_without_guid.csv をソースに GUID を個別解決）
- `get_parameter_identity { fields:['guid'] }` を名前＋kind（type/instance）に応じて呼び出します。20 件前後ずつ分割実行すると安定します。
```powershell
$PY = "Codex/Scripts/Reference/send_revit_command_durable.py"
$PORT = 5210
$results = @()
$targets = Import-Csv -Path (Join-Path $LOGS 'shared_without_guid.csv')
foreach($t in $targets){
  $eid = [int]$t.elementId
  $name = [string]$t.name
  if($eid -le 0 -or [string]::IsNullOrWhiteSpace($name)){ continue }
  $att = if((""+$t.paramKind) -eq 'type'){ 'type' } else { 'instance' }
  $req = @{ target=@{ by='elementId'; value=$eid }; paramName=$name; attachedToOverride=$att; fields=@('guid') } | ConvertTo-Json -Compress
  $tmp = Join-Path $LOGS ("requery_guid_{0}_{1}.json" -f $eid, ([Math]::Abs([Guid]::NewGuid().GetHashCode())))
  python $PY --port $PORT --command get_parameter_identity --params $req --wait-seconds 10 --timeout-sec 20 --output-file $tmp | Out-Null
  $guid = ''
  try { $o = Get-Content -Raw -Encoding UTF8 -Path $tmp | ConvertFrom-Json; $p=$o; if($p.result){ $p=$p.result; if($p.result){ $p=$p.result } }; $guid = ''+$p.parameter.guid } catch {}
  $results += [pscustomobject]@{ elementId=''+$eid; name=$name; paramKind=$t.paramKind; resolvedGuid=$guid }
}
$CSV_RES = Join-Path $LOGS 'shared_without_guid_resolved.csv'
$JSON_RES = Join-Path $LOGS 'shared_without_guid_resolved.json'
$results | Export-Csv -Path $CSV_RES -NoTypeInformation -Encoding utf8BOM
$results | ConvertTo-Json -Depth 5 | Out-File -FilePath $JSON_RES -Encoding utf8
```

手順 J: 再照会結果をメイン CSV/XLSX にマージ
```powershell
$CSV_MAIN = Join-Path $LOGS 'selected_parameters_with_values_guid.csv'
$rows = Import-Csv -Path $CSV_MAIN
$res = Import-Csv -Path (Join-Path $LOGS 'shared_without_guid_resolved.csv')
$map = @{}
foreach($r in $res){ if(-not [string]::IsNullOrWhiteSpace($r.resolvedGuid)){ $key = (''+$r.elementId)+'|'+$r.name; $map[$key] = $r.resolvedGuid } }
$updated = 0
foreach($row in $rows){ $k=(''+$row.elementId)+'|'+$row.name; if($map.ContainsKey($k) -and [string]::IsNullOrWhiteSpace($row.guid)){ $row.guid = $map[$k]; $updated++ } }
$rows | Export-Csv -Path $CSV_MAIN -NoTypeInformation -Encoding utf8BOM
# Excel を再保存
$XLSX_MAIN = [IO.Path]::ChangeExtension($CSV_MAIN,'.xlsx')
$excel = $null; $wb=$null
try { $excel = New-Object -ComObject Excel.Application; $excel.Visible=$false; $excel.DisplayAlerts=$false; $wb=$excel.Workbooks.Open((Resolve-Path $CSV_MAIN).Path); try { $wb.SaveAs($XLSX_MAIN,51) } finally { $wb.Close($true) | Out-Null } } finally { if($excel){ $excel.Quit() | Out-Null; [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel) } }
Write-Host ("[Merged] updated rows="+$updated)
```

信頼性を高めるためのガイドライン
- Built-in/プロジェクト/ファミリパラメータには GUID がありません（空欄が正）。共有のみ GUID を期待します。
- Bulk 値（instance/type）は高速ですが GUID を返しません。GUID が必要な場合は共有パラメータだけ `get_parameter_identity` で解決し、最後に「isShared=True かつ GUID空」を再照会してマージします。
- チャンク実行: `get_parameter_identity` は 20 件前後ずつ処理し、`--wait-seconds 8–12 / --timeout-sec 15–25` で安定化します。失敗分のみ再実行してください。
- マージキー: 基本は `name + kind(type/instance)` と `elementId` の組合せで突合。Built-in は `builtInId` を優先キーにできるとより堅牢です（将来の拡張候補）。
- Excel 再保存: ファイルが開かれていると上書きできない場合があります。閉じるか別名保存で回避してください（例: `_resaved_yyyyMMdd_HHmmss.xlsx`）。

補足（よくある質問）
- なぜ一部の GUID が空欄？
  - GUID が付与されるのは「共有パラメータ」のみです。Built-in やプロジェクト/ファミリパラメータには GUID がありません。
- なぜ一部の value が空欄？
  - Bulk のキーは Built-in 英名のため、表示名（日本語）と一致しないものがあります。上記のマージ手順では Built-in を `get_parameter_identity` で補完しています。
  - `kind:type` の値は `get_type_parameters_bulk` 側にあります。両方の結果を統合してください。
- タイムアウト回避策
  - GUID 解決や Identity の問い合わせは 20 件前後に分割して実行、`--wait-seconds`/`--timeout-sec` を短めに保ちながら再試行する構成が安定です。

関連
- 接続クイックスタート: `Manuals/ConnectionGuide/QUICKSTART.md`
- スクリプト一覧: `Scripts/Reference/README.md`
- Bulk パラメータの詳細: `Manuals/Commands/Bulk_Parameters_EN.md`
- セクションボックスのフォーカス 3D 作成: `Scripts/Reference/create_focus_3d_from_selection.ps1`




