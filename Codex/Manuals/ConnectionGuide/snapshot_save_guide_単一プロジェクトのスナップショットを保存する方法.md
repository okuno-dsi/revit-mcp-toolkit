# 単一プロジェクトのスナップショットを保存する方法

以下は、**単一プロジェクトのスナップショット（要素一覧＋属性のJSON）を保存**する手順と実例です。  
スナップショットは、あとで `compare.mode:"snapshots"` で差分比較に使えます。

---

## これは何を保存するの？

- **モデル内の要素**を、カテゴリ/タイプ/レベル/位置（mm）/向き（梁など）/長さ、必要なら**パラメータ値**（`paramIds`/`builtinIds`）まで**JSONにパック**します。  
- 1ファイルで「後で比較可能な”状態”」を記録できます。

**スナップショット（抜粋のイメージ）**
```json
{
  "title": "Host.rvt",
  "viewId": 401,
  "onlyInView": true,
  "items": [
    {
      "elementId": 300101,
      "uniqueId": "cccc-....-1234",
      "categoryId": -2001320,
      "levelId": 301,
      "levelName": "1FL",
      "typeId": 1501,
      "typeName": "UB 406x178x60",
      "familyName": "UB",
      "cxMm": 12000.0, "cyMm": 3500.0, "czMm": 3000.0,
      "yawDeg": 90.0, "lengthMm": 6000.0,
      "paramById":   { "1234567": 900.0 },
      "builtinById": { "-1002002": "A-01" },
      "bucket": "12000,3500,3000|yaw:90|len:6000"
    }
  ]
}
```

---

## 保存する方法（3通り）

### 方法A：**明示エクスポート**（推奨）
`compare.mode:"export"` を使います。`export.path` を指定すれば**ファイルへ保存**、未指定なら**レスポンスにJSONを同梱**して返します。

```json
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{ "mode":"export" },
    "filters":{
      "includeCategoryIds":[-2000011, -2001320]
    },
    "scope":{
      "viewId":401,
      "onlyElementsInView":true
    },
    "rules":{
      "paramIds":[1234567],
      "builtinIds":[-1002002]
    },
    "export":{
      "enabled": true,
      "path": "C:\\\\temp\\\\snap_host.json"
    }
  },
  "id": 101
}
```

- **何を含めるか**は `filters` / `scope` / `rules` で制御します  
  - `includeCategoryIds` は **BuiltInCategory の整数ID**を推奨（名称の揺れ回避）  
  - `viewId + onlyElementsInView:true` で **ビューに見えている要素**に限定  
  - `paramIds` / `builtinIds` を指定すると、**インスタンス/タイプ**のパラメータを mm/deg 変換で採取

---

### 方法B：**フォールバックエクスポート**（open_docsでbaselineが無い等の”比較不能”時）
`compare.mode:"open_docs"` で相手がいないときに、`fallbackExport` を有効にしておくと**スナップショットを書き出す**運用ができます。

```json
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{"mode":"open_docs","sourceDocTitle":"Host.rvt","baselineDocTitle":"<missing>"},
    "filters":{"includeCategoryIds":[-2001320]},
    "fallbackExport":{"enabled":true,"path":"C:\\\\temp\\\\snap_host.json"}
  },
  "id": 102
}
```

フォールバックエクスポートで「インスタンスの基本情報＋すべてのインスタンスパラメータ」を出したい場合は、エクスポート時のオプションを明示すればOKです。既定では落とさない（=必要最小限）ですが、以下のように**“全部出す” フラグ**を付けられるようにしておくのが安全です。

使い方（JSON-RPC例）
open_docs で baseline が無い → フォールバックで保存（全インスタンスパラメータを出力）
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{
      "mode":"open_docs",
      "sourceDocTitle":"Host.rvt",
      "baselineDocTitle":"<missing>"
    },
    "filters":{
      "includeCategoryIds":[-2000011,-2001320]   // Walls / Structural Framing（ID指定推奨）
    },
    "scope":{
      "viewId":401,
      "onlyElementsInView":true                  // ビューに見えている要素のみ
    },
    "rules":{
      "includeAllInstanceParams": true,          // ★ これを付ける
      "includeAllTypeParams": false              // （任意）タイプも全部出したいなら true
    },
    "fallbackExport":{
      "enabled": true,
      "path":"C:\\\\temp\\\\snap_host_full.json" // 省略時はレスポンスの snapshot に同梱
    }
  },
  "id": 777
}


ポイント

includeAllInstanceParams: true を rules に付けるだけ（デフォルトは false）。

さらにタイプパラメータも出したいなら includeAllTypeParams: true。

出力量が増えるので、カテゴリIDやビュー限定で対象を絞るのが安心です。


---

### 方法C：**ファイルに保存せず、そのままレスポンスで受け取る**
`export.path` を省略すると、**レスポンスの `snapshot` フィールド**に JSON が返ります。

```json
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{ "mode":"export" },
    "filters":{ "includeCategoryIds":[-2000011,-2001320] },
    "rules":{ "paramIds":[1234567] },
    "export":{ "enabled": true }
  },
  "id": 103
}
```

---

## 実行手順（例：curl／ポート5210）

```bash
# 1) リクエスト投入
curl -sS -X POST "http://localhost:5210/enqueue" \
  -H "Content-Type: application/json" \
  --data '{
    "jsonrpc":"2.0",
    "method":"generate_diff_report",
    "params":{
      "compare":{"mode":"export"},
      "filters":{"includeCategoryIds":[-2000011,-2001320]},
      "scope":{"viewId":401,"onlyElementsInView":true},
      "rules":{"paramIds":[1234567],"builtinIds":[-1002002]},
      "export":{"enabled":true,"path":"C:\\\\temp\\\\snap_host.json"}
    },
    "id": 201
  }'

# 2) 結果取得（ファイル保存に成功していれば path が返る。path省略時は snapshot が返る）
curl -sS "http://localhost:5210/get_result"
```

> ⚙️ ポート番号はご環境のリボンUI/ログでご確認ください（例：5210）。

---

## フィルタとユニットのTips

- **カテゴリはID指定**が安全：`includeCategoryIds:[-2000011 /*Walls*/, -2001320 /*Structural Framing*/]`  
  （名称は言語依存で揺れるため、IDを推奨）
- **パラメータID**は `paramIds:[...]` に **Element.Parameter.Id** を、`builtinIds:[...]` に **BuiltInParameter の整数**を渡します。  
- **単位**：長さ/角度/面積/体積などは、**mm/deg/mm²/mm³** に正規化されます（比較時も同仕様）。

---

## 典型パターン

### A. 全体スナップショット（後から別PCで比較）
- `compare.mode:"export"`＋`filters` を広めに（もしくは空で全部）  
- `rules.paramIds/builtinIds` は必要なものだけ（増やしすぎると重くなる）

### B. ビュー限定（見えているものだけ）
- `scope.viewId` と `onlyElementsInView:true`

### C. ファイルに保存せず即利用
- `export.path` を省略 → レスポンスの `snapshot` を**そのまま次工程へ**渡す

---

## よくあるトラブルと対処

- **パスのエスケープ**：JSONでは `\\` が必要（例：`"C:\\\\temp\\\\snap.json"`）。  
- **フォルダの事前作成**：`export.path` の先のフォルダは**自動作成**しますが、権限が必要です。  
- **要素が入っていない**：`filters` と `scope` が絞り込み過ぎていないか確認（`onlyElementsInView:true` で空になりやすい）。
- **重すぎる**：`includeCategoryIds` を絞る、`paramIds/builtinIds` を最小限にする。
- **コンソールの文字化け / エンコーディングエラー**:
  - `export.path` を指定せず、コマンドの応答としてスナップショットを受け取る場合、コンソールで結果を表示したり、リダイレクト (`>`) でファイルに保存しようとすると `UnicodeEncodeError` が発生することがあります。これはWindowsのコンソールが日本語の文字コード(cp932)を標準で使うためです。
  - **【最重要対策】** この問題を確実に避けるには、`export.path` を指定してアドインに直接ファイルを書き出させるか、`Manuals/Scripts/send_revit_command_durable.py` の `--output-file` オプションを使用してください。
  - 詳細は `Most Important 文字化け対策手順書.md` を参照してください。この対策を怠ると、コマンドが成功したかどうかの判別もできず、作業が停滞する原因となります。


---

## 次のステップ（差分比較）

- 別タイミングで保存した 2 つのスナップショットを、`compare.mode:"snapshots"` で比較できます：

```json
{
  "jsonrpc":"2.0",
  "method":"generate_diff_report",
  "params":{
    "compare":{"mode":"snapshots"},
    "sourceSnapshotPath":"C:\\\\temp\\\\snap_host_2025-08-26.json",
    "baselineSnapshotPath":"C:\\\\temp\\\\snap_host_2025-08-12.json",
    "rules":{"moveThresholdMm":40,"checkTypeChange":true}
  },
  "id": 301
}
```

