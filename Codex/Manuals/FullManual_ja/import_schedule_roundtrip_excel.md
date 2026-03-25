# import_schedule_roundtrip_excel

`export_schedule_roundtrip_excel` で作成した Excel を読み込み、編集内容を Revit のパラメータへ反映します。

## 入力
```json
{
  "filePath": "C:\\temp\\room_schedule_roundtrip.xlsx",
  "reportPath": "C:\\temp\\room_schedule_roundtrip_import_report.csv",
  "sheetName": "Schedule"
}
```

## 出力
```json
{
  "ok": true,
  "path": "C:\\temp\\room_schedule_roundtrip.xlsx",
  "reportPath": "C:\\temp\\room_schedule_roundtrip_import_report.csv",
  "updatedCount": 120,
  "skippedCount": 8,
  "failedCount": 0,
  "editableColumnCount": 46
}
```

## 読み込みルール
- hidden の `__ElementId` と `__revit_roundtrip_meta` を使って、行と Revit 要素を対応付けます。
- `display` モードで書き出した Excel は、1 行が複数要素に対応することがあります。その場合は、その行の編集値を対応する全インスタンスへ反映します。
- 空欄セルは安全側で `skip` します。
- Yes/No 系は以下を受け付けます。
  - `☑ / ☐`
  - `ON / OFF`
  - `TRUE / FALSE`
  - `YES / NO`
  - `はい / いいえ`
  - `1 / 0`

## レポート
- `reportPath` に CSV を出力します。
- 列:
  - `row`
  - `elementId`
  - `updated`
  - `skipped`
  - `failed`
  - `message`

## 注意
- このコマンドは `export_schedule_roundtrip_excel` の生成ファイルを前提にしています。
- `__ElementId` 列や `__revit_roundtrip_meta` シートを削除・改名すると import できません。
- 読み取り専用パラメータ列は import しても更新されません。
- `display` モードでは、代表行に編集を入れると、その表示行に含まれる全要素へ一括反映されます。
