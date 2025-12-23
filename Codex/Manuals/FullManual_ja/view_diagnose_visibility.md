# view.diagnose_visibility

- カテゴリ: Diagnostics
- 目的: ビューで「動いたはずなのに見えない」系の罠を診断します（テンプレート、クロップ、カテゴリ表示、テンポラリ表示モードなど）。

## 概要
- Canonical: `view.diagnose_visibility`
- 旧エイリアス: `diagnose_visibility`

想定ユースケース:
- 「ビジュアルオーバーライドを実行したが画面で変化が見えない」
- 「要素がビューから消えている」

## パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---:|---:|
| viewId | integer | no | アクティブビュー |
| view | object | no | アクティブビュー |
| includeCategoryStates | boolean | no | true |
| includeAllCategories | boolean | no | false |
| categoryIds | integer[] | no | 主要カテゴリ |
| includeTemplateParamIds | boolean | no | false |
| maxTemplateParamIds | integer | no | 50 |

メモ:
- `includeAllCategories=true` の場合、全カテゴリの表示状態を列挙します。
- `categoryIds` を指定すると、そのカテゴリ集合のみを対象にします（`includeAllCategories=true` の場合を除く）。

## リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "view.diagnose_visibility",
  "params": { "includeAllCategories": false }
}
```

## 出力（概要）
- `data.view`: id/name/type、displayStyle/detailLevel/discipline/scale 等
- `data.template`: テンプレート適用有無、テンプレート情報、テンプレート制御パラメータ数（任意でサンプルID）
- `data.globalVisibility`: AreModelCategoriesHidden / AreAnnotationCategoriesHidden / ...
- `data.temporaryModes`: 一時的な非表示/アイソレート、隠し要素表示、テンポラリプロパティ等
- `data.crop`: クロップ有効/表示 + クロップサイズ（取得できる場合）
- `data.categories`: カテゴリの表示状態（既定は主要カテゴリ）

## よくある判断
- `data.template.applied=true` の場合: ビューテンプレートが Visibility/Graphics をロックしており、オーバーライドが効いていない可能性があります。テンプレートを外す（`clear_view_template`）か、テンプレート無しのビューで試してください。
- `data.overrides.graphicsOverridesAllowed=false` の場合: そのビュー種別ではグラフィックオーバーライドが許可されていない可能性があります。
- `data.globalVisibility.areModelCategoriesHidden=true` の場合: カテゴリ設定以前に「モデルカテゴリ全体が非表示」になっています。

