# validate_compare_context

- カテゴリ: CompareOps
- 目的: このコマンドは『validate_compare_context』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: validate_compare_context

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| requireSameProject | bool | いいえ/状況による | true |
| requireSameViewType | bool | いいえ/状況による | true |
| resolveByName | bool | いいえ/状況による | true |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "validate_compare_context",
  "params": {
    "requireSameProject": false,
    "requireSameViewType": false,
    "resolveByName": false
  }
}
```

## 関連コマンド

- 