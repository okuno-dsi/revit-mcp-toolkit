# generate_diff_report(スナップショット版)の使い方（例）

事前にRevitプロジェクトで使用されている現在のCategoryIdなどの各種IDを取得してから使用すること


## 1) 同一ウィンドウ（open_docs）で比較：構造フレーム + レベル一致要求 + すべて同時検出
```json
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{"mode":"open_docs","sourceDocTitle":"Host.rvt","baselineDocTitle":"Baseline.rvt"},
    "scope":{"onlyElementsInView":false},
    "filters":{"includeCategoryIds":[-2001320]},
    "rules":{
      "requireLevelMatch": true,
      "checkLevelChange": true,
      "checkTypeChange": true,
      "typeCompareCaseSensitive": true,
      "moveThresholdMm": 30,
      "paramIds":[1234567],
      "builtinIds":[-1002002]
    },
    "output":{"enrichElementInfo":true,"groupByCategory":true,"includeProjectInfo":true}
  },
  "id":1
}
```

## 2) リンク比較（Host=アクティブ、リネーム済リンクタイトルを指定）
```json
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{"mode":"link","baselineDocTitle":"Baseline.rvt"},
    "filters":{"includeCategoryIds":[-2001320]},
    "rules":{"requireLevelMatch":false,"checkTypeChange":true,"moveThresholdMm":50},
    "output":{"enrichElementInfo":true,"groupByCategory":true,"includeProjectInfo":true}
  },
  "id":2
}
```

## 3) 別ウィンドウ/別PC：スナップショット同士を比較（snapshots）
```json
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{"mode":"snapshots"},
    "sourceSnapshotPath":"C:\\temp\\snap_host.json",
    "baselineSnapshotPath":"C:\\temp\\snap_base.json",
    "rules":{"moveThresholdMm":40,"checkTypeChange":true}
  },
  "id":3
}
```

## 4) 比較不能時のフォールバックでスナップショットを返す（open_docs で baseline が無い）
```json
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{"mode":"open_docs","sourceDocTitle":"Host.rvt","baselineDocTitle":"<missing>"},
    "filters":{"includeCategoryIds":[-2001320]},
    "fallbackExport":{"enabled":true,"path":"C:\\temp\\snap_host.json"}
  },
  "id":4
}
```

---

## メモ・注意
- **IDと名称は常に併記**されます（`resolution.categories/parameters`、各 `item` の `categoryId/categoryName` など）。  
  → **ID誤りの早期検出**に役立ちます。
- 既存の**ID指定方針**や**ビュー情報コマンド（categoryId を返す）**と整合する設計です。
- ルータ登録は **`RevitMcpWorker`** のハンドラ一覧へ  
  `new RevitMCPAddin.Commands.Revision.DiffReportCommand()`  
  を **1 行追加**するだけでOK。
- サーバは**ポートごと単一キュー**で独立稼働。**複数ウィンドウ間の直接比較は不可**なので、**スナップショット比較**が実運用の近道です。

