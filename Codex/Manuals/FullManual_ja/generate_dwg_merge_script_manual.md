# generate_dwg_merge_script_manual

- カテゴリ: DxfOps
- 目的: このコマンドは『generate_dwg_merge_script_manual』を生成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: generate_dwg_merge_script_manual

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| fileName | string | いいえ/状況による |  |
| outPath | string | いいえ/状況による |  |
| returnContent | bool | いいえ/状況による | false |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "generate_dwg_merge_script_manual",
  "params": {
    "fileName": "...",
    "outPath": "...",
    "returnContent": false
  }
}
```

## 関連コマンド
## 関連コマンド
- get_grids_with_bubbles
- export_curves_to_dxf
- 