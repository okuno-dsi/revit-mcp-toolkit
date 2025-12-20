# Revit 複数プロジェクト差異検出 手順（検出のみ）

本書は、Revit MCP Add-in を用いて複数プロジェクト（例: ポート 5210/5211）のモデル差異を検出するための手順をまとめたものです。可視化（着色・雲マーク）は範囲外です。差異の抽出に特化した安定手順のみ掲載します。

## 対象・前提
- 対象: Revit 2023+／MCP Add-in 稼働中の複数インスタンス（例: 5210/5211）
- 検出対象: 構造フレーム（-2001320）、構造柱（-2001330）を例示
- ビュー: RSL1（平面）を例に説明（任意ビューで可）
- いずれの方法でも、「検出のみ」を実施します（可視化は実施しません）

## 1) 実行前の確認（必須）
1. 各ポートで Add-in が応答することを確認
   - `agent_bootstrap` を呼び、`project.filePath` と `environment.activeViewName` を確認
   - 例: `POST http://localhost:5210/enqueue {"method":"agent_bootstrap"}`
2. ビューの整合
   - `get_views(includeTemplates=false, detail=true)` で `RSL1` の `viewId` を取得
   - 5210/5211 それぞれで `RSL1` が存在し、比較対象として妥当かを確認
3.（任意）コンテキスト検証
   - `validate_compare_context`（実装がある場合）で、ポート・プロジェクト・ビューの一致条件を機械的に検証

## 2) スナップショット取得（標準）
- API: `snapshot_view_elements`
- 推奨パラメータ:
  - `viewId`: RSL1 の viewId
  - `categoryIds`: `[-2001320, -2001330]`
  - `includeAnalytic`: `true`（線要素端点 `analytic.wire` を含む）
  - `includeHidden`: `false`

例（抜粋）:
```
{"jsonrpc":"2.0","method":"snapshot_view_elements","params":{
  "viewId": 5133620,
  "categoryIds": [-2001320,-2001330],
  "includeAnalytic": true,
  "includeHidden": false
}}
```

注意:
- 返却の `elements` が空のときは、RPC 包みレイヤや可視性の影響を疑う。`get_elements_in_view(idsOnly)` のフォールバック収集も検討。（後述）

## 3) 差異検出パス（2 種）

### パス A: サマリ比較（compare_projects_summary）
概要: 2 プロジェクトのスナップショットをサマリ的に比較し、変更比率と件数を得る。検出キーは既定で型名中心。微細な幾何差やパラメータ差は落ちる場合があるため、必要に応じてパス B で深掘りする。

- API: `compare_projects_summary`
- 典型パラメータ:
  - `projects`: `[{ port:5210, viewId:... }, { port:5211, viewId:... }]`
  - `categories`: `[-2001320,-2001330]`
  - `includeEndpoints`: `true`
  - `endpointsTolMm`: `0`（端点は厳密比較）
  - `numericEpsilon`: `0`（数値同一性の厳密判定）
- 出力: `baseline`, `items[]`（`total`, `modifiedPairs`, `leftOnly`, `rightOnly`, `ratio`, `nameChanged` など）

注意:
- この API は「型差」中心。座標・bbox・型パラメータ差を落とす可能性があるため、「差分あるはずが 0」の場合はパス B で深比較する。

### パス B: 深比較（推奨フォールバック）
概要: ビュー内要素 ID を取得し、両ポートから同一 ID 範囲の詳細を取得して、型・座標・bbox（必要に応じて端点）などを JToken ベースで比較。確実性が高い。

手順:
1. 要素 ID 収集
   - API: `get_elements_in_view`
   - パラメータ: `{ viewId, categoryIds, _shape: { idsOnly: true } }`
   - 5210 と 5211 で取得し、「共通 ID」のみを抽出（`elementId`の交差）
2. 詳細情報取得
   - API: `get_element_info`
   - パラメータ: `{ elementIds: [...], rich: true }`
   - 200 件未満でも 50〜200 件のチャンクに分割して取得（タイムアウト回避）
3. 比較キー（例）
   - 型: `familyName`, `typeName`, `typeId`
   - 幾何: `coordinatesMm`（重心）, `bboxMm`（境界箱）
   - 線端点: `analytic.wire`（A/B の 2 端点）
4. 比較方法
   - JSON 圧縮文字列（`ConvertTo-Json -Compress` 等）で値を比較、または `DeepJsonComparer` のような比較器を利用
   - 数値の許容値（mm）を設けたい場合は、比較前に丸める、または比較器に `NumericEpsilon` を設定
5. 出力
   - 差分件数とサンプルの一覧（`id`, `key`, `left`, `right`）

利点:
- サマリ比較が取りこぼす「座標・bbox の微差」や「型パラメータ」も検出可能

## 4) 代表的な JSON-RPC 例

- RSL1 の viewId 取得:
```
{"jsonrpc":"2.0","method":"get_views","params":{"includeTemplates":false,"detail":true}}
```

- ビュー内要素 ID 取得（idsOnly）:
```
{"jsonrpc":"2.0","method":"get_elements_in_view","params":{
  "viewId": 5133620,
  "categoryIds": [-2001320,-2001330],
  "_shape": { "idsOnly": true }
}}
```

- 要素詳細（チャンク）:
```
{"jsonrpc":"2.0","method":"get_element_info","params":{
  "elementIds": [5089509,5089512,...],
  "rich": true
}}
```

- サマリ比較:
```
{"jsonrpc":"2.0","method":"compare_projects_summary","params":{
  "projects": [
    {"source":"rpc","port":5210,"viewId":5133620},
    {"source":"rpc","port":5211,"viewId":5133620}
  ],
  "categories": [-2001320,-2001330],
  "includeEndpoints": true,
  "endpointsTolMm": 0,
  "numericEpsilon": 0
}}
```

## 5) よくある落とし穴と対処
- ビュー／プロジェクト不一致
  - `agent_bootstrap` と `get_views` で対象ビューの一致を必ず確認
- カテゴリ／可視性
  - テンプレートや表示設定により `snapshot_view_elements` が空になる場合、`get_elements_in_view` の `idsOnly` で ID 収集 → `get_element_info` で詳細取得
- タイムアウト
  - 要素取得は 50〜200 件のチャンクに分ける。重い処理は片ポートずつ実施
- ポート混線
  - 各 RPC の前に `agent_bootstrap` で `process.pid` と `project.filePath` を確認。必要に応じて `validate_compare_context` を活用

## 6) 推奨運用フロー（検出のみ）
1. 5210/5211 の RSL1 を確認（`get_views`）
2. 両ポートで `snapshot_view_elements`
3. まず `compare_projects_summary` でサマリ比較
4. 差分が 0 だが違和感がある場合は、パス B（深比較）を実施
5. 差分リスト（id, key, left/right）を保存してレビュー

---
補足:
- 本書は差異「検出」のみを扱います。可視化（着色・雲マーク作成）については現時点では仕様確定前のため記載していません。

