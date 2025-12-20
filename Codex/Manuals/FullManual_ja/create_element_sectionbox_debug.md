# create_element_sectionbox_debug

- カテゴリ: ViewOps / Debug
- 目的: 任意の要素 ID を指定して、Section ビュー（`ViewFamily.Section`）専用のデバッグ用断面ビューを作成し、使用した座標系や BoundingBox の情報を JSON で取得します。

## 概要
このコマンドは、`ViewSection.CreateSection` に渡す `BoundingBoxXYZ` と `Transform` が Revit の制約に適合しているかを検証するための「実験用」コマンドです。

- Elevation へのフォールバックは行わず、SectionBox だけを試します。
- ビューが作成できなかった場合でも、可能な範囲で world/local の Bounds や viewBasis を `skipped` に含めて返します。

**注意:** 本コマンドはデバッグ／検証用であり、現時点では一部の要素（特に複雑なカーテンウォール等）については断面ビューの作成に失敗することがあります。

## 使い方

- メソッド: create_element_sectionbox_debug

### パラメータ

| 名前              | 型     | 必須 | 既定値 | 説明 |
|-------------------|--------|------|--------|------|
| elementIds        | int[]  | いいえ |  | 対象とする要素 ID の配列。`fromSelection=true` の場合は省略可。 |
| fromSelection     | bool   | いいえ | false | true の場合、アクティブな Revit の選択要素を対象にします（`elementIds` が空のときのみ有効）。 |
| offsetDistance_mm | number | いいえ | 1500  | 要素の BoundingBox 中心からビュー方向にどれだけ離して断面ビューを配置するか（mm）。 |
| cropMargin_mm     | number | いいえ | 200   | 要素 BoundingBox 周囲に付加するマージン（mm）。 |
| viewScale         | int    | いいえ | 50    | 作成する Section ビューの縮尺。 |

### リクエスト例

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_element_sectionbox_debug",
  "params": {
    "fromSelection": true,
    "offsetDistance_mm": 1500,
    "cropMargin_mm": 200,
    "viewScale": 50
  }
}
```

### 結果例（成功時）

```jsonc
{
  "ok": true,
  "msg": "Created 1 section view(s).",
  "items": [
    {
      "ok": true,
      "elementId": 6801576,
      "viewId": 11130001,
      "viewName": "DbgSection_6801576",
      "worldBounds": {
        "min": { "x_ft": 1.52, "y_ft": 40.0, "z_ft": 0.0, "x_mm": 465.0, "y_mm": 12192.0, "z_mm": 0.0 },
        "max": { "x_ft": 5.46, "y_ft": 45.0, "z_ft": 20.0, "x_mm": 1665.0, "y_mm": 13716.0, "z_mm": 6100.0 }
      },
      "localBounds": {
        "min": { "x_ft": -2.0, "y_ft": 0.0, "z_ft": 0.0, "x_mm": -610.0, "y_mm": 0.0, "z_mm": 0.0 },
        "max": { "x_ft": 2.0, "y_ft": 6.0, "z_ft": 4.0, "x_mm": 610.0, "y_mm": 1829.0, "z_mm": 1219.0 }
      },
      "viewBasis": {
        "origin": { "x_ft": 3.5, "y_ft": 38.0, "z_ft": 10.0, "x_mm": 1067.0, "y_mm": 11582.0, "z_mm": 3048.0 },
        "dir":   { "x": 0.0, "y": 1.0, "z": 0.0 },
        "up":    { "x": 0.0, "y": 0.0, "z": 1.0 },
        "right": { "x": 1.0, "y": 0.0, "z": 0.0 }
      }
    }
  ],
  "skipped": []
}
```

### 結果例（失敗時／TODO）

```jsonc
{
  "ok": false,
  "msg": "Created 0 section view(s).",
  "items": [],
  "skipped": [
    {
      "elementId": 6801576,
      "reason": "ArgumentException",
      "debug": {
        "worldBounds": { "min": { /* ... */ }, "max": { /* ... */ } },
        "localBounds": { "min": { /* ... */ }, "max": { /* ... */ } },
        "viewBasis": {
          "origin": { /* ... */ },
          "dir": { "x": 0.0, "y": 1.0, "z": 0.0 },
          "up":  { "x": 0.0, "y": 0.0, "z": 1.0 },
          "right": { "x": 1.0, "y": 0.0, "z": 0.0 }
        }
      }
    }
  ]
}
```

## 現時点での制約と TODO

- 本コマンドは **SectionBox ロジックの検証用** であり、すべての要素に対して断面ビューの作成が保証されるものではありません。
- 特にカーテンウォールなどの複雑な要素については、Revit の `ViewSection.CreateSection` が `ArgumentException` を返し、ビュー作成に失敗する事例が確認されています。
- 現状の実装では:
  - ローカル座標系を `Right/Up/Dir`（グローバル Y+/Z+ ベース）で構築。
    - 重要: `Transform` は **右手系**（`BasisX × BasisY = BasisZ`）である必要があり、満たさない場合 `ViewSection.CreateSection` が失敗することがあります。
  - Z 方向は Min.Z=0, Max.Z=depth（depth>=10mm）となるように正規化。
  - X/Y は要素 BB + マージンで囲んでいます。
- それでも `ArgumentException` が返るケースについては、以下が未解決の課題です:
  - Revit の SectionView が要求する Transform/BBox の詳細な制約（向き・範囲など）の特定。
  - カーテンウォールなど特定カテゴリの要素に対する安全な SectionBox パターンの探索。

**TODO / 将来対応方針**

- Revit UI で手動作成した断面ビューの SectionBox パラメータ（Transform, Min/Max）を取得し、本コマンドの出力と比較することで、Revit が許容するパターンを逆算する。
- ドア／窓の Elevation 生成ロジックと同様に、要素の方向・レベル・高さ情報をより厳密に参照し、SectionBox の基準平面・幅・高さ・奥行きを安定して決定できるようにする。
- 本ロジックは `create_element_elevation_views` の `"SectionBox"` モードでも使用されます（失敗した場合は `"ElevationMarker"` にフォールバックします）。

現時点では、SectionBox 方式は **実験／検証用** にとどめ、運用上は ElevationMarker 方式（`mode="ElevationMarker"`）とドア／窓専用のトリミングロジックを優先して使用することを推奨します。
