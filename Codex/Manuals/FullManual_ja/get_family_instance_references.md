# get_family_instance_references

- Category: ElementOps / FamilyInstanceOps
- 目的: `FamilyInstance` が持つ `FamilyInstanceReferenceType` の参照（stable 参照文字列）を一覧表示します。

## 概要
ガラス/ガラリ/ノブ位置など、ドアの外形以外も含めた「高度な寸法作成」には、ファミリが公開している `Autodesk.Revit.DB.Reference` を正確に指定する必要があります。

本コマンドは `FamilyInstance.GetReferences(FamilyInstanceReferenceType)` を列挙し、各参照を **stable 表現**（文字列）として返します。返ってきた stable 文字列は、例えば `add_door_size_dimensions.dimensionSpecs.refA/refB` に渡せます。

## 使い方
- Method: `get_family_instance_references`

### パラメータ
```jsonc
{
  "elementId": 0,                      // 任意（省略時: 選択中の要素が1つならそれを使用）
  "uniqueId": null,                    // 任意
  "referenceTypes": ["Left","Right"],  // 任意（enum 名でフィルタ、大小文字は無視）
  "includeStable": true,               // 任意（既定: true）
  "includeGeometry": false,            // 任意（既定: false）参照のジオメトリ情報を可能な範囲で返す
  "includeEmpty": false,               // 任意（既定: false）
  "maxPerType": 50                     // 任意（既定: 50）参照タイプごとの最大件数
}
```

### リクエスト例（選択中の要素）
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "get_family_instance_references",
  "params": {
    "includeStable": true,
    "includeEmpty": false
  }
}
```

## 注意点
- 選択した要素が `FamilyInstance` でない場合は `ok:false` になります。
- ファミリ側で参照面が用意されていない場合、多くの `refType` は `count:0` になります。

