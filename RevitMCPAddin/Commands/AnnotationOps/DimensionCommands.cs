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
using System.Text.RegularExpressions;
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
            var p = (cmd.Params as JObject) ?? new JObject();
            int viewId = p.Value<int?>("viewId") ?? uiapp.ActiveUIDocument.ActiveView.Id.IntValue();

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

            var dims = new List<object>();
            foreach (var dim in paged)
            {
                List<int> refs = null;
                if (includeRefs)
                {
                    try
                    {
                        refs = dim.References?.Cast<Reference>().Select(r => r.ElementId.IntValue()).ToList();
                    }
                    catch
                    {
                        refs = null;
                    }
                }

                string value = null;
                if (includeValue)
                {
                    try { value = dim.ValueString; } catch { value = null; }
                }

                object originObj = null;
                bool hasOrigin = false;
                if (includeOrigin)
                {
                    XYZ o = null;
                    try
                    {
                        o = dim.Origin;
                    }
                    catch
                    {
                        // SpotDimension などで Origin が例外になるケースがある
                    }

                    if (o == null)
                    {
                        try
                        {
                            var c = dim.Curve;
                            if (c != null)
                            {
                                o = c.Evaluate(0.5, true);
                            }
                        }
                        catch
                        {
                            // curve 取得不可は継続
                        }
                    }

                    if (o == null)
                    {
                        try
                        {
                            var bb = dim.get_BoundingBox(view) ?? dim.get_BoundingBox(null);
                            if (bb != null)
                            {
                                o = (bb.Min + bb.Max) * 0.5;
                            }
                        }
                        catch
                        {
                            // bbox 取得不可は継続
                        }
                    }

                    if (o != null)
                    {
                        hasOrigin = true;
                        originObj = new
                        {
                            x = UnitHelper.FtToMm(o.X),
                            y = UnitHelper.FtToMm(o.Y),
                            z = UnitHelper.FtToMm(o.Z)
                        };
                    }
                }

                int segmentCount = 0;
                try { segmentCount = dim.NumberOfSegments; } catch { segmentCount = 0; }

                dims.Add(new
                {
                    elementId = dim.Id.IntValue(),
                    name = dim.Name,
                    typeId = dim.GetTypeId().IntValue(),
                    references = refs,
                    value, // Revit書式済み
                    origin = originObj,
                    hasOrigin,
                    segmentCount,
                    dimensionClass = dim.GetType().Name,
                    style = doc.GetElement(dim.GetTypeId())?.Name ?? ""
                });
            }

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

    /// <summary>
    /// source ビューの「柱外形↔通り芯」寸法配置（X/Yオフセット・タイプ）をテンプレート化し、
    /// target ビュー群へ同一ルールで適用する。
    /// 対象ビューは既定で "^1/2_COL_(\\d+)$" に一致するビュー名。
    /// </summary>
    public class ApplyColumnGridDimensionStandardToViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "apply_column_grid_dimension_standard_to_views";

        private sealed class AxisTemplate
        {
            public string Axis = ""; // "X" or "Y"
            public int DimensionTypeId;
            public double OffsetFt;  // axis=X => Y offset, axis=Y => X offset
            public int WidthDimensionTypeId;
            public double? WidthOffsetFt; // axis=X => Y offset, axis=Y => X offset
        }

        private sealed class FaceBundle
        {
            public Reference LeftRef;
            public Reference RightRef;
            public Reference BottomRef;
            public Reference TopRef;
            public double LeftX;
            public double RightX;
            public double BottomY;
            public double TopY;
        }

        private sealed class GridPick
        {
            public Grid VerticalGrid;
            public double VerticalX;
            public Grid HorizontalGrid;
            public double HorizontalY;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null || uidoc == null) return new { ok = false, msg = "No active document." };

            var p = (cmd.Params as JObject) ?? new JObject();
            int sourceViewId = p.Value<int?>("sourceViewId") ?? uidoc.ActiveView.Id.IntValue();
            string viewNameRegex = p.Value<string>("targetViewNameRegex") ?? @"^1/2_COL_(\d+)$";
            bool replaceExisting = p.Value<bool?>("replaceExisting") ?? true;
            bool includeSourceView = p.Value<bool?>("includeSourceView") ?? false;

            var sourceView = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(sourceViewId)) as View;
            if (sourceView == null) return new { ok = false, msg = $"source view not found: {sourceViewId}" };
            if (!(sourceView is ViewPlan)) return new { ok = false, msg = "source view must be plan-like view." };

            if (!TryResolveColumnIdFromView(sourceView, viewNameRegex, out int sourceColumnId))
            {
                return new { ok = false, msg = "source view name does not include column id by regex." };
            }

            var sourceColumn = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(sourceColumnId)) as FamilyInstance;
            if (sourceColumn == null)
            {
                return new { ok = false, msg = $"source column not found: {sourceColumnId}" };
            }

            if (!TryGetElementCenter(sourceColumn, sourceView, out XYZ srcCenter))
            {
                return new { ok = false, msg = "source column center not found." };
            }

            if (!TryExtractAxisTemplates(doc, sourceView, sourceColumnId, srcCenter, out AxisTemplate tx, out AxisTemplate ty))
            {
                return new { ok = false, msg = "source view dimension template (X/Y) not found." };
            }

            var regex = new Regex(viewNameRegex, RegexOptions.Compiled);
            var targetViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && (includeSourceView || v.Id != sourceView.Id))
                .Where(v => regex.IsMatch(v.Name))
                .Where(v => v is ViewPlan)
                .OrderBy(v => v.Name)
                .ToList();

            var rows = new List<object>();
            int okCount = 0;
            int ngCount = 0;

            var originalActiveView = uidoc.ActiveView;
            foreach (var tv in targetViews)
            {
                try
                {
                    // Annotation を確実に対象ビューへ作るため、対象ビューを順次アクティブ化
                    if (uidoc.ActiveView == null || uidoc.ActiveView.Id != tv.Id)
                    {
                        try { uidoc.ActiveView = tv; }
                        catch { /* ignore; continue with explicit view anyway */ }
                    }

                    using (var txOne = new Transaction(doc, "Apply Column Grid Dimension Standard"))
                    {
                        txOne.Start();

                        if (!TryResolveColumnIdFromView(tv, viewNameRegex, out int colId))
                        {
                            rows.Add(new { viewId = tv.Id.IntValue(), viewName = tv.Name, ok = false, msg = "column id parse failed" });
                            ngCount++;
                            txOne.RollBack();
                            continue;
                        }

                        var col = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(colId)) as FamilyInstance;
                        if (col == null)
                        {
                            rows.Add(new { viewId = tv.Id.IntValue(), viewName = tv.Name, ok = false, msg = $"column not found: {colId}" });
                            ngCount++;
                            txOne.RollBack();
                            continue;
                        }

                        if (!TryGetElementCenter(col, tv, out XYZ c))
                        {
                            rows.Add(new { viewId = tv.Id.IntValue(), viewName = tv.Name, ok = false, msg = "column center not found" });
                            ngCount++;
                            txOne.RollBack();
                            continue;
                        }

                        if (!TryPickNearestOrthogonalGrids(doc, tv, c, out GridPick gp))
                        {
                            rows.Add(new { viewId = tv.Id.IntValue(), viewName = tv.Name, ok = false, msg = "vertical/horizontal grid not found in view" });
                            ngCount++;
                            txOne.RollBack();
                            continue;
                        }

                        if (!TryGetFaceBundle(col, tv, out FaceBundle fb))
                        {
                            rows.Add(new { viewId = tv.Id.IntValue(), viewName = tv.Name, ok = false, msg = "column face references not found" });
                            ngCount++;
                            txOne.RollBack();
                            continue;
                        }

                        int deleted = 0;
                        if (replaceExisting)
                        {
                            deleted = DeleteTargetDimensions(doc, tv, colId);
                        }

                        int created = 0;
                        created += CreateAxisDimensionX(doc, tv, fb, gp, c, tx);
                        created += CreateAxisDimensionY(doc, tv, fb, gp, c, ty);

                        rows.Add(new
                        {
                            viewId = tv.Id.IntValue(),
                            viewName = tv.Name,
                            columnId = colId,
                            ok = true,
                            deleted,
                            created,
                            typeX = tx.DimensionTypeId,
                            typeY = ty.DimensionTypeId
                        });
                        okCount++;
                        txOne.Commit();
                    }
                }
                catch (Exception ex)
                {
                    rows.Add(new { viewId = tv.Id.IntValue(), viewName = tv.Name, ok = false, msg = ex.Message });
                    ngCount++;
                }
            }

            if (originalActiveView != null && uidoc.ActiveView != null && uidoc.ActiveView.Id != originalActiveView.Id)
            {
                try { uidoc.ActiveView = originalActiveView; }
                catch { /* ignore */ }
            }

            return new
            {
                ok = ngCount == 0,
                sourceViewId = sourceView.Id.IntValue(),
                sourceViewName = sourceView.Name,
                targetCount = targetViews.Count,
                okCount,
                ngCount,
                results = rows
            };
        }

        private static bool TryResolveColumnIdFromView(View v, string pattern, out int columnId)
        {
            columnId = 0;
            if (v == null) return false;
            var m = Regex.Match(v.Name ?? "", pattern);
            if (!m.Success || m.Groups.Count < 2) return false;
            return int.TryParse(m.Groups[1].Value, out columnId);
        }

        private static bool TryGetElementCenter(Element e, View v, out XYZ center)
        {
            center = null;
            if (e is FamilyInstance fi && fi.Location is LocationPoint lp && lp.Point != null)
            {
                center = lp.Point;
                return true;
            }

            var bb = e.get_BoundingBox(v) ?? e.get_BoundingBox(null);
            if (bb == null) return false;
            center = (bb.Min + bb.Max) * 0.5;
            return true;
        }

        private static bool TryExtractAxisTemplates(Document doc, View sourceView, int sourceColumnId, XYZ sourceCenter, out AxisTemplate tx, out AxisTemplate ty)
        {
            tx = null;
            ty = null;
            var dims = new FilteredElementCollector(doc, sourceView.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .ToList();

            foreach (var d in dims)
            {
                var refs = new List<Reference>();
                try
                {
                    if (d.References != null) refs = d.References.Cast<Reference>().ToList();
                }
                catch { continue; }

                if (refs.Count < 3) continue;
                int colRefCount = refs.Count(r => r.ElementId != null && r.ElementId.IntValue() == sourceColumnId);
                if (colRefCount < 2) continue;

                var gridRef = refs.FirstOrDefault(r =>
                {
                    if (r.ElementId == null) return false;
                    var ge = doc.GetElement(r.ElementId);
                    return ge is Grid;
                });
                if (gridRef == null) continue;

                var grid = doc.GetElement(gridRef.ElementId) as Grid;
                if (grid == null) continue;
                if (!TryGetGridLine(grid, sourceView, out Line gl)) continue;

                if (!TryGetDimensionOriginSafe(d, sourceView, out XYZ o)) continue;

                bool vertical = Math.Abs(gl.Direction.Y) >= Math.Abs(gl.Direction.X);
                if (vertical)
                {
                    if (tx == null)
                    {
                        tx = new AxisTemplate
                        {
                            Axis = "X",
                            DimensionTypeId = d.GetTypeId().IntValue(),
                            OffsetFt = o.Y - sourceCenter.Y
                        };
                    }
                }
                else
                {
                    if (ty == null)
                    {
                        ty = new AxisTemplate
                        {
                            Axis = "Y",
                            DimensionTypeId = d.GetTypeId().IntValue(),
                            OffsetFt = o.X - sourceCenter.X
                        };
                    }
                }
            }

            // 柱外形寸法（柱↔柱、grid なし）も抽出して再現する
            foreach (var d in dims)
            {
                var refs = new List<Reference>();
                try
                {
                    if (d.References != null) refs = d.References.Cast<Reference>().ToList();
                }
                catch { continue; }

                if (refs.Count < 2) continue;
                bool hasGrid = refs.Any(r => r.ElementId != null && (doc.GetElement(r.ElementId) is Grid));
                if (hasGrid) continue;

                int colRefCount = refs.Count(r => r.ElementId != null && r.ElementId.IntValue() == sourceColumnId);
                if (colRefCount < 2) continue;
                bool allColumn = refs.All(r => r.ElementId != null && r.ElementId.IntValue() == sourceColumnId);
                if (!allColumn) continue;

                if (!TryGetDimensionOriginSafe(d, sourceView, out XYZ o)) continue;
                double dx = Math.Abs(o.X - sourceCenter.X);
                double dy = Math.Abs(o.Y - sourceCenter.Y);

                // 水平寸法線(=X方向寸法)は Y オフセットが支配的
                if (dy >= dx)
                {
                    if (tx != null && !tx.WidthOffsetFt.HasValue)
                    {
                        tx.WidthOffsetFt = o.Y - sourceCenter.Y;
                        tx.WidthDimensionTypeId = d.GetTypeId().IntValue();
                    }
                }
                else
                {
                    if (ty != null && !ty.WidthOffsetFt.HasValue)
                    {
                        ty.WidthOffsetFt = o.X - sourceCenter.X;
                        ty.WidthDimensionTypeId = d.GetTypeId().IntValue();
                    }
                }
            }

            return tx != null && ty != null;
        }

        private static bool TryGetGridLine(Grid g, View v, out Line line)
        {
            line = null;
            try
            {
                var cs = g.GetCurvesInView(DatumExtentType.ViewSpecific, v);
                if (cs != null && cs.Count > 0)
                {
                    line = cs[0] as Line;
                    if (line != null) return true;
                }
            }
            catch { }

            try
            {
                var cs = g.GetCurvesInView(DatumExtentType.Model, v);
                if (cs != null && cs.Count > 0)
                {
                    line = cs[0] as Line;
                    if (line != null) return true;
                }
            }
            catch { }

            line = g.Curve as Line;
            return line != null;
        }

        private static bool TryPickNearestOrthogonalGrids(Document doc, View v, XYZ center, out GridPick gp)
        {
            gp = null;
            var grids = new FilteredElementCollector(doc, v.Id)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();
            if (grids.Count == 0) return false;

            Grid bestV = null; double bestVD = double.MaxValue; double bestVX = 0;
            Grid bestH = null; double bestHD = double.MaxValue; double bestHY = 0;

            foreach (var g in grids)
            {
                if (!TryGetGridLine(g, v, out Line ln)) continue;
                var d = ln.Direction.Normalize();
                var p0 = ln.GetEndPoint(0);

                bool vertical = Math.Abs(d.Y) >= Math.Abs(d.X);
                if (vertical)
                {
                    double x = p0.X;
                    double dist = Math.Abs(center.X - x);
                    if (dist < bestVD) { bestVD = dist; bestV = g; bestVX = x; }
                }
                else
                {
                    double y = p0.Y;
                    double dist = Math.Abs(center.Y - y);
                    if (dist < bestHD) { bestHD = dist; bestH = g; bestHY = y; }
                }
            }

            if (bestV == null || bestH == null) return false;
            gp = new GridPick { VerticalGrid = bestV, VerticalX = bestVX, HorizontalGrid = bestH, HorizontalY = bestHY };
            return true;
        }

        private static bool TryGetFaceBundle(FamilyInstance col, View v, out FaceBundle fb)
        {
            fb = null;
            var opt = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                View = v
            };
            var ge = col.get_Geometry(opt);
            if (ge == null) return false;

            bool hasL = false, hasR = false, hasB = false, hasT = false;
            double lx = 0, rx = 0, by = 0, ty = 0;
            Reference lr = null, rr = null, br = null, tr = null;

            Action<GeometryElement> walk = null;
            walk = (elem) =>
            {
                foreach (var go in elem)
                {
                    if (go is GeometryInstance gi)
                    {
                        var inst = gi.GetInstanceGeometry();
                        if (inst != null) walk(inst);
                        continue;
                    }
                    if (go is Solid s && s.Faces.Size > 0)
                    {
                        foreach (Face f in s.Faces)
                        {
                            var pf = f as PlanarFace;
                            if (pf == null || pf.Reference == null) continue;

                            XYZ n = pf.FaceNormal;
                            if (Math.Abs(n.Z) > 0.2) continue; // plan 寸法対象

                            var bb = pf.GetBoundingBox();
                            if (bb == null) continue;
                            var uv = (bb.Min + bb.Max) * 0.5;
                            var c = pf.Evaluate(uv);

                            if (Math.Abs(n.X) >= Math.Abs(n.Y))
                            {
                                if (n.X < 0)
                                {
                                    if (!hasL || c.X < lx) { hasL = true; lx = c.X; lr = pf.Reference; }
                                }
                                else
                                {
                                    if (!hasR || c.X > rx) { hasR = true; rx = c.X; rr = pf.Reference; }
                                }
                            }
                            else
                            {
                                if (n.Y < 0)
                                {
                                    if (!hasB || c.Y < by) { hasB = true; by = c.Y; br = pf.Reference; }
                                }
                                else
                                {
                                    if (!hasT || c.Y > ty) { hasT = true; ty = c.Y; tr = pf.Reference; }
                                }
                            }
                        }
                    }
                }
            };

            walk(ge);
            if (!(hasL && hasR && hasB && hasT)) return false;

            fb = new FaceBundle
            {
                LeftRef = lr,
                RightRef = rr,
                BottomRef = br,
                TopRef = tr,
                LeftX = lx,
                RightX = rx,
                BottomY = by,
                TopY = ty
            };
            return true;
        }

        private static int DeleteTargetDimensions(Document doc, View v, int colId)
        {
            var dims = new FilteredElementCollector(doc, v.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .ToList();
            int deleted = 0;
            foreach (var d in dims)
            {
                try
                {
                    var refs = d.References?.Cast<Reference>().ToList() ?? new List<Reference>();
                    int colRefCount = refs.Count(r => r.ElementId != null && r.ElementId.IntValue() == colId);
                    bool hasGrid = refs.Any(r =>
                    {
                        if (r.ElementId == null) return false;
                        return doc.GetElement(r.ElementId) is Grid;
                    });
                    bool allColumn = refs.Count >= 2 && refs.All(r => r.ElementId != null && r.ElementId.IntValue() == colId);
                    if ((colRefCount >= 2 && hasGrid) || allColumn)
                    {
                        doc.Delete(d.Id);
                        deleted++;
                    }
                }
                catch { }
            }
            return deleted;
        }

        private static int CreateAxisDimensionX(Document doc, View v, FaceBundle fb, GridPick gp, XYZ center, AxisTemplate t)
        {
            int created = 0;

            var ra = new ReferenceArray();
            ra.Append(fb.LeftRef);
            ra.Append(new Reference(gp.VerticalGrid));
            ra.Append(fb.RightRef);

            double y = center.Y + t.OffsetFt;
            double z = center.Z;
            double minX = Math.Min(fb.LeftX, Math.Min(gp.VerticalX, fb.RightX)) - UnitHelper.MmToFt(100.0);
            double maxX = Math.Max(fb.LeftX, Math.Max(gp.VerticalX, fb.RightX)) + UnitHelper.MmToFt(100.0);
            var ln = Line.CreateBound(new XYZ(minX, y, z), new XYZ(maxX, y, z));

            var d = doc.Create.NewDimension(v, ln, ra);
            if (t.DimensionTypeId > 0)
            {
                var tp = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(t.DimensionTypeId)) as DimensionType;
                if (tp != null) d.ChangeTypeId(tp.Id);
            }
            if (d != null) created++;

            if (t.WidthOffsetFt.HasValue)
            {
                var rwa = new ReferenceArray();
                rwa.Append(fb.LeftRef);
                rwa.Append(fb.RightRef);
                double wy = center.Y + t.WidthOffsetFt.Value;
                var wln = Line.CreateBound(new XYZ(minX, wy, z), new XYZ(maxX, wy, z));
                var wd = doc.Create.NewDimension(v, wln, rwa);
                int wTypeId = t.WidthDimensionTypeId > 0 ? t.WidthDimensionTypeId : t.DimensionTypeId;
                if (wd != null && wTypeId > 0)
                {
                    var wtp = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wTypeId)) as DimensionType;
                    if (wtp != null) wd.ChangeTypeId(wtp.Id);
                }
                if (wd != null) created++;
            }
            return created;
        }

        private static int CreateAxisDimensionY(Document doc, View v, FaceBundle fb, GridPick gp, XYZ center, AxisTemplate t)
        {
            int created = 0;

            var ra = new ReferenceArray();
            ra.Append(fb.BottomRef);
            ra.Append(new Reference(gp.HorizontalGrid));
            ra.Append(fb.TopRef);

            double x = center.X + t.OffsetFt;
            double z = center.Z;
            double minY = Math.Min(fb.BottomY, Math.Min(gp.HorizontalY, fb.TopY)) - UnitHelper.MmToFt(100.0);
            double maxY = Math.Max(fb.BottomY, Math.Max(gp.HorizontalY, fb.TopY)) + UnitHelper.MmToFt(100.0);
            var ln = Line.CreateBound(new XYZ(x, minY, z), new XYZ(x, maxY, z));

            var d = doc.Create.NewDimension(v, ln, ra);
            if (t.DimensionTypeId > 0)
            {
                var tp = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(t.DimensionTypeId)) as DimensionType;
                if (tp != null) d.ChangeTypeId(tp.Id);
            }
            if (d != null) created++;

            if (t.WidthOffsetFt.HasValue)
            {
                var rwa = new ReferenceArray();
                rwa.Append(fb.BottomRef);
                rwa.Append(fb.TopRef);
                double wx = center.X + t.WidthOffsetFt.Value;
                var wln = Line.CreateBound(new XYZ(wx, minY, z), new XYZ(wx, maxY, z));
                var wd = doc.Create.NewDimension(v, wln, rwa);
                int wTypeId = t.WidthDimensionTypeId > 0 ? t.WidthDimensionTypeId : t.DimensionTypeId;
                if (wd != null && wTypeId > 0)
                {
                    var wtp = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wTypeId)) as DimensionType;
                    if (wtp != null) wd.ChangeTypeId(wtp.Id);
                }
                if (wd != null) created++;
            }
            return created;
        }

        private static bool TryGetDimensionOriginSafe(Dimension dim, View view, out XYZ o)
        {
            o = null;
            try
            {
                o = dim.Origin;
                if (o != null) return true;
            }
            catch { }

            try
            {
                var c = dim.Curve;
                if (c != null)
                {
                    o = c.Evaluate(0.5, true);
                    if (o != null) return true;
                }
            }
            catch { }

            try
            {
                var bb = dim.get_BoundingBox(view) ?? dim.get_BoundingBox(null);
                if (bb != null)
                {
                    o = (bb.Min + bb.Max) * 0.5;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}


