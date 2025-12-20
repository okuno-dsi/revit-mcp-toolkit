# close_views_except — 指定した開いているビュー以外を閉じる（UI）

概要
- 入力で指定した開いているビュー集合を維持し、それ以外の開いているビューを閉じます。
- 直接の「個別閉じる」APIがないため、`Close Inactive Views` を活用して実現します。

メソッド
- `close_views_except`

パラメータ（いずれか）
- `viewIds` (int[]): 残したい開いているビューID
- `uniqueIds` (string[]): 残したいビューの UniqueId（開いているものに限定）
- `names` (string[]): 残したいビュー名（大文字小文字を無視、開いているものに限定）

仕様
- 指定に一致した開いているビューをすべて保持し、それ以外を閉じます。
- Revit上は必ず最低1つのビューが開いている必要があるため、指定が空/不一致だった場合はアクティブビューまたは先頭の開いているビューを保持します（`keptActiveDueToLimit:true`）。

レスポンス例
```
{
  "ok": true,
  "keepRequested": [101,102],
  "baselineId": 101,
  "keptActiveDueToLimit": false,
  "closedCount": 3,
  "closedIds": [103,104,105]
}
```

関連
- `list_open_views`（現在開いているビューの列挙）
- `close_views`（指定したビューを閉じる：除外ではなく直接指定）

