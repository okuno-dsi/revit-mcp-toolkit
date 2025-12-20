# change_mep_element_type

- カテゴリ: MEPOps
- 目的: このコマンドは『change_mep_element_type』を変更します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: change_mep_element_type

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による |  |
| newTypeId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "change_mep_element_type",
  "params": {
    "elementId": 0,
    "newTypeId": 0
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
- get_mep_parameters
- 