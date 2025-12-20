# close_views — 指定した開いているビューを閉じる（UI）

注意
- Revit API には「特定のタブを閉じる」直接APIが無いため、`Close Inactive Views` を活用して目的の結果を再現します。
- 実装は「保持したいビューだけを一旦残して、それ以外を閉じる → 残したいビューを再度開く」手順になるため、タブ順や表示配置はリセットされます。

メソッド
- `close_views`

パラメータ（いずれか）
- `viewIds` (int[]): 閉じたい開いているビューID
- `uniqueIds` (string[]): 閉じたいビューの UniqueId（開いているものに限定してマッチ）
- `names` (string[]): 閉じたいビュー名（大文字小文字を無視、開いているものに限定）

仕様
- 現在開いている UI ビュー集合から、指定に一致するものを閉じます。
- Revit上は必ず最低1つのビューが開いている必要があるため、全てを閉じる指定の場合はアクティブビューを残します（`keptActiveDueToLimit:true`）。

レスポンス例
```
{
  "ok": true,
  "requested": [102,103],
  "keptIds": [101],
  "baselineId": 101,
  "keptActiveDueToLimit": false,
  "closedCount": 2,
  "closedIds": [102,103]
}
```

関連
- 開いているビューの列挙は `list_open_views`
- すべての非アクティブビューを閉じるのは `close_inactive_views`

