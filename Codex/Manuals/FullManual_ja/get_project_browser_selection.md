# get_project_browser_selection

- カテゴリ: Misc
- 目的: 現在の選択を、プロジェクトブラウザ由来の種別ごとに分類して取得します。

## 概要
選択された要素IDを、以下の配列に分類して返します。
- `families`
- `familyTypes`
- `views`
- `sheets`
- `schedules`
- `others`
- `missing`

加えて、`selectionSource`（`live` / `stash`）も返します。

## 使い方
- メソッド: `get_project_browser_selection`
- パラメータ（任意）:
  - `retry.maxWaitMs` (int)
  - `retry.pollMs` (int)
  - `fallbackToStash` (bool)
  - `maxAgeMs` (int)
  - `allowCrossDoc` (bool)
  - `allowCrossView` (bool)

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_project_browser_selection",
  "params": {
    "retry": { "maxWaitMs": 1000, "pollMs": 100 },
    "fallbackToStash": true
  }
}
```

## 戻り値（例）
```jsonc
{
  "ok": true,
  "source": "live",
  "selectionSource": "live",
  "count": 2,
  "families": [],
  "familyTypes": [],
  "views": [{ "elementId": 123, "name": "1FL", "viewType": "FloorPlan" }],
  "sheets": [],
  "schedules": [],
  "others": [],
  "missing": [],
  "counts": {
    "families": 0,
    "familyTypes": 0,
    "views": 1,
    "sheets": 0,
    "schedules": 0,
    "others": 0,
    "missing": 0
  },
  "msg": "OK"
}
```

## 補足
- プロジェクトブラウザ上で要素IDを持たない行は、要素として取得できません。
- モデル選択との判別を含めて扱う場合は `get_selected_element_ids` と併用してください。

## 関連コマンド
- get_selected_element_ids
- get_element_info
