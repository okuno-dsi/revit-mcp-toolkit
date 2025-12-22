// ================================================================
// File: Commands/AnnotationOps/DimensionCommands.cs
// 機能: 寸法注記の取得・作成・削除・移動・スタイル/フォーマット操作・アラインメント
// 単位: 入出力は mm（内部は ft/rad）。表示文字列は Revit 書式に従う。
// Target: .NET Framework 4.8 / C# 8 / Revit 2023+
// Depends: RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand, UnitHelper)
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    /// <summary>
    /// ビュー内の寸法注記(Dimension)を全件取得（座標は mm）
    /// </summary>
    public class GetDimensionsInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_dimensions_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int viewId = p.Value<int>("viewId");

            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            if (view == null)
                return new { ok = false, msg = $"View not found: {viewId}" };

            // Shape and optional filters
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? int.MaxValue);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? 0);

            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeOrigin = p.Value<bool?>("includeOrigin") ?? true;
            bool includeRefs = p.Value<bool?>("includeReferences") ?? true;
            bool includeValue = p.Value<bool?>("includeValue") ?? true;
            string nameContains = p.Value<string>("nameContains");
            var typeIdsFilter = (p["typeIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();

            var collector = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>();

            if (typeIdsFilter.Count > 0)
                collector = collector.Where(d => typeIdsFilter.Contains(d.GetTypeId().IntValue()));

            if (!string.IsNullOrWhiteSpace(nameContains))
                collector = collector.Where(d => (d.Name ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var all = collector.Select(d => d).ToList();
            int totalCount = all.Count;
            if (summaryOnly)
                return new { ok = true, viewId = view.Id.IntValue(), totalCount };

            IEnumerable<Dimension> paged = all;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(d => d.Id.IntValue()).ToList();
                return new { ok = true, viewId = view.Id.IntValue(), totalCount, elementIds = ids };
            }

            var dims = paged.Select(dim => new
            {
                elementId = dim.Id.IntValue(),
                name = dim.Name,
                typeId = dim.GetTypeId().IntValue(),
                references = includeRefs ? dim.References?.Cast<Reference>().Select(r => r.ElementId.IntValue()).ToList() : null,
                value = includeValue ? dim.ValueString : null, // Revit書式済み
                origin = includeOrigin ? new
                {
                    x = UnitHelper.FtToMm(dim.Origin.X),
                    y = UnitHelper.FtToMm(dim.Origin.Y),
                    z = UnitHelper.FtToMm(dim.Origin.Z)
                } : null,
                style = doc.GetElement(dim.GetTypeId())?.Name ?? ""
            }).ToList();

            return new { ok = true, viewId = view.Id.IntValue(), totalCount, items = dims };
        }
    }

    /// <summary>
    /// 指定参照要素間に寸法注記を新規作成（例：壁芯寸法）
    /// refs: [elementId, elementId, ...] / viewId 必須 / 任意 typeId
    /// </summary>
    public class CreateDimensionCommand : IRevitCommandHandler
    {
        public string CommandName => "create_dimension";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int viewId = p.Value<int>("viewId");
            int typeId = p.Value<int?>("typeId") ?? 0;

            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            if (view == null) return new { ok = false, msg = $"View not found: {viewId}" };

            var refsArr = p["refs"] as JArray;
            if (refsArr == null || refsArr.Count < 2)
                return new { ok = false, msg = "Invalid parameters: need viewId and at least 2 refs." };

            // 1) 参照候補を収集
            var refArray = new ReferenceArray();
            var problems = new List<object>();
            var pickedElems = new List<Element>();

            foreach (var jt in refsArr)
            {
                int eid = jt.Value<int>();
                var elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                if (elem == null)
                {
                    problems.Add(new { elementId = eid, reason = "Element not found." });
                    continue;
                }
                pickedElems.Add(elem);

                var r = TryGetDimensionReference(elem);
                if (r != null)
                {
                    refArray.Append(r);
                }
                else
                {
                    problems.Add(new
                    {
                        elementId = eid,
                        reason = "参照が取得できません（Grid/LocationCurve/一部のDetail Line以外は不可など）。"
                    });
                }
            }

            if (refArray.Size != 2)
            {
                return new
                {
                    ok = false,
                    msg = "Failed to create dimension: Invalid number of references.",
                    details = new { collected = refArray.Size, problems }
                };
            }

            // 2) 寸法線（Line）の決定：両者の中点→端点0 の順でフォールバック
            XYZ a, b;
            if (!TryGetMidPoint(pickedElems[0], out a)) a = TryGetAnyEndPoint(pickedElems[0]) ?? XYZ.Zero;
            if (!TryGetMidPoint(pickedElems[1], out b)) b = TryGetAnyEndPoint(pickedElems[1]) ?? XYZ.Zero;

            if (a.IsAlmostEqualTo(XYZ.Zero) || b.IsAlmostEqualTo(XYZ.Zero))
            {
                return new
                {
                    ok = false,
                    msg = "参照は取得できましたが、寸法線の両端点を決定できませんでした（ビュー平面外・0長さ等）。",
                    details = new { a = a.ToString(), b = b.ToString() }
                };
            }

            var line = Line.CreateBound(a, b);

            // 3) 作成
            Dimension dim = null;
            using (var tx = new Transaction(doc, "Create Dimension"))
            {
                try
                {
                    tx.Start();
                    dim = doc.Create.NewDimension(view, line, refArray);

                    if (typeId > 0)
                    {
                        var typeElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as DimensionType;
                        if (typeElem != null) dim.ChangeTypeId(typeElem.Id);
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Failed to create dimension: {ex.Message}" };
                }
            }

            return new { ok = true, elementId = dim.Id.IntValue() };
        }

        // --- 参照取得の拡張 ---
        private Reference TryGetDimensionReference(Element e)
        {
            // 1) Grid は new Reference(grid) で寸法参照にできる
            if (e is Grid g)
            {
                try { return new Reference(g); }
                catch { /* 続行 */ }
            }

            // 2) LocationCurve（壁・梁・柱など）→ Curve.Reference
            if (e.Location is LocationCurve lc)
            {
                try
                {
                    var r = lc.Curve?.Reference;
                    if (r != null) return r;
                }
                catch { /* 続行 */ }
            }

            // 3) 詳細線（ViewSpecific な CurveElement）
            if (e is CurveElement ce && ce.ViewSpecific)
            {
                try
                {
                    var r = ce.GeometryCurve?.Reference; // 取れない場合もある
                    if (r != null) return r;
                }
                catch { /* 続行 */ }
            }

            // 4) 最後の手段：要素参照で通るケースもある
            try { return new Reference(e); } catch { }

            return null;
        }

        // 中点の推定（Grid/LocationCurve/DetailCurve）
        private bool TryGetMidPoint(Element e, out XYZ mid)
        {
            mid = XYZ.Zero;

            if (e is Grid g)
            {
                var c = g.Curve;
                if (c != null) { mid = c.Evaluate(0.5, true); return true; }
            }

            if (e.Location is LocationCurve lc && lc.Curve != null)
            {
                mid = lc.Curve.Evaluate(0.5, true);
                return true;
            }

            if (e is CurveElement ce && ce.GeometryCurve != null)
            {
                mid = ce.GeometryCurve.Evaluate(0.5, true);
                return true;
            }

            return false;
        }

        // 端点0だけでも取る（最終フォールバック）
        private XYZ? TryGetAnyEndPoint(Element e)
        {
            if (e is Grid g && g.Curve != null) return g.Curve.GetEndPoint(0);
            if (e.Location is LocationCurve lc && lc.Curve != null) return lc.Curve.GetEndPoint(0);
            if (e is CurveElement ce && ce.GeometryCurve != null) return ce.GeometryCurve.GetEndPoint(0);
            return null;
        }
    }

    /// <summary>
    /// 寸法注記の削除
    /// </summary>
    public class DeleteDimensionCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_dimension";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int dimId = ((JObject)cmd.Params).Value<int>("elementId");

            using (var tx = new Transaction(doc, "Delete Dimension"))
            {
                tx.Start();
                doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(dimId));
                tx.Commit();
            }
            return new { ok = true };
        }
    }

    /// <summary>
    /// 寸法注記の平行移動（寸法線のOriginをXYZ移動）
    /// dx/dy/dz は mm で受け取り
    /// </summary>
    public class MoveDimensionCommand : IRevitCommandHandler
    {
        public string CommandName => "move_dimension";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int dimId = p.Value<int>("elementId");
            double dx = p.Value<double?>("dx") ?? 0;
            double dy = p.Value<double?>("dy") ?? 0;
            double dz = p.Value<double?>("dz") ?? 0;

            var dim = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(dimId)) as Dimension;
            if (dim == null)
                return new { ok = false, msg = "Dimension not found." };

            using (var tx = new Transaction(doc, "Move Dimension"))
            {
                tx.Start();
                var moveVec = new XYZ(UnitHelper.MmToFt(dx), UnitHelper.MmToFt(dy), UnitHelper.MmToFt(dz));
                ElementTransformUtils.MoveElement(doc, dim.Id, moveVec);
                tx.Commit();
            }
            return new { ok = true };
        }
    }

    /// <summary>
    /// 寸法注記のアラインメント（端点座標を揃える）
    /// </summary>
    public class AlignDimensionCommand : IRevitCommandHandler
    {
        public string CommandName => "align_dimension";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            // 単純化：指定寸法をターゲット要素の中心線参照で作り直す
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int dimId = p.Value<int>("elementId");
            int targetElemId = p.Value<int>("targetElementId");

            var dim = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(dimId)) as Dimension;
            var tgtElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(targetElemId));
            if (dim == null || tgtElem == null)
                return new { ok = false, msg = "Dimension or target not found." };

            var tgtLoc = tgtElem.Location as LocationCurve;
            if (tgtLoc == null)
                return new { ok = false, msg = "Target does not have a LocationCurve." };

            using (var tx = new Transaction(doc, "Align Dimension"))
            {
                tx.Start();
                try
                {
                    var pt0 = tgtLoc.Curve.GetEndPoint(0);
                    var pt1 = tgtLoc.Curve.GetEndPoint(1);

                    var refArray = new ReferenceArray();
                    foreach (Reference r in dim.References) refArray.Append(r);

                    var newLine = Line.CreateBound(pt0, pt1);
                    var newDim = doc.Create.NewDimension(dim.View, newLine, refArray);

                    doc.Delete(dim.Id);
                    tx.Commit();

                    return new { ok = true, elementId = newDim.Id.IntValue() };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Failed to align dimension: {ex.Message}" };
                }
            }
        }
    }

    /// <summary>
    /// 寸法のスタイル・書式を更新
    /// - typeId: DimensionType の変更（任意）
    /// - unitSymbol: "mm" | "cm" | "m" のような長さ単位（任意）
    ///   ※ Revit 2023+ の FormatOptions を安全に更新（失敗したら既定のまま）
    /// </summary>
    public class UpdateDimensionFormatCommand : IRevitCommandHandler
    {
        public string CommandName => "update_dimension_format";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int dimId = p.Value<int>("elementId");
            string unitSymbol = p.Value<string>("unitSymbol"); // 省略可: "mm","cm","m"
            int? newTypeId = p.Value<int?>("typeId");          // 省略可

            var dim = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(dimId)) as Dimension;
            if (dim == null)
                return new { ok = false, msg = $"Dimension {dimId} not found." };

            using (var tx = new Transaction(doc, "Update Dimension Format"))
            {
                tx.Start();

                // ① タイプ変更（任意）
                if (newTypeId.HasValue)
                {
                    var tp = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(newTypeId.Value)) as DimensionType;
                    if (tp != null) dim.ChangeTypeId(tp.Id);
                }

                // ② 単位種別の変更（任意・可能な限り安全に）
                if (!string.IsNullOrWhiteSpace(unitSymbol))
                {
                    try
                    {
                        var dimType = doc.GetElement(dim.GetTypeId()) as DimensionType;
                        if (dimType != null)
                        {
                            var fo = dimType.GetUnitsFormatOptions(); // 2023+ API
                            ForgeTypeId lengthUnit = fo.GetUnitTypeId(); // 既存値

                            switch (unitSymbol.Trim().ToLower())
                            {
                                case "mm": lengthUnit = UnitTypeId.Millimeters; break;
                                case "cm": lengthUnit = UnitTypeId.Centimeters; break;
                                case "m": lengthUnit = UnitTypeId.Meters; break;
                                default:
                                    lengthUnit = fo.GetUnitTypeId(); // 未知指定は何もしない
                                    break;
                            }

                            fo.SetUnitTypeId(lengthUnit);
                            dimType.SetUnitsFormatOptions(fo);
                        }
                    }
                    catch
                    {
                        // テンプレート/バージョン依存で失敗する可能性 → 無視（安全側）
                    }
                }

                tx.Commit();
            }

            return new { ok = true };
        }
    }

    /// <summary>
    /// 寸法タイプ一覧取得
    /// </summary>
    public class GetDimensionTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_dimension_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            var p = (cmd.Params as JObject) ?? new JObject();
            var shape = p["_shape"] as JObject;
            int limit = Math.Max(0, shape?["page"]?.Value<int?>("limit") ?? int.MaxValue);
            int skip = Math.Max(0, shape?["page"]?.Value<int?>("skip") ?? shape?["page"]?.Value<int?>("offset") ?? 0);
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            string nameContains = p.Value<string>("nameContains");

            var coll = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>();

            if (!string.IsNullOrWhiteSpace(nameContains))
                coll = coll.Where(t => (t.Name ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = coll
                .Select(t => new { t, name = t.Name ?? string.Empty, id = t.Id.IntValue() })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.t)
                .ToList();

            int totalCount = ordered.Count;
            if (summaryOnly)
                return new { ok = true, totalCount };

            IEnumerable<DimensionType> paged = ordered;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(t => t.Id.IntValue()).ToList();
                return new { ok = true, totalCount, typeIds = ids };
            }

            var types = paged
                .Select(t => new
                {
                    typeId = t.Id.IntValue(),
                    name = t.Name,
                    style = t.StyleType.ToString()
                })
                .ToList();

            return new { ok = true, totalCount, types };
        }
    }
}


