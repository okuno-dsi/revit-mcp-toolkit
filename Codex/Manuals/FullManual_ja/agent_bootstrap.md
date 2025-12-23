# agent_bootstrap

- カテゴリ: MetaOps
- 目的: 起動直後の初期コンテキストをまとめて取得する（ウォームアップ用）

## 概要
`agent_bootstrap` は、Revit プロセス/アクティブドキュメント/アクティブビュー/単位など、エージェントが最初に必要とする情報をまとめて返します。

- メソッド: `agent_bootstrap`
- パラメータ: なし

主な戻り値:
- `server`: Revit プロセス情報（`product`, `process.pid`）
- `project`: 互換用のプロジェクト/ドキュメント情報（name, number, filePath など）
- `environment`: 互換用の環境情報（units, activeViewId, activeViewName）
- `document`: 新規クライアント向けの統合コンテキスト（`project` + `environment` を統合）
- `commands`: よく使うコマンドのリストなど（`hot`）
- `policies`: エージェント向けの運用ポリシー（例: `idFirst`）
- `knownErrors`: よくあるエラーコードのヒント

既存クライアントは `project.*` / `environment.*` を読み続けても動作し、新規クライアントは `document.*` を優先することを推奨します。

## 用語（任意）
`term_map_ja.json` が利用できる場合、`terminology` が追加されます。
- `term_map_version`
- `defaults`（例: `断面 => SECTION_VERTICAL (create_section)`）
- `disambiguation`（例: `SECTION_vs_PLAN: prefer SECTION_VERTICAL; override PLAN if 平断面/…`）

## リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "agent_bootstrap",
  "params": {}
}
```

## 関連
- help.get_context
- help.search_commands
- help.describe_command
- start_command_logging
- stop_command_logging
