# create_cable_tray

- カテゴリ: MEPOps
- 目的: このコマンドは『create_cable_tray』を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: create_cable_tray

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| heightMm | unknown | いいえ/状況による |  |
| levelId | int | いいえ/状況による |  |
| trayTypeId | int | いいえ/状況による |  |
| widthMm | unknown | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_cable_tray",
  "params": {
    "heightMm": "...",
    "levelId": 0,
    "trayTypeId": 0,
    "widthMm": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- create_pipe
- create_conduit
- get_mep_elements
- move_mep_element
- delete_mep_element
- change_mep_element_type
- get_mep_parameters
- 