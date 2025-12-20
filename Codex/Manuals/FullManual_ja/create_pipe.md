# create_pipe

- カテゴリ: MEPOps
- 目的: このコマンドは『create_pipe』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_pipe

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| diameterMm | unknown | いいえ/状況による |  |
| levelId | int | いいえ/状況による |  |
| pipeTypeId | int | いいえ/状況による |  |
| slopePermil | unknown | いいえ/状況による |  |
| systemTypeId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_pipe",
  "params": {
    "diameterMm": "...",
    "levelId": 0,
    "pipeTypeId": 0,
    "slopePermil": "...",
    "systemTypeId": 0
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