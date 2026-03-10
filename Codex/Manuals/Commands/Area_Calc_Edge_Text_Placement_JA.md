# Area Edge Length Text Placement (JA)

対象コマンド:
- `area.calc_edge_lengths` / `calc_area_edge_lengths`
- `area.calc_run_all` / `calc_area_run_all`

目的:
- 辺長注記（TextNote）の配置を、線分中心と文字中心で一致させる。
- 縮尺変更時にも注記位置の見え方が崩れにくい設定で運用する。

## 新しい設定パラメータ
- `lengthTextCenterAtSegment` (bool, default: `true`)
  - `true`: 線分の中点に文字挿入点を置く（推奨）
  - `false`: 従来どおり法線方向オフセットを使用
- `lengthTextHorizontalAlignment` (`left|center|right`, default: `center`)
  - `center` 指定で文字中心が挿入点に一致

## 推奨設定（実務）
```json
{
  "lengthTextCenterAtSegment": true,
  "lengthTextHorizontalAlignment": "center"
}
```

## 例: calc_area_run_all
```json
{
  "fromSelection": true,
  "plotEdgeText": true,
  "lengthTextCenterAtSegment": true,
  "lengthTextHorizontalAlignment": "center"
}
```

## 注意
- 回転は線分方向に追従します（ビュー平面へ投影した方向）。
- `lengthTextCenterAtSegment=false` の場合は、重なり回避のため従来オフセットロジックが使われます。
