# TODO (DWG / AutoCAD)

## DWG 레이어属性運用の統一
- Revit主体プロジェクト:
  - DWG出力設定（Export Setup）で線色・線種・線幅マッピングを運用する。
- AutoCAD主体プロジェクト:
  - 書き出し後にレイヤ属性（色・線種・線幅）を一括上書きする運用を確立する。

## 社内標準DWGひな形の反映
- 社内標準DWGひな形の改訂版が確定したら、Revit→DWG変換ルールを策定する。
- 変換ルールは Export Setup と AutoCAD後処理（レイヤ属性上書き）の両方に対応させる。

## AutoCAD後処理（レイヤ属性上書き）
- レイヤ属性マップ（CSV/JSON）の仕様を確定する。
  - 例: `pattern,targetLayer,color,linetype,lineweight`
- AutoCAD COM/accore の両方で同じマップを適用できるようにする。

## 依存ライブラリ（COM）
- `pywin32`（必須）
  - AIエージェントがインストール支援する: `python -m pip install pywin32`
- その他の外部ライブラリは不要（標準ライブラリのみ）。
