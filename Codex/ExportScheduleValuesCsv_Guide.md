# Export Schedule “As-Displayed” Values & ElementId — Implementation Guide

重要（仕様変更）
- `export_schedule_values_csv` はアドインから削除されました（安定版へ集約）。
- 集計表の書き出しは下記の安定版コマンドをご利用ください。
  - CSV: `export_schedule_to_csv`
  - Excel: `export_schedule_to_excel`

以下は旧実装の参考情報として残しています（背景知識）。

---

## TL;DR（先に結論）

- **見たままの値**は `TableSectionData.GetCellText(...)` を使えば **UI表示どおり**（単位・丸め・記号・計算列を含む）で取得できます。  
- **行ごとの ElementId** は、スケジュールが **アイテム化され、グループ/小計/合計が除去** され、かつ **ID_PARAM 列が含まれている** 場合に **取得できます**。  
- **最小修正の指針**：  
  1) 反射はやめて **公開APIで `IsItemized=true`、`ShowGrandTotals=false`、Sort/Group全消去**。  
  2) **ID列は公開APIで追加**し、列インデックスは **ParamId=(BuiltInParameter.ID_PARAM)** 由来で特定。  
  3) 値の取得は **“見たまま優先”**（`GetCellText`）に切り替え、**値セマンティクスが欲しい時だけ** 列方向の **下方向 FillDown** を適用。  
  4) 合計行・小計行など **ElementId を持たない行はスキップ**。

---

## 実装変更ポイント（最小で堅く）

### 1) アイテム化 & 合計非表示 & Sort/Group クリアを **公開API** で

```csharp
var def = temp.Definition;
try { def.IsItemized = true; } catch { /* older Revit? ignore */ }
try { def.ShowGrandTotals = false; } catch { try { def.ShowGrandTotal = false; } catch { } }
while (def.GetSortGroupFieldCount() > 0)
{
    def.RemoveSortGroupField(0);
}
```

> Revit のバージョンにより `ShowGrandTotals` / `ShowGrandTotal` のどちらかになります。**両対応**にしておくと安心です。

### 2) **要素ID** 列は公開APIで **追加** し、**ParamId=ID_PARAM** で列特定

```csharp
// 既にID列がなければ追加
var def = temp.Definition;
var schedulables = def.GetSchedulableFields();
var idSched = schedulables.FirstOrDefault(sf =>
    sf.ParameterId != null && sf.ParameterId.IntegerValue == (int)BuiltInParameter.ID_PARAM);
if (idSched != null)
{
    def.AddField(ScheduleFieldType.Instance, idSched);
}

// 列インデックスの特定は ParamId 由来を最優先
int idColIndex = FindElementIdColumnIndexByFieldParam(def, fields);
// だめならヘッダ文字列からのフォールバック
if (idColIndex < 0) idColIndex = FindElementIdColumnIndex(bodySec, displayNames);
```

> 見出し文字列のあいまい一致より、**ParamId での特定が圧倒的に堅牢**。

### 3) **見たまま優先**（`GetCellText`）→ 値セマンティクスが必要なら **列方向 FillDown**

```csharp
// まず全セルを "見たまま" でテーブル化
var table = ReadSection(bodySec);

// 値セマンティクスが必要な場合のみ、列方向（下方向）にブランク埋め
if (fillBlanks) FillBlanksDownwards(table);

// ElementId 列を解決（ParamId優先 → 見出し推測）
int idColIndex = FindElementIdColumnIndexByFieldParam(def, fields);
if (idColIndex < 0) idColIndex = FindElementIdColumnIndex(bodySec, displayNames);
if (idColIndex < 0) return ResultUtil.Err("Failed to resolve Element Id column.");
```

> こうすることで、**単位・丸め・記号・計算列の結果**まで **UI一致**でCSVに落とせます。  
> 「実値（Double のフィート値等）」が必要な用途は **別コマンド**（`get_param_values` など）で扱いを分けるのが◎。

### 4) **ElementId を持たない行（集計・小計・合計）はスキップ**
```csharp
for (int r = 0; r < table.Length; r++)
{
    var idText = table[r][idColIndex];
    if (!int.TryParse(idText, out var eid) || eid <= 0) continue; // 集計行などは除外
    sb.AppendLine(string.Join(delimiter, table[r].Select(EscapeCsv)));
}
```

---

## よくある“ズレ”と対策

| 症状 | 原因 | 対策 |
|---|---|---|
| 値がUI表示と違う | パラメータ直読み（AsDouble など）を優先している | **GetCellText を第一候補**に。数値化が必要なら別コマンドに分離 |
| ElementId が列で見つからない | 見出し名がカスタム／言語差 | **ParamId=ID_PARAM** で列特定。見出しは**最後のフォールバック** |
| 同じ値の空欄が抜ける | 「繰り返し値を空欄」設定 | **列方向の下方向 FillDown** を実施（行方向ではなく**列方向**） |
| 合計行が混ざる | Itemizeがオフ、またはGrandTotalが有効 | **IsItemized=true / ShowGrandTotals=false**、Sort/Group を全削除 |

---

## どうしても ID が取れないケース（仕様上の限界）

- **集計行・小計行・合計行**：1:1で要素に対応しないため **ElementId を定義できません**（スキップが正解）。  
- **マテリアル集計**：ID列が**マテリアルID**になりがち。**ホスト要素ID**が必要なら、専用スケジュールを作るか、MCP側で**要素⇄マテリアル行の逆引き辞書**を用意する必要があります。  
- **複合/計算列**：値は**見たまま**で取得し、**実値**や**再計算**は別ツールで。

---

## 運用の小ワザ

- **2モード提供**（使い分けが明確に）  
  - `export_schedule_display_csv` … 完全 **見たまま**（`GetCellText`のみ、ID列は存在前提）  
  - `export_schedule_values_csv` … 見たまま＋**列方向 FillDown** で値セマンティクス寄りに  
- **ログ出力**（JSON）に以下を残すと便利：  
  - スケジュール名 / ViewId  
  - `IsItemized` / `ShowGrandTotals` / SortGroupクリアの結果  
  - ID列の解決方法（**ParamId一致** or **ヘッダ推測**）

---

## 差分イメージ（最小パッチ）

```diff
- // Try to force itemize and clear grouping/sorting via reflection (API depends on version)
- var def = temp.Definition;
- { /* reflection to set IsItemized/ShowGrandTotal(s) */ }
- { /* reflection to ClearSortGroupFields */ }
+ var def = temp.Definition;
+ try { def.IsItemized = true; } catch { /* older Revit? ignore */ }
+ try { def.ShowGrandTotals = false; } catch { try { def.ShowGrandTotal = false; } catch { } }
+ while (def.GetSortGroupFieldCount() > 0) { def.RemoveSortGroupField(0); }

- // Resolve cells value via resolvers first, GetCellText as fallback
+ // 見たまま優先：まず全セル GetCellText でテーブル化
+ var table = ReadSection(bodySec);
+ if (fillBlanks) FillBlanksDownwards(table); // 列方向の下埋め
+ int idColIndex = FindElementIdColumnIndexByFieldParam(def, fields);
+ if (idColIndex < 0) idColIndex = FindElementIdColumnIndex(bodySec, displayNames);
+ if (idColIndex < 0) return ResultUtil.Err("Failed to resolve Element Id column.");
+ for (int r = 0; r < table.Length; r++)
+ {
+     var idText = table[r][idColIndex];
+     if (!int.TryParse(idText, out var eid) || eid <= 0) continue; // 集計行などスキップ
+     sb.AppendLine(string.Join(delimiter, table[r].Select(EscapeCsv)));
+ }
```

---

## まとめ

- **“見たまま優先・IDはParamIdで確定・ブランクは列方向で埋める”** の 3点セットで、**現場ズレ**は大幅に減ります。  
- 反射は最終手段。まずは **公開API** で組み立てて、ダメな箇所だけ最小限にリカバリするのがおすすめ。

---

### 付記：バージョン差異の取り扱い

- `ShowGrandTotals` / `ShowGrandTotal` の差は **try/catch で両対応**。
- `ScheduleDefinition` のフィールド追加系メソッドは、**`AddField(ScheduleFieldType, SchedulableField)` を優先**。  
  もし `ElementId` 版がある環境なら使っても良いが、**`SchedulableField` のほうが互換性が高い**です。
