# place_roof_brace_from_prompt

- カテゴリ: ElementOps
- 目的: 梁配置をもとに、WPF UI を使って屋根ブレースを配置します（対話型）。

## 概要
対話型コマンドです（Revit UI 上の操作が必要）。詳細は英語版を参照してください。

- 英語版: `../FullManual/place_roof_brace_from_prompt.md`

## 追記（ブレースタイプの絞り込み）
`braceTypeFilterParam` / `braceTypeContains` / `braceTypeExclude` / `braceTypeFamilyContains` / `braceTypeNameContains` を指定すると、
ブレースタイプのドロップダウンを条件で絞り込みできます（AND 条件）。
