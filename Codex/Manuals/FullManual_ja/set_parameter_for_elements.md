# set_parameter_for_elements

- カテゴリ: Parameters
- 目的: 複数の要素に対して、同じパラメータに同じ値を一括で設定します。

## 概要
このコマンドは、`elementIds` で指定した要素集合に対して、1つのパラメータを同一の値でまとめて更新します。  
AI エージェントやスクリプトから、「フラグやラベルを一括で付ける」用途を想定しています。

## 使い方
- メソッド: set_parameter_for_elements

### パラメータ
```jsonc
{
  "elementIds": [1001, 1002, 1003],
  "param": {
    "name": "コメント",
    "builtIn": null,
    "sharedGuid": null
  },
  "value": {
    "storageType": "String",
    "stringValue": "外壁"
  },
  "options": {
    "stopOnFirstError": false,
    "skipReadOnly": true,
    "ignoreMissingOnElement": true
  }
}
```

- `elementIds` (int 配列, 必須):
  - 更新対象の要素ID (`ElementId.IntegerValue`) を指定します。

- `param` (object, 必須):
  - パラメータ特定方法。少なくとも 1 つを指定します。
    - `name` (string): パラメータ名（例: `"コメント"`）。
    - `builtIn` (string): `BuiltInParameter` 列挙名（例: `"WALL_ATTR_FIRE_RATING"`）。
    - `sharedGuid` (string): 共有パラメータの GUID 文字列。
  - 解決優先度: `builtIn` > `sharedGuid` > `name`。

- `value` (object, 必須):
  - `storageType`: `"String" | "Integer" | "Double" | "ElementId"` のいずれか。
  - `storageType` に応じて、以下のいずれか 1 つを指定します。
    - `stringValue`: 文字列（`"String"` 用）
    - `intValue`: 整数（`"Integer"` 用）
    - `doubleValue`: 数値（`"Double"` 用、`UnitHelper` に従い外部値として解釈。通常は SI 単位）
    - `elementIdValue`: 整数（`"ElementId"` 用、対象 `ElementId.IntegerValue`）

- `options` (object, 任意):
  - `stopOnFirstError` (bool, 既定値: `false`)
    - `true` の場合、最初に失敗した要素の時点で残りの要素を処理せずに終了します。
  - `skipReadOnly` (bool, 既定値: `true`)
    - `true` の場合、読み取り専用パラメータはその要素だけ失敗として記録し、バッチ全体は継続します（`stopOnFirstError=true` の場合を除く）。
  - `ignoreMissingOnElement` (bool, 既定値: `true`)
    - `true` の場合、その要素にパラメータが存在しないケースを失敗として記録しつつ、
      少なくとも 1 つの要素が更新できていれば全体として `ok: true` を返すことができます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "set-comment-walls-3f",
  "method": "set_parameter_for_elements",
  "params": {
    "elementIds": [1001, 1002, 1003],
    "param": {
      "name": "コメント"
    },
    "value": {
      "storageType": "String",
      "stringValue": "外壁"
    },
    "options": {
      "stopOnFirstError": false,
      "skipReadOnly": true,
      "ignoreMissingOnElement": true
    }
  }
}
```

### 例: ElementId 型パラメータの設定（鉄筋フック種別など）
ElementId 型のパラメータ（例: 鉄筋のフック種別）を設定する場合は次の形になります:

```jsonc
{
  "elementIds": [5945871],
  "param": { "name": "始端のフック" },
  "value": { "storageType": "ElementId", "elementIdValue": 4857530 }
}
```

## 戻り値

### 正常終了
```jsonc
{
  "ok": true,
  "msg": "Updated 120 elements. 8 elements failed.",
  "stats": {
    "totalRequested": 128,
    "successCount": 120,
    "failureCount": 8
  },
  "results": [
    {
      "elementId": 1001,
      "ok": true,
      "scope": "Instance",
      "msg": "Updated",
      "resolvedBy": "name:コメント"
    },
    {
      "elementId": 1002,
      "ok": false,
      "scope": "Instance",
      "msg": "Parameter 'コメント' is read-only.",
      "resolvedBy": "name:コメント"
    }
  ]
}
```

### 致命的エラー例
```json
{
  "ok": false,
  "msg": "value.storageType が必要です。String/Integer/Double/ElementId のいずれかを指定してください。",
  "stats": {
    "totalRequested": 3,
    "successCount": 0,
    "failureCount": 0
  },
  "results": []
}
```

## 備考

- 本コマンドは主に **インスタンスパラメータ** 向けですが、タイプパラメータも更新可能です。
  - タイプパラメータを更新した場合、その型を使用している他の要素にも影響する点に注意してください。
  - `results[].scope` が `"Type"` の場合、その更新はタイプに対するものです。
- `Double` の値は `UnitHelper.TrySetParameterByExternalValue` によって解釈されます。
  - 既定では SI 系（mm/m2/m3/deg）での指定を前提としています。
- `get_elements_by_category_and_level` と組み合わせることで、
  - 「3階の壁の `コメント` をすべて `外壁` に」
  - 「1階の構造フレームの shared parameter `耐火区分` をすべて `2` に」
  といったワークフローを簡潔に表現できます。

## 関連コマンド
- update_parameters_batch
- get_elements_by_category_and_level
- set_room_param
- update_level_parameter
