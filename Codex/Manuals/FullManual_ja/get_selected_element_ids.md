# get_selected_element_ids

- カテゴリ: Misc
- 目的: 現在の選択要素IDを取得し、モデル選択かプロジェクトブラウザ選択かを判別します。

## 概要
選択要素の elementId（int 配列）に加えて、次を返します。
- `selectionKind`: `Model` / `ProjectBrowser` / `Mixed` / `None` / `ProjectBrowserNonElementOrNone`
- `browserElementIds` / `modelElementIds` / `missingElementIds`
- `selectionSource`（`live` または `stash`）

## 使い方
- メソッド: `get_selected_element_ids`
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
  "method": "get_selected_element_ids",
  "params": {}
}
```

## 戻り値（例）
```jsonc
{
  "ok": true,
  "elementIds": [1234567, 1234568],
  "count": 2,
  "selectionKind": "Model",
  "isProjectBrowserActive": false,
  "browserElementIds": [],
  "modelElementIds": [1234567, 1234568],
  "missingElementIds": [],
  "classificationCounts": { "browser": 0, "model": 2, "missing": 0 },
  "source": "live",
  "selectionSource": "live",
  "docKey": "xxxx",
  "activeViewId": 890123
}
```

補足:
- 未選択の場合は `count=0`、`elementIds=[]` になります。
- プロジェクトブラウザで要素IDを持たない行は、モデル要素としては返りません。
- ブラウザ側の内訳確認は `get_project_browser_selection` を使ってください。

## 関連コマンド
- get_project_browser_selection
- restore_selection
- get_element_info
