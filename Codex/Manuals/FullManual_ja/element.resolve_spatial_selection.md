# element.resolve_spatial_selection

- カテゴリ: Spatial
- 目的: Room / Space / Area の取り違え（または誤ったID）を、近傍の目的の空間要素へ解決します。

## 概要
次のようなときに使います。

- 本当は **部屋（Room）** のコマンドを実行したかったが、誤って **スペース（Space）** や **エリア（Area）**（またはタグ）を選択してしまった
- `elementId` があるが、それが Room/Space/Area のどれか分からない

解決の優先順位（決定論）:
1) タグのアンラップ（RoomTag/SpaceTag/AreaTag → 実体）
2) 既に目的の種別ならそのまま返す
3) 含有判定優先（境界セグメントを用いた 2D 点内判定）
4) 含有が取れない場合は距離最短（ただし `maxDistanceMeters` 以内）

補足:
- このコマンド自体は **モデルを変更しません**（解決したIDを返すだけです）。
- 多くの Room/Space/Area 系コマンドは内部で自動補正します。挙動確認・デバッグ用途にこのコマンドを使えます。

## 使い方
- メソッド: `element.resolve_spatial_selection`（canonical）
- 旧名: `resolve_spatial_selection`（deprecated）

### パラメータ
| 名前 | 型 | 必須 | 既定値 |
|---|---|---|---|
| elementId | int | いいえ* | null |
| fromSelection | bool | いいえ | false |
| desiredKind | string（`room`/`space`/`area`） | はい |  |
| maxDistanceMeters | number | いいえ | 0.5 |

`*` `elementId` は、`fromSelection=true` かつ要素を選択している場合は省略できます。

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": "fix-1",
  "method": "element.resolve_spatial_selection",
  "params": {
    "fromSelection": true,
    "desiredKind": "room",
    "maxDistanceMeters": 0.5
  }
}
```

## 戻り値
- `ok`: boolean
- `original`: `{ id, kind }`（kind は `room`/`space`/`area`/`unknown`）
- `resolved`: `{ id, kind }`（kind は指定した `desiredKind`）
- `byContainment`: boolean（含有で決まった場合 true）
- `distanceMeters`: number（既に正しければ 0）
- `msg`: string（説明メッセージ）

### 失敗時
次の場合は `ok:false` になります。
- `elementId` が不正、または要素が見つからない
- 近傍に目的の空間要素が見つからない
- 距離最短で見つかったが `maxDistanceMeters` を超える

