// ================================================================
// File: Commands/AnnotationOps/FilledRegionCommands.cs
// Filled Region (塗りつぶし領域) の取得/作成/タイプ変更/境界置き換え/削除コマンド
// 対応: .NET Framework 4.8 / C# 8 / Revit 2023 API
// ポイント:
//  - get_filled_region_types          : FilledRegionType 一覧
//  - get_filled_regions_in_view      : ビュー内の FilledRegion 一覧
//  - create_filled_region            : mm 座標のポリゴンから FilledRegion を作成
//  - set_filled_region_type          : FilledRegion のタイプ変更（パターン/色の切替）
//  - replace_filled_region_boundary  : 既存 FilledRegion の境界を新ポリゴンで置き換え
//  - delete_filled_region(s)         : FilledRegion の削除
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    internal static class FilledRegionHelpers
    {
        public static View ResolveView(UIDocument uidoc, JObject p)
        {
            if (uidoc == null)
                throw new InvalidOperationException("アクティブビューが見つかりません。");

            var doc = uidoc.Document;
            // viewId があれば優先、なければアクティブビュー
            View? v = null;

            var vid = p.Value<int?>("viewId") ?? 0;
            if (vid > 0)
                v = doc.GetElement(new ElementId(vid)) as View;
            if (v == null)
                v = uidoc?.ActiveView;

            if (v == null)
                throw new InvalidOperationException("アクティブビューまたは viewId に対応するビューが見つかりません。");
            if (v.IsTemplate)
                throw new InvalidOperationException("テンプレートビューでは FilledRegion を作成/編集できません。");

            return v;
        }

        public static IList<IList<Curve>> ReadLoopsMm(JArray loops)
        {
            var result = new List<IList<Curve>>();
            if (loops == null || loops.Count == 0)
                throw new InvalidOperationException("loops[] が必要です。");

            foreach (var loopTok in loops.OfType<JArray>())
            {
                var ptsFt = new List<XYZ>();
                foreach (var ptTok in loopTok.OfType<JObject>())
                {
                    var x = ptTok.Value<double>("x");
                    var y = ptTok.Value<double>("y");
                    var z = ptTok.Value<double?>("z") ?? 0.0;
                    ptsFt.Add(UnitHelper.MmToXyz(x, y, z));
                }
                if (ptsFt.Count < 3)
                    continue;

                var curves = new List<Curve>();
                for (int i = 0; i < ptsFt.Count; i++)
                {
                    var p0 = ptsFt[i];
                    var p1 = ptsFt[(i + 1) % ptsFt.Count];
                    if (p0.IsAlmostEqualTo(p1)) continue;
                    curves.Add(Line.CreateBound(p0, p1));
                }
                if (curves.Count >= 3)
                    result.Add(curves);
            }

            if (result.Count == 0)
                throw new InvalidOperationException("有効なループ (頂点数 3 以上) がありません。");

            return result;
        }

        /// <summary>
        /// FilledRegion.Create のバージョン差に耐える互換ラッパー。
        /// 試行順:
        ///  1) Create(Document, ElementId, View, IList&lt;CurveLoop&gt;)
        ///  2) Create(Document, ElementId, SketchPlane, IList&lt;CurveLoop&gt;)
        ///  3) Create(Document, ElementId, ElementId, IList&lt;CurveLoop&gt;)
        ///  4) Create(Document, ElementId, View, IList&lt;Curve&gt;)
        ///  5) Create(Document, ElementId, ElementId, IList&lt;Curve&gt;)
        ///  6) Create(Document, ElementId, View, CurveArray)
        ///  7) Create(Document, ElementId, ElementId, CurveArray)
        /// </summary>
        public static FilledRegion CreateFilledRegionCompat(
            Document doc,
            ElementId typeId,
            View view,
            IList<IList<Curve>> boundaries)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (boundaries == null || boundaries.Count == 0)
                throw new InvalidOperationException("少なくとも 1 つの境界ループが必要です。");

            var tFr = typeof(FilledRegion);

            // Loop 群 (CurveLoop)
            var loops = new List<CurveLoop>();
            foreach (var loopCurves in boundaries)
            {
                if (loopCurves == null) continue;
                var cl = new CurveLoop();
                foreach (var c in loopCurves)
                {
                    if (c == null) continue;
                    cl.Append(c);
                }
                if (cl.Any())
                    loops.Add(cl);
            }

            // 平坦な Curve 配列
            var flatCurves = boundaries
                .Where(l => l != null)
                .SelectMany(l => l)
                .Where(c => c != null)
                .ToList();

            // 1) (Document, ElementId, View, IList<CurveLoop>)
            try
            {
                var mViewLoops = tFr.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(View), typeof(IList<CurveLoop>) },
                    null);
                if (mViewLoops != null && loops.Count > 0)
                {
                    var fr = mViewLoops.Invoke(null, new object[] { doc, typeId, view, loops }) as FilledRegion;
                    if (fr != null) return fr;
                }
            }
            catch { /* try next */ }

            // 2) (Document, ElementId, SketchPlane, IList<CurveLoop>)
            try
            {
                var mPlaneLoops = tFr.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(SketchPlane), typeof(IList<CurveLoop>) },
                    null);
                if (mPlaneLoops != null && loops.Count > 0)
                {
                    SketchPlane sp = view.SketchPlane;
                    if (sp == null)
                    {
                        var plane = Plane.CreateByNormalAndOrigin(view.ViewDirection, view.Origin);
                        sp = SketchPlane.Create(doc, plane);
                    }
                    var fr = mPlaneLoops.Invoke(null, new object[] { doc, typeId, sp, loops }) as FilledRegion;
                    if (fr != null) return fr;
                }
            }
            catch { /* try next */ }

            // 3) (Document, ElementId, ElementId, IList<CurveLoop>)
            try
            {
                var mViewIdLoops = tFr.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(ElementId), typeof(IList<CurveLoop>) },
                    null);
                if (mViewIdLoops != null && loops.Count > 0)
                {
                    var fr = mViewIdLoops.Invoke(null, new object[] { doc, typeId, view.Id, loops }) as FilledRegion;
                    if (fr != null) return fr;
                }
            }
            catch { /* try next */ }

            // 4) (Document, ElementId, View, IList<Curve>)
            try
            {
                var mViewCurves = tFr.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(View), typeof(IList<Curve>) },
                    null);
                if (mViewCurves != null && flatCurves.Count > 0)
                {
                    var fr = mViewCurves.Invoke(null, new object[] { doc, typeId, view, flatCurves }) as FilledRegion;
                    if (fr != null) return fr;
                }
            }
            catch { /* try next */ }

            // 5) (Document, ElementId, ElementId, IList<Curve>)
            try
            {
                var mViewIdCurves = tFr.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(ElementId), typeof(IList<Curve>) },
                    null);
                if (mViewIdCurves != null && flatCurves.Count > 0)
                {
                    var fr = mViewIdCurves.Invoke(null, new object[] { doc, typeId, view.Id, flatCurves }) as FilledRegion;
                    if (fr != null) return fr;
                }
            }
            catch { /* try next */ }

            // 6) (Document, ElementId, View, CurveArray)
            try
            {
                var arr = new CurveArray();
                foreach (var c in flatCurves) arr.Append(c);

                var mViewArr = tFr.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(View), typeof(CurveArray) },
                    null);
                if (mViewArr != null && arr.Size > 0)
                {
                    var fr = mViewArr.Invoke(null, new object[] { doc, typeId, view, arr }) as FilledRegion;
                    if (fr != null) return fr;
                }
            }
            catch { /* try next */ }

            // 7) (Document, ElementId, ElementId, CurveArray)
            try
            {
                var arr = new CurveArray();
                foreach (var c in flatCurves) arr.Append(c);

                var mViewIdArr = tFr.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Document), typeof(ElementId), typeof(ElementId), typeof(CurveArray) },
                    null);
                if (mViewIdArr != null && arr.Size > 0)
                {
                    var fr = mViewIdArr.Invoke(null, new object[] { doc, typeId, view.Id, arr }) as FilledRegion;
                    if (fr != null) return fr;
                }
            }
            catch { /* try next */ }

            throw new InvalidOperationException("FilledRegion.Create の互換オーバーロードが見つかりません。");
        }
    }

    // ------------------------------------------------------------
    // FilledRegionType 一覧
    // ------------------------------------------------------------
    public class GetFilledRegionTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_filled_region_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType))
                    .Cast<FilledRegionType>()
                    .OrderBy(t => t.Name)
                    .ToList();

                var items = new List<object>(types.Count);
                foreach (var t in types)
                {
                    var fgId = t.ForegroundPatternId;
                    var bgId = t.BackgroundPatternId;
                    var fgColor = t.ForegroundPatternColor;
                    var bgColor = t.BackgroundPatternColor;

                    string? fgName = null;
                    string? bgName = null;

                    if (fgId != null && fgId != ElementId.InvalidElementId)
                        fgName = (doc.GetElement(fgId) as FillPatternElement)?.Name;
                    if (bgId != null && bgId != ElementId.InvalidElementId)
                        bgName = (doc.GetElement(bgId) as FillPatternElement)?.Name;

                    items.Add(new
                    {
                        typeId = t.Id.IntegerValue,
                        uniqueId = t.UniqueId,
                        name = t.Name,
                        isMasking = t.IsMasking,
                        foregroundPatternId = fgId?.IntegerValue,
                        foregroundPatternName = fgName,
                        foregroundColor = fgColor != null ? new { r = (int)fgColor.Red, g = (int)fgColor.Green, b = (int)fgColor.Blue } : null,
                        backgroundPatternId = bgId?.IntegerValue,
                        backgroundPatternName = bgName,
                        backgroundColor = bgColor != null ? new { r = (int)bgColor.Red, g = (int)bgColor.Green, b = (int)bgColor.Blue } : null
                    });
                }

                return new
                {
                    ok = true,
                    totalCount = items.Count,
                    items
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // ビュー内の FilledRegion 一覧
    // ------------------------------------------------------------
    public class GetFilledRegionsInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_filled_regions_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            try
            {
                var view = FilledRegionHelpers.ResolveView(uidoc!, p);
                var col = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FilledRegion))
                    .Cast<FilledRegion>()
                    .ToList();

                var items = col.Select(fr => new
                {
                    elementId = fr.Id.IntegerValue,
                    uniqueId = fr.UniqueId,
                    typeId = fr.GetTypeId().IntegerValue,
                    viewId = view.Id.IntegerValue,
                    isMasking = fr.IsMasking
                }).ToList();

                return new
                {
                    ok = true,
                    viewId = view.Id.IntegerValue,
                    totalCount = items.Count,
                    items
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // FilledRegion の作成
    // params:
    //   viewId  : int (必須)
    //   typeId  : int (任意, 省略時は既定タイプ)
    //   loops   : [ [ {x,y,z}, ... ], ... ]  (mm 単位, 1つ以上)
    // ------------------------------------------------------------
    public class CreateFilledRegionCommand : IRevitCommandHandler
    {
        public string CommandName => "create_filled_region";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            try
            {
                var view = FilledRegionHelpers.ResolveView(uidoc!, p);

                // typeId 解決
                int typeId = p.Value<int?>("typeId") ?? 0;
                ElementId typeEid;
                FilledRegionType? baseType = null;
                if (typeId > 0)
                {
                    typeEid = new ElementId(typeId);
                    baseType = doc.GetElement(typeEid) as FilledRegionType;
                    if (baseType == null)
                        throw new InvalidOperationException($"FilledRegionType が見つかりません: typeId={typeId}");
                }
                else
                {
                    typeEid = doc.GetDefaultElementTypeId(ElementTypeGroup.FilledRegionType);
                    baseType = doc.GetElement(typeEid) as FilledRegionType;
                    if (baseType == null || typeEid == ElementId.InvalidElementId)
                        throw new InvalidOperationException("既定の FilledRegionType が解決できません。typeId を指定してください。");
                }

                var loopsTok = p["loops"] as JArray;
                var boundaries = FilledRegionHelpers.ReadLoopsMm(loopsTok ?? new JArray());

                FilledRegion fr;

                using (var tx = new Transaction(doc, "Create Filled Region"))
                {
                    tx.Start();
                    // パターン指定がある場合はタイプを複製して適用
                    var fgPatIdOpt = p.Value<int?>("foregroundPatternId");
                    var fgPatName = p.Value<string>("foregroundPatternName");
                    var bgPatIdOpt = p.Value<int?>("backgroundPatternId");
                    var bgPatName = p.Value<string>("backgroundPatternName");

                    ElementId typeToUseId = typeEid;

                    bool hasFgOverride = (fgPatIdOpt.HasValue && fgPatIdOpt.Value > 0) || !string.IsNullOrWhiteSpace(fgPatName);
                    bool hasBgOverride = (bgPatIdOpt.HasValue && bgPatIdOpt.Value > 0) || !string.IsNullOrWhiteSpace(bgPatName);

                    if (hasFgOverride || hasBgOverride)
                    {
                        if (baseType == null)
                            throw new InvalidOperationException("パターンを指定する場合は有効な FilledRegionType が必要です。");

                        FillPatternElement? ResolvePattern(int? idOpt, string? nameOpt)
                        {
                            FillPatternElement? fp = null;
                            if (idOpt.HasValue && idOpt.Value > 0)
                            {
                                fp = doc.GetElement(new ElementId(idOpt.Value)) as FillPatternElement;
                            }
                            if (fp == null && !string.IsNullOrWhiteSpace(nameOpt))
                            {
                                fp = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FillPatternElement))
                                    .Cast<FillPatternElement>()
                                    .FirstOrDefault(x => string.Equals(x.Name, nameOpt, StringComparison.OrdinalIgnoreCase));
                            }
                            return fp;
                        }

                        var fgElem = hasFgOverride ? ResolvePattern(fgPatIdOpt, fgPatName) : null;
                        var bgElem = hasBgOverride ? ResolvePattern(bgPatIdOpt, bgPatName) : null;

                        if (hasFgOverride && fgElem == null)
                            throw new InvalidOperationException("指定された前景パターンが見つかりません。");
                        if (hasBgOverride && bgElem == null)
                            throw new InvalidOperationException("指定された背景パターンが見つかりません。");

                        // 既存タイプを複製し、パターンだけ差し替えた専用タイプを作成
                        var dupNameBase = baseType.Name + "_McpFill";
                        var dupName = dupNameBase;
                        int idx = 1;
                        while (new FilteredElementCollector(doc)
                               .OfClass(typeof(FilledRegionType))
                               .Cast<FilledRegionType>()
                               .Any(t => string.Equals(t.Name, dupName, StringComparison.OrdinalIgnoreCase)))
                        {
                            dupName = $"{dupNameBase}_{idx++}";
                        }

                        var dupType = baseType.Duplicate(dupName) as FilledRegionType;
                        if (dupType == null)
                            throw new InvalidOperationException("FilledRegionType の複製に失敗しました。");

                        if (fgElem != null)
                        {
                            dupType.ForegroundPatternId = fgElem.Id;
                        }
                        if (bgElem != null)
                        {
                            dupType.BackgroundPatternId = bgElem.Id;
                        }

                        typeToUseId = dupType.Id;
                    }

                    fr = FilledRegionHelpers.CreateFilledRegionCompat(doc, typeToUseId, view, boundaries);
                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    elementId = fr.Id.IntegerValue,
                    uniqueId = fr.UniqueId,
                    typeId = fr.GetTypeId().IntegerValue,
                    viewId = view.Id.IntegerValue
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // FilledRegion のタイプ変更（色/パターンはタイプ側で定義）
    // params:
    //   elementIds : [int,...]  (必須)
    //   typeId     : int        (必須)
    // ------------------------------------------------------------
    public class SetFilledRegionTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "set_filled_region_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();
            var idsTok = p["elementIds"] as JArray;
            if (idsTok == null || idsTok.Count == 0)
                return new { ok = false, msg = "elementIds[] が必要です。" };

            int typeId = p.Value<int?>("typeId") ?? 0;
            if (typeId <= 0)
                return new { ok = false, msg = "typeId が必要です。" };

            var typeEid = new ElementId(typeId);
            var typeElem = doc.GetElement(typeEid) as FilledRegionType;
            if (typeElem == null)
                return new { ok = false, msg = $"FilledRegionType が見つかりません: typeId={typeId}" };

            var ids = idsTok
                .Select(t => (t.Type == JTokenType.Integer) ? t.Value<int>() : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            int success = 0, failed = 0;
            var results = new List<object>();

            using (var tx = new Transaction(doc, "Set FilledRegion Type"))
            {
                tx.Start();

                foreach (var id in ids)
                {
                    try
                    {
                        var fr = doc.GetElement(new ElementId(id)) as FilledRegion;
                        if (fr == null)
                        {
                            failed++;
                            results.Add(new { elementId = id, ok = false, msg = "FilledRegion not found." });
                            continue;
                        }

                        fr.ChangeTypeId(typeEid);
                        success++;
                        results.Add(new { elementId = id, ok = true });
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        results.Add(new { elementId = id, ok = false, msg = ex.Message });
                    }
                }

                tx.Commit();
            }

            bool okOverall = success > 0;
            string msg = okOverall
                ? $"Updated {success} regions. {failed} regions failed."
                : $"No regions updated. {failed} regions failed.";

            return new
            {
                ok = okOverall,
                msg,
                stats = new { successCount = success, failureCount = failed },
                results
            };
        }
    }

    // ------------------------------------------------------------
    // FilledRegion 境界の置き換え（新しい loops から再作成）
    // params:
    //   elementId : int  (必須)
    //   loops     : [ [ {x,y,z}, ... ], ... ]  (mm, create と同じ)
    // 挙動:
    //   同じ ViewId / TypeId で新しい FilledRegion を作成し、元の要素を削除して差し替えます。
    // ------------------------------------------------------------
    public class ReplaceFilledRegionBoundaryCommand : IRevitCommandHandler
    {
        public string CommandName => "replace_filled_region_boundary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();
            int eid = p.Value<int?>("elementId") ?? 0;
            if (eid <= 0) return new { ok = false, msg = "elementId が必要です。" };

            var frOld = doc.GetElement(new ElementId(eid)) as FilledRegion;
            if (frOld == null) return new { ok = false, msg = "FilledRegion が見つかりません。" };

            var loopsTok = p["loops"] as JArray;
            if (loopsTok == null || loopsTok.Count == 0)
                return new { ok = false, msg = "loops[] が必要です。" };

            try
            {
                var view = doc.GetElement(frOld.OwnerViewId) as View;
                if (view == null)
                    return new { ok = false, msg = "FilledRegion のビューが解決できません。" };

                var boundaries = FilledRegionHelpers.ReadLoopsMm(loopsTok);
                FilledRegion frNew;

                using (var tx = new Transaction(doc, "Replace FilledRegion Boundary"))
                {
                    tx.Start();
                    var typeId = frOld.GetTypeId();
                    frNew = FilledRegionHelpers.CreateFilledRegionCompat(doc, typeId, view, boundaries);
                    doc.Delete(frOld.Id);
                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    oldElementId = eid,
                    newElementId = frNew.Id.IntegerValue,
                    newUniqueId = frNew.UniqueId,
                    viewId = view.Id.IntegerValue
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // FilledRegion 削除
    // params:
    //   elementIds : [int,...]
    // ------------------------------------------------------------
    public class DeleteFilledRegionCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_filled_region";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();
            var idsTok = p["elementIds"] as JArray;
            if (idsTok == null || idsTok.Count == 0)
                return new { ok = false, msg = "elementIds[] が必要です。" };

            var ids = idsTok
                .Select(t => (t.Type == JTokenType.Integer) ? t.Value<int>() : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return new { ok = false, msg = "有効な elementIds がありません。" };

            int deletedCount = 0;
            var deletedIds = new List<int>();

            using (var tx = new Transaction(doc, "Delete FilledRegion"))
            {
                tx.Start();
                foreach (var id in ids)
                {
                    try
                    {
                        var fr = doc.GetElement(new ElementId(id)) as FilledRegion;
                        if (fr == null) continue;
                        var deleted = doc.Delete(fr.Id);
                        if (deleted != null && deleted.Count > 0)
                        {
                            deletedCount++;
                            deletedIds.Add(id);
                        }
                    }
                    catch
                    {
                        // ignore per-element errors; report aggregate
                    }
                }
                tx.Commit();
            }

            return new
            {
                ok = true,
                msg = $"Deleted {deletedCount} filled regions.",
                deletedCount,
                requestedCount = ids.Count,
                deletedElementIds = deletedIds
            };
        }
    }
}
