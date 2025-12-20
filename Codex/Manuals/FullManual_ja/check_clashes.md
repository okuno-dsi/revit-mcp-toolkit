# check_clashes

- カテゴリ: AnalysisOps
- 目的: このコマンドは『check_clashes』を検査します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: check_clashes

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| maxPairs | int | いいえ/状況による | 100000 |
| method | string | いいえ/状況による | bbox |
| minVolumeMm3 | number | いいえ/状況による | 0.0 |
| namesOnly | bool | いいえ/状況による | false |
| toleranceMm | number | いいえ/状況による | 0.0 |
| viewId | int | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "check_clashes",
  "params": {
    "maxPairs": 0,
    "method": "...",
    "minVolumeMm3": 0.0,
    "namesOnly": false,
    "toleranceMm": 0.0,
    "viewId": 0
  }
}
```

## 関連コマンド
## 関連コマンド
- summarize_family_types_by_category
- diff_elements
- 