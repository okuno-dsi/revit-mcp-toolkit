# scan_paint_and_split_regions

- カテゴリ: Materials
- 用途: プロジェクト内の要素を走査し、「塗装されている面」または「Split Face（分割面）を持つ面」が存在する要素を一覧します。

## 概要
指定したカテゴリ（または既定の代表カテゴリ）に属する要素について、ジオメトリを走査します。  
- 少なくとも 1 つの塗装された面を持つ要素  
- 少なくとも 1 つの Split Face 領域を持つ面を持つ要素  
のみを返します。  
オプションで、面単位の安定参照文字列を返すこともできます（後続の詳細取得コマンドで利用する想定）。

## 使い方
- メソッド: `scan_paint_and_split_regions`

### パラメータ
```jsonc
{
  "categories": ["Walls", "Floors", "Ceilings"],  // 任意: 任意のカテゴリ名
  "includeFaceRefs": false,                       // 任意: 既定 false
  "maxFacesPerElement": 50,                       // 任意: includeFaceRefs = true のときのみ有効
  "limit": 0                                      // 任意: 0 または null で上限なし
}
```

- `categories` (string[], 任意)
  - 走査対象とするカテゴリ名の配列です。
  - 英語のフレンドリ名（`"Walls"`, `"Floors"`, `"Roofs"`, `"Generic Models"` など）に加え、日本語名（`"壁"`, `"床"`, `"構造フレーム"` など）や `BuiltInCategory` 名（`"OST_Walls"` など）も指定できます。
  - 解決できない名前や、モデルカテゴリでないものは **静かに無視** されます。
  - 省略または空配列の場合は、「壁・床・屋根・天井・一般モデル・柱・構造柱・構造フレーム・ドア・窓」などの代表的なモデルカテゴリを既定で走査します。
- `includeFaceRefs` (bool, 任意, 既定 `false`)
  - `false`: 要素単位のフラグ（`hasPaint`, `hasSplitRegions`）のみ返します（最速）。
  - `true`: 条件を満たす面ごとに `faces[]` 情報（安定参照文字列など）も返します。
- `maxFacesPerElement` (int, 任意, 既定 `50`)
  - `includeFaceRefs == true` のときのみ使用されます。
  - 要素 1 件あたりの face 情報の件数上限です。複雑な形状の要素の出力サイズを抑えるための制限です。
- `limit` (int, 任意, 既定 `0`)
  - 返却する要素数の上限です。
  - `0` または `null` の場合は上限なしです。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "scan-paint-walls-floors",
  "method": "scan_paint_and_split_regions",
  "params": {
    "categories": ["Walls", "Floors"],
    "includeFaceRefs": true,
    "maxFacesPerElement": 20,
    "limit": 200
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "msg": null,
  "items": [
    {
      "elementId": 123456,
      "uniqueId": "f8f6455a-...",
      "category": "Walls",
      "hasPaint": true,
      "hasSplitRegions": true,
      "faces": [
        {
          "stableReference": "RVT_LINK_INSTANCE:...:ELEMENT_ID:...:FACE_ID:...",
          "hasPaint": true,
          "hasRegions": true,
          "regionCount": 3
        }
      ]
    }
  ]
}
```

- `ok` (bool): 成功時 `true`。入力エラーや内部エラー時は `false`。
- `msg` (string|null): 成功時は `null`。エラー時は説明文（例: `"No valid categories resolved. Nothing scanned."`）。
- `items` (配列):
  - 少なくとも 1 つの塗装面、または Split Face を持つ面が存在する要素のみが含まれます。
  - 条件に合う要素が無い場合は空配列になりますが、エラーにはなりません。

各要素 (`PaintAndRegionElementInfo`) のフィールド:
- `elementId` / `uniqueId`: Revit の要素ID／ユニークID。
- `category`: 要素のカテゴリ名（`elem.Category?.Name`）。
- `hasPaint`: 要素内のいずれかの面が塗装されていれば `true`。
- `hasSplitRegions`: 要素内のいずれかの面が Split Face を持っていれば `true`。
- `faces` (任意, `includeFaceRefs == true` のときのみ):
  - `stableReference`: 面の安定参照文字列。後続の詳細取得コマンドで再利用できます（取得に失敗した場合は `null`）。
  - `hasPaint`: その面が塗装されているかどうか。
  - `hasRegions`: その面が Split Face 領域を持つかどうか（`Face.HasRegions`）。
  - `regionCount`: `Face.GetRegions()` による領域数（失敗時は `0`）。

## 補足

- 一部のカテゴリ／要素は、そもそも塗装できない、または Solid/Face を持たない場合がありますが、その場合も例外を投げずに「該当なし」としてスキップされます。
- `includeFaceRefs` を `true` にすると、面ごとの安定参照を JSON に含められるため、別コマンドで個別の塗装情報やマテリアル情報を詳しく取得するときの「入口」として利用できます。
- 大規模プロジェクトでは、`categories`／`limit`／`maxFacesPerElement` を併用して、処理時間とレスポンスサイズをコントロールすることを推奨します。

## 関連コマンド
- `get_paint_info`
- `apply_paint`
- `remove_paint`

