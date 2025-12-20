# set_element_workset

- カテゴリ: WorksetOps
- 目的: このコマンドは『set_element_workset』を設定します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: set_element_workset

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ/状況による |  |
| worksetId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_element_workset",
  "params": {
    "elementId": 0,
    "worksetId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- get_element_workset
- create_workset
- 