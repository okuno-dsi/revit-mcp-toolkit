# get_fill_patterns

- カテゴリ: Document/Patterns
- 用途: 現在のプロジェクトに登録されている塗りつぶしパターン（FillPatternElement）の一覧を取得します。

## 概要
アクティブな Revit ドキュメント内の `FillPatternElement` を列挙し、パターン名・ID・ソリッドかどうか・ターゲット（ドラフティング/モデル）を返します。  
`create_filled_region` で前景/背景パターンを指定する前に、候補となるパターンを調べる用途に使えます。

## 使い方
- メソッド: `get_fill_patterns`

### パラメータ
```jsonc
{
  "target": "Drafting",     // 任意: "Drafting" | "Model" | "Any"（既定: "Any"）
  "solidOnly": true,        // 任意: true = ソリッドのみ, false = 非ソリッドのみ, 省略 = 両方
  "nameContains": "Solid"   // 任意: パターン名の部分一致（大文字小文字無視）
}
```

- `target` (string, 任意)
  - `"Drafting"`: ドラフティング（注釈用）パターンのみ。
  - `"Model"`: モデルパターンのみ。
  - `"Any"` または省略: ターゲットでフィルタしません（すべて）。
- `solidOnly` (bool, 任意)
  - `true`: ソリッド塗りのみ取得。
  - `false`: ソリッド以外のみ取得。
  - 省略: ソリッド/非ソリッドの両方を含みます。
- `nameContains` (string, 任意)
  - パターン名に指定文字列を含むものだけに絞り込みます（大文字小文字無視）。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "fillpatterns-solid-drafting",
  "method": "get_fill_patterns",
  "params": {
    "target": "Drafting",
    "solidOnly": true
  }
}
```

## 戻り値
```jsonc
{
  "ok": true,
  "totalCount": 3,
  "items": [
    {
      "elementId": 2001,
      "name": "Solid fill",
      "isSolid": true,
      "target": "Drafting"
    }
  ]
}
```

## 補足

- `elementId` は `FillPatternElement.Id.IntegerValue` に対応します。  
  `create_filled_region` の `foregroundPatternId` / `backgroundPatternId` にそのまま渡せます。
- `name` は Revit の［塗りつぶしパターン］ダイアログに表示される名称です。
- `target` は Revit の `FillPatternTarget`（`Drafting` / `Model`）を文字列化したものです。

## 関連コマンド
- `get_filled_region_types`
- `create_filled_region`

