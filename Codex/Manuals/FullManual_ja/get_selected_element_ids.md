# get_selected_element_ids

- カテゴリ: Misc
- 目的: 現在 UI で選択している要素の elementId を取得します。

## 概要
選択要素の elementId（int 配列）と、activeViewId / docTitle などのコンテキスト情報を返します。

## 使い方
- メソッド: `get_selected_element_ids`
- パラメータ: なし

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
  "activeViewId": 890123,
  "docTitle": "プロジェクト1",
  "msg": "OK"
}
```

補足:
- 未選択の場合は `count=0`、`elementIds=[]` になります。

## 関連コマンド
- restore_selection
- get_element_info

