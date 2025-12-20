# ExcelMCP サーバー取扱説明書（日本語）

ExcelMCP は、Excel ブックを HTTP API 経由で検査・操作するための ASP.NET Core Minimal API です。

- **Excel COM 系**: 起動中のデスクトップ Excel を自動操作（Excel が起動している必要あり）
- **ファイル系**: ClosedXML/OpenXML で `.xlsx` を直接操作（Excel 起動不要）

## 前提

- Windows
- .NET 8 SDK
- COM 系を使う場合: Microsoft Excel（デスクトップ版）がインストールされ、同一ユーザーのデスクトップセッションで起動していること

## 起動

`ExcelMCP` フォルダで実行します。

```
# 開発（Swagger UI 有効）
dotnet run --urls http://localhost:5215

# Release
dotnet run -c Release --urls http://localhost:5215

# PowerShell ヘルパー
pwsh .\start.ps1 -Port 5215
```

疎通確認:

```
Invoke-RestMethod http://localhost:5215/health | ConvertTo-Json -Compress
```

## ログ

- コンソールに加え、以下にも出力します:
  - `%TEMP%\ExcelMCP\logs\excelmcp-YYYYMMDD.log`
- 1 リクエストごとに `[REQ]` / `[RES]`、例外時に `[ERR]` を記録します。

## 共通仕様（重要）

- 多くのエンドポイントは HTTP 200 でも `{ "ok": false, "msg": "..." }` を返します。必ず `ok` を確認してください。
- JSON のフィールド名は `camelCase` です（例: `workbookFullName`, `sheetName`, `rangeA1`）。
- パスはフルパス推奨（例: `C:\\path\\book.xlsx`）。
- **ファイル系**は `.xlsx` を開いて保存します。Excel で編集中/ロック中のファイルは失敗することがあります（閉じてから実行）。
- A1 表記:
  - セル: `B2`
  - 範囲: `A1:C10`
  - 行: `1:1`（COM 側で利用可）

## エンドポイント一覧（Program.cs 準拠）

### 稼働

- `GET /` サーバー情報
- `GET /health` ヘルスチェック

### Excel COM（Excel 起動必須）

- `GET /list_open_workbooks` 開いているブック一覧
- `POST /com/activate_workbook` ブックをアクティブ化
- `POST /com/activate_sheet` シートをアクティブ化
- `POST /com/read_cells` 範囲読み取り
- `POST /com/write_cells` セル書き込み（開始セル＋2次元配列）
- `POST /com/append_rows` 末尾に行追記（StartColumn の最初の空行から）
- `POST /com/format_range` 範囲の書式設定（整列/フォント/塗りつぶし/幅/高さ/オートフィット）
- `POST /com/save_workbook` 保存/別名保存
- `POST /com/add_sheet` シート追加
- `POST /com/delete_sheet` シート削除
- `POST /com/sort_range` 範囲並べ替え（Excel ネイティブ＋フォールバック）
- `POST /com/delete_empty_columns` データのない列削除（ヘッダーあり前提）
- `POST /com/delete_columns_by_index` 列インデックス指定削除
- `POST /com/resize` 列/行のリサイズ（幅/高さ/オートフィット）
- `POST /com/merge_vertical_header_cells` 3行ヘッダーの縦方向マージ（行1..3固定）
- `POST /com/get_vba_module` VBA モジュール取得（マクロ設定が必要）
- `POST /com/set_vba_module` VBA モジュール上書き（マクロ設定が必要）

### ファイルベース（Excel 起動不要）

- `POST /file/align_headers` 指定ヘッダー順に合わせた新規ファイル作成
- `POST /sheet_info` シート一覧・使用範囲
- `POST /read_cells` セル読み取り（使用セルのみ）
- `POST /write_cells` セル書き込み
- `POST /append_rows` 末尾に行追記
- `POST /set_formula` 数式設定
- `POST /format_sheet` 列幅/行高/オートフィット
- `POST /format_range_fill` 範囲を白塗り（現状は白のみ）
- `POST /clear_bg_by_f_threshold` F列<=閾値 の行(A:J)を白塗り
- `POST /to_csv` CSV 出力
- `POST /to_json` JSON 出力（records/matrix）
- `POST /list_charts` グラフ一覧（OpenXML, read-only）
- `POST /parse_plan` 罫線/ラベル抽出（Revit ワークフロー向け）

## COM 系: 共通（ブック指定）

COM 系は「起動中の Excel」から対象ブック/シートを探します。

- `workbookFullName`（推奨）: ブックのフルパス
- `workbookName`: Excel ウィンドウのブック名（`workbookFullName` 未指定時の代替）
- 両方省略時: `ActiveWorkbook` を使用

### GET /list_open_workbooks

レスポンス例:

```json
{
  "ok": true,
  "found": true,
  "workbooks": [
    { "name": "Book1.xlsx", "fullName": "C:\\path\\Book1.xlsx", "path": "C:\\path", "saved": true }
  ]
}
```

### POST /com/activate_workbook

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx" }
```

### POST /com/activate_sheet

`sheetName` または `sheetIndex`（1始まり）で指定します。両方省略時は ActiveSheet。

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet1" }
```

### POST /com/read_cells

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "rangeA1": "A1:C5",
  "useValue2": true
}
```

レスポンス例:

```json
{ "ok": true, "rows": [["A1", "B1", "C1"], ["A2", 123, true]] }
```

### POST /com/write_cells

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "startCell": "A10",
  "values": [["API test", 123], ["次行", 456]]
}
```

レスポンス例:

```json
{ "ok": true, "wroteRows": 2, "wroteCols": 2 }
```

### POST /com/append_rows

`startColumn` の末尾（最初の空行）に追記します。

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "startColumn": "A",
  "rows": [["r1c1", "r1c2"], ["r2c1", "r2c2"]]
}
```

レスポンス例:

```json
{ "ok": true, "appendedRows": 2, "toRow": 42 }
```

### POST /com/format_range

`target` は A1 範囲。色は `#RRGGBB` または `rgb(r,g,b)`。

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "target": "A1:C10",
  "numberFormat": "General",
  "horizontalAlign": "center",
  "verticalAlign": "center",
  "wrapText": true,
  "fontName": "Calibri",
  "fontSize": 11,
  "bold": true,
  "italic": false,
  "fontColor": "#000000",
  "fillColor": "#FFF2CC",
  "columnWidth": 18,
  "rowHeight": 22,
  "autoFitColumns": true,
  "autoFitRows": true
}
```

### POST /com/save_workbook

保存:

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx" }
```

別名保存:

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "saveAsFullName": "C:\\path\\Book1_copy.xlsx" }
```

### POST /com/add_sheet

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "newSheetName": "NewSheet",
  "afterSheetName": "Sheet1"
}
```

レスポンス例:

```json
{ "ok": true, "name": "NewSheet" }
```

### POST /com/delete_sheet

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet2" }
```

### POST /com/sort_range

`keys` は `columnIndex`（範囲内1始まり）または `columnKey`（例: `"B"`）で指定できます。

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "rangeA1": "A2:E100",
  "hasHeader": true,
  "keys": [{ "columnIndex": 1, "order": "asc" }, { "columnKey": "C", "order": "desc" }]
}
```

レスポンス例:

```json
{ "ok": true, "engine": "excel" }
```

（Excel ネイティブで失敗した場合は `engine: "fallback"` で値のみ並べ替えを行います）

### POST /com/delete_empty_columns

ヘッダーはあるが、データ開始行〜最終行の範囲で全て空の列を削除します。

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "headerRow": 1,
  "dataStartRow": 2,
  "maxDataRows": 400,
  "maxHeaderScanCols": 100,
  "protectHeaders": ["elementId"]
}
```

レスポンス例:

```json
{
  "ok": true,
  "deletedCount": 3,
  "deleted": [{ "column": 7, "header": "foo" }],
  "headerRow": 1,
  "dataStartRow": 2,
  "lastDataRow": 120,
  "lastHeaderCol": 40
}
```

### POST /com/delete_columns_by_index

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet1", "columns": [23, 24, 25] }
```

レスポンス例:

```json
{ "ok": true, "deleted": 3 }
```

### POST /com/resize

列/行インデックス（1始まり）を指定して幅/高さを変更します。幅/高さ未指定時はオートフィットします。

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet1", "columnIndices": [1, 2, 3], "columnWidth": 18 }
```

```json
{ "workbookFullName": "C:\\path\\Book1.xlsx", "sheetName": "Sheet1", "rowIndices": [1, 2, 3], "autoFitRows": true }
```

レスポンス例:

```json
{ "ok": true, "colsChanged": 3, "rowsChanged": 0 }
```

### POST /com/merge_vertical_header_cells

3行ヘッダー（行1..3）を縦方向にマージします（既にマージ済みのセルはスキップ）。

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsx",
  "sheetName": "Sheet1",
  "startColumn": "A",
  "endColumn": "BQ",
  "dryRun": true,
  "allowMergeTopTwo": true
}
```

レスポンス例:

```json
{ "ok": true, "dryRun": true, "mergedCount": 12, "skippedCount": 3, "mergedRanges": ["A1:A3", "B2:B3"] }
```

### POST /com/get_vba_module / POST /com/set_vba_module

VBA の読み書きには Excel の設定で「VBA プロジェクト オブジェクト モデルへのアクセスを信頼する」が必要です。

取得:

```json
{ "workbookFullName": "C:\\path\\Book1.xlsm", "moduleName": "Module1" }
```

レスポンス例:

```json
{ "ok": true, "moduleName": "Module1", "code": "Sub Hello()\\nEnd Sub\\n" }
```

上書き:

```json
{
  "workbookFullName": "C:\\path\\Book1.xlsm",
  "moduleName": "Module1",
  "code": "Sub Hello()\\nMsgBox \\\"Hi\\\"\\nEnd Sub\\n"
}
```

## ファイルベース API

### POST /file/align_headers（新規ファイル作成）

説明:

- `excelPath` を読み取り、新しい `.xlsx` を作成します（`outputPath` 未指定時は拡張子前に `R` を付与）。
- 1 行目（`headerRow`）を `headers` に揃えます。
- データ行はヘッダー名（大文字小文字無視）で対応列をコピーします。
- `treatParamNameAsName=true` の場合、`name` 列は `paramName` を代入可能です。

```json
{
  "excelPath": "C:\\path\\Source.xlsx",
  "outputPath": null,
  "headers": ["elementId", "paramKind", "name", "value"],
  "headerRow": 1,
  "scanHeaderCols": 120,
  "keyColumn": 1,
  "maxDataRows": 500,
  "treatParamNameAsName": true
}
```

レスポンス例:

```json
{ "ok": true, "output": "C:\\path\\SourceR.xlsx" }
```

### POST /sheet_info

```json
{ "excelPath": "C:\\path\\book.xlsx" }
```

レスポンス例:

```json
{
  "ok": true,
  "sheets": [
    { "name": "Sheet1", "usedRange": "A1:F20", "firstCell": "A1", "lastCell": "F20", "rowCount": 20, "columnCount": 6 }
  ]
}
```

### POST /read_cells

指定範囲、または `rangeA1` 未指定時は UsedRange の「使用セル（CellsUsed）」を返します（全セルは返しません）。

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "rangeA1": "A1:C5", "returnRaw": true, "returnFormatted": false, "includeFormula": true }
```

レスポンス例:

```json
{ "ok": true, "cells": [{ "address": "B2", "row": 2, "column": 2, "value": "123.45", "text": null, "formula": "=SUM(A1:A10)", "dataType": "Number" }] }
```

### POST /write_cells（上書き保存）

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "startCell": "B2", "values": [[1, 2, 3], ["a", "b", "c"]], "treatNullAsClear": false }
```

### POST /append_rows（末尾に追記）

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "startColumn": "A", "rows": [["X", "Y", "Z"], [10, 20, 30]] }
```

### POST /set_formula

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "target": "E2:E3", "formulaA1": "=SUM(B2:D2)" }
```

### POST /format_sheet

列幅/行高/オートフィットをまとめて指定できます。

```json
{
  "excelPath": "C:\\path\\book.xlsx",
  "sheetName": "Sheet1",
  "autoFitColumns": true,
  "columnWidths": { "A": 18, "C": 120 },
  "widthUnit": "pixels"
}
```

### POST /format_range_fill（現状: 白塗りのみ）

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "target": "A1:C10" }
```

### POST /clear_bg_by_f_threshold

F 列（既定 `F`）の数値が `threshold` 以下の行について、A..J 列の背景を白にします。

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "threshold": 3.0, "headerRow": 1, "fColumn": "F" }
```

### POST /to_csv

```json
{ "excelPath": "C:\\path\\book.xlsx", "sheetName": "Sheet1", "outputCsvPath": "C:\\tmp\\out.csv", "delimiter": ",", "quote": "\"", "useFormattedText": false, "encodingName": "utf-8" }
```

### POST /to_json

- `mode="records"`: ヘッダー行（`headerRow`）をキーにして `{...}` 配列を出力
- `mode="matrix"`: 2次元配列を出力
- `rangeA1` 未指定時は UsedRange を対象

```json
{
  "excelPath": "C:\\path\\book.xlsx",
  "sheetName": "Sheet1",
  "rangeA1": "A1:E100",
  "outputJsonPath": "C:\\tmp\\out.json",
  "mode": "records",
  "headerRow": 1,
  "useFormattedText": false,
  "indented": true,
  "emptyAsNull": true,
  "skipBlankRows": true
}
```

### POST /list_charts（read-only）

```json
{ "excelPath": "C:\\path\\book.xlsx" }
```

### POST /parse_plan（罫線/ラベル抽出）

グリッドの罫線を壁（線分）として抽出し、セル文字列をラベルとして返します。

```json
{ "excelPath": "C:\\path\\plan.xlsx", "sheetName": "Sheet1", "cellSizeMeters": 1.0, "useColorMask": false }
```

## 動作確認（テストスクリプト）

`ExcelMCP/test_requests.ps1` が一通りの API を叩きます。

```
pwsh -ExecutionPolicy Bypass -File .\test_requests.ps1 -BaseUrl http://localhost:5215 -ExcelPath C:\path\book.xlsx
```

## トラブルシュート

- `Excel is not running`（COM 系）: Excel を起動してから再実行してください（同一ユーザー/同一デスクトップセッションが必要です）。
- `RPC_E_SERVERCALL_RETRYLATER`（COM 系）: Excel がビジーです。少し待って再試行してください。
- `VBProject not accessible`（VBA 系）: Excel のマクロ設定で「VBA プロジェクト オブジェクト モデルへのアクセスを信頼する」を有効化してください。
- ファイル系が失敗する: 対象 `.xlsx` が Excel で開かれてロックされていないか確認してください。
