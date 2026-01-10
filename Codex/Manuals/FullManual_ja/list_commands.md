# list_commands

- カテゴリ: MetaOps
- 目的: 現在登録されているコマンド名一覧を返します。

## 概要
日本語版は簡易説明のみです。詳細（返り値／例）は英語版を参照してください。

- 英語版: `../FullManual/list_commands.md`

Step 4 の正規名/従来名ポリシー:
- 返却される `commands[]` は **正規名（namespaced: `*.*`）** が基本です。
- `includeDeprecated=true` で、従来名（エイリアス）も含めて返せます（発見用途では deprecated 扱い）。

### パラメータ（抜粋）
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| includeDeprecated | boolean | いいえ | false |
| includeDetails | boolean | いいえ | false |
