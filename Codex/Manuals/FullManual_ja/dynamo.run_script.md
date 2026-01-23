# dynamo.run_script

- カテゴリ: Dynamo
- 目的: Dynamo グラフ (.dyn) を入力指定付きで実行します。

> ⚠ 注意: Dynamo 実行は環境依存・不安定なケースが多いため **原則推奨しません**。十分に検証した上で利用してください。

## 概要
`RevitMCPAddin/Dynamo/Scripts` 配下の `.dyn` を Revit 内で実行します。入力は `.dyn` の `Inputs` セクションの `Name` または `Id` で一致させます。実行用に `%LOCALAPPDATA%\\RevitMCP\\dynamo\\runs` へ一時コピーを作成します。

## 使い方
- メソッド: dynamo.run_script
- パラメータ:
  - `script` (string, 必須): Scripts 配下のファイル名 or 相対パス
  - `inputs` (object, 任意): 入力の上書き
  - `timeoutMs` (int, 任意): 評価完了までの待機時間（既定 120000）
  - `showUi` (bool, 任意): Dynamo UI を表示するか（既定 false）
  - `forceManualRun` (bool, 任意): 手動実行モードを強制（既定 false）
  - `checkExisting` (bool, 任意): 既存 Dynamo モデル再利用（既定 true）
  - `shutdownModel` (bool, 任意): 実行後にモデルを終了（既定 false）
  - `hardKillRevit` (bool, 任意): 実行後に Revit を強制終了（既定 false）
  - `hardKillDelayMs` (int, 任意): 強制終了までの遅延（既定 5000、最小 1000、最大 600000）

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "dynamo.run_script",
  "params": {
    "script": "move_elements.dyn",
    "inputs": {
      "element_ids": [12345, 67890],
      "offset_mm": [1000, 0, 0]
    }
  }
}
```

## 注意点
- 入力は文字列化して渡します（リストや点入力は JSON 配列を想定）。
- 出力はベストエフォートで取得します（取得できない場合は `outputsUnavailable=true`）。
- `hardKillRevit=true` は未保存の変更を破棄して Revit を終了します（夜間の無人運転のみ推奨）。
- `hardKillRevit=true` は実行後に (1) 作業状態スナップショット (2) Worksharing 同期 (該当時) (3) 保存（トランザクションでブロックされた場合は UI スレッドでリトライ） (4) Revit 再起動＋同一ファイル再オープン を試行し、その後に強制終了します。
- 途中で失敗しても強制終了は行われます。詳細は add-in ログで確認してください。
- 強制終了時は MCP サーバー停止を試行し、停止確認（ロック/プロセス状態）をログに記録します。

### パラメータスキーマ
```json
{
  "type": "object",
  "properties": {
    "script": { "type": "string" },
    "inputs": { "type": "object" },
    "timeoutMs": { "type": "integer" },
    "showUi": { "type": "boolean" },
    "forceManualRun": { "type": "boolean" },
    "checkExisting": { "type": "boolean" },
    "shutdownModel": { "type": "boolean" },
    "hardKillRevit": { "type": "boolean" },
    "hardKillDelayMs": { "type": "integer" }
  }
}
```

### 結果スキーマ
```json
{
  "type": "object",
  "properties": {
    "result": { "type": "object" }
  },
  "additionalProperties": true
}
```
