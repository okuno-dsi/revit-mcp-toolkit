# family.batch_add_parameter_from_folder

- カテゴリ: Family
- 種別: write
- リスク: high
- 概要: フォルダ内の `.rfa` を順に開き、ファミリパラメータまたは共有パラメータを安全に追加し、保存して閉じます。

## 目的
オフラインのファミリフォルダに対して、複数の `.rfa` へ一括でパラメータを追加します。

このコマンドは安全側で動作します。
- `.rfa` のみを編集します
- `.rvt` プロジェクトは編集しません
- 既定で上書き前にバックアップを作成します
- ファイル単位の JSONL 監査ログを保存します
- V1 では、既存の不一致パラメータを破壊的に置換しません

## 呼び出し
- メソッド: `family.batch_add_parameter_from_folder`

### ルート引数
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---|---|---|
| `folderPath` | string | はい |  | `.rfa` を探す対象フォルダ |
| `searchPattern` | string | いいえ | `*.rfa` | 検索パターン |
| `recursive` | bool | いいえ | `false` | サブフォルダも再帰検索するか |
| `dryRun` | bool | いいえ | `false` | 保存せずに検証だけ行うか |
| `continueOnError` | bool | いいえ | `true` | 失敗後も残りを続行するか |
| `saveMode` | string | いいえ | `overwrite` | `overwrite` または `save_as_copy` |
| `outputFolder` | string | 条件付き |  | `save_as_copy` 時に必須 |
| `createBackup` | bool | いいえ | `true` | 上書き前にバックアップ作成 |
| `backupFolder` | string | いいえ | `<folderPath>\\_backup` | バックアップ保存先 |
| `closeWithoutSaveOnNoChange` | bool | いいえ | `true` | 変更なし時に保存せず閉じる |
| `defaultSharedParameterFile` | string | いいえ |  | 共有パラメータファイルの既定値 |
| `defaultSharedParameterGroupName` | string | いいえ | `Common` | 共有パラメータ定義グループの既定値 |
| `parameters` | array | はい |  | 追加するパラメータ定義一覧 |

### `parameters[]`
| 名前 | 型 | 必須 | 説明 |
|---|---|---|---|
| `parameterMode` | string | はい | `shared` または `family` |
| `parameterName` | string | はい | 追加対象のパラメータ名 |
| `parameterGroup` | string | いいえ | Revit のパラメータグループ。既定は `PG_DATA` |
| `isInstance` | bool | いいえ | 既定は `true` |
| `onExists` | string | いいえ | `skip` または `error`。既定は `skip` |
| `sharedParameterFile` | string | shared 時 | 共有パラメータファイル。`sharedParametersFile` でも可 |
| `sharedParameterGroupName` | string | shared 時 | 共有パラメータファイル内のグループ名 |
| `sharedParameterDefinitionName` | string | shared 時 | 定義名を `parameterName` と別にしたい場合 |
| `sharedParameterGuid` | string | shared 時推奨 | GUID 優先照合を行います。`guid` / `sharedGuid` も可 |
| `familySpecType` | string | family 時 | `parameterMode=family` のとき必須 |

## 共有パラメータファイルに関する注意
- Revit が読める共有パラメータファイルを使用してください
- 日本語を含む場合は、実運用上 `UTF-16 LE with BOM` が最も安全です
- ファイルを開けない場合は、各ファイル結果に次のような失敗が出ます
  - `Shared parameter file could not be opened. Error in readParamDatabase`

## 安全動作
- 一致する既存パラメータは `skip`
- 不一致の既存パラメータは V1 では破壊的置換しない
- 共有パラメータは GUID 優先で照合
- 例外時でもファミリドキュメントは安全に close
- `createBackup=true` の場合は上書き前にバックアップ作成

## リクエスト例

### 共有パラメータ追加の dry-run
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "family.batch_add_parameter_from_folder",
  "params": {
    "folderPath": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\Family",
    "searchPattern": "*.rfa",
    "recursive": false,
    "dryRun": true,
    "saveMode": "overwrite",
    "defaultSharedParameterFile": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\family_batch_test_shared_params.txt",
    "defaultSharedParameterGroupName": "Common",
    "parameters": [
      {
        "parameterMode": "shared",
        "parameterName": "ファミリパラメータ追加確認",
        "parameterGroup": "PG_DATA",
        "isInstance": true,
        "onExists": "skip",
        "sharedParameterGuid": "1e3103c5-868d-47f1-b347-0f4669cc3b7d"
      }
    ]
  }
}
```

### 上書き保存 + バックアップあり
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "family.batch_add_parameter_from_folder",
  "params": {
    "folderPath": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\Family",
    "searchPattern": "*.rfa",
    "recursive": false,
    "dryRun": false,
    "continueOnError": true,
    "saveMode": "overwrite",
    "createBackup": true,
    "backupFolder": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\Family__backup",
    "defaultSharedParameterFile": "C:\\Users\\<you>\\Documents\\Revit_MCP\\Samples\\family_batch_test_shared_params.txt",
    "defaultSharedParameterGroupName": "Common",
    "parameters": [
      {
        "parameterMode": "shared",
        "parameterName": "ファミリパラメータ追加確認",
        "parameterGroup": "PG_DATA",
        "isInstance": true,
        "onExists": "skip",
        "sharedParameterGuid": "1e3103c5-868d-47f1-b347-0f4669cc3b7d"
      }
    ]
  }
}
```

## 戻り値
主な返却項目:
- `processed`
- `succeeded`
- `failed`
- `skipped`
- `dryRun`
- `logPath`
- `items[]`（ファイルごとの結果）

各ファイル結果には次が含まれます。
- `filePath`
- `ok`
- `action`
- `saved`
- `savePath`
- `backupPath`
- `addedCount`
- `skippedCount`
- `failedCount`
- `elapsedMs`
- `messages[]`
- `parameterResults[]`

## ログ保存先
- `%USERPROFILE%\\Documents\\Revit_MCP\\Logs\\family.batch_add_parameter_from_folder\\*.jsonl`

## 関連コマンド
- `family.query_loaded`
- `set_family_type_parameter`
- `update_family_instance_parameter`
