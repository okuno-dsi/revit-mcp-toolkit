# RevitMCP 協働チャット（クイックガイド）

Revit の作業共有チーム向けに、**人間が主役**の協働チャット（ログが正）を追加します。Codex GUI へは `Copy→Codex` で **手動**で引き渡します。

## 保存場所（共有・正）
- ルート: `<中央モデルのフォルダ>\\_RevitMCP\\projects\\<docKey>\\`
- チャットログ: `_RevitMCP\\projects\\<docKey>\\chat\\writers\\*.jsonl`（書き手ごとの append-only JSONL）
- クラウドモデル（ACC/BIM 360）は保存先を解決できないためチャットを無効化します。
  - `docKey` は **プロジェクト固有の安定ID**（ViewWorkspace/Ledger の ProjectToken と同等）です。
  - 同一フォルダ内に複数の `.rvt` がある場合でもログが混ざらないよう、`docKey` で分離します。
  - `docKey` が不明な場合は `projects\\path-<hash>\\...` にフォールバックします（暫定）。

## サーバー側（キュー不要）の RPC
これらは **DurableQueue に enqueue しません**（Revit がビジーでも動きます）。

- `chat.post`
- `chat.list`
- `chat.inbox.list`（現状: `@userId` へのメンションを抽出）

重要: 最初に `docPathHint`（中央モデルのパス推奨）と `docKey`（推奨）を 1回渡して、サーバーが保存ルートを確定できる必要があります。Revit Add-in は ViewActivated のタイミングで自動初期化します。

## Revit Add-in UI（AIを使わないメンバーも利用）
- リボン: `RevitMCPServer` タブ → `GUI` パネル → `Chat`
- 招待: `Invite` ボタン → ユーザーID（Revit Username想定）を入力 → `ws://Project/Invites` へ投稿
- 受信: 自分宛の `@userId` を検知すると、Revit作業を妨げないトースト通知を表示します

## Codex GUI（Chat Monitor）
- Codex GUI 上部の `Chat` ボタン → Chat Monitor（`chat.list` をポーリング）
- 重要（コンプライアンス）: Chat Monitor は **AIを自動実行しません**。また、AIの結果をチャットへ自動投稿（`chat.post`）もしません。
- ショートカット: 対象メッセージを選択 → `Copy→Codex`
  - メッセージ本文をクリップボードにコピーし、Codex GUI の入力欄へ貼り付けます（**実行はしません**）。
  - 実行する場合は、ユーザーが Codex GUI 側で `Send` を押してください。
- `Copy` / `Copy All` でチャット内容のコピーも可能です。

## 最小 JSON-RPC 例
投稿:
```json
POST /rpc/chat.post
{ "jsonrpc":"2.0","id":"1","params":{
  "docPathHint":"\\\\Server\\ProjectA\\Central.rvt",
  "docKey":"8d5bcfd8-1222-46da-b13b-23336aae66c5",
  "channel":"ws://Project/General",
  "text":"こんにちは @userB",
  "type":"note",
  "actor":{"type":"human","id":"userA","name":"User A"}
}}
```

一覧:
```json
POST /rpc/chat.list
{ "jsonrpc":"2.0","id":"1","params":{
  "docPathHint":"\\\\Server\\ProjectA\\Central.rvt",
  "docKey":"8d5bcfd8-1222-46da-b13b-23336aae66c5",
  "channel":"ws://Project/General",
  "limit":100
}}
```

受信（メンション）:
```json
POST /rpc/chat.inbox.list
{ "jsonrpc":"2.0","id":"1","params":{
  "docPathHint":"\\\\Server\\ProjectA\\Central.rvt",
  "docKey":"8d5bcfd8-1222-46da-b13b-23336aae66c5",
  "channel":"ws://Project/Invites",
  "userId":"userB",
  "limit":50
}}
```
