## PowerShell Execution Policy (Windows)

一部の環境では、`*.ps1` スクリプトが未署名だと既定ポリシーで実行がブロックされます（"is not digitally signed"）。

重要: 実行時だけ一時的に回避してください。

- プロセス限定（現在の PowerShell セッションのみ）
  - `powershell -ExecutionPolicy Bypass` で起動、または実行時に `-ExecutionPolicy Bypass` を付与
  - 例:
    - `pwsh -ExecutionPolicy Bypass -File Codex/Scripts/Reference/test_connection.ps1 -Port 5210`
    - `pwsh -ExecutionPolicy Bypass -File Codex/Scripts/Reference/export_walls_by_type_simple.ps1 -Port 5210`
- セッション内で一時設定（終了で自動復帰）
  - `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`

注意
- マシン/ユーザースコープでの恒久変更（`-Scope LocalMachine`, `-Scope CurrentUser`）は推奨しません。必要時は管理者と合意のうえで実施してください。

参考: Microsoft Docs "about_Execution_Policies"




