# replace_view_on_sheet

- カテゴリ: ViewOps / ViewSheet
- 目的: シート上に既に配置されているビュー（または集計表）を、別のビューに入れ替えます。位置はそのまま維持することができ、必要に応じて回転・縮尺も引き継げます。
- 目的: シート上に既に配置されているビュー（または集計表）を、別のビューに入れ替えます。位置はそのまま維持することができ、必要に応じて回転・縮尺も引き継げます。さらに、スケールが異なるビュー同士でも「グリッド交点」を基準に位置合わせできます。

## 概要

`replace_view_on_sheet` は、

- 「このシートのこのビューポート（または集計表）を、別のビューに差し替えたい」  
- 「レイアウト位置はそのまま、ビューだけ差し替えたい」

といったケースを簡単に扱うためのコマンドです。

- 既存の配置要素（Viewport / ScheduleSheetInstance）を特定し、
- 旧ビューを削除して、
- 新しいビューを同じ位置（または指定位置）に再配置します。

## 使い方

- メソッド: `replace_view_on_sheet`

### 主なパラメータ

既存配置の特定（どのビューを置き換えるか）

| 名前 | 型 | 必須 | 説明 |
|------|----|------|------|
| viewportId | int | 条件付き | 既にシート上にある Viewport / ScheduleSheetInstance の ID。指定されていれば最優先で使われます。 |
| sheetId / uniqueId / sheetNumber | int / string | 条件付き | `viewportId` が無い場合に使用するシートの指定。 |
| oldViewId | int | 条件付き | 置き換え対象の旧ビュー ID。 |
| oldViewUniqueId | string | 条件付き | 置き換え対象の旧ビュー UniqueId。 |

新しいビューの指定

| 名前 | 型 | 必須 | 説明 |
|------|----|------|------|
| newViewId | int | どちらか必須 | 新しく配置したいビューの ID（テンプレートは不可）。 |
| newViewUniqueId | string | どちらか必須 | 新しく配置したいビューの UniqueId。 |

位置・回転・縮尺の制御

| 名前 | 型 | 必須 | 既定値 | 説明 |
|------|----|------|--------|------|
| keepLocation | bool | いいえ | true | true の場合、旧ビューポート（または集計表）の中心位置をそのまま新しいビューにも使います。 |
| centerOnSheet | bool | いいえ | false | `keepLocation:false` のときのみ有効。true ならシート中心に新しいビューを配置します。 |
| location | object `{x,y}` | 条件付き | なし | `keepLocation:false` かつ `centerOnSheet:false` のときに使用。シート座標の中心位置（mm）。 |
| copyRotation | bool | いいえ | true | 旧 Viewport の `Rotation` を新しい Viewport にコピーします。 |
| copyScale | bool | いいえ | false | true の場合、旧ビューの `View.Scale` を新しいビューに適用します（集計表ビューは対象外）。 |

スケール差対応の位置合わせ（グリッド交点アンカー）

| 名前 | 型 | 必須 | 説明 |
|------|----|------|------|
| alignByGridIntersection | object | いいえ | `{ referenceViewportId, gridA, gridB, enabled? }` を指定すると、グリッド交点を基準に新旧Viewportの位置合わせを行います。 |
| referenceViewportId | int | 条件付き | 参照元のViewport ID。 |
| gridA / gridB | string | 条件付き | モデル上の交点を作るグリッド名（例: `X1` と `Y3`）。 |
| enabled | bool | いいえ | 明示的にオン/オフを指定したい場合に使用。 |

### 挙動の詳細

1. 置き換え対象の特定
   - `viewportId` が指定されていれば、そこからシートと旧ビューを特定します。
   - それ以外の場合は、`sheetId` / `sheetNumber` などと `oldViewId` / `oldViewUniqueId` の組み合わせで、対象シート上の Viewport / ScheduleSheetInstance を探します。
2. 旧配置の位置を取得
   - Viewport の場合: `Viewport.GetBoxCenter()` の中心（取得に失敗したら `GetBoxOutline()` の中心）。
   - 集計表の場合: `ScheduleSheetInstance.Point`。
3. 位置オプションの反映
   - 既定（`keepLocation:true`）では、この旧位置をそのまま新しいビューに使います。
   - `keepLocation:false` の場合:
     - `centerOnSheet:true` ならシートの中心を使う（シートサイズは mm ベースで取得）。
     - それ以外では `location {x,y}`（mm）で指定した位置を使用します。
4. ビュー差し替え
   - 1 つのトランザクションの中で、旧 Viewport / ScheduleSheetInstance を削除し、新しいビューを同じシートに作成します。
   - 新しいビューがスケジュールなら `ScheduleSheetInstance.Create`、それ以外は `Viewport.Create` を使用します（`Viewport.CanAddViewToSheet` でチェック）。
5. 回転・縮尺のコピー（任意）
   - `copyRotation:true` かつ旧配置が Viewport の場合、旧 Viewport の `Rotation` を新しい Viewport にセットします。
   - `copyScale:true` の場合、旧ビューの `Scale` を新しいビューに設定します（API が許可しない場合は失敗せず、警告メッセージを返します）。
6. グリッド交点アンカーでの位置合わせ（任意）
   - `alignByGridIntersection` が指定されている場合、`gridA` x `gridB` の交点（モデル座標）を取得します。
   - 参照Viewport (`referenceViewportId`) と新Viewportの両方で、この交点をシート座標へ投影します。
   - 投影差分（sheet delta）だけ新Viewport中心を移動し、交点位置が一致するように調整します。
   - これにより、ビュー縮尺が異なる場合でも座標合わせが可能です。

## レスポンス

- 成功時:

```json
{
  "ok": true,
  "sheetId": 12345,
  "oldViewId": 111,
  "newViewId": 222,
  "usedLocationFromOld": true,
  "copyRotation": true,
  "copyScale": false,
  "scaleWarning": null,
  "alignWarning": null,
  "alignment": {
    "anchorModelMm": { "x": 0.0, "y": 0.0, "z": 0.0 },
    "deltaSheetMm": { "x": -25.795, "y": -61.927 }
  },
  "result": {
    "kind": "viewport",
    "viewportId": 999,
    "sheetId": 12345,
    "viewId": 222
  }
}
```

### 3) スケール差がある2つのビューを、グリッド交点で位置合わせして差し替え

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "replace_view_on_sheet",
  "params": {
    "viewportId": 5785889,
    "newViewId": 4383347,
    "alignByGridIntersection": {
      "referenceViewportId": 5785884,
      "gridA": "X1",
      "gridB": "Y3",
      "enabled": true
    }
  }
}
```

- 失敗時（例）:

```json
{
  "ok": false,
  "msg": "viewportId か oldViewId/oldViewUniqueId のいずれかを指定してください。"
}
```

## リクエスト例

### 1) viewportId を使って単純に差し替える

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "replace_view_on_sheet",
  "params": {
    "viewportId": 123456,
    "newViewId": 789012
  }
}
```

### 2) シート番号と旧ビュー ID で指定し、シート中心に新ビューを配置（縮尺もコピー）

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "replace_view_on_sheet",
  "params": {
    "sheetNumber": "A-101",
    "oldViewId": 234567,
    "newViewId": 890123,
    "keepLocation": false,
    "centerOnSheet": true,
    "copyScale": true
  }
}
```

## 関連コマンド

- `place_view_on_sheet` : ビューを新規にシートへ配置
- `remove_view_from_sheet` : シートからビュー／集計表を取り除く
- `get_view_placements` : あるビューがどのシートにどのように配置されているかを取得
