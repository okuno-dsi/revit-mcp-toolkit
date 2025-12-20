# compare_view_states — ビュー状態の比較（プロパティ／可視性）

概要
- 複数ビューをベースライン（基準）と比較し、プロパティ値やカテゴリ・フィルタ・ワークセットの可視性差分、必要に応じて要素レベルの非表示差分を一覧化します。
- 複製ビューを重ね合わせた際に、オリジナルと食い違いがないかの確認に有効です。

メソッド
- `compare_view_states`

パラメータ（任意）
- `baselineViewId` (int): 基準ビューID。未指定時は `viewIds[0]`（なければアクティブビュー）。
- `viewIds` (int[]): 比較対象ビューIDの配列。未指定時は開いているUIビュー（基準を除く）を対象に試行。
- `includeCategories` (bool, 既定: true): カテゴリ可視性（hidden）を比較。
- `includeFilters` (bool, 既定: true): ビューフィルタの可視性を比較。
- `includeWorksets` (bool, 既定: true): ワークセットの可視性を比較（ワークシェア有効時）。
- `includeHiddenElements` (bool, 既定: false): 要素レベルの非表示を比較（重い処理）。
- `includeElementOverlapSummary` (bool, 既定: false): 比較対象ビュー群の「要素重複サマリ」を返す（軽量な件数要約）。

比較対象プロパティ（既定）
- `viewType`, `scale`, `discipline`, `detailLevel`, `templateViewId`

レスポンス例
```
{
  "ok": true,
  "baseline": { "viewId": 101, "viewName": "Base" },
  "comparisons": [
    {
      "viewId": 102,
      "viewName": "Copy-A",
      "diffs": {
        "properties": [ { "property": "scale", "baseline": "100", "target": "150" } ],
        "categories": { "toHidden": [ -2001000 ], "toVisible": [ -2002000 ] },
        "filters": { "toHidden": [ 12345 ], "toVisible": [] },
        "worksets": { "changed": [ { "worksetId": 5, "baseline": "Visible", "target": "Hidden" } ] },
        "hiddenElements": { "onlyBaseline": [ 200001 ], "onlyTarget": [ 200099 ] }
      }
    }
  ]
}
```

呼び出し例（要素レベルは除外、軽量）
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command compare_view_states --params '{"baselineViewId":101,"viewIds":[102,103],"includeHiddenElements":false}' --output-file Work/<Project>_<Port>/Logs/compare_view_states.json
```

要素重複サマリの取得（軽量サマリのみ追加）
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command compare_view_states --params '{"baselineViewId":101,"viewIds":[102,103,104],"includeElementOverlapSummary":true}' --output-file Work/<Project>_<Port>/Logs/compare_view_states.overlap.json
```

要素重複サマリのレスポンス例（抜粋）
```
{
  "ok": true,
  "includeElementOverlapSummary": true,
  "elementOverlap": {
    "baselineCount": 793,
    "targetCounts": [ {"viewId": 102, "count": 415}, {"viewId": 103, "count": 91}, {"viewId": 104, "count": 55} ],
    "unionTargetsCount": 543,
    "overlapAllTargetsCount": 9,
    "equalsBaselineVsUnion": false,
    "onlyInBaselineCount": 541,
    "onlyInUnionCount": 291,
    "pairwiseIntersections": [ {"a": 102, "b": 103, "count": 9 }, {"a": 102, "b": 104, "count": 9 } ],
    "sampleCommonIds": [ 39399, 2010960, 2677548, ... ]
  }
}
```

Tips
- 要素レベル比較はモデル全体を走査するため重くなります。必要な場合のみ `includeHiddenElements:true` を指定してください。
- オリジナルとの差異が多い場合は、`save_view_state` と `restore_view_state` を併用し、テンプレートや一時非表示の扱いを整理してから比較すると安定します。
