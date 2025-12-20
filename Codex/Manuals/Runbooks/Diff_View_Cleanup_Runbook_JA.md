# Diff ビュー削除手順（安全・確実な実行フロー）

最終更新: 2025-11-08

本手順は、比較用に作成した「RSL1 Diff」等の Diff ビューを、ポート間の環境差や表記揺れに影響されず、確実に削除するための運用ガイドです。

## 目的
- 複製/再実行時に残存した Diff ビューを正規化した名前照合で漏れなく削除する。
- ドキュメント/ビューのコンテキスト不一致による誤判定を防ぐ。

## 原因になりやすいポイント
- ビュー名の表記揺れ（全角/半角・全角スペース・末尾空白・大小文字差）。
- 複数ドキュメントが同ポートで開いており、意図しないドキュメントを参照している。
- UIの前面化とAPIのアクティブビューがずれるタイミング依存。

## 基本方針
1) ドキュメント固定: 対象ドキュメントの代表ビュー（例: `RSL1`）をアクティブ化する。
2) 広域列挙: `get_views(includeTemplates=false)` で全ビューを列挙する。
3) 名前正規化: NFKC 正規化 + 大小無視 + すべての空白（半角/全角 U+3000）除去。
4) 候補抽出: 正規化名が `rsl1diff` と等しい、または `rsl1` と `diff` の両トークンを含むものを候補とする。
5) 削除: `delete_view(viewId)` を候補全件に対して実行。
6) 検証: 同一条件で再列挙し、残件=0 を確認。

## PowerShell スニペット（send_revit_command_durable.py 利用）
```
$PY = Join-Path $PWD 'Manuals/Scripts/send_revit_command_durable.py'

function Normalize-ViewName([string]$name){
  if([string]::IsNullOrWhiteSpace($name)){ return '' }
  $n = $name.Normalize([System.Text.NormalizationForm]::FormKC)
  $n = $n.ToLowerInvariant()
  $n = $n -replace "\u3000"," "    # 全角スペース→半角
  $n = $n -replace "\s",""       # すべての空白削除
  return $n
}

function Remove-DiffViews([int]$Port,[string]$baseName='RSL1',[string]$diffToken='Diff'){
  # 1) ドキュメント固定
  try { python $PY --port $Port --command activate_view --params (@{ name=$baseName } | ConvertTo-Json -Compress) | Out-Null } catch {}

  # 2) 広域列挙
  $res = python $PY --port $Port --command get_views --params (@{ includeTemplates=$false } | ConvertTo-Json -Compress) | ConvertFrom-Json
  $views = @($res.result.result.views)

  # 3) 正規化＋候補抽出
  $target = @()
  foreach($v in $views){
    $norm = Normalize-ViewName $v.name
    if($norm -eq 'rsl1diff') { $target += $v; continue }
    if($norm.Contains('rsl1') -and $norm.Contains('diff')) { $target += $v; continue }
  }

  # 4) 削除
  $deleted=0
  foreach($v in $target){
    $vid = [int]$v.viewId
    $del = python $PY --port $Port --command delete_view --params (@{ viewId=$vid } | ConvertTo-Json -Compress) | ConvertFrom-Json
    if($del.result.result.ok){ $deleted++ }
  }

  # 5) 検証
  $res2 = python $PY --port $Port --command get_views --params (@{ includeTemplates=$false } | ConvertTo-Json -Compress) | ConvertFrom-Json
  $remain = @($res2.result.result.views | ForEach-Object { Normalize-ViewName $_.name } | Where-Object { ($_ -eq 'rsl1diff') -or (($_.Contains('rsl1')) -and ($_.Contains('diff'))) }).Count

  [PSCustomObject]@{ port=$Port; deleted=$deleted; remain=$remain }
}

# 例: 5210/5211 の両方で削除
Remove-DiffViews -Port 5210
Remove-DiffViews -Port 5211
```

## ベストプラクティス
- 比較・可視化の前に必ず本手順で既存 Diff ビューをクリーンにする。
- `ensure_compare_view` は `onNameConflict='returnExisting'` を使用し、重複作成を避ける。
- 雲マーク等のアノテーションを作成する場合、Comments にジョブID/日時を記録し、再実行時はそれに基づき安全に掃除する。
- 複数ドキュメントが開いている場合は、`get_open_documents` で対象を特定し、`activate_view` でコンテキスト固定してから実行する。

## 既知の注意点
- Revit の UI 前面化と API のアクティブはタイミングでずれる場合があるため、`activate_view` の直後に `get_current_view` で検証するのが望ましい。
- ビュー名の正規化は NFKC を採用。案件で独自の命名規則がある場合は、上記抽出条件を適宜拡張する。

