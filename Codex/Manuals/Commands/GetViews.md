# get_views — プロジェクト内のビュー一覧

概要
- ドキュメント内のビューを列挙します。既定は基本情報（ID/名前/種類/テンプレートか/印刷可否）。`detail:true` で詳細情報（スケール/ビューファミリ型/テンプレート/配置シートなど）を追加します。

メソッド
- `get_views`

パラメータ（すべて任意）
- `includeTemplates` (bool, 既定: false): ビューテンプレートも含める
- `detail` (bool, 既定: false): 追加情報を含める
- `viewType` (string): ViewType 名の部分一致フィルタ（例: "plan", "threeD" など）
- `nameContains` (string): Name の部分一致フィルタ

レスポンス例（基本）
```
{
  "ok": true,
  "count": 123,
  "views": [
    {
      "viewId": 314,
      "uniqueId": "...",
      "name": "1FL 平面図",
      "viewType": "FloorPlan",
      "isTemplate": false,
      "canBePrinted": true
    }
  ]
}
```

レスポンス例（detail:true）
```
{
  "ok": true,
  "count": 2,
  "views": [
    {
      "viewId": 314,
      "uniqueId": "...",
      "name": "1FL 平面図",
      "viewType": "FloorPlan",
      "isTemplate": false,
      "canBePrinted": true,
      "scale": 100,
      "discipline": "Architectural",
      "viewFamilyTypeId": 987,
      "viewFamilyTypeName": "建築平面図",
      "templateViewId": 456,
      "templateName": "平面図_標準",
      "cropBoxActive": true,
      "cropBoxVisible": false,
      "placedOnSheet": true,
      "sheetIds": [2001]
    }
  ]
}
```

呼び出し例（PowerShell）
```
python Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_views --params '{"detail":true,"includeTemplates":false}' --output-file Work/<ProjectName>_<Port>/Logs/views.json
```

関連
- 開いているUIビュー（タブ/ウィンドウ）だけを列挙したい場合は `list_open_views` を使用してください。

