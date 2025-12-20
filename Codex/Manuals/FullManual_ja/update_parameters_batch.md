# update_parameters_batch

- カテゴリ: ParamOps
- 目的: このコマンドは『update_parameters_batch』を更新します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: update_parameters_batch

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| batchSize | int | いいえ/状況による |  |
| maxMillisPerTx | int | いいえ/状況による | 2500 |
| startIndex | int | いいえ/状況による | 0 |
| suppressItems | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "update_parameters_batch",
  "params": {
    "batchSize": 0,
    "maxMillisPerTx": 0,
    "startIndex": 0,
    "suppressItems": false
  }
}
```

## 関連コマンド
## 関連コマンド
- get_param_meta
- get_parameter_identity
- get_type_parameters_bulk
- get_instance_parameters_bulk
- 