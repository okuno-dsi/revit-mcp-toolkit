# move_mep_element

- カテゴリ: MEPOps
- 目的: このコマンドは『move_mep_element』を移動します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: move_mep_element

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dx | number | いいえ/状況による |  |
| dy | number | いいえ/状況による |  |
| dz | number | いいえ/状況による |  |
| elementId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "move_mep_element",
  "params": {
    "dx": 0.0,
    "dy": 0.0,
    "dz": 0.0,
    "elementId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- create_pipe
- create_cable_tray
- create_conduit
- get_mep_elements
- delete_mep_element
- change_mep_element_type
- get_mep_parameters
- 