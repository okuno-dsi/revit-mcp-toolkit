
# Revitビュー要素のDXFエクスポート手順 (壁・グリッド対応版)

## 1. 目的

Revitで現在開いているビューに表示されている壁の基準線と、グリッド線（バブル付き）を、それぞれ指定したレイヤー名でDXFファイルとしてエクスポートします。

**【重要: IDの取り扱いについて】**
Revit要素の識別には、`Element ID`（整数値）と`Unique ID`（GUID文字列）の2種類があります。`Unique ID`はプロジェクト間で一意であり、より堅牢な識別子として推奨されます。Revit MCPアドインは、`Unique ID`が提供された場合、自動的に対応する`Element ID`に変換して処理する機能を備えています。可能な限り`Unique ID`の使用を優先してください。

## 2. 前提条件

*   Revitが起動しており、モデルが開かれていること。
*   Revitアドイン（RevitMCPAddin）がロードされており、サーバーが動作していること。
*   Revitアドインの `export_curves_to_dxf` コマンドが、`curves`と`grids`のパラメータを同時に受け付ける新しい仕様に対応していること。
*   DXFファイルの出力先ディレクトリが事前に存在すること。

## 3. スクリプトの概要 (`export_walls_and_grids_v2.py`)

このスクリプトは、以下のステップで動作します。

1.  現在アクティブなビューの`Element ID`または`Unique ID`を取得します。
2.  そのビューに表示されているすべての要素の`Element ID`または`Unique ID`のリストを取得します。
3.  プロジェクト内のすべての壁とグリッドの情報を取得します。
4.  ビューに表示されている壁とグリッドをフィルタリングし、DXFエクスポート用のデータを作成します。
5.  作成したデータを、新しいAPI仕様に準拠した形式でDXFファイルとしてエクスポートします。

## 4. パラメータ

*   `--port` (オプション): Revitアドインが動作しているポート番号。デフォルト: `5210`
*   `--output` (オプション): 出力するDXFファイルのフルパス。デフォルト: `C:/tmp/walls_and_grids_v2.dxf`
*   `--grid-layer` (オプション): グリッド線とバブルを出力するレイヤー名。デフォルト: `グリッド`
*   `--wall-layer` (オプション): 壁の基準線を出力するレイヤー名。デフォルト: `壁`
*   `--radius` (オプション): グリッドバブルの半径（単位：mm）。デフォルト: `150.0`

## 5. 実行コマンド

Revitで対象のビューを開いた状態で、コマンドプロンプトで以下のコマンドを実行します。

```bash
chcp 65001 && set PYTHONIOENCODING=utf-8 && python Python/export_walls_and_grids_v2.py --output "C:/path/to/your/output.dxf" --grid-layer "MyGrids" --wall-layer "MyWalls"
```

*   各パラメータは必要に応じて変更してください。

## 6. API仕様のポイント

このスクリプトが利用する `export_curves_to_dxf` コマンドは、以下のJSON形式のパラメータを受け付けます。

*   `curves`: 線分（壁など）のリスト。各要素は `type`, `start`, `end`, `layer` を持ちます。
*   `grids`: グリッドのリスト。各要素は `gridId`, `geometry`, `name`, `position`, `radius`, `layer` を持ちます。
*   座標のキーは `x`, `y` の小文字です。

```json
{
  "curves": [
    {"type": "Line", "start": {"x": 0, "y": 0}, "end": {"x": 1000, "y": 0}, "layer": "壁"}
  ],
  "grids": [
    {
      "gridId": 123,
      "geometry": {"type": "Line", "start": {"x": 0, "y": 0}, "end": {"x": 0, "y": 1000}},
      "name": "X1",
      "position": {"x": 0, "y": 0},
      "radius": 150,
      "layer": "グリッド"
    }
  ],
  "outputFilePath": "C:/tmp/walls_and_grids_v2.dxf",
  "layerName": "DefaultLayer"
}
```

## 7. 注意事項

*   **フルパス指定:** `outputFilePath` は必ずフルパスで指定してください。
*   **出力先ディレクトリ:** 出力先のディレクトリは事前に作成しておく必要があります。
*   **エラー:** エラーが発生した場合は、コマンドプロンプトのメッセージを確認してください。

