# dynamo.list_scripts

- カテゴリ: Dynamo
- 目的: `Dynamo/Scripts` 配下の .dyn を一覧表示します。

> ⚠ 注意: Dynamo 実行は環境依存・不安定なケースが多いため **原則推奨しません**。十分に検証した上で利用してください。

## 概要
`RevitMCPAddin/Dynamo/Scripts` に置かれた `.dyn` を列挙し、入力/出力などのメタ情報を返します。`ScriptMetadata/<name>.json` がある場合は説明や入力定義を上書きします。

## 使い方
- メソッド: dynamo.list_scripts
- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "dynamo.list_scripts",
  "params": {}
}
```

## 結果のポイント
- `scripts[]`: `name`, `fileName`, `relativePath`, `inputs`, `outputs`
- `scriptsRoot`: スクリプト配置フォルダ
- `metadataRoot`: メタデータ配置フォルダ
- `dynamoReady`: Dynamo ランタイムが検出できたか
- `dynamoError`: 検出できない場合の理由

### パラメータスキーマ
```json
{
  "type": "object",
  "properties": {}
}
```

### 結果スキーマ
```json
{
  "type": "object",
  "properties": {
    "scripts": { "type": "array" },
    "scriptsRoot": { "type": "string" },
    "metadataRoot": { "type": "string" },
    "dynamoReady": { "type": "boolean" },
    "dynamoError": { "type": "string" }
  },
  "additionalProperties": true
}
```
