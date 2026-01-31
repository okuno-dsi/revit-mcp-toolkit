# rebar_plan_auto

- カテゴリ: Rebar
- 目的: 選択した柱/梁（構造柱/構造フレーム）に対して、自動配筋の「作成計画（plan）」を返します（モデルは変更しません）。

## 概要
v1 の計画ルール:
- 構造柱: 主筋（周辺配筋: `options.columnMainBarsPerFace`）+ 帯筋
  - 既定: 中間高さで 2分割（柱脚=`base` / 柱頭=`head`）して 2 セット（`options.columnTieSplitByMidHeight=true` が既定）
  - 任意: 帯筋パターンを指定した場合は複数セット（mapping `Column.Attr.Tie.PatternJson` または `options.columnTiePattern`）
- 梁（構造フレーム）: 主筋（上下）+ スターラップ（1セット）
  - 既定の主筋本数: 上=2、下=2（`options.beamMainTopCount/beamMainBottomCount` または mapping の梁属性キーで上書き）
  - 多段筋（最大3段）: mapping が `Beam.Attr.MainBar.TopCount2/TopCount3/BottomCount2/BottomCount3` を返す場合、2段目/3段目も作成します。
    - 配置ルール（2段目/3段目）: 本数=1 は **1段目の左端位置**に揃え、本数>=2 は左端/右端を必ず含め、3本目以降は **1段目の鉄筋位置にスナップ**します（best-effort）。
    - plan の `actions[].side` / `actions[].layerIndex` を付与します（デバッグ/検証用）。

形状は近似です。梁の長手方向の範囲は Solid ジオメトリから得た物理形状の範囲を優先します（柱芯まで伸びる LocationCurve 端点だと端部のスターラップ等がずれるため）。Solid が取れない場合は BoundingBox にフォールバックします。LocationCurve 範囲は参考（デバッグ）として返します（自動化の足場用途）。

### 梁の start / end 定義（重要）
梁（構造フレーム）の「始端/終端」は **梁の `LocationCurve` の端点**で定義します。

- start（始端） = `LocationCurve.EndPoint(0)`
- end（終端） = `LocationCurve.EndPoint(1)`

この定義は **スターラップだけでなく主筋等の start/end 系オプション**（例: `beamMainBarStartExtensionMm/EndExtensionMm`、`beamMainBarStartBend*/EndBend*`、`beamStirrupStartOffsetMm/EndOffsetMm`）に共通で適用されます。

内部実装では、Revit の shape-driven レイアウトの都合上「軸方向は min→max（axisStart < axisEnd）」の順を維持します。そのため、`EndPoint(0)` が軸の max 側にある場合は **min/max と start/end をマッピング**します。デバッグ用途で `hosts[].beamAxisStartEndByLocationCurve.startIsMinAxis` を返します。

柱主筋の本数（各面本数）は、mapping が `Column.Attr.MainBar.BarsPerFaceX` / `Column.Attr.MainBar.BarsPerFaceY` を返す場合はそれを優先し、無い場合は `Column.Attr.MainBar.BarsPerFace` を使用します（パラメータ名は `RebarMapping.json` のプロファイルで吸収）。

- `BarsPerFaceX` / `BarsPerFaceY` は「各面の本数（4隅込み）」として扱います。矩形柱では 4隅が重複しないように座標で de-dup し、概ね合計本数は `2*X + 2*Y - 4` になります（best-effort）。
- 代表例（SIRBIM）: `BarsPerFaceX` は **Y軸方向に平行な面**（= X固定の面）に並ぶ鉄筋本数、`BarsPerFaceY` は **X軸方向に平行な面**（= Y固定の面）に並ぶ鉄筋本数、という命名規約です。

#### 被り厚さ（Cover）の扱い
- 可能な限り、ホスト要素インスタンスの「鉄筋被り」設定（例: `CLEAR_COVER_TOP/BOTTOM/OTHER` → `RebarCoverType.CoverDistance`）を読み取り、配筋位置に反映します。
- mapping が `Host.Cover.Top/Bottom/Other` を返す場合は、それを優先して上書きします（best-effort）。これは、プロジェクト/ファミリが被りをカスタムのインスタンスパラメータ（例: `かぶり厚-上`）で持っていて、Revit標準のかぶりタイプが実質 `0` になっている場合の救済にも使えます。
- 反映された値は `hosts[].coversMm` に入り、`sources` で由来（`hostInstance` / `mapping` / `default`）も返します。
- さらに、ホスト要素が「面ごとの被り」をインスタンスパラメータ（例: `かぶり厚-上/下/左/右`）として持っている場合は、**断面方向の配置**にはそれらを優先して使います（best-effort）。この値は `hosts[].coverFacesMm`（up/down/left/right）として返します。Revit標準の「かぶりタイプ（ElementId）」を書き換えるものではありません。

#### 柱の上下端（軸方向）の被り厚さ（上/下）の扱い
- 既定（`options.columnAxisEndCoverUsesConcreteNeighborCheck=true`）では、柱の上下端について **軸方向の被り**（上端=top / 下端=bottom）は「端部が外部に露出している場合のみ」適用します。
- 柱の上/下に **コンクリート系**の「構造柱」「構造基礎」が接していると推定できる場合は、その側の軸方向被りを **0** とみなし、主筋/帯筋が端部（ジョイント）まで届くようにします（best-effort）。
- コンクリート判定は best-effort で、`MaterialClass` が取得できる場合はそれを優先し（`steel` / `metal` / `metallic` / `鋼` / `鉄骨` 系は除外）、取得できない場合はマテリアル名の文字列（例: `コンクリート` / `Concrete` / `FC*` / `RC*` など）で判定します。
- 一部のファミリではローカル軸が反転していることがあります。その場合でも `hosts[].axisPositiveIsUp`（+軸が world 上方向か）/`hosts[].axisDotZ` を使って world の上/下を補正し、top/bottom の判定（被り/隣接判定）が逆転しないようにしています（best-effort）。
- デバッグ出力:
  - 検出結果: `hosts[].columnAxisEndConcreteNeighbor`
  - 適用した軸方向被り: `hosts[].columnAxisEndCoverEffectiveMm` / `hosts[].columnAxisEndCoverEffectiveMm_ties`
  - 主筋の端部延長のクランプ: `hosts[].columnMainBarAxisExtensionMm`

#### 被り厚さ（Cover）の確認（安全運用）
- 被り厚さのパラメータ名は、プロジェクト/ファミリ作成者/言語によって変わります。ツール側で「どのパラメータを読むべきか」が確定できない場合（または被りが `options.coverMinMm` 未満の場合）は、`ok:false` / `code:"COVER_CONFIRMATION_REQUIRED"` を返して停止します（この状態では作図しません）。
- `hosts[].coverCandidateParamNames`（被り候補パラメータ名）、`hosts[].coverFacesMmRaw`、`hosts[].coversMm` を確認し、次のいずれかで再実行してください。
  - `options.coverConfirmProceed:true`（「この読み取り/丸めで進めて良い」という明示同意。`options.coverClampToMin:true` の場合は最小値へ丸めます）
  - `options.coverParamNames` で面ごとのパラメータ名を明示指定（`up/down/left/right` の配列）

注意: ユーザーから「このパラメータを読んで/書いて」と依頼が来た場合は、実行前に `snoop` コマンドでパラメータ名・型（長さ/実数/文字列等）を確認し、指定が曖昧ならユーザーに問い合わせてから実行してください。

### 鉄筋タイプ（RebarBarType）の解決について
- まずは「名前の完全一致」（`options.mainBarTypeName` / `options.tieBarTypeName` または mapping の `Common.*BarType`）で `RebarBarType` を探します。
- 見つからない場合でも、指定文字列に `D25` のような径情報が含まれていれば、**径一致で `RebarBarType` を探索**します（命名規則に依存しないため）。
  - 解決結果はホストごとに `mainBarTypeResolve` / `tieBarTypeResolve` として返します（requested/resolved/resolvedBy/diameterMm）。

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
- 梁スターラップはフックタイプ（例: 135度）も指定できます（best-effort）。`beamStirrupUseHooks=true` の場合でも、スターラップは **閉じた形状（閉ループ）** のまま作成し、作成後にインスタンスパラメータでフック種別/回転を設定します。
- 注意: `RebarHookType` は `RebarStyle` との相性があります。帯筋/スターラップ（`style:"StirrupTie"`）では、`StirrupTie` に適合するフックを優先して採用します。`標準フック - 135 度` のように非互換（Standard専用）のフック名が指定されている場合は、その名前での一致は無視し、角度（例: `135°`）から適合フックを探索して採用します。プロジェクト内に適合フックが存在しない場合は、安全のためフックを無効化し、plan に警告を返します（best-effort）。
- 柱の主筋についても、端部延長/短縮や90度折り曲げ（線分表現）を options で指定できます。
- 柱の帯筋（せん断補強筋）についても、開始/終了位置オフセットや135度フック等を options で指定できます（best-effort）。
- 柱帯筋は `Column.Attr.Tie.PatternJson`（mapping）または `options.columnTiePattern` を指定すると、任意の「参照面 + セグメント（方向/本数/ピッチ）」で帯筋セットを作ります（best-effort）。
  - 参照面 `beam_top` / `beam_bottom` は「梁せい（`Host.Section.Height`）+ LocationCurve のZ（=レベル/オフセット由来）」から上下面Zを推定します（best-effort、必要に応じて bbox で補助）。
  - 見つからない場合は通常の1セットにフォールバックします。
  - 互換のため `options.columnTieJointPatternEnabled=true` も残しています（内部的には `columnTiePattern` に変換して実行）。
- 梁スターラップの開始/終了は、構造柱の BoundingBox から「柱面（支持面）」を推定してそこに寄せます（best-effort）。
  - まず結合（Join）された柱を試し、無い場合は梁端点近傍の柱を探索します。
- 既存の（同一ホスト内の）タグ付き鉄筋に対して、UIパラメータの
  - `始端のフック`
  - `終端のフック`
  - `始端でのフックの回転`（環境により `始端のフックの回転` の場合もあります）
  - `終端でのフックの回転`（環境により `終端のフックの回転` の場合もあります）
  をユーザーが調整している場合、`options.hookAutoDetectFromExistingTaggedRebar=true`（既定）で、その設定を次回の自動作成に引き継ぎます（options でフック指定が無い場合、best-effort）。梁スターラップも同様に扱います。

  - 角度の注意: 一部の角度パラメータで `180°` を設定すると `0°` に正規化されることがあります。表示を `180°` 付近にしたい場合は `179.9°` のように僅かにずらす回避策が有効です。

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
| options.coverConfirmEnabled | bool | no | true | true の場合、被りパラメータが不足/曖昧、または `coverMinMm` 未満のときに明示確認を要求します。 |
| options.coverConfirmProceed | bool | no | false | `COVER_CONFIRMATION_REQUIRED` を解除して続行するための明示同意フラグ。 |
| options.coverMinMm | number | no | 40 | 被りの最小値（mm、確認/丸めの基準）。 |
| options.coverClampToMin | bool | no | true | true の場合、`coverMinMm` 未満の被り値を最小値へ丸めます（best-effort）。 |
| options.coverParamNames | object | no |  | 面ごとの被りパラメータ名のヒント: `{ up: string[]; down: string[]; left: string[]; right: string[] }`。 |
| options.beamMainTopCount | int | no |  | 梁主筋の上端本数（ユーザー指定の上書き）。 |
| options.beamMainBottomCount | int | no |  | 梁主筋の下端本数（ユーザー指定の上書き）。 |
| options.beamMainBarStartExtensionMm | number | no | 0 | 梁主筋の始点延長(mm)。正で延長、負で短縮。 |
| options.beamMainBarEndExtensionMm | number | no | 0 | 梁主筋の終点延長(mm)。正で延長、負で短縮。 |
| options.beamMainBarEmbedIntoSupportColumns | bool | no | true | true の場合、支持柱内へ主筋を延長します（best-effort）。 |
| options.beamMainBarEmbedIntoSupportColumnRatio | number | no | 0.75 | 延長長さ = ratio × 支持柱幅（梁軸方向）。 |
| options.beamSupportSearchRangeMm | number | no | 1500 | 支持柱探索範囲（梁端点近傍、mm）。 |
| options.beamSupportFaceToleranceMm | number | no | 250 | 支持柱と梁端面の一致判定許容（mm）。 |
| options.beamStirrupUseHooks | bool | no | true | true の場合、梁スターラップを「開いた折れ線」で作成し、両端にフックを適用します（best-effort）。 |
| options.beamStirrupHookAngleDeg | number | no | 0 | フック角度（deg、例: `135`）。未指定でフック有効の場合は `135` を既定とします（best-effort）。`beamStirrupHookTypeName` が未指定の場合に一致する `RebarHookType` を探索します。 |
| options.beamStirrupHookTypeName | string | no |  | `RebarHookType` の正確な名前（角度より優先）。 |
| options.beamStirrupHookOrientationStart | string | no | `left` | `left`/`right`（Revit の HookOrientation）。 |
| options.beamStirrupHookOrientationEnd | string | no | `right` | `left`/`right`（Revit の HookOrientation）。 |
| options.beamStirrupHookStartRotationDeg | number | no | 0 | 始端フックの回転（deg）。 |
| options.beamStirrupHookEndRotationDeg | number | no | 179.9 | 終端フックの回転（deg）。 |
| options.beamStirrupStartOffsetMm | number | no | 0 | 梁端面からの開始オフセット（mm）。 |
| options.beamStirrupEndOffsetMm | number | no | 0 | 梁端面からの終了オフセット（mm）。 |
| options.columnMainBarBottomExtensionMm | number | no | 0 | 柱主筋の下端延長(mm)。正で延長、負で短縮。 |
| options.columnMainBarTopExtensionMm | number | no | 0 | 柱主筋の上端延長(mm)。正で延長、負で短縮。 |
| options.columnMainBarStartBendLengthMm | number | no | 0 | 柱主筋の始端90度折り曲げ長さ(mm)。 |
| options.columnMainBarEndBendLengthMm | number | no | 0 | 柱主筋の終端90度折り曲げ長さ(mm)。 |
| options.columnMainBarStartBendDir | string | no | `none` | `up`/`down`/`none`（world Z基準）。 |
| options.columnMainBarEndBendDir | string | no | `none` | `up`/`down`/`none`（world Z基準）。 |
| options.columnMainBarsPerFace | int | no | 2 | 柱主筋の「各面の本数」（>=2）。2 は四隅のみ、5 は 16本（矩形柱の例）。 |
| options.columnAxisEndCoverUsesConcreteNeighborCheck | bool | no | true | true の場合、柱上下端の「コンクリート系の構造柱/構造基礎」の隣接を推定し、端部が外部露出でない側は **軸方向被りを0** とみなします（best-effort）。 |
| options.columnConcreteNeighborSearchRangeMm | number | no | 1500 | コンクリート隣接推定の探索範囲（柱上下端周り、mm）。 |
| options.columnConcreteNeighborTouchTolMm | number | no | 50 | 軸方向の「端部面に接している」判定の許容（mm）。 |
| options.columnTieBottomOffsetMm | number | no | 0 | 柱下端面から最初の帯筋までのオフセット(mm)。 |
| options.columnTieTopOffsetMm | number | no | 0 | 柱上端面から最後の帯筋までのオフセット(mm)。 |
| options.columnTieSplitByMidHeight | bool | no | true | true の場合、柱帯筋を中間高さで 2分割し、`base`（柱脚）+`head`（柱頭）の 2セットにします。 |
| options.columnTiePitchBaseMm | number | no |  | 柱脚（base）側の帯筋ピッチ(mm)。未指定/0 の場合は柱頭側の値を流用、なければ `Common.Arrangement.Spacing` を使用します。 |
| options.columnTiePitchHeadMm | number | no |  | 柱頭（head）側の帯筋ピッチ(mm)。未指定/0 の場合は柱脚側の値を流用、なければ `Common.Arrangement.Spacing` を使用します。 |
| options.columnTieUseHooks | bool | no | true | true の場合でも柱帯筋は閉ループのまま作成し、作成後にインスタンスパラメータでフック種別/回転を設定します（best-effort）。 |
| options.columnTieHookAngleDeg | number | no | 0 | フック角度（deg、例: `135`）。未指定でフック有効の場合は `135` を既定とします（best-effort）。`columnTieHookTypeName` が未指定の場合に一致する `RebarHookType` を探索します。 |
| options.columnTieHookTypeName | string | no |  | `RebarHookType` の正確な名前（角度より優先）。 |
| options.columnTieHookOrientationStart | string | no | `left` | `left`/`right`。 |
| options.columnTieHookOrientationEnd | string | no | `right` | `left`/`right`。 |
| options.columnTieHookStartRotationDeg | number | no | 0 | 始端フックの回転（deg）。 |
| options.columnTieHookEndRotationDeg | number | no | 179.9 | 終端フックの回転（deg）。 |
| options.columnTieJointPatternEnabled | bool | no | false | true の場合、柱帯筋を「梁上面基準の上部/下部パターン」に置き換えます（2セット、best-effort）。 |
| options.columnTieJointAboveCount | int | no | 3 | 梁上面基準の上側の帯筋本数（基準面の帯筋を含む）。 |
| options.columnTieJointAbovePitchMm | number | no | 100 | 上側のピッチ(mm)。 |
| options.columnTieJointBelowCount | int | no | 2 | 梁上面基準の下側の帯筋本数（基準面の帯筋は含まない）。 |
| options.columnTieJointBelowPitchMm | number | no | 150 | 下側のピッチ(mm)。 |
| options.columnTieJointBeamSearchRangeMm | number | no | 1500 | 参照梁探索の範囲（柱 bbox をXY方向に膨らませる量、mm）。 |
| options.columnTieJointBeamXYToleranceMm | number | no | 250 | 参照梁の柱 bbox への重なり判定許容（mm）。 |
| options.columnTiePattern | object | no |  | 柱帯筋の任意パターン（推奨）。mapping の `Column.Attr.Tie.PatternJson` と同等。 |
| options.hookAutoDetectFromExistingTaggedRebar | bool | no | true | true の場合、options でフック指定が無いときに「同一ホスト内のタグ付き鉄筋」からフック種別/回転を引き継ぎます（best-effort）。 |
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

## Column.Attr.Tie.PatternJson（例）
reference.kind の対応: `beam_top` / `beam_bottom` / `column_top` / `column_bottom` / `column_mid`。

`Column.Attr.Tie.PatternJson`（または `options.columnTiePattern`）は次のような JSON です（例: 梁上面を基準に、上3本@100、下2本@150）。
```json
{
  "reference": { "kind": "beam_top", "offsetMm": 0, "beamSearchRangeMm": 1500, "beamXYToleranceMm": 250 },
  "segments": [
    { "name": "above", "direction": "up", "startOffsetMm": 0, "count": 3, "pitchMm": 100 },
    { "name": "below", "direction": "down", "startOffsetMm": 150, "count": 2, "pitchMm": 150 }
  ]
}
```

中間高さ（`column_mid`）を基準にする例:
```json
{
  "reference": { "kind": "column_mid", "offsetMm": 0 },
  "segments": [
    { "name": "head", "direction": "up", "startOffsetMm": 0, "count": 6, "pitchMm": 100 },
    { "name": "base", "direction": "down", "startOffsetMm": 0, "count": 6, "pitchMm": 100 }
  ]
}
```

## 関連
- `rebar_apply_plan`
- `rebar_mapping_resolve`
- `rebar_layout_update_by_host`
