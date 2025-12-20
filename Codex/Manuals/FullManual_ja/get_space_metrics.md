# get_space_metrics

- カテゴリ: Space
- 目的: Space のメトリクス（面積/体積/周長）を取得します。

## 概要
周長は `GetBoundarySegments(...)` による計算結果を集計します（A: 計算境界）。`Manuals/FullManual_ja/spatial_boundary_location.md` 参照。

## 使い方
- メソッド: get_space_metrics

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---:|:---:|---|
| elementId | int | はい |  |
| boundaryLocation | string | いいえ | `Finish` |

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_space_metrics",
  "params": {
    "elementId": 123456,
    "boundaryLocation": "Finish"
  }
}
```

## 関連コマンド
- delete_space
- get_space_params
- get_spaces
- move_space
- update_space
- get_space_boundary
- get_space_boundary_walls

補足:
- レスポンスには `boundaryLocation` と `units` が含まれます。
