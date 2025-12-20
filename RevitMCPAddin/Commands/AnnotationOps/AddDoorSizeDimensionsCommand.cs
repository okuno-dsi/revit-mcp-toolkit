// ================================================================
// File: Commands/AnnotationOps/AddDoorSizeDimensionsCommand.cs
// Purpose: Add associative door width/height dimensions in a view.
// Target : .NET Framework 4.8 / C# 8 / Revit 2023+
// Notes  :
//  - Uses FamilyInstance reference planes (Left/Right/Bottom/Top) when available.
//  - Dimension line placement is computed in the view coordinate system and mapped back to world.
//  - Default units: mm (I/O). Internal: ft.
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    public class AddDoorSizeDimensionsCommand : IRevitCommandHandler
    {
        public string CommandName => "add_door_size_dimensions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd.Params as JObject ?? new JObject();

            int viewId = p.Value<int?>("viewId") ?? uidoc.ActiveView?.Id.IntegerValue ?? 0;
            if (viewId <= 0) return new { ok = false, msg = "viewId を解決できませんでした。" };

            var view = doc.GetElement(new ElementId(viewId)) as View;
            if (view == null) return new { ok = false, msg = $"View not found: {viewId}" };

            // Optional: detach template
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            if (detachTemplate && view.ViewTemplateId != ElementId.InvalidElementId)
            {
                using (var tx0 = new Transaction(doc, "Detach View Template (add_door_size_dimensions)"))
                {
                    try
                    {
                        tx0.Start();
                        TxnUtil.ConfigureProceedWithWarnings(tx0);
                        view.ViewTemplateId = ElementId.InvalidElementId;
                        tx0.Commit();
                    }
                    catch
                    {
                        try { tx0.RollBack(); } catch { }
                    }
                }
            }

            // Params
            bool addWidth = p.Value<bool?>("addWidth") ?? true;
            bool addHeight = p.Value<bool?>("addHeight") ?? true;
            bool debug = p.Value<bool?>("debug") ?? false;

            // Offset / stacking
            double offsetMm = p.Value<double?>("offsetMm") ?? 200.0;
            var widthOffsetsMm = ReadDoubleArray(p["widthOffsetsMm"]);
            var heightOffsetsMm = ReadDoubleArray(p["heightOffsetsMm"]);
            double? widthOffsetMmSingle = p.Value<double?>("widthOffsetMm");
            double? heightOffsetMmSingle = p.Value<double?>("heightOffsetMm");
            var requestedWidthOffsetsMm = BuildOffsetsMm(widthOffsetsMm, widthOffsetMmSingle, offsetMm);
            var requestedHeightOffsetsMm = BuildOffsetsMm(heightOffsetsMm, heightOffsetMmSingle, offsetMm);

            // Placement
            string widthSide = NormalizeSide(p.Value<string>("widthSide"), "top", new[] { "top", "bottom" });
            string heightSide = NormalizeSide(p.Value<string>("heightSide"), "left", new[] { "left", "right" });

            // Visibility / crop clamp
            bool ensureVisible = p.Value<bool?>("ensureVisible") ?? true;
            bool ensureDimensionsVisible = p.Value<bool?>("ensureDimensionsVisible") ?? ensureVisible;
            bool keepInsideCrop = p.Value<bool?>("keepInsideCrop") ?? true;
            bool expandCropToFit = p.Value<bool?>("expandCropToFit") ?? p.Value<bool?>("expandCrop") ?? keepInsideCrop;
            double cropMarginMm = p.Value<double?>("cropMarginMm") ?? 30.0;
            double cropMarginFt = UnitHelper.MmToFt(cropMarginMm);

            // Dimension type selection (optional)
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            var resolvedType = ResolveDimensionType(doc, typeId, typeName);
            if (!resolvedType.ok)
                return new { ok = false, msg = resolvedType.msg ?? "寸法タイプ(typeId/typeName)の解決に失敗しました。" };

            // Offset mode:
            // - "absolute"   : offsets are absolute model-space distances from the element bounds (legacy)
            // - "leaderPlus" : offsets = (leaderLengthPaperMm * viewScale) + requestedOffsetMm
            // Default: leaderPlus (better readability for element views with tight crop)
            string offsetMode = (p.Value<string>("offsetMode") ?? p.Value<string>("offset_mode") ?? "leaderPlus").Trim();
            if (string.IsNullOrWhiteSpace(offsetMode)) offsetMode = "leaderPlus";
            offsetMode = offsetMode.Trim().ToLowerInvariant();
            bool useLeaderPlus = (offsetMode == "leaderplus" || offsetMode == "leader_plus" || offsetMode == "leader+");

            double? leaderLengthPaperMmOverride = p.Value<double?>("leaderLengthPaperMm")
                ?? p.Value<double?>("leader_length_paper_mm")
                ?? p.Value<double?>("leaderLengthMmPaper");

            double baseOffsetPaperMm = 0.0;
            double baseOffsetModelMm = 0.0;
            string baseOffsetSource = useLeaderPlus ? "unresolved" : "offsetMode=absolute";

            if (useLeaderPlus)
            {
                var effectiveTypeId = ResolveEffectiveDimensionTypeId(doc, resolvedType.typeId);
                baseOffsetPaperMm = ResolveLeaderLengthPaperMm(doc, effectiveTypeId, leaderLengthPaperMmOverride, out baseOffsetSource);
                int scale = 1;
                try { scale = Math.Max(1, view.Scale); } catch { scale = 1; }
                baseOffsetModelMm = baseOffsetPaperMm * scale;
            }

            var finalWidthOffsetsMm = ApplyBaseOffset(requestedWidthOffsetsMm, baseOffsetModelMm);
            var finalHeightOffsetsMm = ApplyBaseOffset(requestedHeightOffsetsMm, baseOffsetModelMm);

            // Optional overrides for created dimensions
            var overrideRgb = p["overrideRgb"] as JObject;
            var overrideColor = TryReadColor(overrideRgb);
            OverrideGraphicSettings ogs = null;
            if (overrideColor != null)
            {
                ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(overrideColor);
            }

            // Extra/custom dimensions
            var extraSpecs = p["dimensionSpecs"] as JArray;

            // Targets
            var targetIdsTok = (p["elementIds"] as JArray) ?? (p["doorIds"] as JArray);
            var doorElems = new List<Element>();
            if (targetIdsTok != null && targetIdsTok.Count > 0)
            {
                foreach (var jt in targetIdsTok)
                {
                    int eid = 0;
                    try { eid = jt.Value<int>(); } catch { eid = 0; }
                    if (eid <= 0) continue;
                    var e = doc.GetElement(new ElementId(eid));
                    if (e == null) continue;
                    if (e.Category?.Id?.IntegerValue != (int)BuiltInCategory.OST_Doors) continue;
                    doorElems.Add(e);
                }
            }
            else
            {
                try
                {
                    doorElems = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .WhereElementIsNotElementType()
                        .ToElements()
                        .ToList();
                }
                catch
                {
                    doorElems = new List<Element>();
                }
            }

            if (doorElems.Count == 0)
                return new { ok = false, viewId, msg = "ビュー内にドアが見つかりませんでした。", details = new { viewName = view.Name, viewType = view.ViewType.ToString() } };

            // View coordinate system basis (prefer CropBox.Transform when possible)
            BoundingBoxXYZ crop = null;
            try { crop = view.CropBox; } catch { crop = null; }
            bool cropActive = false;
            try { cropActive = view.CropBoxActive; } catch { cropActive = false; }

            XYZ cropMin = null;
            XYZ cropMax = null;
            Transform toWorld = null;
            if (!TryGetViewTransformFromCrop(crop, out toWorld, out cropMin, out cropMax))
            {
                XYZ origin = view.Origin;
                XYZ ux = NormalizeOrDefault(view.RightDirection, XYZ.BasisX);
                XYZ uy = NormalizeOrDefault(view.UpDirection, XYZ.BasisY);
                XYZ uz = NormalizeOrDefault(view.ViewDirection, XYZ.BasisZ);

                toWorld = Transform.Identity;
                toWorld.Origin = origin;
                toWorld.BasisX = ux;
                toWorld.BasisY = uy;
                toWorld.BasisZ = uz;
            }

            Transform toView;
            try { toView = toWorld.Inverse; }
            catch
            {
                return new { ok = false, viewId, msg = "ビュー座標系の逆変換を構築できませんでした（Transform.Inverse）。" };
            }

            if (!cropActive)
            {
                cropMin = null;
                cropMax = null;
            }

            bool useCropClamp = keepInsideCrop && cropActive && cropMin != null && cropMax != null;

            var created = new List<object>();
            var skipped = new List<object>();
            var visibility = new List<object>();
            bool anyCropExpanded = false;

            using (var tx = new Transaction(doc, "Add Door Size Dimensions"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                // Ensure "Dimensions" category is visible so created dimensions show up and are discoverable
                if (ensureDimensionsVisible)
                {
                    try
                    {
                        var cat = Category.GetCategory(doc, BuiltInCategory.OST_Dimensions);
                        if (cat != null)
                        {
                            bool hidden = false;
                            try { hidden = view.GetCategoryHidden(cat.Id); } catch { hidden = false; }
                            if (hidden)
                            {
                                bool templateApplied = (view.ViewTemplateId != ElementId.InvalidElementId);
                                if (templateApplied && !detachTemplate)
                                {
                                    tx.RollBack();
                                    return new
                                    {
                                        ok = false,
                                        code = "VIEW_TEMPLATE_LOCK",
                                        msg = "ビューにビューテンプレートが適用されているため、寸法カテゴリの表示設定を変更できません。detachViewTemplate:true を指定するか、手動でテンプレートを外してください。",
                                        details = new { viewId = view.Id.IntegerValue, viewName = view.Name, templateViewId = view.ViewTemplateId.IntegerValue }
                                    };
                                }

                                view.SetCategoryHidden(cat.Id, false);
                                visibility.Add(new { categoryId = cat.Id.IntegerValue, name = cat.Name, visible = true, changed = true });
                            }
                            else
                            {
                                visibility.Add(new { categoryId = cat.Id.IntegerValue, name = cat.Name, visible = true, changed = false });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        visibility.Add(new { categoryId = (int)BuiltInCategory.OST_Dimensions, name = "Dimensions", visible = (bool?)null, changed = false, error = ex.Message });
                    }
                }

                foreach (var doorElem in doorElems)
                {
                    int doorId = doorElem.Id.IntegerValue;

                    try
                    {
                        var fi = doorElem as FamilyInstance;
                        if (fi == null)
                        {
                            skipped.Add(new { elementId = doorId, reason = "ドアが FamilyInstance ではありません。" });
                            continue;
                        }

                        var refsForWidth = addWidth ? TryGetRefs(fi, FamilyInstanceReferenceType.Left, FamilyInstanceReferenceType.Right) : null;
                        var refsForHeight = addHeight ? TryGetRefs(fi, FamilyInstanceReferenceType.Bottom, FamilyInstanceReferenceType.Top) : null;

                        if (addWidth && refsForWidth == null)
                            skipped.Add(new { elementId = doorId, kind = "width", reason = "Left/Right の参照を取得できませんでした（FamilyInstanceReferenceType）。" });
                        if (addHeight && refsForHeight == null)
                            skipped.Add(new { elementId = doorId, kind = "height", reason = "Bottom/Top の参照を取得できませんでした（FamilyInstanceReferenceType）。" });

                        // Bounds (view local) for placement
                        var bb = SafeGetBoundingBox(doorElem, view);
                        if (bb == null)
                        {
                            skipped.Add(new { elementId = doorId, reason = "BoundingBox を取得できませんでした。" });
                            continue;
                        }

                        var bounds = ComputeBoundsInViewLocal(bb, toView);
                        if (!bounds.ok)
                        {
                            skipped.Add(new { elementId = doorId, reason = bounds.msg ?? "ビュー座標でのBB計算に失敗しました。" });
                            continue;
                        }

                        var items = new List<object>();

                        if (refsForWidth != null)
                        {
                            for (int oi = 0; oi < finalWidthOffsetsMm.Count; oi++)
                            {
                                double omm = finalWidthOffsetsMm[oi];
                                double requestedOmm = (oi < requestedWidthOffsetsMm.Count) ? requestedWidthOffsetsMm[oi] : omm;

                                var ra = new ReferenceArray();
                                ra.Append(refsForWidth.Item1);
                                ra.Append(refsForWidth.Item2);

                                double desiredY = (widthSide == "bottom")
                                    ? (bounds.minY - UnitHelper.MmToFt(omm))
                                    : (bounds.maxY + UnitHelper.MmToFt(omm));

                                bool clamped = false;
                                bool cropExpanded = false;
                                string cropExpandError = null;
                                double y = desiredY;

                                if (useCropClamp)
                                {
                                    if (expandCropToFit)
                                    {
                                        bool expanded;
                                        string err;
                                        bool okExpand = TryExpandCropToFitPoint(view, ref cropMin, ref cropMax, null, desiredY, cropMarginFt, out expanded, out err);
                                        cropExpanded = expanded;
                                        if (expanded) anyCropExpanded = true;
                                        if (!okExpand && !string.IsNullOrWhiteSpace(err)) cropExpandError = err;
                                    }

                                    y = Clamp(desiredY, cropMin.Y + cropMarginFt, cropMax.Y - cropMarginFt, out clamped);
                                }

                                var a = toWorld.OfPoint(new XYZ(bounds.minX, y, 0.0));
                                var b = toWorld.OfPoint(new XYZ(bounds.maxX, y, 0.0));

                                var line = SafeCreateBoundLine(a, b);
                                if (line != null)
                                {
                                    var dim = doc.Create.NewDimension(view, line, ra);
                                    ApplyDimensionType(dim, resolvedType.typeId);
                                    ApplyOverrides(view, dim.Id, ogs);
                                    items.Add(new
                                    {
                                        kind = "width",
                                        dimensionId = dim.Id.IntegerValue,
                                        side = widthSide,
                                        requestedOffsetMm = requestedOmm,
                                        baseOffsetModelMm,
                                        offsetMm = omm,
                                        clampedToCrop = clamped,
                                        cropExpanded,
                                        cropExpandError
                                    });
                                }
                                else
                                {
                                    skipped.Add(new { elementId = doorId, kind = "width", reason = "寸法線(Line)が 0 長さ等で作成できませんでした。" });
                                }
                            }
                        }

                        if (refsForHeight != null)
                        {
                            for (int oi = 0; oi < finalHeightOffsetsMm.Count; oi++)
                            {
                                double omm = finalHeightOffsetsMm[oi];
                                double requestedOmm = (oi < requestedHeightOffsetsMm.Count) ? requestedHeightOffsetsMm[oi] : omm;

                                var ra = new ReferenceArray();
                                ra.Append(refsForHeight.Item1);
                                ra.Append(refsForHeight.Item2);

                                double desiredX = (heightSide == "right")
                                    ? (bounds.maxX + UnitHelper.MmToFt(omm))
                                    : (bounds.minX - UnitHelper.MmToFt(omm));

                                bool clamped = false;
                                bool cropExpanded = false;
                                string cropExpandError = null;
                                double x = desiredX;

                                if (useCropClamp)
                                {
                                    if (expandCropToFit)
                                    {
                                        bool expanded;
                                        string err;
                                        bool okExpand = TryExpandCropToFitPoint(view, ref cropMin, ref cropMax, desiredX, null, cropMarginFt, out expanded, out err);
                                        cropExpanded = expanded;
                                        if (expanded) anyCropExpanded = true;
                                        if (!okExpand && !string.IsNullOrWhiteSpace(err)) cropExpandError = err;
                                    }

                                    x = Clamp(desiredX, cropMin.X + cropMarginFt, cropMax.X - cropMarginFt, out clamped);
                                }

                                var a = toWorld.OfPoint(new XYZ(x, bounds.minY, 0.0));
                                var b = toWorld.OfPoint(new XYZ(x, bounds.maxY, 0.0));

                                var line = SafeCreateBoundLine(a, b);
                                if (line != null)
                                {
                                    var dim = doc.Create.NewDimension(view, line, ra);
                                    ApplyDimensionType(dim, resolvedType.typeId);
                                    ApplyOverrides(view, dim.Id, ogs);
                                    items.Add(new
                                    {
                                        kind = "height",
                                        dimensionId = dim.Id.IntegerValue,
                                        side = heightSide,
                                        requestedOffsetMm = requestedOmm,
                                        baseOffsetModelMm,
                                        offsetMm = omm,
                                        clampedToCrop = clamped,
                                        cropExpanded,
                                        cropExpandError
                                    });
                                }
                                else
                                {
                                    skipped.Add(new { elementId = doorId, kind = "height", reason = "寸法線(Line)が 0 長さ等で作成できませんでした。" });
                                }
                            }
                        }

                        // Custom dimensions (advanced): arbitrary reference-pairs
                        if (extraSpecs != null && extraSpecs.Count > 0)
                        {
                            foreach (var specTok in extraSpecs)
                            {
                                var spec = specTok as JObject;
                                if (spec == null) continue;
                                if (spec.Value<bool?>("enabled") == false) continue;

                                string specName = (spec.Value<string>("name") ?? spec.Value<string>("kind") ?? "custom").Trim();
                                string orientation = (spec.Value<string>("orientation") ?? "horizontal").Trim().ToLowerInvariant();
                                bool isHorizontal = (orientation == "horizontal" || orientation == "h" || orientation == "x");
                                bool isVertical = (orientation == "vertical" || orientation == "v" || orientation == "y");
                                if (!isHorizontal && !isVertical) { isHorizontal = true; isVertical = false; }

                                string side = NormalizeSide(
                                    spec.Value<string>("side"),
                                    isHorizontal ? "top" : "left",
                                    isHorizontal ? new[] { "top", "bottom" } : new[] { "left", "right" });

                                double specOffsetMm = spec.Value<double?>("offsetMm") ?? offsetMm;
                                double specFinalOffsetMm = specOffsetMm + (useLeaderPlus ? baseOffsetModelMm : 0.0);

                                var refA = ResolveFamilyInstanceReference(doc, fi, spec["refA"]);
                                var refB = ResolveFamilyInstanceReference(doc, fi, spec["refB"]);
                                if (refA == null || refB == null)
                                {
                                    skipped.Add(new { elementId = doorId, kind = specName, reason = "refA/refB を解決できませんでした。get_family_instance_references で参照一覧を確認してください。" });
                                    continue;
                                }

                                var specType = ResolveDimensionType(doc, spec.Value<int?>("typeId") ?? 0, spec.Value<string>("typeName"));
                                if (!specType.ok)
                                {
                                    skipped.Add(new { elementId = doorId, kind = specName, reason = specType.msg ?? "寸法タイプ(typeId/typeName)が不正です。" });
                                    continue;
                                }

                                var ra = new ReferenceArray();
                                ra.Append(refA);
                                ra.Append(refB);

                                Line line = null;
                                bool clamped = false;
                                bool cropExpanded = false;
                                string cropExpandError = null;

                                if (isHorizontal)
                                {
                                    double desiredY = (side == "bottom")
                                        ? (bounds.minY - UnitHelper.MmToFt(specFinalOffsetMm))
                                        : (bounds.maxY + UnitHelper.MmToFt(specFinalOffsetMm));

                                    double y = desiredY;

                                    if (useCropClamp)
                                    {
                                        if (expandCropToFit)
                                        {
                                            bool expanded;
                                            string err;
                                            bool okExpand = TryExpandCropToFitPoint(view, ref cropMin, ref cropMax, null, desiredY, cropMarginFt, out expanded, out err);
                                            cropExpanded = expanded;
                                            if (expanded) anyCropExpanded = true;
                                            if (!okExpand && !string.IsNullOrWhiteSpace(err)) cropExpandError = err;
                                        }

                                        y = Clamp(desiredY, cropMin.Y + cropMarginFt, cropMax.Y - cropMarginFt, out clamped);
                                    }

                                    var a = toWorld.OfPoint(new XYZ(bounds.minX, y, 0.0));
                                    var b = toWorld.OfPoint(new XYZ(bounds.maxX, y, 0.0));
                                    line = SafeCreateBoundLine(a, b);
                                }
                                else
                                {
                                    double desiredX = (side == "right")
                                        ? (bounds.maxX + UnitHelper.MmToFt(specFinalOffsetMm))
                                        : (bounds.minX - UnitHelper.MmToFt(specFinalOffsetMm));

                                    double x = desiredX;

                                    if (useCropClamp)
                                    {
                                        if (expandCropToFit)
                                        {
                                            bool expanded;
                                            string err;
                                            bool okExpand = TryExpandCropToFitPoint(view, ref cropMin, ref cropMax, desiredX, null, cropMarginFt, out expanded, out err);
                                            cropExpanded = expanded;
                                            if (expanded) anyCropExpanded = true;
                                            if (!okExpand && !string.IsNullOrWhiteSpace(err)) cropExpandError = err;
                                        }

                                        x = Clamp(desiredX, cropMin.X + cropMarginFt, cropMax.X - cropMarginFt, out clamped);
                                    }

                                    var a = toWorld.OfPoint(new XYZ(x, bounds.minY, 0.0));
                                    var b = toWorld.OfPoint(new XYZ(x, bounds.maxY, 0.0));
                                    line = SafeCreateBoundLine(a, b);
                                }

                                if (line == null)
                                {
                                    skipped.Add(new { elementId = doorId, kind = specName, reason = "寸法線(Line)が 0 長さ等で作成できませんでした。" });
                                    continue;
                                }

                                var dim = doc.Create.NewDimension(view, line, ra);
                                var useTypeId = specType.typeId != ElementId.InvalidElementId ? specType.typeId : resolvedType.typeId;
                                ApplyDimensionType(dim, useTypeId);
                                ApplyOverrides(view, dim.Id, ogs);

                                items.Add(new
                                {
                                    kind = specName,
                                    dimensionId = dim.Id.IntegerValue,
                                    orientation = isHorizontal ? "horizontal" : "vertical",
                                    side,
                                    requestedOffsetMm = specOffsetMm,
                                    baseOffsetModelMm,
                                    offsetMm = specFinalOffsetMm,
                                    clampedToCrop = clamped,
                                    cropExpanded,
                                    cropExpandError
                                });
                            }
                        }

                        if (items.Count > 0)
                        {
                            if (debug)
                            {
                                created.Add(new
                                {
                                    elementId = doorId,
                                    created = items,
                                    boundsViewFt = new { bounds.minX, bounds.minY, bounds.maxX, bounds.maxY },
                                    boundsViewMm = new
                                    {
                                        minX = UnitHelper.FtToMm(bounds.minX),
                                        minY = UnitHelper.FtToMm(bounds.minY),
                                        maxX = UnitHelper.FtToMm(bounds.maxX),
                                        maxY = UnitHelper.FtToMm(bounds.maxY)
                                    }
                                });
                            }
                            else
                            {
                                created.Add(new { elementId = doorId, created = items });
                            }
                        }
                    }
                    catch (Exception exEach)
                    {
                        skipped.Add(new { elementId = doorId, reason = "例外: " + exEach.Message });
                    }
                }

                tx.Commit();
            }

            // Force immediate UI update (best-effort)
            try { doc.Regenerate(); } catch { }
            try { uidoc?.RefreshActiveView(); } catch { }

            return new
            {
                ok = true,
                viewId,
                viewName = view.Name,
                viewType = view.ViewType.ToString(),
                doorCount = doorElems.Count,
                createdCount = created.Count,
                offsetMm,
                offsetMode,
                baseOffsetPaperMm,
                baseOffsetModelMm,
                baseOffsetSource,
                dimensionType = resolvedType.typeId != ElementId.InvalidElementId
                    ? new { typeId = resolvedType.typeId.IntegerValue, typeName = resolvedType.typeName }
                    : null,
                crop = new
                {
                    cropActive,
                    keepInsideCrop,
                    expandCropToFit,
                    cropMarginMm,
                    cropExpanded = anyCropExpanded
                },
                visibility,
                items = created,
                skipped
            };
        }

        private static XYZ NormalizeOrDefault(XYZ v, XYZ fallback)
        {
            try
            {
                if (v != null && v.GetLength() > 1e-9) return v.Normalize();
            }
            catch { }
            return fallback;
        }

        private static string NormalizeSide(string raw, string fallback, string[] allowed)
        {
            var s = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(s)) return fallback;
            foreach (var a in allowed)
                if (s == a) return s;
            return fallback;
        }

        private static List<double> ReadDoubleArray(JToken tok)
        {
            var res = new List<double>();
            try
            {
                var arr = tok as JArray;
                if (arr == null) return res;
                foreach (var x in arr)
                {
                    try { res.Add(x.Value<double>()); } catch { }
                }
            }
            catch { }
            return res;
        }

        private static List<double> BuildOffsetsMm(List<double> offsets, double? single, double fallback)
        {
            if (offsets != null && offsets.Count > 0) return offsets;
            if (single.HasValue) return new List<double> { single.Value };
            return new List<double> { fallback };
        }

        private static List<double> ApplyBaseOffset(List<double> offsetsMm, double baseOffsetModelMm)
        {
            var res = new List<double>();
            if (offsetsMm == null || offsetsMm.Count == 0)
            {
                res.Add(Math.Max(0.0, baseOffsetModelMm));
                return res;
            }

            foreach (var o in offsetsMm)
            {
                double v = o + baseOffsetModelMm;
                if (v < 0) v = 0;
                res.Add(v);
            }
            return res;
        }

        private static ElementId ResolveEffectiveDimensionTypeId(Document doc, ElementId requestedTypeId)
        {
            if (requestedTypeId != null && requestedTypeId != ElementId.InvalidElementId) return requestedTypeId;
            if (doc == null) return ElementId.InvalidElementId;

            try
            {
                // Door size dims are linear dimensions; use the project's default linear DimensionType if available.
                var id = doc.GetDefaultElementTypeId(ElementTypeGroup.LinearDimensionType);
                if (id != null && id != ElementId.InvalidElementId) return id;
            }
            catch { }

            try
            {
                var t = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .FirstOrDefault();
                if (t != null) return t.Id;
            }
            catch { }

            return ElementId.InvalidElementId;
        }

        private static double ResolveLeaderLengthPaperMm(Document doc, ElementId dimTypeId, double? overridePaperMm, out string source)
        {
            source = "unresolved";

            try
            {
                if (overridePaperMm.HasValue && overridePaperMm.Value > 0)
                {
                    source = "param:leaderLengthPaperMm";
                    return overridePaperMm.Value;
                }
            }
            catch { }

            DimensionType dt = null;
            try
            {
                if (doc != null && dimTypeId != null && dimTypeId != ElementId.InvalidElementId)
                    dt = doc.GetElement(dimTypeId) as DimensionType;
            }
            catch { dt = null; }

            if (dt != null)
            {
                // 1) Try likely explicit parameter names (JP/EN)
                string matchedName;
                double mm;
                if (TryGetLengthParamMm(dt, out mm, out matchedName,
                    "引出線長さ", "引出し線長さ", "引き出し線長さ",
                    "Leader Length", "Leader length", "leader length"))
                {
                    source = "DimensionType:" + matchedName;
                    return mm;
                }

                // 2) Try keyword scan (JP/EN)
                if (TryFindLengthParamByKeywordMm(dt, out mm, out matchedName, new[] { "引出", "Leader" }))
                {
                    source = "DimensionType~:" + matchedName;
                    return mm;
                }

                // 3) Fallback: Text size (paper) as a robust proxy
                if (TryGetTextSizePaperMm(dt, out mm, out matchedName))
                {
                    source = "DimensionType:" + matchedName;
                    return mm;
                }
            }

            source = "fallback:2.5mm";
            return 2.5;
        }

        private static bool TryGetTextSizePaperMm(DimensionType dt, out double mm, out string matchedName)
        {
            mm = 0.0;
            matchedName = null;
            if (dt == null) return false;

            try
            {
                var p = dt.get_Parameter(BuiltInParameter.TEXT_SIZE);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double raw = p.AsDouble();
                    double vmm = UnitHelper.FtToMm(raw);
                    if (!double.IsNaN(vmm) && !double.IsInfinity(vmm) && vmm > 0)
                    {
                        mm = vmm;
                        matchedName = p.Definition?.Name ?? "TEXT_SIZE";
                        return true;
                    }
                }
            }
            catch { }

            // Locale-dependent fallback (best effort)
            try
            {
                if (TryGetLengthParamMm(dt, out var vmm, out var name, "文字サイズ", "Text Size"))
                {
                    mm = vmm;
                    matchedName = name;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryGetLengthParamMm(Element e, out double mm, out string matchedName, params string[] candidateNames)
        {
            mm = 0.0;
            matchedName = null;
            if (e == null || candidateNames == null || candidateNames.Length == 0) return false;

            foreach (var name in candidateNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                try
                {
                    var p = e.LookupParameter(name);
                    if (p == null) continue;
                    if (p.StorageType != StorageType.Double) continue;
                    double raw = p.AsDouble();
                    double vmm = UnitHelper.FtToMm(raw);
                    if (double.IsNaN(vmm) || double.IsInfinity(vmm) || vmm <= 0) continue;
                    mm = vmm;
                    matchedName = p.Definition?.Name ?? name;
                    return true;
                }
                catch { }
            }

            return false;
        }

        private static bool TryFindLengthParamByKeywordMm(DimensionType dt, out double mm, out string matchedName, string[] keywords)
        {
            mm = 0.0;
            matchedName = null;
            if (dt == null || keywords == null || keywords.Length == 0) return false;

            try
            {
                foreach (Parameter p in dt.Parameters)
                {
                    if (p == null) continue;
                    if (p.StorageType != StorageType.Double) continue;

                    var n = p.Definition?.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(n)) continue;

                    bool hit = false;
                    foreach (var k in keywords)
                    {
                        if (string.IsNullOrWhiteSpace(k)) continue;
                        if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                    }
                    if (!hit) continue;

                    double raw = 0.0;
                    try { raw = p.AsDouble(); } catch { continue; }
                    double vmm = UnitHelper.FtToMm(raw);
                    if (double.IsNaN(vmm) || double.IsInfinity(vmm) || vmm <= 0) continue;

                    mm = vmm;
                    matchedName = n;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryExpandCropToFitPoint(View view, ref XYZ cropMin, ref XYZ cropMax, double? x, double? y, double marginFt,
            out bool expanded, out string error)
        {
            expanded = false;
            error = null;
            if (view == null) return false;

            // Only expand when the desired coordinate is outside the "safe" range (crop ± margin).
            try
            {
                if (cropMin != null && cropMax != null)
                {
                    bool needsExpand = false;
                    if (x.HasValue)
                    {
                        var xv = x.Value;
                        if (xv < (cropMin.X + marginFt) || xv > (cropMax.X - marginFt)) needsExpand = true;
                    }
                    if (y.HasValue)
                    {
                        var yv = y.Value;
                        if (yv < (cropMin.Y + marginFt) || yv > (cropMax.Y - marginFt)) needsExpand = true;
                    }
                    if (!needsExpand) return true;
                }
            }
            catch { }

            try
            {
                var crop = view.CropBox;
                if (crop == null) return false;

                var min = crop.Min;
                var max = crop.Max;

                double newMinX = min.X;
                double newMinY = min.Y;
                double newMaxX = max.X;
                double newMaxY = max.Y;

                if (x.HasValue)
                {
                    double xv = x.Value;
                    if (xv < newMinX + marginFt) newMinX = xv - marginFt;
                    if (xv > newMaxX - marginFt) newMaxX = xv + marginFt;
                }

                if (y.HasValue)
                {
                    double yv = y.Value;
                    if (yv < newMinY + marginFt) newMinY = yv - marginFt;
                    if (yv > newMaxY - marginFt) newMaxY = yv + marginFt;
                }

                bool changed = (Math.Abs(newMinX - min.X) > 1e-9)
                    || (Math.Abs(newMinY - min.Y) > 1e-9)
                    || (Math.Abs(newMaxX - max.X) > 1e-9)
                    || (Math.Abs(newMaxY - max.Y) > 1e-9);
                if (!changed) return true;

                var newCrop = new BoundingBoxXYZ();
                newCrop.Transform = crop.Transform;
                newCrop.Min = new XYZ(newMinX, newMinY, min.Z);
                newCrop.Max = new XYZ(newMaxX, newMaxY, max.Z);

                view.CropBox = newCrop;

                cropMin = newCrop.Min;
                cropMax = newCrop.Max;
                expanded = true;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static double Clamp(double v, double min, double max, out bool didClamp)
        {
            didClamp = false;
            if (v < min) { didClamp = true; return min; }
            if (v > max) { didClamp = true; return max; }
            return v;
        }

        private static Color TryReadColor(JObject rgb)
        {
            try
            {
                if (rgb == null) return null;
                byte r = (byte)Math.Max(0, Math.Min(255, rgb.Value<int?>("r") ?? 0));
                byte g = (byte)Math.Max(0, Math.Min(255, rgb.Value<int?>("g") ?? 0));
                byte b = (byte)Math.Max(0, Math.Min(255, rgb.Value<int?>("b") ?? 0));
                return new Color(r, g, b);
            }
            catch { return null; }
        }

        private static (bool ok, string msg, ElementId typeId, string typeName) ResolveDimensionType(Document doc, int typeId, string typeName)
        {
            try
            {
                if (typeId > 0)
                {
                    var t = doc.GetElement(new ElementId(typeId)) as DimensionType;
                    if (t == null) return (false, $"DimensionType not found: {typeId}", ElementId.InvalidElementId, null);
                    return (true, null, t.Id, t.Name);
                }

                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    var t = new FilteredElementCollector(doc)
                        .OfClass(typeof(DimensionType))
                        .Cast<DimensionType>()
                        .FirstOrDefault(x => string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
                    if (t == null) return (false, $"DimensionType not found by name: '{typeName}'", ElementId.InvalidElementId, null);
                    return (true, null, t.Id, t.Name);
                }

                return (true, null, ElementId.InvalidElementId, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, ElementId.InvalidElementId, null);
            }
        }

        private static void ApplyDimensionType(Dimension dim, ElementId typeId)
        {
            if (dim == null) return;
            if (typeId == null || typeId == ElementId.InvalidElementId) return;
            try { dim.ChangeTypeId(typeId); } catch { }
        }

        private static void ApplyOverrides(View view, ElementId elementId, OverrideGraphicSettings ogs)
        {
            if (view == null || elementId == null || ogs == null) return;
            try { view.SetElementOverrides(elementId, ogs); } catch { }
        }

        private static Reference ResolveFamilyInstanceReference(Document doc, FamilyInstance fi, JToken tok)
        {
            if (doc == null || fi == null) return null;
            if (tok == null || tok.Type == JTokenType.Null) return null;

            // String => stable representation
            if (tok.Type == JTokenType.String)
            {
                var s = tok.Value<string>();
                if (string.IsNullOrWhiteSpace(s)) return null;
                try { return Reference.ParseFromStableRepresentation(doc, s); } catch { return null; }
            }

            if (tok.Type != JTokenType.Object) return null;
            var o = (JObject)tok;

            // stable: "..."
            var stable = o.Value<string>("stable");
            if (!string.IsNullOrWhiteSpace(stable))
            {
                try { return Reference.ParseFromStableRepresentation(doc, stable); } catch { return null; }
            }

            // refType/index: { refType:"Left", index:0 }
            var rt = (o.Value<string>("refType") ?? o.Value<string>("referenceType") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rt)) return null;
            if (!Enum.TryParse(rt, true, out FamilyInstanceReferenceType refType))
                return null;

            int idx = o.Value<int?>("index") ?? 0;
            if (idx < 0) idx = 0;

            try
            {
                var refs = fi.GetReferences(refType);
                if (refs == null || refs.Count == 0) return null;
                if (idx >= refs.Count) return null;
                return refs[idx];
            }
            catch { return null; }
        }

        private static bool TryGetViewTransformFromCrop(BoundingBoxXYZ crop, out Transform toWorld, out XYZ min, out XYZ max)
        {
            toWorld = null;
            min = null;
            max = null;
            try
            {
                if (crop == null) return false;
                if (crop.Transform == null) return false;

                var t = crop.Transform;
                var bx = NormalizeOrDefault(t.BasisX, XYZ.BasisX);
                var by = NormalizeOrDefault(t.BasisY, XYZ.BasisY);
                var bz = NormalizeOrDefault(t.BasisZ, XYZ.BasisZ);

                toWorld = Transform.Identity;
                toWorld.Origin = t.Origin;
                toWorld.BasisX = bx;
                toWorld.BasisY = by;
                toWorld.BasisZ = bz;

                min = crop.Min;
                max = crop.Max;
                return true;
            }
            catch
            {
                toWorld = null;
                min = null;
                max = null;
                return false;
            }
        }

        private static Tuple<Reference, Reference>? TryGetRefs(FamilyInstance fi, FamilyInstanceReferenceType a, FamilyInstanceReferenceType b)
        {
            var ra = TryGetFirstRef(fi, a);
            var rb = TryGetFirstRef(fi, b);
            if (ra == null || rb == null) return null;
            return Tuple.Create(ra, rb);
        }

        private static Reference? TryGetFirstRef(FamilyInstance fi, FamilyInstanceReferenceType t)
        {
            try
            {
                var refs = fi.GetReferences(t);
                if (refs != null && refs.Count > 0) return refs[0];
            }
            catch { }
            return null;
        }

        private static BoundingBoxXYZ? SafeGetBoundingBox(Element e, View view)
        {
            try
            {
                var bb = e.get_BoundingBox(view);
                if (bb != null) return bb;
            }
            catch { }
            try
            {
                return e.get_BoundingBox(null);
            }
            catch { }
            return null;
        }

        private static (bool ok, string? msg, double minX, double minY, double maxX, double maxY) ComputeBoundsInViewLocal(BoundingBoxXYZ bb, Transform toView)
        {
            try
            {
                var t = bb.Transform ?? Transform.Identity;
                var min = bb.Min;
                var max = bb.Max;

                var cornersLocal = new[]
                {
                    new XYZ(min.X, min.Y, min.Z),
                    new XYZ(min.X, min.Y, max.Z),
                    new XYZ(min.X, max.Y, min.Z),
                    new XYZ(min.X, max.Y, max.Z),
                    new XYZ(max.X, min.Y, min.Z),
                    new XYZ(max.X, min.Y, max.Z),
                    new XYZ(max.X, max.Y, min.Z),
                    new XYZ(max.X, max.Y, max.Z)
                };

                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

                foreach (var c in cornersLocal)
                {
                    var w = t.OfPoint(c);
                    var v = toView.OfPoint(w);
                    if (v.X < minX) minX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y > maxY) maxY = v.Y;
                }

                if (double.IsInfinity(minX) || double.IsInfinity(minY) || double.IsInfinity(maxX) || double.IsInfinity(maxY))
                    return (false, "BoundingBox のローカル変換で Infinity が発生しました。", 0, 0, 0, 0);

                return (true, null, minX, minY, maxX, maxY);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, 0, 0, 0, 0);
            }
        }

        private static Line? SafeCreateBoundLine(XYZ a, XYZ b)
        {
            try
            {
                if (a == null || b == null) return null;
                if (a.IsAlmostEqualTo(b)) return null;
                return Line.CreateBound(a, b);
            }
            catch { }
            return null;
        }
    }
}
