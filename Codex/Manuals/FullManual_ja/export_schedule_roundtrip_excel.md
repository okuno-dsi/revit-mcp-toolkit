# export_schedule_roundtrip_excel

現在表示している集計表、または指定した集計表を、Excel で編集して Revit に戻せる往復形式で `.xlsx` に書き出します。

部屋集計に限らず、一般的な Revit 集計表を対象にします。インスタンスパラメータ列とタイプパラメータ列の両方に対応します。

## 用途
- 設備担当者が Excel 上で部屋や要素のパラメータを編集する
- Yes/No 系は `☑ / ☐` で入力しやすくする
- 後で `import_schedule_roundtrip_excel` で Revit に戻す

## 入力
```json
{
  "viewId": 123456,
  "viewName": "部屋集計（環境WG）",
  "filePath": "C:\\temp\\room_schedule_roundtrip.xlsx",
  "autoFit": true,
  "mode": "roundtrip"
}
```

`filePath` の代わりに `outputPath` も使用できます。

`mode`:
- `roundtrip`
  - 従来どおり 1 行 = 1 インスタンス
- `display`
  - Revit の集計表表示に近い行構成
  - hidden の `__ElementId` には、その表示行に対応する複数要素 ID が `;` 区切りで入ることがあります

## 省略時の挙動
- `viewId` / `viewName` 未指定:
  - アクティブ文書の現在ビューが集計表ならそれを使用
- `filePath` / `outputPath` 未指定:
  - 一時フォルダに自動保存

## 出力
```json
{
  "ok": true,
  "path": "C:\\temp\\room_schedule_roundtrip.xlsx",
  "mode": "display",
  "scheduleViewId": 123456,
  "scheduleName": "部屋集計（環境WG）",
  "itemizedRowCount": 99,
  "exportedRowCount": 42,
  "editableColumnCount": 46,
  "booleanColumnCount": 18
}
```

## Excel ファイルの構成
- `Schedule`
  - 編集対象シート
- `README`
  - 入力ルールの簡易説明
- `__revit_roundtrip_meta`
  - Revit に戻すための hidden メタ情報

## 入力ルール
- A列 `__ElementId` は hidden です。削除しないでください。
- `display` モードでは、A列に 1 行あたり複数の要素 ID が入ることがあります。
- `☑ / ☐` 列はそのまま切り替えてください。
- 灰色列は読み取り専用です。編集しても import 時は反映されません。
- 数値列は Revit 集計表で見えている形式のまま編集してください。
- レベルなど `ElementId` 系の参照列は、可能な限り ID ではなく表示名で書き出します。
- 列幅は自動調整しますが、広がりすぎないように上限 `20` で抑えます。
- Yes/No 列は見やすさ優先で細い固定幅にします。

## 実装上の注意
- 書き出し時に一時 duplicate 集計表を作成し、`Itemized` 化して 1 行 = 1 要素 にしています。
- 要素対応付けには hidden の `__ElementId` と hidden メタシートを使用します。
- 日本語 Revit で `ID を交換` / `図形 ID を交換` として露出する ID 列にも対応します。
- `display` モードでは、表示行と itemized 行を内部マッピングしているため、同じ表示行に属する複数インスタンスへ同じ編集値を反映できます。
