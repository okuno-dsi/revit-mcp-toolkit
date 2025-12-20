# diff_elements

- カテゴリ: AnalysisOps
- 目的: このコマンドは『diff_elements』を実行します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: diff_elements

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| endpointsTolMm | number | いいえ/状況による | 30.0 |
| includeEndpoints | bool | いいえ/状況による | true |
| posTolMm | number | いいえ/状況による | 600.0 |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "diff_elements",
  "params": {
    "endpointsTolMm": 0.0,
    "includeEndpoints": false,
    "posTolMm": 0.0
  }
}
```

## 関連コマンド
## 関連コマンド
- summarize_family_types_by_category
- check_clashes
- 