# 凡例ビュー関連コマンド（制約付き／将来対応含む）

この節では、凡例ビューまわりのコマンドのうち、

- 現時点で **制約付きだが実際に利用できるもの**
- 今後の Revit バージョンや API 拡張により対応候補となるもの

を整理します。

## 対象コマンド一覧

- `set_legend_component_type`  … ドア／窓の Legend コンポーネントのタイプ切替（LEGEND_COMPONENT 経由）
- `populate_door_window_legend_from_template`  … 将来候補（現在はスタブ）

いずれも「ドア／窓の凡例ビューをテンプレートベースで自動構成する」ために設計されたものです。

---

## `set_legend_component_type`

**目的**

- 凡例ビュー上の凡例コンポーネント（カテゴリ: `凡例コンポーネント` / `OST_LegendComponents`）について、
  - 既存の Legend コンポーネントを再利用しつつ、
  - 表示しているドア／窓タイプを別のタイプに差し替える。

**想定ユースケース**

- `DoorWindow_Legend_Template` のような「ベース凡例」にサンプルコンポーネントを 1～2 個だけ置いておき、
- Python などのスクリプト側でコピー・レイアウトしたうえで、
  各 Legend コンポーネントをドア／窓タイプごとに切り替える。

**現時点の状態（Revit 2024）**

- `ChangeTypeId()` を凡例コンポーネントに対して呼び出すと、

  > This Element cannot have type assigned.

  という例外が返されます（LegendComponent は FamilyInstance ではないため）。
- 一方、`BuiltInParameter.LEGEND_COMPONENT`（ストレージ型 ElementId）の値として  
  ドア／窓の `FamilySymbol.Id` を `Set()` し、`doc.Regenerate()` することで、
  **Legend コンポーネントが表現するドア／窓タイプを切り替えることができます。**
- MCP の `set_legend_component_type` はこの方式を実装しており、
  - ドア凡例: A1→B1 への切り替え
  - 窓凡例: M01→M17 への切り替え
  が実プロジェクトで正常に動作することを確認済みです。

**コマンドの仕様（簡略）**

- 引数:
  - `legendComponentId` / `elementId` : 対象 Legend コンポーネントの ElementId (int)
  - `targetTypeId` / `typeId`        : 変更先のドア／窓タイプ（`FamilySymbol.Id`）
  - `expectedCategory`               : `"Doors"` / `"Windows"` など（任意, カテゴリ確認用）
- 動作:
  1. 対象が `OST_LegendComponents` であることをチェック
  2. `elem.get_Parameter(BuiltInParameter.LEGEND_COMPONENT)` を取得
  3. 既存の ElementId を `oldTypeId` として保存
  4. `legendParam.Set(targetTypeId); doc.Regenerate();` を実行
  5. `changed` フラグと `oldTypeId` / `newTypeId` を返す
- 戻り値（例）:

  ```json
  {
    "ok": true,
    "msg": "Legend コンポーネントのタイプを変更しました。",
    "elementId": 60576259,
    "oldTypeId": 60171437,
    "newTypeId": 60171439,
    "changed": true
  }
  ```

**制約と注意点**

- `LEGEND_COMPONENT` パラメータが
  - 見つからない場合
  - 読み取り専用の場合
  はエラーとなり、タイプ変更は行われません。
- Legend で表示できないタイプを指定した場合、Revit 内部の制約により `.Set()` が例外になる可能性があります。
- `get_element_info` が返す `typeName` は表示用の組み立て文字列であり、直接編集することはできません。  
  （`LEGEND_COMPONENT` を通じて間接的に変わります。）

**補助コマンド**

- 次のコマンドと組み合わせて使うことを想定しています。
  - `create_legend_view_from_template`
  - `copy_legend_components_between_views`
  - `layout_legend_components_in_view`
  - `get_door_window_types_for_schedule`

---

## `populate_door_window_legend_from_template`

**目的**

- ひとつの JSON-RPC 呼び出しで、以下をまとめて行う「ドア／窓凡例の一括生成」コマンドです。
  1. ベース凡例ビューとターゲット凡例ビューの解決
  2. ベース凡例からサンプル Legend コンポーネントの取得（ドア／窓）
  3. ドア／窓タイプの列挙（Type Mark 等による）
  4. サンプルをコピーしてタイプを差し替え、凡例を埋める

**現時点の状態**

- Revit MCP の UI イベントポンプとトランザクション時間の制約により、
  - 「コピー＋レイアウト＋タイプ差し替え」を 1 コマンドでまとめて行うと、タイムアウトや UI 応答停止のリスクが高いことが分かりました。
- そのため、このコマンドは現在「スタブ実装」として残しており、呼び出しても `ok:false` とメッセージを返すだけです。

**推奨代替フロー**

1. `create_legend_view_from_template` でターゲット凡例ビューを作成。
2. `copy_legend_components_between_views` でベース凡例のサンプルコンポーネントをコピー。
3. `layout_legend_components_in_view` で Legend コンポーネントをグリッド状に整列。
4. `get_door_window_types_for_schedule` でドア／窓タイプ一覧を取得。
5. Python などから `set_legend_component_type` を使って、Legend コンポーネントごとに表現するタイプを切り替える。

---

## 注意事項

- `set_legend_component_type` は、**ドア／窓の Legend コンポーネント**については実際に動作することを確認済みです。
- ただし、`LEGEND_COMPONENT` の有無や Revit 内部制約に依存するため、すべてのカテゴリ・すべてのタイプでの動作が保証されているわけではありません。
- `populate_door_window_legend_from_template` は「将来、トランザクション設計や API 制約の改善があった場合に再評価する候補」として、現時点ではスタブ実装のまま残しています。

