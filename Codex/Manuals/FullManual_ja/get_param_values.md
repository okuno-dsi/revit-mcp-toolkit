# get_param_values

- カテゴリ: ParamOps
- 目的: このコマンドは『get_param_values』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_param_values

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| includeMeta | bool | いいえ | true | spec や readOnly などのメタ情報も返す |
| mode | string | いいえ | element | `element` / `type` / `category` |
| scope | string | いいえ | auto | `auto` / `instance` / `type` |
| docGuid | string | いいえ |  | 対象文書の `docGuid` / `docKey` |
| docTitle | string | いいえ |  | 対象文書タイトル |
| docPath | string | いいえ |  | 対象文書フルパス |

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

## 補足
- `docGuid` / `docTitle` / `docPath` を指定すると、非アクティブ文書からの読取に使えます。
- 同じ文書ヒントは `meta.extensions` に入れても解決されます。
- `mode=element` では `elementId`、`mode=type` では `typeId`、`mode=category` では `category` が別途必要です。
