# list_dwg_export_setups

- カテゴリ: Export
- 目的: プロジェクトに保存されている DWG/DXF 出力設定の一覧を取得します。

## 概要
- Revit プロジェクト内の保存済み DWG 出力設定名を列挙します。
- `detail=true` を指定すると、各設定の簡易サマリも返します。
- このコマンドは設定の読取専用です。

## メソッド
- `list_dwg_export_setups`

## パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| detail | bool | いいえ | false | `true` の場合、各設定のファイルバージョン、色モード、レイヤ数などの要約を返します。 |

## レスポンス
- `activeName`: 現在のアクティブ設定名
- `setups[]`: 設定一覧
  - `name`
  - `isActive`
  - `summary` (`detail=true` の場合のみ)

## 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "list_dwg_export_setups",
  "params": {
    "detail": true
  }
}
```

## 用途
- プロジェクト作成者がどの DWG 出力設定名を保存しているか確認したい
- どの設定が現在アクティブか知りたい
- 詳細取得の前に設定名を確認したい
