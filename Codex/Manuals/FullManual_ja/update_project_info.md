# update_project_info（プロジェクト情報の更新）

Revit の「プロジェクト情報（Project Information）」要素の文字列フィールドを安全に更新します。

- メソッド: `update_project_info`
- カテゴリ: Bootstrap/Project
- 種別: write

パラメータ（すべて任意・文字列）
- `projectName`（プロジェクト名）
- `projectNumber`（プロジェクト番号）
- `clientName`（施主名 など）
- `status`（ステータス）
- `issueDate`（発行日 等）
- `address`（住所）

注意事項
- 書き込み可能な文字列パラメータのみ更新します。存在しない／読み取り専用の項目はスキップします。
- 1トランザクションでまとめて更新しますが、設定できた項目のみ確定します。
- 対象はアクティブドキュメントの ProjectInformation 要素です。

使用例
```powershell
# PowerShell（直接）
pwsh -File Manuals/Scripts/send_revit_command_durable.py `
  --port 5211 --command update_project_info `
  --params '{"projectName":"テストBIMモデル","projectNumber":"P-001"}'
```

```bash
# Python（直接）
python Manuals/Scripts/send_revit_command_durable.py \
  --port 5211 \
  --command update_project_info \
  --params '{"clientName":"○○建設","status":"基本設計"}'
```

応答
```json
{ "ok": true, "updated": 2 }
```

トラブルシュート
- `Unknown command: update_project_info` が出る場合は、Add-in を更新して Revit を再起動（コマンド登録の再読み込み）してください。
- 作業共有や権限により項目がロックされていると `updated` が 0 の場合があります。

