# get_room_boundary_lines_in_view

- カテゴリ: Room
- 目的: Room Separation Lines（部屋境界線/線要素）の一覧を取得します。

## 概要
このコマンドは **B) 線要素（Room Separation Lines）** を対象にします。計算境界（`Room.GetBoundarySegments(...)`）は返しません。

注意:
- プロジェクト内で Room Separation Lines が作図されていない場合、部屋が存在していても結果は空になり得ます。
- ユーザー入力が「部屋の境界線」等で曖昧な場合は、A) 計算境界か B) 線要素かを実行前に確認してください（`Manuals/FullManual_ja/spatial_boundary_location.md` 参照）。

## 使い方
- メソッド: get_room_boundary_lines_in_view

- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_room_boundary_lines_in_view",
  "params": {}
}
```

## 関連コマンド
- summarize_rooms_by_level
- validate_create_room
- get_rooms
- get_room_params
- set_room_param
- get_room_boundary
- create_room
