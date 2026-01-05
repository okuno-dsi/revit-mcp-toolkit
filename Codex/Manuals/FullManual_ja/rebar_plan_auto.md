# rebar_plan_auto

- カテゴリ: Rebar
- 目的: 選択した柱/梁（構造柱/構造フレーム）に対して、自動配筋の「作成計画（plan）」を返します（モデルは変更しません）。

## 概要
v1 の計画ルール:
- 構造柱: 主筋（4隅）+ 帯筋（1セット）
- 梁（構造フレーム）: 主筋（上下）+ スターラップ（1セット）
  - 既定の主筋本数: 上=2、下=2（`options.beamMainTopCount/beamMainBottomCount` または mapping の梁属性キーで上書き）

形状は近似です。梁の長手方向の範囲は Solid ジオメトリから得た物理形状の範囲を優先します（柱芯まで伸びる LocationCurve 端点だと端部のスターラップ等がずれるため）。Solid が取れない場合は BoundingBox にフォールバックします。LocationCurve 範囲は参考（デバッグ）として返します（自動化の足場用途）。

### 梁（マッピング駆動の配筋属性）について
- `RebarMapping.json` のプロファイルに梁用の論理キー（例: `Beam.Attr.MainBar.DiameterMm`, `Beam.Attr.MainBar.TopCount`, `Beam.Attr.Stirrup.DiameterMm`, `Beam.Attr.Stirrup.PitchMidMm`）が定義されている場合、
  `options.beamUseTypeParams=true` でそれらを優先して（best-effort）本数/ピッチを決めます。
- 鉄筋タイプは「径から一致する `RebarBarType` を探索」して選びます（`D29` などの命名に依存しません）。
- パラメータ名はファミリ作成者や言語で変わるため、パラメータ名はソースコードに埋め込まず `RebarMapping.json` に登録します（例: `rc_beam_attr_jp_v1`）。

### 梁の形状オプション（v1の足場機能）
- 主筋の始点/終点は `beamMainBarStartExtensionMm` / `beamMainBarEndExtensionMm` で bbox から延長/短縮できます。
- 梁主筋は既定で、支持柱が推定できる場合に **柱内へ「0.75×柱幅（梁軸方向）」** だけ伸ばします（best-effort）。必要なら options で無効化/調整できます。
- 端部の90度折り曲げは、主筋の曲線を「追加の線分」で表現します（`beamMainBarStartBendLengthMm` / `beamMainBarEndBendLengthMm`、方向 `up|down`）。
- スターラップの始点コーナーは `beamStirrupStartCorner` で回転できます（多くのケースで上側が好まれます）。
- 梁スターラップはフックタイプ（例: 135度）も指定できます（best-effort）。`beamStirrupUseHooks=true` の場合、スターラップの曲線は「開いた折れ線」として作成し、両端に `RebarHookType` を適用します。
- 注意: `RebarHookType` は `RebarStyle` との相性があります。`標準フック` を指定した場合、ツール側で `style:"Standard"` に切り替えて作成します（best-effort）。
- 柱の主筋についても、端部延長/短縮や90度折り曲げ（線分表現）を options で指定できます。
- 柱の帯筋（せん断補強筋）についても、開始/終了位置オフセットや135度フック等を options で指定できます（best-effort）。
- 梁スターラップの開始/終了は、構造柱の BoundingBox から「柱面（支持面）」を推定してそこに寄せます（best-effort）。
  - まず結合（Join）された柱を試し、無い場合は梁端点近傍の柱を探索します。

## 使い方
- Method: `rebar_plan_auto`

### パラメータ
| 名前 | 型 | 必須 | 既定値 | 説明 |
|---|---|---:|---:|---|
| hostElementIds | int[] | no |  | 未指定の場合は選択から取得します（`useSelectionIfEmpty=true`）。 |
| useSelectionIfEmpty | bool | no | true | `hostElementIds` が空の場合に選択を使う。 |
| profile | string | no |  | `RebarMapping.json` のプロファイル名（任意）。 |
| options | object | no |  | 上書きオプション。 |
| options.tagComments | string | no | `RevitMcp:AutoRebar` | `rebar_apply_plan` 実行時に、作成した鉄筋の `Comments` に入れるタグ。 |
| options.mainBarTypeName | string | no |  | 主筋の鉄筋タイプ名（mapping `Common.MainBar.BarType` の上書き）。 |
| options.tieBarTypeName | string | no |  | 帯筋/スターラップの鉄筋タイプ名（mapping `Common.TieBar.BarType` の上書き）。 |
| options.includeMainBars | bool | no | true | 主筋の作成計画を含めるか。 |
| options.includeTies | bool | no | true | 柱の帯筋を含めるか。 |
| options.includeStirrups | bool | no | true | 梁のスターラップを含めるか。 |
| options.beamUseTypeParams | bool | no | true | `RebarMapping.json` の梁用論理キー（プロファイル依存）を使って、本数/ピッチを上書きします。 |
| options.beamMainTopCount | int | no |  | 梁主筋の上端本数（ユーザー指定の上書き）。 |
| options.beamMainBottomCount | int | no |  | 梁主筋の下端本数（ユーザー指定の上書き）。 |
| options.beamMainBarStartExtensionMm | number | no | 0 | 梁主筋の始点延長(mm)。正で延長、負で短縮。 |
| options.beamMainBarEndExtensionMm | number | no | 0 | 梁主筋の終点延長(mm)。正で延長、負で短縮。 |
| options.beamMainBarEmbedIntoSupportColumns | bool | no | true | true の場合、支持柱内へ主筋を延長します（best-effort）。 |
| options.beamMainBarEmbedIntoSupportColumnRatio | number | no | 0.75 | 延長長さ = ratio × 支持柱幅（梁軸方向）。 |
| options.beamSupportSearchRangeMm | number | no | 1500 | 支持柱探索範囲（梁端点近傍、mm）。 |
| options.beamSupportFaceToleranceMm | number | no | 250 | 支持柱と梁端面の一致判定許容（mm）。 |
| options.beamStirrupUseHooks | bool | no | false | true の場合、梁スターラップを「開いた折れ線」で作成し、両端にフックを適用します（best-effort）。 |
| options.beamStirrupHookAngleDeg | number | no | 0 | フック角度（deg、例: `135`）。`beamStirrupHookTypeName` が未指定の場合に一致する `RebarHookType` を探索します。 |
| options.beamStirrupHookTypeName | string | no |  | `RebarHookType` の正確な名前（角度より優先）。 |
| options.beamStirrupHookOrientationStart | string | no | `left` | `left`/`right`（Revit の HookOrientation）。 |
| options.beamStirrupHookOrientationEnd | string | no | `right` | `left`/`right`（Revit の HookOrientation）。 |
| options.beamStirrupStartOffsetMm | number | no | 0 | 梁端面からの開始オフセット（mm）。 |
| options.beamStirrupEndOffsetMm | number | no | 0 | 梁端面からの終了オフセット（mm）。 |
| options.columnMainBarBottomExtensionMm | number | no | 0 | 柱主筋の下端延長(mm)。正で延長、負で短縮。 |
| options.columnMainBarTopExtensionMm | number | no | 0 | 柱主筋の上端延長(mm)。正で延長、負で短縮。 |
| options.columnMainBarStartBendLengthMm | number | no | 0 | 柱主筋の始端90度折り曲げ長さ(mm)。 |
| options.columnMainBarEndBendLengthMm | number | no | 0 | 柱主筋の終端90度折り曲げ長さ(mm)。 |
| options.columnMainBarStartBendDir | string | no | `none` | `up`/`down`/`none`（world Z基準）。 |
| options.columnMainBarEndBendDir | string | no | `none` | `up`/`down`/`none`（world Z基準）。 |
| options.columnTieBottomOffsetMm | number | no | 0 | 柱下端面から最初の帯筋までのオフセット(mm)。 |
| options.columnTieTopOffsetMm | number | no | 0 | 柱上端面から最後の帯筋までのオフセット(mm)。 |
| options.columnTieUseHooks | bool | no | false | true の場合、柱帯筋を「開いた折れ線」で作成し、両端にフックを適用します（best-effort）。 |
| options.columnTieHookAngleDeg | number | no | 0 | フック角度（deg、例: `135`）。`columnTieHookTypeName` が未指定の場合に一致する `RebarHookType` を探索します。 |
| options.columnTieHookTypeName | string | no |  | `RebarHookType` の正確な名前（角度より優先）。 |
| options.columnTieHookOrientationStart | string | no | `left` | `left`/`right`。 |
| options.columnTieHookOrientationEnd | string | no | `right` | `left`/`right`。 |
| options.beamMainBarStartBendLengthMm | number | no | 0 | 梁主筋の始点90度折り曲げ長さ(mm)。0で無効。 |
| options.beamMainBarEndBendLengthMm | number | no | 0 | 梁主筋の終点90度折り曲げ長さ(mm)。0で無効。 |
| options.beamMainBarStartBendDir | string | no | `none` | `up` / `down` / `none`（ローカル上方向基準）。 |
| options.beamMainBarEndBendDir | string | no | `none` | `up` / `down` / `none`（ローカル上方向基準）。 |
| options.beamStirrupStartCorner | string | no | `top_left` | `bottom_left` / `bottom_right` / `top_right` / `top_left`（ループ順の回転）。 |
| options.layout | object | no |  | 帯筋/スターラップのレイアウト指定（`rebar_layout_update.layout` と同じ形式）。 |
| options.includeMappingDebug | bool | no | false | true の場合、plan 内に mapping のソース情報も出します。 |
| options.preferMappingArrayLength | bool | no | false | true の場合、mapping の `Common.Arrangement.ArrayLength` を優先します。 |

## 戻り値メモ
- `planVersion` / `mappingStatus` / `hosts[]`（`actions[]` に曲線と layout を含む）

## 関連
- `rebar_apply_plan`
- `rebar_mapping_resolve`
- `rebar_layout_update_by_host`
