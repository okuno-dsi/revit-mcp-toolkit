# create_duct

- カテゴリ: MEPOps
- 目的: このコマンドは『create_duct』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_duct

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| diameterMm | unknown | いいえ/状況による |  |
| ductTypeId | int | いいえ/状況による |  |
| heightMm | unknown | いいえ/状況による |  |
| levelId | int | いいえ/状況による |  |
| systemTypeId | int | いいえ/状況による |  |
| widthMm | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_duct",
  "params": {
    "diameterMm": "...",
    "ductTypeId": 0,
    "heightMm": "...",
    "levelId": 0,
    "systemTypeId": 0,
    "widthMm": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- create_cable_tray
- create_conduit
- get_mep_elements
- move_mep_element
- delete_mep_element
- change_mep_element_type
- get_mep_parameters
- 