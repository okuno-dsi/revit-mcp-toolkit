# get_instance_parameters_bulk

- カテゴリ: ParamOps
- 目的: 複数要素のインスタンスパラメータを一括取得します（ID指定 or カテゴリ指定、ページング対応）。

## 概要
大量要素から「必要なパラメータだけ」を効率的に抜き出す用途です。

## 使い方
- メソッド: `get_instance_parameters_bulk`

### パラメータ
```jsonc
{
  "elementIds": [1001, 1002],
  "categories": [-2000011],
  "paramKeys": [
    "Comments",
    { "name": "終端でのフックの回転" }
  ],
  "page": { "startIndex": 0, "batchSize": 200 }
}
```

- `paramKeys`（必須）: 取得したいパラメータ指定（配列）。
  - 文字列: パラメータ名として扱います。
  - object 指定も可能:
    - `name`（string）: パラメータ名
    - `builtInId`（int）: BuiltInParameter の整数値
    - `guid`（string）: 共有パラメータ GUID
- 対象指定:
  - 推奨: `elementIds`（int配列）
  - または `categories`（BuiltInCategory の整数値）
  - どちらも無い場合は「モデル全走査」を避けるためエラーになります。
- ページング:
  - `page.startIndex`（既定 0）
  - `page.batchSize`（既定: 最大 ~500）

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_instance_parameters_bulk",
  "params": {
    "elementIds": [1234567],
    "paramKeys": ["コメント", "マーク"],
    "page": { "startIndex": 0, "batchSize": 200 }
  }
}
```

## 戻り値（例）
```jsonc
{
  "ok": true,
  "items": [
    {
      "ok": true,
      "elementId": 1234567,
      "categoryId": -2000011,
      "typeId": 7654321,
      "params": { "コメント": "..." },
      "display": { "コメント": "..." }
    }
  ],
  "nextIndex": null,
  "completed": true,
  "totalCount": 1
}
```

## 関連コマンド
- get_param_meta
- get_parameter_identity
- get_type_parameters_bulk
- update_parameters_batch

