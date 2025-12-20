# create_area_scheme

- カテゴリ: Area
- 目的: （未サポート）エリアスキーム（AreaScheme）を作成します。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

重要
- Revit 2024 のAPI/現行アドインでは、新しい `AreaScheme` の作成はサポートされていません。このコマンドは `ok=false` を返します。
- Revit のUIでエリアスキームを作成した後、`get_area_schemes` で `schemeId` を取得してください。

## 使い方
- メソッド: create_area_scheme

- パラメータ: なし（無視されます）

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "create_area_scheme",
  "params": {}
}
```

### 現在の戻り値例（現状の挙動）
```json
{
  "ok": false,
  "msg": "AreaScheme の作成は、このRevitバージョン/APIではサポートされていません（UIで作成後、get_area_schemesで取得してください）。"
}
```

## 関連コマンド
- get_area_schemes
- create_area
- get_areas
- get_area_params
- update_area
- move_area
- delete_area
- get_area_boundary_walls
- 
