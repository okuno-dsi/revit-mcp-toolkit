# IfcCli / IfcCore 詳細マニュアル（JA）

## 1. 概要

`IfcCli` は IFC ファイルを解析・検証するための軽量なコマンドラインツールです。
内部では `IfcCore` という小さなライブラリを利用しており、次のような機能を提供します。

- IFC の簡易モデル（`IfcModel` / `IfcEntity`）の構築
- プロパティの充足率（Fill Rate）集計とプロファイル生成
- プロファイルに基づく IFC ファイルのデータ品質チェック
- `IFCBUILDINGSTOREY` と配置情報を使ったレベル（階）認識
- STEP 拡張 Unicode 文字列（`\X2\...\X0\`）の自動デコード（日本語に対応）

主な用途:

- Revit 等から出力した IFC の「どのプロパティがどの程度埋まっているか」を確認する
- 良い IFC サンプルから「要求プロパティプロファイル」を作成する
- 新しい IFC をプロファイルに照らして検証し、抜けているプロパラメータを洗い出す
- 「3階の梁はどれ？」「2階の部屋（IFCSPACE）はどれ？」といったレベル別の問い合わせ

ツールは IFC ファイルを **読み取り専用** で扱い、書き換えは行いません。

---

## 2. プロジェクト構成

Codex リポジトリ内のパス（例）:

- `IfcCli\`
  - `IfcCli.csproj` – CLI 実行ファイルプロジェクト（`net6.0`）
  - `Program.cs` – コマンドラインのエントリポイントと引数解析
  - `IfcCore\`
    - `IfcCore.csproj` – コアライブラリ（`net6.0`）
    - `Models.cs` – エンティティ、統計、プロファイル、チェック結果などのクラス
    - `Services.cs` – アナライザ／プロファイル生成／チェックの実装
    - `IfcLoader.cs` – STEP 形式テキストのパーサ（レベル・Unicode も含む）
    - `Logging.cs` – 簡易ログ出力
  - `Docs\`
    - `IfcCli_Manual_en.md` – 英語版マニュアル
    - `IfcCli_Manual_ja.md` – このファイル

ビルド後の実行ファイル:

- `IfcCli\bin\Release\net6.0\IfcCli.exe`

---

## 3. ビルドと起動方法

### 3.1 ビルド

```powershell
cd %USERPROFILE%\Documents\VS2022\Ver602\Codex\IfcCli
dotnet build -c Release
```

補足:

- `.NET 6` のサポート終了に関する警告（NETSDK1138）が出ますが、ビルドは成功します。
- 成功すると `bin\Release\net6.0\IfcCli.exe` が生成されます。

### 3.2 実行

```powershell
cd %USERPROFILE%\Documents\VS2022\Ver602\Codex\IfcCli\bin\Release\net6.0
.\IfcCli.exe --help
```

サポートされているコマンド:

- `analyze-sample`
- `check-ifc`
- `stats`
- `list-by-storey`
- `dump-spaces`

各コマンドは実行ディレクトリにログファイルを出力します:

- `IfcProfileAnalyzer_yyyyMMdd_HHmmss.log`

---

## 4. コア概念（IfcCore）

### 4.1 IfcModel / IfcEntity

`IfcModel` は IFC の簡易的なメモリモデルです。

- `SourcePath` – IFC ファイルのフルパス
- `EntitiesById` – 数値 ID（#474 → 474）をキーにした辞書
- `EntitiesByType` – IFC 型名（`"IFCSPACE"` など）をキーにした辞書

`IfcEntity` は 1 つの IFC インスタンスを表します。

- `Id` – 数値 ID（例: 474）
- `IfcType` – 型名（例: `"IFCSPACE"`, `"IFCBEAM"`）
- `GlobalId` – IFC GUID（1 番目の引数）
- `Name` – デコード済みの名前（3 番目の引数、STEP Unicode デコード済み）
- `StoreyId` – 紐づいている `IFCBUILDINGSTOREY` の数値 ID（存在すれば）
- `StoreyName` – 紐づいている階名（例: `"1FL"`, `"2FL"`）
- `Properties` – `PropertyKey -> bool` の辞書（値が入っているかどうか）

`PropertyKey`:

- `Pset` – プロパティセット名（例: `Pset_SpaceCommon`）
- `Prop` – プロパティ名（例: `FloorCovering`）

`Properties` の値は、「そのプロパティに値が入っているかどうか」のフラグです。
値そのもの（数値や文字列）は保持せず、充足率分析に特化しています。

### 4.2 AnalysisResult / ProfileDefinition

`AnalysisResult` は解析結果の統計情報です。

- `SourceFiles` – 解析対象となった IFC ファイル一覧
- `Entities` – IFC 型名ごとの統計 (`EntityStats`)

`EntityStats`:

- `InstanceCount` – その型のインスタンス数
- `Properties` – `PropertyKey -> PropertyStats`

`PropertyStats`:

- `EntityCount` – そのプロパティを持つエンティティ数
- `ValueCount` – 値が入っているエンティティ数
- `FillRate` – `ValueCount / EntityCount`

`ProfileDefinition` は「要求プロパティプロファイル」の定義です。

- `ProfileName`, `ProfileVersion`, `CreatedAt`
- `SourceFiles` – プロファイル生成に使った IFC ファイル一覧
- `EntityRules` – IFC 型名 → `EntityRule`

`EntityRule`:

- `RequiredProperties` – `RequiredPropertyRule` のリスト
  - `Pset`, `Name`, `MinFillRate` – 要求される最小充足率

### 4.3 CheckResult

`check-ifc` 実行結果は `CheckResult` として JSON で出力されます。

- `Ok` – ルールをすべて満たしていれば `true`
- `ProfileName` – 使用したプロファイル名
- `TargetFile` – 対象 IFC ファイル
- `Summary` – エラー／警告件数
- `Items` – 個別の問題 (`CheckItem`)

`CheckItem`:

- `Severity` – `"error"` または `"warning"`
- `EntityName` – IFC 型名（例: `IFCSPACE`）
- `IfcGuid` – 対象エンティティの GUID
- `Pset`, `Property` – 問題があったプロパティ
- `Message` – 説明文

### 4.4 レベル（階）の扱い

階情報は 2 段階で決定されます。

1. **IFCRELCONTAINEDINSPATIALSTRUCTURE による直接紐づけ（優先）**

   - `IFCBUILDINGSTOREY` を読み取り:
     - ID（例: `#114`）
     - Name（例: `"2FL"`、Unicode デコード済み）
     - Elevation（高さ、IFC の長さ単位）
   - `IFCRELCONTAINEDINSPATIALSTRUCTURE` のうち `RelatingStructure` が建物ストーリーであるものを辿り、
     `RelatedElements` に含まれるエレメントに `StoreyId` / `StoreyName` を付与します。

2. **`IFCSPACE` に対するフォールバック推定**

   一部の `IFCSPACE` が上記のリレーションを持たないケースに対応するため、
   オブジェクト配置から Z（高さ）を計算し、最も近いストーリーに割り当てます。

   - `IFCCARTESIANPOINT` から Z 座標を取得
   - `IFCAXIS2PLACEMENT3D` の location から Z を取得
   - `IFCLOCALPLACEMENT` は親の placement Z ＋自身の Axis2Placement3D Z を再帰的に合算
   - `IFCSPACE` の `ObjectPlacement` を辿って Z を計算し、Z に最も近い `IFCBUILDINGSTOREY` を選択

この処理により、Revit から出した一般的な IFC について、
「2 階の部屋」「3階の梁」といったレベル別の問い合わせが可能になります。

### 4.5 STEP Unicode の自動デコード

IFC のテキストは STEP 拡張表記で Unicode が埋め込まれていることがあります。

例:

- `'\X2\4E8B52D95BA4\X0\201'` → `"事務室201"`  
- `'\X2\97627A4D\X0\:8261599'` → `"面積:8261599"`

`IfcLoader.Unwrap` は次の処理を行います。

1. 文字列をトリムし、前後の `'` を除去
2. `\X2\...\X0\` パターンを検出
3. 4 桁の 16 進数を 1 文字の Unicode として変換
4. 変換結果を .NET の `string` として返却

このデコードは次の箇所で使われます。

- `IFCBUILDINGSTOREY.Name`（例: `"1FL"`, `"2FL"` や日本語名）
- エンティティの `Name`（`IFCSPACE` の部屋番号など）
- プロパティセット名／プロパティ名

CLI コマンドは、すべてデコード済みの文字列を直接扱えます。

---

## 5. コマンド一覧

### 5.1 共通仕様

- 最初の引数がコマンド名です（例: `analyze-sample`）。
- 以降の引数はオプション（`--ifc`, `--out` など）です。
- 実行のたびに `IfcProfileAnalyzer_yyyyMMdd_HHmmss.log` をカレントフォルダに出力します。

一般形:

```powershell
.\IfcCli.exe <command> [options]
```

### 5.2 `analyze-sample` – プロファイル生成

良質な IFC サンプルから「要求プロパティプロファイル」を作成します。

**書式**

```powershell
.\IfcCli.exe analyze-sample --input sample1.ifc [sample2.ifc ...] --out profile.json --min-fill-rate 0.9
```

**オプション**

- `--input` `<path ...>`  
  1 個以上の IFC ファイル。次の `--` オプションが現れるまでが入力として解釈されます。

- `--out` `<file>`  
  出力するプロファイル JSON のパス（省略時は `profile.json`）。

- `--min-fill-rate` `<double>`  
  必須プロパティとみなすための最小充足率（既定値: `0.9`）。

**動作**

1. 指定された IFC をすべて読み込み、`Analyzer` でプロパティ統計を作成。
2. `ProfileGen` が `ProfileDefinition` を生成し、
   `FillRate >= MinFillRate` を満たすプロパティを「要求プロパティ」として選択。
3. 結果を JSON で `--out` 先に保存。

### 5.3 `check-ifc` – プロファイルに対するチェック

`analyze-sample` で作成したプロファイルに対して、IFC を検証します。

**書式**

```powershell
.\IfcCli.exe check-ifc --ifc model.ifc --profile profile.json --out check.json
```

**オプション**

- `--ifc` `<file>` – チェック対象 IFC（必須）
- `--profile` `<file>` – 使用するプロファイル JSON（必須）
- `--out` `<file>` – 結果 JSON のパス（既定: `check.json`）

**動作**

1. プロファイル JSON を `ProfileDefinition` として読み込み。
2. IFC を `IfcLoader` で読み込み。
3. `ProfileCheck` がプロファイルと実際の充足率を比較し、
   不足／不足気味のプロパティを `CheckResult` にまとめる。
4. JSON として `--out` 先に出力。

終了コードは常に `0` です。合否判定は JSON の `Ok` フラグや `Items` を参照してください。

### 5.4 `stats` – 詳細統計の出力

1 つの IFC ファイルについて、プロパティ充足率の詳細統計を出力します。

**書式**

```powershell
.\IfcCli.exe stats --ifc model.ifc --out stats.json
```

**オプション**

- `--ifc` `<file>` – 対象 IFC（必須）
- `--out` `<file>` – 結果 JSON のパス（既定: `stats.json`）

**動作**

`analyze-sample` とほぼ同じ処理ですが、プロファイル生成ではなく raw な統計 (`AnalysisResult`) をそのまま JSON 出力します。

### 5.5 `list-by-storey` – 階別要素一覧

指定した階（ストーリー）に属するエレメントを、IFC 型でフィルタして一覧表示します。

**書式**

```powershell
.\IfcCli.exe list-by-storey --ifc model.ifc --storey 3FL [--type IFCBEAM]
```

**オプション**

- `--ifc` `<file>` – 対象 IFC（必須）
- `--storey` `<name>` – 階名（例: `1FL`, `2FL`, `3FL`）（必須）  
  ※ エイリアス `--level` も利用可能
- `--type` `<IFC type>` – IFC 型名による絞り込み（例: `IFCBEAM`, `IFCSPACE`）

**動作**

1. IFC を読み込み、`StoreyName` が設定されたエンティティを集計。
2. `StoreyName == storey`（大文字小文字は無視）でフィルタ。
3. `--type` 指定があれば `IfcType` でもフィルタ。
4. ID 順に並べて、次のように出力:

   ```text
   Elements on storey '2FL' of type IFCSPACE: count=...
     #474  type=IFCSPACE  Name=33  GlobalId=...
   ```

**使用例**

- 「3FL の梁一覧」  
  `--storey 3FL --type IFCBEAM`

- 「2FL の部屋（IFCSPACE）一覧」  
  `--storey 2FL --type IFCSPACE`

階名は `IFCBUILDINGSTOREY.Name` を Unicode デコードした値が使われるため、
日本語の階名をそのまま指定することも可能です。

### 5.6 `dump-spaces` – IFCSPACE 診断

`IFCSPACE` の件数と `Name` / `StoreyName` を一覧表示する診断用コマンドです。

**書式**

```powershell
.\IfcCli.exe dump-spaces --ifc model.ifc
```

**動作**

1. IFC を読み込み、`IFCSPACE` をカウント。
2. 次の形式で一覧表示します。

   ```text
   IFCSPACE count=120
     #474  Name=33  Storey=2FL
     #506  Name=34  Storey=2FL
     ...
   ```

階推定や日本語デコードが正しく動いているか確認する際に利用してください。

### 5.7 `export-ids` – IDS ファイルへのエクスポート

`analyze-sample` で生成したプロファイル JSON から、buildingSMART IDS 形式の XML を出力します。

**書式**

```powershell
.\IfcCli.exe export-ids --profile profile.json --out review_requirements.ids.xml [--include-comments]
```

**オプション**

- `--profile` `<file>` – 入力プロファイル JSON（`analyze-sample` の出力）（必須）  
- `--out` `<file>` – 出力 IDS XML パス（必須）  
- `--include-comments` – 指定すると、プロファイル名や MinFillRate を XML コメントとして追記

**動作**

1. プロファイル JSON を `ProfileDefinition` として読み込みます。
2. `profile.EntityRules` の各エンティティ（例: `IfcSpace`, `IfcWall`）について:
   - `<specification name="<Entity>_Requirements">` を作成
   - `<applicability><entity>IfcSpace</entity></applicability>` のように適用対象エンティティを設定
   - `<requirements>` 以下に、各要求プロパティを `<property>` 要素として追加します。

   例:

   ```xml
   <property>
     <pset>Pset_SpaceCommon</pset>
     <name>GrossArea</name>
     <occurrence>required</occurrence>
   </property>
   ```

3. 全体としては次のような IDS 文書になります。

   ```xml
   <ids>
     <specification name="IfcSpace_Requirements">
       <applicability>
         <entity>IfcSpace</entity>
       </applicability>
       <requirements>
         <!-- property entries -->
       </requirements>
     </specification>
     <!-- 他のエンティティごとの specification -->
   </ids>
   ```

**エラー処理**

- プロファイル JSON が読めない・不正な場合 → 標準エラー出力にメッセージ、終了コード `1`  
- 出力ファイルに書き込めない場合（権限など） → メッセージ、終了コード `2`  


---

## 6. 具体的なワークフロー例

### 6.1 良い IFC サンプルからプロファイル作成

1. Revit 等から「模範的」な IFC を 1 つ以上エクスポート。
2. 次のように実行:

   ```powershell
   .\IfcCli.exe analyze-sample `
     --input good1.ifc good2.ifc `
     --out profile.json `
     --min-fill-rate 0.9
   ```

3. `profile.json` を確認し、必要なプロパティが含まれているか確認。

### 6.2 新規 IFC をプロファイルに対してチェック

1. 上記で生成した `profile.json` を用意。
2. 新しい IFC について次を実行:

   ```powershell
   .\IfcCli.exe check-ifc --ifc new.ifc --profile profile.json --out new_check.json
   ```

3. `new_check.json` を開き、
   不足しているプロパティや充足率の低いプロパティを確認。

### 6.3 レベル別質問への回答（「2階の部屋」など）

例: サンプル IFC `Revit_BIM申請サンプルモデル_01.ifc` の 3 階の梁:

```powershell
.\IfcCli.exe list-by-storey `
  --ifc Revit_BIM申請サンプルモデル_01.ifc `
  --storey 3FL `
  --type IFCBEAM
```

2 階の部屋（IFCSPACE）一覧:

```powershell
.\IfcCli.exe list-by-storey `
  --ifc Revit_BIM申請サンプルモデル_01.ifc `
  --storey 2FL `
  --type IFCSPACE
```

階の割り当てや部屋名が想定通りか不安な場合は、次で診断できます。

```powershell
.\IfcCli.exe dump-spaces --ifc Revit_BIM申請サンプルモデル_01.ifc
```

---

## 7. 前提条件と制限事項

### 7.1 対応 IFC バージョンとスコープ

- テキスト STEP 形式の IFC2x3 / IFC4 を対象としています。
- ただし、完全な IFC スキーマを解釈しているわけではなく、主に次のエンティティにフォーカスしています。
  - `IFCPROPERTYSET` / `IFCPROPERTYSINGLEVALUE`
  - `IFCRELDEFINESBYPROPERTIES`
  - `IFCBUILDINGSTOREY`
  - `IFCRELCONTAINEDINSPATIALSTRUCTURE`
  - `IFCCARTESIANPOINT` / `IFCAXIS2PLACEMENT3D` / `IFCLOCALPLACEMENT`
- ジオメトリ（ソリッド・面等）は解析しておらず、
  高さ（Z）と階の判定に必要な最低限の情報のみ利用しています。

### 7.2 プロパティ値の扱い

- `Properties` には「値が入っているかどうか」だけが格納され、
  値そのもの（数値／文字列）は保持していません。
- Enum や Measure 型なども現在は「空でないかどうか」という判定だけです。

値の中身を解析・分類したい場合は、`IfcLoader` と `Analyzer` の拡張が必要です。

### 7.3 階推定の精度

- `IFCRELCONTAINEDINSPATIALSTRUCTURE` による紐づけがあれば、それを優先します。
- `IFCSPACE` のフォールバックでは、垂直方向の距離が最も近いストーリーに割り当てます。
- 現状では、「あまりにも遠い場合は無視する」といった閾値は設けていません。
  （必要になれば `|Z - Elevation|` による上限を導入することも可能です。）

### 7.4 性能

- ファイルを 1 行ずつ読み込み、すべての生エンティティ情報をメモリ上に保持します。
- 一般的な建築モデルのサイズであれば問題ありませんが、超大型 IFC ではメモリ使用量が増えます。
- 現時点ではストリーミング型の処理ではありません。

---

## 8. 拡張のポイント

ツールは拡張しやすい構成になっています。

- 新しい CLI コマンドを追加する場合:
  - `Program.cs` に `RunXxx` メソッドを追加
  - メインスイッチにコマンド名を追加

- 追加の関係や要素を解析したい場合:
  - `IfcLoader` に新しい型やリレーションの処理を追加
  - 必要に応じて `IfcEntity` にプロパティを追加

- よりリッチな出力（例: レベル別・カテゴリ別の JSON レポート）:
  - `IfcModel.EntitiesByType` / `EntitiesById` をもとに `System.Text.Json` で整形出力

文字列の Unicode デコードはすべて `Unwrap` に集約されています。
IFC の STRING 引数を読み取る新しいコードは、そのまま Unicode（日本語含む）を扱えます。
