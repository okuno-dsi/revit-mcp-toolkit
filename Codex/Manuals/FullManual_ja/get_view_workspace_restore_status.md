# get_view_workspace_restore_status

- カテゴリ: UI / ビュー
- 目的: `restore_view_workspace` の進捗/状態を返します（Idlingによる段階実行のため）。

## 概要
`restore_view_workspace` は Idling により段階的に復元します。このコマンドで次が確認できます:
- 復元が動いているか
- どこまで処理したか
- 警告/エラー

## パラメータ
なし

## 結果（概要）
- `active`, `done`, `sessionId`
- `totalViews`, `index`, `phase`
- `openedOrActivated`, `appliedZoom`, `applied3d`, `missingViews`
- `warnings`, `error`

## 関連
- [restore_view_workspace](restore_view_workspace.md)

