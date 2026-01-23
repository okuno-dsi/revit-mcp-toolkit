# 既存壁に密着する壁を作成（面合わせ）

- カテゴリ: ElementOps
- 種別: write
- 目的: 既存壁の指定側に、指定した壁タイプの「密着（面合わせ）壁」を作成します。

既存壁の指定した側・配置基準（仕上面/躯体面/芯）に「密着（面合わせ）」するように、指定タイプの新規壁を作成します。

典型用途: 選択中の壁の **-Y 側** に、仕上げ壁タイプ（例: `"(内壁)W5"`）を重ね壁として密着配置する。

## コマンド
- 正式: `element.create_flush_walls`
- 別名: `create_flush_walls`

## パラメータ
- `sourceWallIds`（任意, `int[]`）: 対象となる既存壁ID。省略/空の場合は現在選択（Wallのみ）を使用します。
- `newWallTypeNameOrId`（必須, `string`）: 新規壁の `WallType` を **名前** または **ElementId文字列** で指定します。
  - 互換キー（`newWallTypeNameOrId` が空のときのみ使用）: `newWallTypeName`, `wallTypeName`, `wallTypeId`
- `sideMode`（任意, `string`, 既定 `ByGlobalDirection`）: 既存壁のどちら側に作成するか。
  - `ByGlobalDirection`（「-Y側」指定に推奨）
  - `ByExterior`
  - `ByInterior`
- `globalDirection`（任意, `double[3]`, 既定 `[0,-1,0]`）: `sideMode=ByGlobalDirection` のときのみ使用。
  - 「仕上面の法線（2D）」がこの方向に最も近い側を選択します（XYのみ。Zは無視）。
- `sourcePlane`（任意, `string`, 既定 `FinishFace`）: 既存壁側の基準（接する側）。
  - `FinishFace` / `CoreFace` / `WallCenterline` / `CoreCenterline`
- `newPlane`（任意, `string`, 既定 空）: 新規壁側の基準（既存壁に向く側）。空の場合は `sourcePlane` と同じ。
- `newExteriorMode`（任意, `string`, 既定 `MatchSourceExterior`）: 新規壁の Exterior 方向の決め方。
  - `AwayFromSource` / `MatchSourceExterior` / `OppositeSourceExterior`
- `miterJoints`（任意, `bool`, 既定 `true`）: 折れ点をミターで接続（Line-Line のみ、best-effort）。
- `copyVerticalConstraints`（任意, `bool`, 既定 `true`）: 上下拘束（ベース/トップのレベル、オフセット、高さ）を複製します。
  - 注意: **Attach Top/Base（アタッチ）** は複製しません（Revit 2023 の制約）。

## 例（JSON-RPC）
現在選択中の壁の **グローバル -Y 側** に密着する壁を作成:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "element.create_flush_walls",
  "params": {
    "newWallTypeNameOrId": "(内壁)W5",
    "sideMode": "ByGlobalDirection",
    "globalDirection": [0, -1, 0],
    "sourcePlane": "FinishFace",
    "newPlane": "FinishFace",
    "newExteriorMode": "MatchSourceExterior",
    "miterJoints": true,
    "copyVerticalConstraints": true
  }
}
```

## 戻り値（概形）
- `ok`, `msg`
- `createdWallIds`（`int[]`）
- `warnings`（`string[]`）

## 補足
- 面合わせは、壁の `LocationCurve`（壁芯）と `CompoundStructure`（層構造）から距離を算出して行います（Faceベースのオフセットより安定）。
- `LocationCurve` が取得できない壁はスキップされ、`warnings` に理由が出ます。
