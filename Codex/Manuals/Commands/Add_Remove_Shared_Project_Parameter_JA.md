# 共有プロジェクトパラメータの追加・削除（RevitMCP）

目的
- Revit プロジェクトに共有パラメータを安全にバインド（追加）／既存のプロジェクトパラメータバインドを解除（削除）するためのコマンド仕様と使用例をまとめます。
- AI/自動化から扱いやすい JSON-RPC 形式で、明確なエラーメッセージを返します。

対象バージョン
- Revit 2023+（実装は Revit 2024 API に合わせて `SpecTypeId`/`ForgeTypeId` を使用）

---

## add_shared_project_parameter
共有パラメータ定義をプロジェクトにバインドします（インスタンス/タイプ、カテゴリセット、表示グループを指定）。

- method: `add_shared_project_parameter`
- params:
  - `name` (string, 必須): 共有パラメータ名（共有パラメータファイル上の定義名）
  - `guid` (string, 任意): 共有パラメータ GUID。指定があれば一致する定義を使用／なければ新規作成時に付与
  - `groupName` (string, 必須): 共有パラメータファイル（.txt）のグループ名（例: `Daiken_Rooms`）
  - `parameterType` (string, 必須): 候補 `Text|YesNo|Length|Area|Volume`（適合しない場合は Text で作成）
  - `parameterGroup` (string, 必須): `BuiltInParameterGroup` の文字列（例: `PG_TEXT`）
  - `isInstance` (bool, 必須): `true`=インスタンス, `false`=タイプ
  - `categories` (string[], 必須): バインド対象カテゴリ名配列（例: `Rooms`, `Walls`）
  - `createDefinitionIfMissing` (bool, 既定: true): 同名 GUID 一致の定義が無い場合に新規作成可とする

- 正常応答 例
```json
{
  "ok": true,
  "result": {
    "name": "Room_AirConditioningTarget",
    "guid": "00000000-0000-0000-0000-000000000000",
    "groupName": "Daiken_Rooms",
    "parameterGroup": "PG_TEXT",
    "isInstance": true,
    "categoriesBound": ["Rooms","Spaces"],
    "categoriesSkipped": []
  }
}
```

- 代表的なエラー
  - 共有パラメータファイル未設定/オープン不可
  - カテゴリ名解決エラー（全て無効で CategorySet が空）
  - Insert/ReInsert 失敗（同名異定義競合など）
  - いずれも `{"ok":false,"msg":"...","detail":{...}}` を返します

実装メモ
- Revit 2024 API に合わせて `ExternalDefinitionCreationOptions(name, ForgeTypeId)` を使用しています。
  - `parameterType` は `Text|YesNo|Length|Area|Volume` を `SpecTypeId` にリフレクションで解決（未知は `SpecTypeId.String.Text`）。
- `parameterGroup` は `BuiltInParameterGroup` 文字列で受け取り（例: `PG_TEXT`）。
- カテゴリは `doc.Settings.Categories.get_Item(name)` で名前解決。存在しないものは `categoriesSkipped` に記録。

---

## remove_project_parameter_binding
既存のプロジェクトパラメータのバインドを解除します（共有/非共有を問わず BindingMap から除去）。

- method: `remove_project_parameter_binding`
- params:
  - `match` (object): `name` と `guid` の任意組み合わせ
  - `matchMode` (string, 既定: `name_and_guid`): `guid_only | name_only | name_and_guid`

- 正常応答 例
```json
{
  "ok": true,
  "result": {
    "removedCount": 1,
    "matchedDefinitions": [
      {"name":"Room_AirConditioningTarget","guid":"000...","parameterGroup":"PG_TEXT"}
    ]
  }
}
```

- 一致無しエラー
```json
{
  "ok": false,
  "msg": "No matching project parameter binding found.",
  "detail": {"removedCount":0,"matchMode":"name_and_guid","targetName":"...","targetGuid":"..."}
}
```

---

## よくある注意点 / ベストプラクティス
- 共有パラメータの GUID は「共有パラメータ（ExternalDefinition）」にのみ存在します。Built-in / プロジェクト / ファミリパラメータには GUID はありません。
- 共有パラメータファイル（.txt）が未設定だと作成・バインドができません。Revit の「共有パラメータ」設定を確認してください。
- カテゴリ名は UI 表示名に依存します。ローカライズ差異がある環境では解決に失敗する場合があるため、実際の `doc.Settings.Categories` 名称に合わせてください。
- 既存定義との競合（同名・別 GUID）で Insert 失敗する場合があります。ReInsert で上書きを試み、それでも不可なら明示的に既存の解除 → 追加をご検討ください。

---

## GUID/値/表示の取得・Excel 出力（関連ランブック）
- 共有パラメータ GUID を含めて確実に取得・保存する手順は、次のランブックを参照してください。
  - `Manuals/Runbooks/Export_Selected_Parameters_With_GUID_to_Excel_JA.md`
- 概要:
  - Bulk で値/表示を高速取得 → `get_parameter_identity` で共有パラメータの GUID を個別解決 → `isShared=True かつ GUID 空` を抽出し再照会 → 最終 CSV/XLSX にマージ
  - 失敗時は 20 件程度に分割し `--wait-seconds`/`--timeout-sec` を調整しリトライ

