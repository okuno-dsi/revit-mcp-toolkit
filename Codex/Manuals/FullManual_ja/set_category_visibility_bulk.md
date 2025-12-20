# set_category_visibility_bulk

- カテゴリ: ViewOps
- 目的: ビュー内のカテゴリ表示を一括で制御します。  
  例) 「壁・床・構造柱だけ表示し、それ以外のモデルカテゴリをすべて非表示にする」など。

## 概要
既存の `set_category_visibility` をまとめて扱えるようにしたコマンドです。  
主な用途:

- `keep_only` モード: 指定されたカテゴリだけ表示し、それ以外（指定されたカテゴリタイプのもの）はすべて非表示。
- `hide_only` モード: 指定されたカテゴリだけ非表示にし、その他は変更しない。

ビューごとの設定であり、ビューテンプレートが適用されている場合はデタッチするオプションもあります。

## 使い方
- メソッド名: `set_category_visibility_bulk`

### パラメータ
```jsonc
{
  "viewId": 11121378,                // 任意, 省略時はアクティブグラフィックビュー
  "mode": "keep_only",               // "keep_only" | "hide_only" （既定 "keep_only"）
  "categoryType": "Model",           // "Model" | "Annotation" | "All" （既定 "Model"）
  "keepCategoryIds": [-2000011],     // mode=keep_only のとき必須
  "hideCategoryIds": [-2000080],     // mode=hide_only のとき必須
  "detachViewTemplate": true         // 任意, 既定 false
}
```

- `viewId` (int, 任意)  
  対象ビューの ID。指定がない場合はアクティブなグラフィックビューを対象にします。
- `mode` (string, 任意, 既定 `"keep_only"`)  
  - `"keep_only"`: `keepCategoryIds` に挙げたカテゴリだけ表示し、それ以外（`categoryType` で絞り込まれたカテゴリ）は非表示にします。
  - `"hide_only"`: `hideCategoryIds` に挙げたカテゴリだけ非表示にし、その他のカテゴリは変更しません。
- `categoryType` (string, 任意, 既定 `"Model"`)  
  一括操作の対象とするカテゴリ種別:
  - `"Model"`: モデルカテゴリのみ。
  - `"Annotation"`: 注釈カテゴリのみ。
  - `"All"`: すべてのカテゴリタイプ。
- `keepCategoryIds` (int[], `mode == "keep_only"` のとき必須)  
  表示を維持するカテゴリ ID（`BuiltInCategory` の整数値、例: `-2000011` = `OST_Walls`）。
- `hideCategoryIds` (int[], `mode == "hide_only"` のとき必須)  
  非表示にするカテゴリ ID（`BuiltInCategory` の整数値）。
- `detachViewTemplate` (bool, 任意, 既定 `false`)  
  対象ビューにビューテンプレートが適用されている場合、`true` でテンプレートを一時的に解除してから処理を行います。  
  `false` のままテンプレート付きビューに対して呼ぶと、変更せずに「テンプレートが掛かっています」というヒントを返します。

### 例: 壁・床・構造柱だけ表示する
```jsonc
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "set_category_visibility_bulk",
  "params": {
    "viewId": 11121378,
    "mode": "keep_only",
    "categoryType": "Model",
    "keepCategoryIds": [
      -2000011,  // OST_Walls
      -2000032,  // OST_Floors
      -2001330   // OST_StructuralColumns
    ],
    "detachViewTemplate": true
  }
}
```

### 例: 家具と植栽だけ非表示にする
```jsonc
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "set_category_visibility_bulk",
  "params": {
    "viewId": 11121378,
    "mode": "hide_only",
    "categoryType": "Model",
    "hideCategoryIds": [
      -2000080,  // OST_Furniture
      -2000040   // OST_Planting
    ],
    "detachViewTemplate": true
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "viewId": 11121378,
  "mode": "keep_only",
  "categoryType": "Model",
  "changed": 54,
  "skipped": 12,
  "errors": []
}
```

- `ok` (bool): コマンド全体として成功したかどうか。
- `viewId` (int): 対象ビューの ID。
- `mode` / `categoryType`: 実際に適用されたパラメータ。
- `changed` (int): 可視／不可視状態が実際に変更されたカテゴリ数。
- `skipped` (int): 処理対象外または非表示制御できなかったカテゴリ数（例: このビュー種別で非表示にできないカテゴリ）。
- `errors` (array): カテゴリごとのエラー情報（`categoryId` / `name` / `error` など）。

ビューテンプレートが適用されているビューに対し、`detachViewTemplate:false` のまま呼んだ場合は、次のような結果になります:
```jsonc
{
  "ok": true,
  "viewId": 11121378,
  "changed": 0,
  "skipped": 0,
  "templateApplied": true,
  "templateViewId": 123456,
  "appliedTo": "skipped",
  "msg": "View has a template; set detachViewTemplate:true to proceed."
}
```

## 補足

- このコマンドは **カテゴリ単位** での表示制御を行います。  
  個々の要素を強調表示したい場合は、`set_visual_override` や `batch_set_visual_override` を利用してください。
- `categoryType = "Model"` を指定すると、モデルカテゴリだけを一括制御できます。  
  たとえば「床・壁・構造柱だけ表示」や「設備系カテゴリだけ消す」といった操作に向いています。
- カテゴリ ID は `BuiltInCategory` の整数値（`-2000011` = `OST_Walls` 等）で指定します。

