# get_elements_by_category_and_level

- カテゴリ: Selection/Query
- 目的: 単一の Revit カテゴリ＋レベルを指定して要素 ID を取得します。

## 概要
このコマンドは、**1つのカテゴリ**（例: Walls / Floors / Structural Framing）と **1つのレベル**（名前または elementId）を指定して、該当する要素の ID をまとめて取得します。  
取得した ID は、`update_wall_parameter` や `move_structural_frame` など他のコマンドに渡して処理することを想定しています。

## 使い方
- メソッド: get_elements_by_category_and_level

### パラメータ
```jsonc
{
  "category": "Walls",
  "levelSelector": {
    "levelName": "Level 3"
    // または: "levelId": 123456
  },
  "options": {
    "includeTypeInfo": true,
    "includeCategoryName": true,
    "maxResults": 0
  }
}
```

- `category` (string, 必須):
  - 論理的なカテゴリ名、または BuiltInCategory の列挙名。
  - 例:
    - `"Walls"` → `OST_Walls`
    - `"Floors"` → `OST_Floors`
    - `"Structural Framing"` → `OST_StructuralFraming`
    - `"Rooms"` → `OST_Rooms`
    - `"Spaces"` → `OST_MEPSpaces`
    - `"Doors"` → `OST_Doors`
    - `"Windows"` → `OST_Windows`
    - `"OST_Walls"`（列挙名を直接指定）

- `levelSelector` (object, 必須):
  - **推奨:** `levelId` (int) – `Level` 要素の Revit elementId  
    レベル名は表記ゆれや打ち間違いが起こりやすいため、可能であれば別コマンドで一度レベルを取得し、その `elementId` をここに渡す運用を基本としてください。
  - 対話的な利用などでレベル名を直接指定したい場合のみ、次の指定も使用できます（両方指定した場合は `levelId` が優先されます）。
    - `levelName` (string) – 例: `"Level 3"`, `"3F"`, `"３階"`  
      - まず完全一致（大文字小文字区別あり）、見つからない場合は大文字小文字を無視して再検索します。

- `options` (object, 任意):
  - `includeTypeInfo` (bool, 既定値: false)  
    - true の場合、各要素に `typeId` を含めます。
  - `includeCategoryName` (bool, 既定値: true)  
    - true の場合、各要素に `categoryName` を含めます。
  - `maxResults` (int, 既定値: 0 = 無制限)  
    - 0 以下の場合は「制限なし」。  
    - 0 より大きい場合、その件数に達したら検索を打ち切ります（タイムアウト回避に有効）。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "walls-level3",
  "method": "get_elements_by_category_and_level",
  "params": {
    "category": "Walls",
    "levelSelector": {
      "levelName": "Level 3"
    },
    "options": {
      "includeTypeInfo": true,
      "includeCategoryName": true,
      "maxResults": 0
    }
  }
}
```

## 戻り値

### 正常終了
```jsonc
{
  "ok": true,
  "msg": "Found 128 elements in category 'Walls' on level 'Level 3'.",
  "items": [
    {
      "elementId": 456789,
      "uniqueId": "d4e55c1a-3f58-4d16-9c35-...",
      "categoryName": "Walls",
      "levelId": 111,
      "levelName": "Level 3",
      "typeId": 2233
    }
  ],
  "debug": {
    "category": "Walls",
    "levelResolvedBy": "NameExact",
    "totalCandidates": 989,
    "filteredCount": 128
  }
}
```

### レベルが見つからない場合
```json
{
  "ok": false,
  "msg": "Level not found. levelName='3F' levelId=null",
  "items": []
}
```

### パラメータ不足
```json
{
  "ok": false,
  "msg": "levelSelector.levelName または levelSelector.levelId のいずれかが必要です。",
  "items": []
}
```

## 備考

- このコマンドは **1 回につき 1 カテゴリのみ** を対象とします。  
  複数カテゴリ（例: Walls と Floors をまとめて取得）の場合は、
  クライアント側でカテゴリごとに本コマンドを複数回呼び出し、`items` をマージしてください。
- `debug` オブジェクトにより、レベルの解決方法（`levelResolvedBy`）や、
  検索対象候補数・フィルタ後の件数を確認できます。
- 非常に大きなモデルでは、`maxResults` を指定して結果件数を抑えたり、
  ページングパターン（`skip`/`limit` など）と組み合わせてタイムアウトを避けることを推奨します。

## 関連コマンド
- get_walls
- summarize_rooms_by_level
- update_wall_parameter
- get_rooms
