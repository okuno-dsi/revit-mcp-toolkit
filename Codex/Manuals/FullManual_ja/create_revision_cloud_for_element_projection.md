# create_revision_cloud_for_element_projection

- カテゴリ: RevisionCloud
- 目的: このコマンドは『create_revision_cloud_for_element_projection』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_revision_cloud_for_element_projection

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| debug | bool | いいえ/状況による | false |
| elementId | unknown | いいえ/状況による |  |
| elementIds | unknown | いいえ/状況による |  |
| focusMarginMm | number | いいえ/状況による | 150.0 |
| minRectSizeMm | number | いいえ/状況による | 50.0 |
| mode | string | いいえ/状況による | obb |
| paddingMm | number | いいえ/状況による | 100.0 |
| planeSource | string | いいえ/状況による | view |
| preZoom | string | いいえ/状況による |  |
| restoreZoom | bool | いいえ/状況による | false |
| revisionId | unknown | いいえ/状況による |  |
| tagHeightMm | number | いいえ/状況による | 0.0 |
| tagWidthMm | number | いいえ/状況による | 0.0 |
| uniqueId | unknown | いいえ/状況による |  |
| viewId | unknown | いいえ/状況による |  |
| widthMm | number | いいえ/状況による | 0.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_revision_cloud_for_element_projection",
  "params": {
    "debug": false,
    "elementId": "...",
    "elementIds": "...",
    "focusMarginMm": 0.0,
    "minRectSizeMm": 0.0,
    "mode": "...",
    "paddingMm": 0.0,
    "planeSource": "...",
    "preZoom": "...",
    "restoreZoom": false,
    "revisionId": "...",
    "tagHeightMm": 0.0,
    "tagWidthMm": 0.0,
    "uniqueId": "...",
    "viewId": "...",
    "widthMm": 0.0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_default_revision
- create_revision_circle
- move_revision_cloud
- delete_revision_cloud
- update_revision
- get_revision_cloud_types
- get_revision_cloud_type_parameters
- 