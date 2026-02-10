# 水平ブレース配置（梁ベイ + パターングリッドUI）マニュアル — RevitMCP

目的
- 指定レベルの梁配置からベイ（矩形セル）を自動検出し、WPFグリッドUIで水平ブレースのパターンとタイプを指定して Revit に配置するための手順・仕様をまとめます。
- `dryRun` により、モデルを変更せずに検出ベイ・配置計画（JSON）を確認できます。

対象バージョン
- Revit 2024 / RevitMCPAddin (.NET Framework 4.8)

---

## place_roof_brace_from_prompt

梁配置からベイを検出し、UIで指定したパターンに従って水平ブレースを配置します（現行実装は StructuralType.Beam として作成）。

- method: `place_roof_brace_from_prompt`
- 実行フロー
  1. 梁（構造フレーム/梁）を収集し、ベイ（矩形）を自動検出
  2. 既存ブレースを検出して UI に赤線表示
  3. UIでベイごとのパターン・タイプを編集
  4. OK で計画確定（`dryRun=false` のとき Revit に配置）
  5. 計画 JSON を `%LOCALAPPDATA%\RevitMCP\logs` に保存

### params

|キー|型|必須|既定|説明|
|---|---|---|---|---|
|`levelName`|string|必須|—|対象レベル名。梁の `LevelId` が空のモデルでも、レベル高さ近傍 Z の梁を自動拾うフォールバックあり。|
|`xGrids`|string[]|任意|モデル通り芯名|X方向の通り芯名ラベル（表示のみ）。省略時はモデルの通り芯（Grid）名を自動取得してヘッダーに表示します（ベイ境界とは独立）。|
|`yGrids`|string[]|任意|モデル通り芯名|Y方向の通り芯名ラベル（表示のみ）。省略時はモデルの通り芯（Grid）名を自動取得してヘッダーに表示します（ベイ境界とは独立）。|
|`useG`|bool|任意|true|判定文字列に `G` を含む梁タイプを採用する（markContains 未指定時）。|
|`useB`|bool|任意|true|判定文字列に `B` を含む梁タイプを採用する（markContains 未指定時）。|
|`ignore`|string / string[]|任意|空|判定文字列に含まれていたら除外する記号。例: `"ignore":"Z,#"`。|
|`markContains`|string / string[]|任意|空|指定すると `markParam` で取得した文字列にこのトークンを含む梁だけ採用（G/B 判定は無視）。例: `"markContains":["SG"]`。|
|`markParam`|object|任意|空|G/B 判定に使う「梁タイプパラメータ」の指定（柔軟指定）。未指定時は `"ХДНЖ"` → タイプ名の順でフォールバック。|
|`braceTypes`|object[]|必須|—|UIで選べるブレースタイプ一覧。各要素 `{code,symbol,typeName,familyName?}`。UI表示は `symbol:typeName`。|
|`defaultBraceTypeCode`|string|任意|braceTypes先頭|UIの初期選択タイプ。|
|`braceTypeFilterParam`|object|任意|空|**ブレースタイプ絞り込み用のパラメータ指定**（ParamResolver形式）。例: `{ "paramName": "符号" }`。|
|`braceTypeContains`|string[]|任意|空|ブレースタイプを **含むトークンで絞り込み**。`braceTypeFilterParam` があればその値、なければ符号/Type Mark/TypeName を参照。|
|`braceTypeExclude`|string[]|任意|空|ブレースタイプの **除外トークン**。|
|`braceTypeFamilyContains`|string[]|任意|空|**ファミリ名**に含むトークンで絞り込み。|
|`braceTypeNameContains`|string[]|任意|空|**タイプ名**に含むトークンで絞り込み。|
|`zOffsetMm`|double|任意|0.0|配置ブレースの Z オフセット（mm）。|
|`dryRun`|bool|任意|false|true の場合は UI/計画作成のみで配置しない。|

#### markParam の指定方法
`get_parameter_identity` で取得できる情報に合わせて指定できます。

- 名前で指定:
```json
"markParam": { "paramName": "符号" }
```

- 共有パラメータ GUID で指定:
```json
"markParam": { "guid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" }
```

- built-in / paramId で指定（負の値は built-in として扱われます）:
```json
"markParam": { "paramId": -1234567 }
```

- paramId で指定（正の値も可。`get_parameter_identity` の戻り値をそのまま使えます）:
```json
"markParam": { "paramId": 123456 }
```

### 使用例

#### 1) ベイ検出とUI確認（dryRun）
```json
{
  "levelName": "2FL",
  "xGrids": ["X1","X2","X3"],
  "yGrids": ["Y1","Y2","Y3"],
  "useG": true,
  "useB": false,
  "braceTypes": [
    { "code":"HB", "symbol":"HB", "typeName":"PHRFLH1_HB" }
  ],
  "defaultBraceTypeCode": "HB",
  "zOffsetMm": 0.0,
  "dryRun": true
}
```

戻り値には `summary` と `planLogPath` が含まれます。  
`planLogPath` に保存された JSON で検出ベイ・計画内容を確認できます。

#### 2) 実配置（dryRun=false）
上記と同じ条件で `dryRun:false` にして実行し、UIでパターンを指定後 OK を押します。

#### 3) 4FL / 符号に SG を含む梁だけ対象（dryRun）
```json
{
  "levelName": "4FL",
  "markParam": { "paramName": "符号" },
  "markContains": ["SG"],
  "braceTypes": [
    { "code":"HV2", "symbol":"HV2", "typeName":"HV2" },
    { "code":"HV20A", "symbol":"HV20A", "typeName":"HV20A" },
    { "code":"HV30", "symbol":"HV30", "typeName":"HV30" }
  ],
  "defaultBraceTypeCode": "HV2",
  "zOffsetMm": 0.0,
  "dryRun": true
}
```

#### 4) ブレースタイプの絞り込み（ファミリ名・タイプ名）
```json
{
  "levelName": "2FL",
  "gridSource": "selection",
  "gridElementIds": [101,102,103],
  "braceTypeFamilyContains": ["ﾌﾞﾚｰｽ"],
  "braceTypeNameContains": ["L"]
}
```

### UI操作

- 上部コンボで「符号:タイプ名」を選択。
- UI はモデルレスです（表示中も Revit 側でズーム/パン等が可能）。OK/Cancel で確定すると処理が進みます。
- 各ベイセルのクリックでパターンが循環します:
  - `None → X → \ (BackSlash) → / (Slash) → None`
- 非 None にした最初のタイミングのタイプが、そのセルに記憶されます。
- 既にパターンが入っているセルでタイプだけ変えたい場合:
  1. 上部コンボでタイプ変更
  2. そのセルを 1 回クリック  
  → 形状は変えず、タイプのみ更新されます。
- ヘッダーの通り芯名（Grid）は **表示用**です。ベイ境界（ブレース配置の基準）は梁等の配置から自動検出されるため、通り芯位置と一致しない場合があります（仕様）。

### 既存ブレースの扱い

- 既存の水平ブレース（Brace/Beam が混在していても）検出できたベイは赤い斜線で表示されます（LevelId が無効な場合も Z 近傍等でフォールバック）。
- そのベイをクリックすると:
  - 1回目: 上書きモードに入り、黒線パターン選択開始（配置時に既存を削除）
  - パターンを `None` まで戻すと上書き解除 → 赤線表示に戻り **既存を残す** ルールです。

### ログ / 出力

- 計画 JSON は `%LOCALAPPDATA%\RevitMCP\logs\roof_brace_plan_<Project>_<timestamp>.json` に保存されます。
- `dryRun=false` の応答 `summary` には:
  - `baysWithBraces`: パターンが None 以外のベイ数
  - `braceInstancesCreated`: 作成したブレース数
  - `braceElementIds`: 作成した elementId 一覧

---

## よくある注意点 / トラブルシュート

- **UIが出ない**
  - クライアントが Revit のサーバポート（例: 5210）と一致しているか確認。
  - Addin はポーリングで受信するため、実行後数秒待つ。
  - ウィンドウが Revit の背面や別モニタに出ることがあるためタスクバーから前面に出す。

- **ベイが検出されない**
  - `useG/useB/ignore/markParam` の判定条件で梁が全て除外されていないか確認。
  - 梁の `LevelId` が空のモデルでも Z フォールバックが動きますが、隣接レベル間隔が大きい場合は自動拡張後でも拾えないことがあります。その場合は条件を見直してください。

- **ブレースが置かれない**
  - ベイセルを一度もクリックせず OK すると全て None のままなので 0 本になります。
  - `braceTypes[*].typeName` がプロジェクト内に存在するか確認。

- **配置されたが 2FL ビューで見えない**
  - ブレースファミリによっては `LevelId` が空になることがあるため、配置後に参照レベルを明示設定しています。
  - それでも見えない場合は、ビューのカテゴリ/フィルタ（構造フレームの表示、フィルタ条件、作業セットなど）を確認してください。
