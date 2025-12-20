# get_mep_parameters

- カテゴリ: MEPOps
- 目的: このコマンドは『get_mep_parameters』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_mep_parameters

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| count | int | いいえ/状況による |  |
| elementId | unknown | いいえ/状況による |  |
| includeDisplay | bool | いいえ/状況による | true |
| includeRaw | bool | いいえ/状況による | true |
| includeUnit | bool | いいえ/状況による | true |
| namesOnly | bool | いいえ/状況による | false |
| siDigits | int | いいえ/状況による | 3 |
| skip | int | いいえ/状況による | 0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_mep_parameters",
  "params": {
    "count": 0,
    "elementId": "...",
    "includeDisplay": false,
    "includeRaw": false,
    "includeUnit": false,
    "namesOnly": false,
    "siDigits": 0,
    "skip": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_pipe
- create_cable_tray
- create_conduit
- get_mep_elements
- move_mep_element
- delete_mep_element
- change_mep_element_type
- 