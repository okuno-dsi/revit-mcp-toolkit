# 集計表CSV出力に関する重要な注意点と推奨事項

本書では、Revit 集計表（ViewSchedule）を CSV に出力する際に遭遇しやすい「空欄」や「実値が入らない」事象の理由と、実務での推奨設定をまとめます。

## 1) なぜ空欄になるのか（API仕様）
- Revit API の `ViewSchedule` は、行やセルに紐づく「要素ID（ElementId）」を直接は返しません。取得できるのは各セルの「表示文字列（`TableSectionData.GetCellText()`）」です。
- 集計表の並べ替え/グループ化/集計によって、UI では「同一値を空欄で表示」することがあります。このとき `GetCellText()` は実際に空文字を返すため、CSV でも空欄になります。
- そのため、列に **一見値が見えている** ように見えても、内部的には「マージのスレーブ側セル」であり、APIは空を返すことがあります。

## 2) 実値で出したいときの推奨設定
実務で「行→要素」対応を取り、タイプ名/幅/高さ/マークなどを確実に出すには、下記の設定を推奨します。

- 集計表に「要素 ID（ID / 要素 ID）」列を追加（可視列とする）
- 「アイテムを個別に表示」を有効化（グループ化・集計は必要に応じてオフ）

これにより、CSVに「要素 ID」が出力され、行ごとの要素識別が可能になります。以降は要素/タイプから実値を引いて整形できます。

## 3) 代替手段（設計メモ）
- プログラム側で一時的に複製した集計表に対し、上記設定（要素ID列の追加＋アイテム個別）を適用し、CSVを出力することも可能です。
- ただし、既存ビューの見た目と異なる可能性があるため、**提出物やレビューで「見た目の忠実さ」を重視する用途**では、元の集計表からの出力（=空欄が残る）を選ぶか、提出専用のビュー定義を用意してください。

## 4) まとめ（運用指針）
- 集計表のCSVで空欄が発生するのはAPI仕様に起因します（グループ化・マージ時の空欄はそのまま空文字になる）。
- 実値での分析・後工程に用いるCSVが必要な場合は、集計表に「要素 ID」を可視列として追加し、「アイテムを個別に表示」を有効化してください。
- 見た目の忠実さが重要な場合は、元集計表からのCSVを利用し、空欄は仕様として受け止めるか、補完（Fill）付き出力を使ってください。

## 4.1) 重要な編集ポリシー（集計表は直接編集しない）
- 集計表上のセルを MCP から直接編集する運用は非推奨です（性能劣化・非効率）。
- 代わりに、集計表に表示されている「元の要素パラメータ」を更新してください（部屋・壁・ファミリインスタンスなど）。
  - 例：部屋の仕上げ項目は `set_room_param { elementId, paramName, value }` で更新し、集計表に反映させる。
  - 例：壁のコメント等は `update_wall_parameter { elementId, paramName, value }` を使用。
- 理由：集計表セル編集は行スキャンやUI依存が発生しやすく、要素パラメータ更新に比べて大幅に遅く不安定です。
- ベストプラクティス：
  - 集計表に「要素 ID」列を可視で追加しておく（対象要素の特定が容易）。
  - 可能なら `UniqueId` で指定 → Add-in 側で ElementId に解決（より堅牢）。
  - 値の一括変更は、対象要素の ID リストを作成してパラメータ更新コマンドをバッチ送信。

---

## 5) コマンド（安定版）と使い分け（重要）

- `export_schedule_to_csv`（見たまま出力）
  - UI表示どおりのセル文字列をCSV化します。行はそのままなので、グループ化や繰り返し空欄も反映されます。
  - 例（PowerShell/JSON-RPC）:
    - `python Scripts/Reference/send_revit_command_durable.py --port 5210 --command export_schedule_to_csv --params '{"scheduleViewId": <ID>, "outputFilePath": "C:/path/out.csv", "includeHeader": true}'`

- `export_schedule_to_excel`（Excel出力）
  - 集計表を直接 .xlsx に書き出します（UTF-8テキストはExcelセルへ、数値/単位はUI表示準拠）。
  - 例（PowerShell/JSON-RPC）:
    - `python Scripts/Reference/send_revit_command_durable.py --port 5210 --command export_schedule_to_excel --params '{"viewId": <ID>, "filePath": "C:/path/out.xlsx"}'`

注意（変更点）
- 旧コマンド `export_schedule_values_csv` は削除されました。値セマンティクス寄りの補完（列方向FillDown等）が必要な場合は、CSV出力後に表計算側で補完する運用に切り替えてください。

---

## 6) 操作手順（UIでの推奨設定）

1. 対象の集計表に「要素ID（ID / 要素 ID）」列を追加します（可視列）。ElementIdが使えない場合は代替として IfcGUID でも可。
2. 集計表の「アイテムを個別に表示」をオンにします。
3. 合計/小計を必要に応じてオフにし、並べ替え/グループ化を必要最小限にします。

この設定を行っておくと、どの出力コマンドでも欠損が少ないCSVが得られます。特に「ドア」などでタイプ名が空になる事象は改善します。

---

## 7) フィールド定義を点検する（内部名/表示名/サンプル）

「表示名」「フィールド設定の内部名」「実際の表示サンプル」を確認するには、`inspect_schedule_fields` を使います。

例（ドア集計のフィールド確認）：

```
python Scripts/Reference/send_revit_command_durable.py ^
  --port 5210 ^
  --command inspect_schedule_fields ^
  --params "{\"title\":\"ドア\",\"samplePerField\":5}" ^
  --output-file Projects/<ProjectName>_<Port>/Logs/inspect_fields_door.json
```

これにより、列ヘッダの表示名と、スケジュールフィールドの内部名の対応が把握でき、空欄原因の切り分けが容易になります。

---

## 8) 一括出力スクリプト（比較運用に便利）

`Scripts/Reference/export_all_schedules_to_csv.ps1` は、全集計表を一括CSV化します。

- As‑Is（見たまま）
  - `pwsh -ExecutionPolicy Bypass -File Scripts/Reference/export_all_schedules_to_csv.ps1 -Port 5210 -OutDir "Projects/…/Schedules_Compare/AsIs"`
- Itemize + Fill（値セマンティクス寄り）
  - `pwsh -ExecutionPolicy Bypass -File Scripts/Reference/export_all_schedules_to_csv.ps1 -Port 5210 -OutDir "Projects/…/Schedules_Compare/Itemize_Fill" -FillBlanks -Itemize`

生成された2セットを比較すると、「見た目そのまま」と「値の補完あり」の違いを確認できます。

---

## 9) トラブルシューティング（簡易）

| 症状 | ありがちな原因 | 対処 |
|---|---|---|
| 値がUIと違う | 直接パラメータを読んでいる（単位・丸め未反映） | `GetCellText` を使うコマンド（本ガイド準拠）を使用 |
| 同じ値が空欄になる | 「同一値を空欄表示」設定 | 列方向 FillDown（`-FillBlanks`）を使うか、アイテム化 |
| 行が合計/小計だけ出る | アイテム化が無効、または合計行のみのビュー | `IsItemized=true`、`ShowGrandTotals=false`、並べ替え/グループ解除 |
| 要素ID列が見つからない | 見出し名の言語差/カスタム | スケジュールに「要素ID」を追加して可視化（推奨） |
| マテリアル集計でホストIDが必要 | 行IDがマテリアルIDになる | 専用スケジュールを作成、または別途逆引き辞書を用意 |

---

## 10) 既知の制約（仕様）

- 合計/小計行は要素に1:1対応しないため ElementId を持ちません（スキップが正解）。
- マテリアル集計では ID 列がマテリアルIDを指す場合があります。ホスト要素IDが必要な際は別途設計が必要です。
- IfcGUID は行の安定識別子として便利ですが、すべてのカテゴリ/設定で常に取得できるわけではありません。主キーとしての利用は要検討です。

---

## 11) 参考（実装メモ）

- `export_schedule_to_csv` は、オプションで一時複製ビューに対して `IsItemized` の適用と下方向 Fill を提供します。
- `export_schedule_to_excel` は、Excel ワークブックへ直接書き出します（CSVと同じくUI表示準拠）。
- どちらのモードでも、集計表に「要素ID」列を可視で含めることを強く推奨します（欠損削減・後工程の突合せが容易）。





