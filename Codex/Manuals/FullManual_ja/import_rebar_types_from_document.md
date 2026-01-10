# import_rebar_types_from_document

- カテゴリ: Rebar
- 目的: 別の `.rvt/.rte`（ドナー）から、`RebarBarType` / `RebarHookType` / `RebarShape` をアクティブドキュメントへ取り込みます。

## 概要
意匠モデル等では、プロジェクトに **鉄筋タイプや鉄筋形状が一切存在しない（0件）** ことがあります。  
この状態だと、配筋系コマンドは `BAR_TYPE_NOT_FOUND` 等で失敗します。

本コマンドは、配筋の前提となる「鉄筋タイプ（必要なら形状・フック）」を取り込むためのユーティリティです。

## 使い方
- Method: `import_rebar_types_from_document`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| dryRun | bool | no | true | true の場合はドナーを開いて件数のみ確認し、モデルは変更しません。 |
| sourcePath | string | no |  | ドナー `.rvt/.rte` のパス。省略時は既知のテンプレートを順に探索（best-effort）。 |
| includeHookTypes | bool | no | true | `RebarHookType` も取り込みます。 |
| includeShapes | bool | no | false | `RebarShape` を取り込みます（プロジェクト側が 0 件なら推奨）。 |
| diametersMm | int[] | no |  | `RebarBarType` を径(mm)でフィルタして取り込みます（形状・フックはフィルタしません）。 |

### dryRun 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "import_rebar_types_from_document",
  "params": {
    "dryRun": true,
    "sourcePath": "C:/ProgramData/Autodesk/RVT 2024/Templates/Japanese/Structural Analysis-DefaultJPNJPN.rte",
    "includeHookTypes": true,
    "includeShapes": true
  }
}
```

### 取り込み実行例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "import_rebar_types_from_document",
  "params": {
    "dryRun": false,
    "sourcePath": "C:/ProgramData/Autodesk/RVT 2024/Templates/Japanese/Structural Analysis-DefaultJPNJPN.rte",
    "includeHookTypes": true,
    "includeShapes": true
  }
}
```

## 注意
- コピーは `CopyElements` で行い、重複名がある場合は「宛先を優先（Use destination types）」で処理します。
- 取り込み後、`list_rebar_bar_types` で件数が増えていることを確認してから、`rebar_plan_auto` / `rebar_regenerate_delete_recreate` を実行してください。

