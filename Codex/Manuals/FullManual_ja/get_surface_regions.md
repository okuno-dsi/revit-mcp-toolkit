# get_surface_regions

- カテゴリ: SurfaceOps
- 目的: このコマンドは『get_surface_regions』を取得します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: get_surface_regions

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| dir | string | いいえ/状況による |  |
| hostKind | string | いいえ/状況による | auto |
| includePainted | bool | いいえ/状況による | true |
| includeUnpainted | bool | いいえ/状況による | true |
| side | string | いいえ/状況による |  |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_surface_regions",
  "params": {
    "dir": "...",
    "hostKind": "...",
    "includePainted": false,
    "includeUnpainted": false,
    "side": "..."
  }
}
```