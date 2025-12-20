# compare_projects_summary

- カテゴリ: CompareOps
- 目的: このコマンドは『compare_projects_summary』を比較します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: compare_projects_summary

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| baselineIndex | int | いいえ/状況による | 0 |
| endpointsTolMm | number | いいえ/状況による | 30.0 |
| includeEndpoints | bool | いいえ/状況による | true |
| ok | bool | いいえ/状況による |  |
| posTolMm | number | いいえ/状況による | 600.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "compare_projects_summary",
  "params": {
    "baselineIndex": 0,
    "endpointsTolMm": 0.0,
    "includeEndpoints": false,
    "ok": false,
    "posTolMm": 0.0
  }
}
```

## 関連コマンド

- 