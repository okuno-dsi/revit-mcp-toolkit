# draw_colored_line_segments

- カテゴリ: AnnotationOps
- Canonical: `view.draw_colored_line_segments`
- Legacy: `draw_colored_line_segments`

## 概要
座標データセット（mm）から、現在のビュー（または `viewId` 指定のビュー）に **詳細線分（Detail Line）** を作成し、指定色で着色します。

- 入力は **mm**（`{x,y,z}`）。
- 色・線の太さは **要素ごとのグラフィックス上書き**（Projection Line Color / Weight）で適用します（線種そのものは変更しません）。
- `get_room_perimeter_with_columns` などが返す `loops[].segments[]` をそのまま流し込めます。

## パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---|---|
| `viewId` | int | いいえ | (現在のアクティブビュー) | 描画先ビューID。省略時は現在のアクティブビュー。 |
| `segments` | object[] | いいえ |  | 線分配列。`[{start:{x,y,z}, end:{x,y,z}, lineRgb?, lineWeight?}, ...]`（mm）。 |
| `loops` | object[] | いいえ |  | ループ配列。`[{segments:[...]}]`（`get_room_perimeter_with_columns` の戻り形に合わせた形式）。 |
| `dataset` | object | いいえ |  | `dataset.segments` / `dataset.loops` を読めるラッパー。 |
| `lineRgb` | object | いいえ | `{r:255,g:0,b:0}` | 既定の線色。`{r,g,b}`（0-255）。 |
| `r`,`g`,`b` | int | いいえ |  | `lineRgb` の代替指定。 |
| `lineWeight` | int | いいえ |  | 既定の線の太さ（1–16）。 |
| `applyOverrides` | bool | いいえ | `true` | `true` の場合、線色/太さを上書き適用。 |
| `detachViewTemplate` | bool | いいえ | `false` | `true` の場合、ビューのテンプレートを解除してから描画（モデルを変更します）。 |
| `batchSize` | int | いいえ | `500` | 大量作成時のバッチサイズ。 |
| `startIndex` | int | いいえ | `0` | 再開用の開始インデックス。 |
| `maxMillisPerTx` | int | いいえ | `3000` | 1トランザクションあたりの目安上限（ms）。 |
| `refreshView` | bool | いいえ | `true` | 作成後に `Regenerate/RefreshActiveView` を試行。 |
| `returnIds` | bool | いいえ | `true` | `true` の場合 `createdElementIds` を返します。 |

## 注意（テンプレート）
ビューにテンプレートが設定されている状態で `applyOverrides=true` の場合、`errorCode=VIEW_TEMPLATE_LOCK` を返して処理を中断します。  
テンプレート解除が必要です（または `detachViewTemplate=true`）。

## 例1: 周長（柱考慮）を赤線で描く
1) `get_room_perimeter_with_columns` で `includeSegments=true` にして `loops` を得ます。  
2) 返ってきた `loops` をこのコマンドに渡します。

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.draw_colored_line_segments",
  "params": {
    "viewId": 6781038,
    "loops": [
      { "loopIndex": 0, "segments": [ { "start": {"x":0,"y":0,"z":0}, "end": {"x":1000,"y":0,"z":0} } ] }
    ],
    "lineRgb": { "r": 255, "g": 0, "b": 0 },
    "lineWeight": 6
  }
}
```

## 例2: 任意の線分集合を色付きで描く（segments）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.draw_colored_line_segments",
  "params": {
    "segments": [
      { "start": {"x":0,"y":0,"z":0}, "end": {"x":1000,"y":0,"z":0} },
      { "start": {"x":1000,"y":0,"z":0}, "end": {"x":1000,"y":1000,"z":0}, "lineRgb": {"r":0,"g":128,"b":255} }
    ],
    "lineRgb": { "r": 255, "g": 0, "b": 0 },
    "lineWeight": 3
  }
}
```

