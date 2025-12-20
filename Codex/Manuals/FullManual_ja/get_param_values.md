# get_param_values

- カテゴリ: ParamOps
- 目的: このコマンドは『get_param_values』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_param_values

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| includeMeta | bool | いいえ/状況による | true |
| mode | string | いいえ/状況による | element |
| scope | string | いいえ/状況による | auto |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_param_values",
  "params": {
    "includeMeta": false,
    "mode": "...",
    "scope": "..."
  }
}
```

## 関連コマンド
## 関連コマンド
- get_parameter_identity
- get_type_parameters_bulk
- get_instance_parameters_bulk
- update_parameters_batch
- 