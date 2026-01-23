# get_spatial_params_bulk

- カテゴリ: Spatial
- 目的: Room / Space / Area のパラメータをまとめて取得します。

## 概要
Room / Space / Area のパラメータを一括取得するコマンドです。要素数とパラメータ数の両方でページングできます。

- `kind` / `kinds` が未指定の場合は `room` が既定です。
- `elementIds` が空の場合は、指定した kind を **全件取得** します（または `all=true`）。
- `paramSkip` / `paramCount` で、1要素あたりのパラメータ件数を絞れます。

## 使い方
- メソッド: get_spatial_params_bulk

### パラメータ
| 名前 | 型 | 必須 | 説明 |
|---|---|---|---|
| kind | string | いいえ | `room` / `space` / `area` のいずれか |
| kinds | string[] | いいえ | 複数指定（例: `["room","space"]`） |
| elementIds | int[] | いいえ | 対象要素ID |
| all | bool | いいえ | kind 全件取得 |
| elementSkip | int | いいえ | 要素ページングのスキップ |
| elementCount | int | いいえ | 要素ページングの件数 |
| paramSkip | int | いいえ | パラメータのスキップ |
| paramCount | int | いいえ | パラメータの件数 |
| unitsMode | string | いいえ | `SI` / `Project` / `Raw` / `Both`（既定: `SI`） |

補足:
- `skip` / `count` は `elementSkip` / `elementCount` の別名として使えます。
- `unitsMode` は `parameters[]` の値表現に反映されます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_spatial_params_bulk",
  "params": {
    "kind": "room",
    "all": true,
    "elementSkip": 0,
    "elementCount": 50,
    "paramSkip": 0,
    "paramCount": 30,
    "unitsMode": "SI"
  }
}
```

## レスポンス
```jsonc
{
  "ok": true,
  "totalCount": 120,
  "elementSkip": 0,
  "elementCount": 50,
  "paramSkip": 0,
  "paramCount": 30,
  "units": { "Length": "mm", "Area": "m2", "Volume": "m3", "Angle": "deg" },
  "mode": "SI",
  "items": [
    {
      "kind": "room",
      "elementId": 6808338,
      "uniqueId": "f7b1d8a1-...-000a5f",
      "name": "事務室2-1",
      "levelId": 12345,
      "levelName": "2FL",
      "totalParams": 86,
      "parameters": [
        {
          "name": "面積",
          "id": 123456,
          "storageType": "Double",
          "isReadOnly": true,
          "dataType": "autodesk.spec.aec:area-2.0.0",
          "value": 23.45,
          "unit": "m2",
          "display": "23.45 m²",
          "raw": 252.48
        }
      ]
    }
  ]
}
```

## 関連
- spatial.suggest_params
- get_room_params
- get_space_params
- get_area_params
