# map_room_area_space

- カテゴリ: Rooms/Spaces
- 目的: 指定した部屋と同じレベルにある対応するエリア/MEPスペースを特定します。名前/番号一致を優先し、一致しない場合は最近傍の平面重心（mm）で探索します。比較用ファクターのみの出力にも対応します。

## 使い方
- メソッド: `map_room_area_space`

### パラメータ
- `roomId`（必須）: 部屋の要素ID（int）。
- `strategy`（任意）: `"name_then_nearest"`（既定）— 同レベルで名前/番号一致→だめなら最近傍の重心。
- `emitFactorsOnly`（任意 bool）: `true` で各ターゲットについて `{ level, centroid }` のみを返します。
- `maxDistanceMm`（任意）: 最近傍探索の最大距離しきい値。

### リクエスト例（ファクターのみ）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "map_room_area_space",
  "params": { "roomId": 6809314, "emitFactorsOnly": true }
}
```

### 結果の形
```jsonc
{
  "ok": true,
  "result": {
    "room": { "id": 6809314, "level": "4FL", "name": "事務室4-2", "number": "80" },
    "area": { "id": 6812345, "level": "4FL", "name": "事務室4-2", "centroid": { "xMm": 12345.6, "yMm": 789.0 } },
    "space": { "id": 6806612, "level": "4FL", "name": "会議室2-5", "centroid": { "xMm": 12345.6, "yMm": 789.0 } }
  }
}
```

補足
- レベルは厳密に一致させます。名前/番号一致はレベル整合後に行います。一致しない場合は、`maxDistanceMm` が指定されていればその範囲で最近傍の重心を用います。
- `emitFactorsOnly: true` の場合、ID/名前は省略し、`{ level, centroid }` のみ（クロスポート受け渡し向け）。
- 寸法はすべてミリメートルです。

## 関連
- get_compare_factors
- find_similar_by_factors
