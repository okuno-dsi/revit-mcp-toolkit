// ============================================================================
// File: RevitMCPAddin/Commands/Area/CombinedAreaCommands.cs
// Target : Revit 2023 / .NET Framework 4.8 / C# 8
// Policy : すべての単位変換・座標変換・パラメータ整形を RevitMCPAddin.Core.UnitHelper に統一
// Notes  : 匿名型の ?: で型が分岐しないよう、Dictionary<string,object> を使用（C#8安全）
// ============================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
// エイリアス
using ArchArea = Autodesk.Revit.DB.Area;

namespace RevitMCPAddin.Commands.Area
{
    // ------------------------------------------------------------------------
    // 共通ユーティリティ
    // ------------------------------------------------------------------------
    internal static class AreaCommon
    {
        public static ViewPlan ResolveAreaPlanView(Document doc, JObject p, out Level level)
        {
            level = null;
            View v = null;
            if (p.TryGetValue("viewId", out var vidTok))
                v = doc.GetElement(new ElementId(vidTok.Value<int>())) as View;
            if (v == null && p.TryGetValue("viewUniqueId", out var vuidTok))
                v = doc.GetElement(vuidTok.Value<string>()) as View;

            var vp = v as ViewPlan;
            if (vp == null || vp.ViewType != ViewType.AreaPlan)
                throw new InvalidOperationException("Area Plan ビュー(viewId/viewUniqueId)を指定してください。");

            level = vp.GenLevel ?? throw new InvalidOperationException("Area Plan のレベルが解決できません。");
            if (vp.SketchPlane == null)
                throw new InvalidOperationException("Area Plan の SketchPlane がありません。任意のスケッチ操作を一度実行してください。");

            return vp;
        }

        public static bool IsAreaBoundaryLine(Element e)
            => e is CurveElement ce && ce.Category != null
               && ce.Category.Id.IntegerValue == (int)BuiltInCategory.OST_AreaSchemeLines;

        public static IEnumerable<CurveElement> GetAreaBoundaryLinesInView(Document doc, View view)
            => new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(ce => ce.Category != null &&
                             ce.Category.Id.IntegerValue == (int)BuiltInCategory.OST_AreaSchemeLines);

        public static bool CurveEquals(Curve a, Curve b, double tolFt)
        {
            if (a == null || b == null) return false;
            var a0 = a.GetEndPoint(0); var a1 = a.GetEndPoint(1);
            var b0 = b.GetEndPoint(0); var b1 = b.GetEndPoint(1);
            bool samesense = a0.DistanceTo(b0) <= tolFt && a1.DistanceTo(b1) <= tolFt;
            bool reversesense = a0.DistanceTo(b1) <= tolFt && a1.DistanceTo(b0) <= tolFt;
            if (samesense || reversesense) return true;

            if (Math.Abs(a.ApproximateLength - b.ApproximateLength) > tolFt) return false;
            var mid = b.Evaluate(0.5, true);
            return a.Distance(mid) <= tolFt;
        }
    }

    internal static class AreaGeom
    {
        public static XYZ MmToXyz(JObject pt)
            => UnitHelper.MmToXyz(pt.Value<double>("x"), pt.Value<double>("y"), pt.Value<double>("z"));

        public static JObject XyzToMm(XYZ p, int digits = 3)
        {
            var (x, y, z) = UnitHelper.XyzToMm(p);
            return new JObject
            {
                ["x"] = Math.Round(x, digits),
                ["y"] = Math.Round(y, digits),
                ["z"] = Math.Round(z, digits)
            };
        }

        public static bool IntersectCurves(Curve a, Curve b, double tolFt, out XYZ ip)
        {
            ip = null;
            var res = a.Intersect(b, out IntersectionResultArray ira);
            if (res != SetComparisonResult.Overlap || ira == null || ira.Size == 0) return false;

            var p0 = ira.get_Item(0)?.XYZPoint;
            if (p0 == null) return false;

            var ap = a.Project(p0); var bp = b.Project(p0);
            if (ap == null || bp == null) return false;
            if (ap.Distance > tolFt || bp.Distance > tolFt) return false;

            ip = p0;
            return true;
        }
    }

    // ------------------------------------------------------------------------
    // A) Area 要素の CRUD / 情報取得
    // ------------------------------------------------------------------------
    public class CreateAreaCommand : IRevitCommandHandler
    {
        public string CommandName => "create_area";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            // Prefer an explicit AreaPlan view when provided (multi-area-scheme safe).
            // Backward compatible: if viewId/viewUniqueId is omitted, falls back to levelId resolution.
            ViewPlan fixedAreaPlan = null;
            try
            {
                JToken viewIdTok = null;
                JToken viewUidTok = null;
                p.TryGetValue("viewId", out viewIdTok);
                p.TryGetValue("viewUniqueId", out viewUidTok);
                if (viewIdTok != null || viewUidTok != null)
                {
                    View v = null;
                    if (viewIdTok != null) v = doc.GetElement(new ElementId(viewIdTok.Value<int>())) as View;
                    if (v == null && viewUidTok != null) v = doc.GetElement(viewUidTok.Value<string>()) as View;
                    fixedAreaPlan = v as ViewPlan;
                    if (fixedAreaPlan == null || fixedAreaPlan.ViewType != ViewType.AreaPlan)
                        return new { ok = false, msg = "viewId/viewUniqueId must reference an Area Plan view (ViewType.AreaPlan)." };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "Failed to resolve viewId/viewUniqueId: " + ex.Message };
            }

            // Batch mode: items[] with time-slice controls
            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var created = new List<object>();
                int processed = 0;
                int nextIndex = startIndex;
                var sw = Stopwatch.StartNew();

                using (var tx = new Transaction(doc, "Create Areas (batch)"))
                {
                    tx.Start();
                    TxnUtil.ConfigureProceedWithWarnings(tx);
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        // Resolve target AreaPlan view:
                        // 1) per-item viewId/viewUniqueId, else 2) parent viewId/viewUniqueId, else 3) levelId lookup.
                        ViewPlan viewPlanItem = null;

                        JToken itVidTok = null;
                        JToken itVuidTok = null;
                        it.TryGetValue("viewId", out itVidTok);
                        it.TryGetValue("viewUniqueId", out itVuidTok);
                        if (itVidTok != null || itVuidTok != null)
                        {
                            View v2 = null;
                            if (itVidTok != null) v2 = doc.GetElement(new ElementId(itVidTok.Value<int>())) as View;
                            if (v2 == null && itVuidTok != null) v2 = doc.GetElement(itVuidTok.Value<string>()) as View;
                            var vp2 = v2 as ViewPlan;
                            if (vp2 != null && vp2.ViewType == ViewType.AreaPlan) viewPlanItem = vp2;
                        }

                        if (viewPlanItem == null) viewPlanItem = fixedAreaPlan;

                        int levelIdItem = 0;
                        if (viewPlanItem != null)
                        {
                            levelIdItem = viewPlanItem.GenLevel?.Id.IntegerValue ?? 0;
                        }
                        else
                        {
                            // Per-item: allow levelId override, else fallback to parent
                            levelIdItem = it.Value<int?>("levelId") ?? p.Value<int?>("levelId") ?? 0;
                            if (levelIdItem <= 0) { processed++; nextIndex = i + 1; continue; }

                            var viewPlanByLevel = new FilteredElementCollector(doc)
                                .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                                .FirstOrDefault(vp => vp.ViewType == ViewType.AreaPlan && vp.GenLevel?.Id.IntegerValue == levelIdItem);
                            if (viewPlanByLevel == null) { processed++; nextIndex = i + 1; continue; }
                            viewPlanItem = viewPlanByLevel;
                        }

                        if (!InputPointReader.TryReadXYMm(it, out var xMmItem, out var yMmItem)) continue;

                        var uvItem = new UV(UnitHelper.MmToFt(xMmItem), UnitHelper.MmToFt(yMmItem));
                        var areaEl = doc.Create.NewArea(viewPlanItem, uvItem);

                        created.Add(new
                        {
                            areaId = areaEl.Id.IntegerValue,
                            levelId = levelIdItem,
                            viewId = viewPlanItem.Id.IntegerValue
                        });

                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }

                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= itemsArr.Count;
                return new { ok = true, countCreated = processed, created, completed, nextIndex = completed ? (int?)null : nextIndex };
            }

            ViewPlan viewPlan = fixedAreaPlan;
            int levelId = 0;
            if (viewPlan != null)
            {
                levelId = viewPlan.GenLevel?.Id.IntegerValue ?? 0;
                if (levelId <= 0) return new { ok = false, msg = "AreaPlan view has no GenLevel." };
            }
            else
            {
                levelId = p.Value<int?>("levelId") ?? 0;
                if (levelId <= 0) return new { ok = false, msg = "levelId (or viewId/viewUniqueId) is required." };

                var level = doc.GetElement(new ElementId(levelId)) as Level
                            ?? throw new InvalidOperationException($"Level not found: {levelId}");

                viewPlan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                    .FirstOrDefault(vp => vp.ViewType == ViewType.AreaPlan && vp.GenLevel?.Id.IntegerValue == levelId)
                    ?? throw new InvalidOperationException($"AreaPlan view not found for level {level.Name}");
            }

            // --- InputPointReaderで x,y(mm) を取得 ---
            if (!InputPointReader.TryReadXYMm(p, out var xMm, out var yMm))
                return new { ok = false, msg = "x, y (mm) are required. (x/y or location.{x,y} or point.{x,y} or [x,y])" };

            var uv = new UV(UnitHelper.MmToFt(xMm), UnitHelper.MmToFt(yMm));

            using (var tx = new Transaction(doc, "Create Area"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                var area = doc.Create.NewArea(viewPlan, uv);
                tx.Commit();
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                return new { ok = true, areaId = area.Id.IntegerValue, viewId = viewPlan.Id.IntegerValue, levelId, units = UnitHelper.DefaultUnitsMeta() };
            }
        }
    }

    public class DeleteAreaCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_area";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            var idsArr = p["areaIds"] as JArray;
            if (idsArr != null && idsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var sw = Stopwatch.StartNew();
                int processed = 0; int nextIndex = startIndex;
                using (var tx = new Transaction(doc, "Move Areas (batch)"))
                {
                    tx.Start();
                    for (int i = startIndex; i < idsArr.Count; i++)
                    {
                        var it = idsArr[i] as JObject ?? new JObject();
                        var areaEl = doc.GetElement(new ElementId(it.Value<int>("areaId"))) as ArchArea;
                        if (areaEl != null)
                        {
                            var dxFt = UnitHelper.MmToFt(it.Value<double>("dx"));
                            var dyFt = UnitHelper.MmToFt(it.Value<double>("dy"));
                            var dzFt = UnitHelper.MmToFt(it.Value<double>("dz"));
                            try { ElementTransformUtils.MoveElement(doc, areaEl.Id, new XYZ(dxFt, dyFt, dzFt)); } catch { }
                        }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= idsArr.Count;
                return new { ok = true, countDeleted = processed, completed, nextIndex = completed ? (int?)null : nextIndex };
            }

            int id = p.Value<int>("areaId");
            using (var tx = new Transaction(doc, "Delete Area"))
            {
                tx.Start();
                doc.Delete(new ElementId(id));
                tx.Commit();
            }
            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return new { ok = true };
        }
    }

    public class MoveAreaCommand : IRevitCommandHandler
    {
        public string CommandName => "move_area";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var sw = Stopwatch.StartNew();
                int processed = 0; int nextIndex = startIndex;
                using (var tx = new Transaction(doc, "Move Areas (batch)"))
                {
                    tx.Start();
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        var areaEl = doc.GetElement(new ElementId(it.Value<int>("areaId"))) as ArchArea;
                        if (areaEl != null)
                        {
                            var dxFt = UnitHelper.MmToFt(it.Value<double>("dx"));
                            var dyFt = UnitHelper.MmToFt(it.Value<double>("dy"));
                            var dzFt = UnitHelper.MmToFt(it.Value<double>("dz"));
                            try { ElementTransformUtils.MoveElement(doc, areaEl.Id, new XYZ(dxFt, dyFt, dzFt)); } catch { }
                        }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= itemsArr.Count;
                return new { ok = true, processed, completed, nextIndex = completed ? (int?)null : nextIndex };
            }

            var area = doc.GetElement(new ElementId(p.Value<int>("areaId"))) as ArchArea
                       ?? throw new InvalidOperationException("Area not found.");

            var dx = UnitHelper.MmToFt(p.Value<double>("dx"));
            var dy = UnitHelper.MmToFt(p.Value<double>("dy"));
            var dz = UnitHelper.MmToFt(p.Value<double>("dz"));

            using (var tx = new Transaction(doc, "Move Area"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, area.Id, new XYZ(dx, dy, dz));
                tx.Commit();
            }
            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class UpdateAreaCommand : IRevitCommandHandler
    {
        public string CommandName => "update_area";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            // Batch: items[] with { areaId, paramName, value }
            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var sw = Stopwatch.StartNew();
                int processed = 0; int nextIndex = startIndex;
                using (var tx = new Transaction(doc, "Update Area Params (batch)"))
                {
                    tx.Start();
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        try
                        {
                            var areaElItem = doc.GetElement(new ElementId(it.Value<int>("areaId"))) as ArchArea;
                            if (areaElItem == null) { processed++; nextIndex = i + 1; continue; }

                            string paramNameItem = it.Value<string>("paramName");
                            var prmItem = areaElItem.LookupParameter(paramNameItem);
                            if (prmItem == null || prmItem.IsReadOnly) { processed++; nextIndex = i + 1; continue; }

                            var tokenItem = it["value"];
                            switch (prmItem.StorageType)
                            {
                                case StorageType.String:
                                    {
                                        prmItem.Set(tokenItem?.Value<string>() ?? "");
                                        break;
                                    }
                                case StorageType.Integer:
                                    {
                                        if (UnitHelper.TryParseInt(tokenItem?.ToObject<object>(), out var iv))
                                            prmItem.Set(iv);
                                        break;
                                    }
                                case StorageType.ElementId:
                                    {
                                        if (UnitHelper.TryParseInt(tokenItem?.ToObject<object>(), out var iv2))
                                            prmItem.Set(new ElementId(iv2));
                                        break;
                                    }
                                case StorageType.Double:
                                    {
                                        var specItem = UnitHelper.GetSpec(prmItem);
                                        if (UnitHelper.TryParseDouble(tokenItem?.ToObject<object>(), out var dv))
                                            prmItem.Set(UnitHelper.ToInternal(dv, specItem));
                                        break;
                                    }
                            }
                        }
                        catch { }

                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= itemsArr.Count;
                return new { ok = true, processed, completed, nextIndex = completed ? (int?)null : nextIndex };
            }

            var area = doc.GetElement(new ElementId(p.Value<int>("areaId"))) as ArchArea
                       ?? throw new InvalidOperationException("Area not found.");

            string paramName = p.Value<string>("paramName");
            var prm = area.LookupParameter(paramName)
                      ?? throw new InvalidOperationException($"Parameter not found: {paramName}");
            if (prm.IsReadOnly)
                return new { ok = false, msg = $"パラメータ '{paramName}' は読み取り専用です" };

            var token = p["value"];

            using (var tx = new Transaction(doc, "Update Area Param"))
            {
                tx.Start();

                bool ok;
                switch (prm.StorageType)
                {
                    case StorageType.String:
                        ok = prm.Set(token?.Value<string>() ?? "");
                        break;

                    case StorageType.Integer:
                        {
                            if (!UnitHelper.TryParseInt(token?.ToObject<object>(), out var iv))
                                throw new InvalidOperationException("value を整数に変換できません。");
                            ok = prm.Set(iv);
                            break;
                        }

                    case StorageType.ElementId:
                        {
                            if (!UnitHelper.TryParseInt(token?.ToObject<object>(), out var iv))
                                throw new InvalidOperationException("value を ElementId (int) に変換できません。");
                            ok = prm.Set(new ElementId(iv));
                            break;
                        }

                    case StorageType.Double:
                        {
                            // 既定は SI 値を受け取り Spec に従って内部値へ
                            var spec = UnitHelper.GetSpec(prm);
                            if (!UnitHelper.TryParseDouble(token?.ToObject<object>(), out var dv))
                                throw new InvalidOperationException("value を数値に変換できません。");
                            var internalVal = UnitHelper.ToInternal(dv, spec);
                            ok = prm.Set(internalVal);
                            break;
                        }

                    default:
                        tx.RollBack();
                        return new { ok = false, msg = "未対応の StorageType です" };
                }

                tx.Commit();
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                return ok ? (object)new { ok = true } : new { ok = false, msg = "設定に失敗しました" };
            }
        }
    }

    public class GetAreasCommand : IRevitCommandHandler
    {
        public string CommandName => "get_areas";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)(cmd.Params ?? new JObject());
                var mode = UnitHelper.ResolveUnitsMode(doc, p);

                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? int.MaxValue;

                int? levelId = p.Value<int?>("levelId");
                string nameContains = (p.Value<string>("nameContains") ?? "").Trim();
                string numberContains = (p.Value<string>("numberContains") ?? "").Trim();

                double? areaMinM2 = null;
                if (p["areaMinM2"] != null && (p["areaMinM2"].Type == JTokenType.Float || p["areaMinM2"].Type == JTokenType.Integer))
                    areaMinM2 = p.Value<double>("areaMinM2");

                double? areaMaxM2 = null;
                if (p["areaMaxM2"] != null && (p["areaMaxM2"].Type == JTokenType.Float || p["areaMaxM2"].Type == JTokenType.Integer))
                    areaMaxM2 = p.Value<double>("areaMaxM2");

                bool includeParameters = p.Value<bool?>("includeParameters") ?? false;
                bool includeCentroid = p.Value<bool?>("includeCentroid") ?? false;

                string orderBy = (p.Value<string>("orderBy") ?? "id").ToLowerInvariant();
                bool desc = p.Value<bool?>("desc") ?? false;

                var all = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Areas)
                    .WhereElementIsNotElementType()
                    .Cast<ArchArea>()
                    .ToList();

                IEnumerable<ArchArea> q = all;

                if (levelId.HasValue) q = q.Where(a => a.LevelId.IntegerValue == levelId.Value);
                if (!string.IsNullOrEmpty(nameContains)) q = q.Where(a => (a.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrEmpty(numberContains)) q = q.Where(a => (a.Number ?? "").IndexOf(numberContains, StringComparison.OrdinalIgnoreCase) >= 0);

                Func<ArchArea, double> areaSi = a => Math.Round(UnitHelper.ToExternal(a.Area, SpecTypeId.Area) ?? 0.0, 6);

                if (areaMinM2.HasValue) q = q.Where(a => areaSi(a) >= areaMinM2.Value - 1e-9);
                if (areaMaxM2.HasValue) q = q.Where(a => areaSi(a) <= areaMaxM2.Value + 1e-9);

                Func<ArchArea, object> keySel =
                    orderBy switch
                    {
                        "name" => a => (object)(a.Name ?? ""),
                        "number" => a => (object)(a.Number ?? ""),
                        "area" => a => (object)areaSi(a),
                        "level" => a => (object)((doc.GetElement(a.LevelId) as Level)?.Name ?? ""),
                        _ => a => (object)a.Id.IntegerValue
                    };

                q = desc ? q.OrderByDescending(keySel).ThenBy(a => a.Id.IntegerValue)
                         : q.OrderBy(keySel).ThenBy(a => a.Id.IntegerValue);

                int totalCount = q.Count();

                if (skip == 0 && p.ContainsKey("count") && count == 0)
                    return new { ok = true, totalCount, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };

                var page = q.Skip(skip).Take(count).ToList();
                var list = new List<object>(page.Count);

                foreach (var a in page)
                {
                    string levelName = (doc.GetElement(a.LevelId) as Level)?.Name ?? "";
                    double areaM2 = Math.Round(areaSi(a), 3);

                    object areaInfo;
                    switch (mode)
                    {
                        case UnitsMode.Project:
                            areaInfo = new Dictionary<string, object> { ["value"] = UnitHelper.ToExternalProject(doc, a.Area, SpecTypeId.Area), ["unit"] = "project" };
                            break;
                        case UnitsMode.Raw:
                            areaInfo = new Dictionary<string, object> { ["value"] = Math.Round(a.Area, 6), ["unit"] = "raw" };
                            break;
                        case UnitsMode.Both:
                            areaInfo = new Dictionary<string, object>
                            {
                                ["areaSi"] = areaM2,
                                ["unitSi"] = "m2",
                                ["areaProject"] = UnitHelper.ToExternalProject(doc, a.Area, SpecTypeId.Area),
                                ["unitProject"] = "project"
                            };
                            break;
                        case UnitsMode.SI:
                        default:
                            areaInfo = new Dictionary<string, object> { ["value"] = areaM2, ["unit"] = "m2" };
                            break;
                    }

                    var item = new Dictionary<string, object>
                    {
                        ["id"] = a.Id.IntegerValue,
                        ["elementId"] = a.Id.IntegerValue,
                        ["uniqueId"] = a.UniqueId,
                        ["number"] = a.Number ?? "",
                        ["name"] = a.Name ?? "",
                        ["level"] = levelName,
                        ["area"] = areaM2,
                        ["areaInfo"] = areaInfo
                    };

                    if (includeCentroid)
                    {
                        try
                        {
                            var calc = new SpatialElementGeometryCalculator(doc);
                            var res = calc.CalculateSpatialElementGeometry(a);
                            var solid = res.GetGeometry();
                            var c = solid?.ComputeCentroid();
                            if (c != null)
                            {
                                var (x, y, z) = UnitHelper.XyzToMm(c);
                                item["centroid"] = new { x = Math.Round(x, 3), y = Math.Round(y, 3), z = Math.Round(z, 3), unit = "mm" };
                            }
                        }
                        catch { }
                    }

                    if (includeParameters)
                    {
                        var plist = a.Parameters.Cast<Parameter>()
                            .Select(pa => UnitHelper.MapParameter(pa, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3))
                            .ToList();
                        item["parameters"] = plist;
                    }

                    list.Add(item);
                }

                return new { ok = true, totalCount, areas = list, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
            }
            catch (Exception ex) { return new { ok = false, msg = ex.Message }; }
        }
    }

    public class GetAreaParamsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_params";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)cmd.Params ?? new JObject();

                if (!p.TryGetValue("areaId", out var idToken))
                    throw new InvalidOperationException("Parameter 'areaId' is required.");
                int areaId = idToken.Value<int>();

                var area = doc.GetElement(new ElementId(areaId)) as ArchArea
                           ?? throw new InvalidOperationException($"Area not found: {areaId}");

                var mode = UnitHelper.ResolveUnitsMode(doc, p);
                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? int.MaxValue;
                string orderBy = (p.Value<string>("orderBy") ?? "name").ToLowerInvariant();
                bool desc = p.Value<bool?>("desc") ?? false;
                string nameContains = (p.Value<string>("nameContains") ?? "").Trim();
                bool includeDisplay = p.Value<bool?>("includeDisplay") ?? true;
                bool includeRaw = p.Value<bool?>("includeRaw") ?? true;
                bool includeUnit = p.Value<bool?>("includeUnit") ?? true;

                var items = new List<object>();
                foreach (Parameter pa in area.Parameters)
                {
                    if (pa.StorageType == StorageType.None) continue;
                    string defName = pa.Definition?.Name ?? "";
                    if (nameContains.Length > 0 && defName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var mapped = UnitHelper.MapParameter(pa, doc, mode, includeDisplay, includeRaw, siDigits: 3, includeUnit: includeUnit);
                    if (mapped != null) items.Add(mapped);
                }

                Func<dynamic, IComparable> key =
                    orderBy switch
                    {
                        "id" => x => (IComparable)x.id,
                        "storagetype" => x => (IComparable)(x.storageType ?? ""),
                        "datatype" => x => (IComparable)(x.dataType ?? ""),
                        "readonly" => x => (IComparable)((x.isReadOnly ? 1 : 0)),
                        _ => x => (IComparable)(x.name ?? "")
                    };

                items = (desc ? items.OrderByDescending(key).ThenBy(x => ((dynamic)x).name)
                              : items.OrderBy(key).ThenBy(x => ((dynamic)x).name)).ToList();

                int totalCount = items.Count;
                if (skip == 0 && p.ContainsKey("count") && count == 0)
                    return new { ok = true, totalCount, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };

                var page = items.Skip(skip).Take(count).ToList();
                return new { ok = true, totalCount, parameters = page, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
            }
            catch (Exception ex) { return new { ok = false, msg = ex.Message }; }
        }
    }

    // ------------------------------------------------------------------------
    // B) Area 境界（AreaSchemeLines）編集/解析
    // ------------------------------------------------------------------------
    public class CreateAreaBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "create_area_boundary_line";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            Level level; var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);

            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var sw = Stopwatch.StartNew();
                var created = new List<object>();
                int processed = 0; int nextIndex = startIndex;
                using (var tx = new Transaction(doc, "Create Area Boundary Lines (batch)"))
                {
                    tx.Start();
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        try
                        {
                            ViewPlan vpi = vp;
                            if (it["viewId"] != null || it["viewUniqueId"] != null)
                            {
                                Level _; vpi = AreaCommon.ResolveAreaPlanView(doc, it, out _);
                            }
                            var s = it["start"] as JObject; var e = it["end"] as JObject;
                            if (s == null || e == null) { processed++; nextIndex = i + 1; continue; }
                            var p0 = AreaGeom.MmToXyz(s);
                            var p1 = AreaGeom.MmToXyz(e);
                            var line = Line.CreateBound(p0, p1);
                            var ce = doc.Create.NewAreaBoundaryLine(vpi.SketchPlane, line, vpi);
                            created.Add(new { elementId = ce.Id.IntegerValue, viewId = vpi.Id.IntegerValue });
                        }
                        catch { }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= itemsArr.Count;
                return new { ok = true, countCreated = processed, created, completed, nextIndex = completed ? (int?)null : nextIndex, units = UnitHelper.DefaultUnitsMeta() };
            }

            var p0s = AreaGeom.MmToXyz((JObject)p["start"]);
            var p1s = AreaGeom.MmToXyz((JObject)p["end"]);
            var line1 = Line.CreateBound(p0s, p1s);

            using (var tx = new Transaction(doc, "Create Area Boundary Line"))
            {
                tx.Start();
                var ce = doc.Create.NewAreaBoundaryLine(vp.SketchPlane, line1, vp);
                tx.Commit();
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                return new { ok = true, elementId = ce.Id.IntegerValue, units = UnitHelper.DefaultUnitsMeta() };
            }
        }
    }

    public class DeleteAreaBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_area_boundary_line";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            var idsArr = (p["elementIds"] as JArray) ?? (p["items"] as JArray);
            if (idsArr != null && idsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var sw = Stopwatch.StartNew();
                int processed = 0; int nextIndex = startIndex; int deleted = 0;
                using (var tx = new Transaction(doc, "Delete Area Boundary Line(s) (batch)"))
                {
                    tx.Start();
                    for (int i = startIndex; i < idsArr.Count; i++)
                    {
                        int id = idsArr[i].Type == JTokenType.Object ? ((JObject)idsArr[i]).Value<int>("elementId") : idsArr[i].Value<int>();
                        var e = doc.GetElement(new ElementId(id));
                        if (e != null && AreaCommon.IsAreaBoundaryLine(e)) { try { doc.Delete(e.Id); deleted++; } catch { } }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= idsArr.Count;
                return new { ok = true, requested = idsArr.Count, deleted, processed, completed, nextIndex = completed ? (int?)null : nextIndex, units = UnitHelper.DefaultUnitsMeta() };
            }

            var ids = (p["elementIds"] as JArray)?.Values<int>().ToList() ?? new List<int> { p.Value<int>("elementId") };
            int deletedSingle = 0;
            using (var tx = new Transaction(doc, "Delete Area Boundary Line(s)"))
            {
                tx.Start();
                foreach (var id in ids)
                {
                    var e = doc.GetElement(new ElementId(id));
                    if (e != null && AreaCommon.IsAreaBoundaryLine(e))
                    {
                        doc.Delete(e.Id);
                        deletedSingle++;
                    }
                }
                tx.Commit();
            }
            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return new { ok = true, requested = ids.Count, deleted = deletedSingle, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class MoveAreaBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "move_area_boundary_line";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var sw = Stopwatch.StartNew();
                int processed = 0; int nextIndex = startIndex;
                using (var tx = new Transaction(doc, "Move Area Boundary Lines (batch)"))
                {
                    tx.Start();
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        try
                        {
                            int id = it.Value<int>("elementId");
                            double dx = UnitHelper.MmToFt(it.Value<double?>("dx") ?? 0);
                            double dy = UnitHelper.MmToFt(it.Value<double?>("dy") ?? 0);
                            double dz = UnitHelper.MmToFt(it.Value<double?>("dz") ?? 0);
                            ElementTransformUtils.MoveElement(doc, new ElementId(id), new XYZ(dx, dy, dz));
                        }
                        catch { }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= itemsArr.Count;
                return new { ok = true, processed, completed, nextIndex = completed ? (int?)null : nextIndex, units = UnitHelper.DefaultUnitsMeta() };
            }

            int idS = p.Value<int>("elementId");
            double dxS = UnitHelper.MmToFt(p.Value<double?>("dx") ?? 0);
            double dyS = UnitHelper.MmToFt(p.Value<double?>("dy") ?? 0);
            double dzS = UnitHelper.MmToFt(p.Value<double?>("dz") ?? 0);

            using (var tx = new Transaction(doc, "Move Area Boundary Line"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, new ElementId(idS), new XYZ(dxS, dyS, dzS));
                tx.Commit();
            }
            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return new { ok = true, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class TrimAreaBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "trim_area_boundary_line";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            Level level; AreaCommon.ResolveAreaPlanView(doc, p, out level);

            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 25);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var sw = Stopwatch.StartNew();
                int processed = 0; int nextIndex = startIndex;
                var results = new List<object>();
                using (var tx = new Transaction(doc, "Trim Area Boundary Lines (batch)"))
                {
                    tx.Start();
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        try
                        {
                            var ce1 = doc.GetElement(new ElementId(it.Value<int>("lineId"))) as CurveElement;
                            var ce2 = doc.GetElement(new ElementId(it.Value<int>("targetLineId"))) as CurveElement;
                            if (ce1 == null || ce2 == null || !AreaCommon.IsAreaBoundaryLine(ce1) || !AreaCommon.IsAreaBoundaryLine(ce2))
                            { results.Add(new { ok = false, msg = "boundary line(s) not found." }); processed++; nextIndex = i + 1; continue; }
                            var lc1 = ce1.Location as LocationCurve; var c1 = lc1?.Curve;
                            var lc2 = ce2.Location as LocationCurve; var c2 = lc2?.Curve;
                            if (c1 == null || c2 == null) { results.Add(new { ok = false, msg = "LocationCurve is not available." }); processed++; nextIndex = i + 1; continue; }
                            double tolFt = UnitHelper.MmToFt(it.Value<double?>("toleranceMm") ?? p.Value<double?>("toleranceMm") ?? 3.0);
                            if (!AreaGeom.IntersectCurves(c1, c2, tolFt, out var ip))
                            { results.Add(new { ok = false, msg = "No intersection at given tolerance." }); processed++; nextIndex = i + 1; continue; }
                            var a0 = c1.GetEndPoint(0); var a1 = c1.GetEndPoint(1);
                            bool cutStart = (a0.DistanceTo(ip) <= a1.DistanceTo(ip));
                            var newLine = cutStart ? Line.CreateBound(ip, a1) : Line.CreateBound(a0, ip);
                            lc1.Curve = newLine;
                            results.Add(new { ok = true, lineId = ce1.Id.IntegerValue, newStart = AreaGeom.XyzToMm(newLine.GetEndPoint(0)), newEnd = AreaGeom.XyzToMm(newLine.GetEndPoint(1)) });
                        }
                        catch (Exception ex) { results.Add(new { ok = false, msg = ex.Message }); }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= itemsArr.Count;
                return new { ok = true, processed, results, completed, nextIndex = completed ? (int?)null : nextIndex, units = UnitHelper.DefaultUnitsMeta() };
            }

            var ce1s = doc.GetElement(new ElementId(p.Value<int>("lineId"))) as CurveElement;
            var ce2s = doc.GetElement(new ElementId(p.Value<int>("targetLineId"))) as CurveElement;
            if (ce1s == null || ce2s == null || !AreaCommon.IsAreaBoundaryLine(ce1s) || !AreaCommon.IsAreaBoundaryLine(ce2s))
                return new { ok = false, msg = "boundary line(s) not found." };

            var lc1s = ce1s.Location as LocationCurve; var c1s = lc1s?.Curve;
            var lc2s = ce2s.Location as LocationCurve; var c2s = lc2s?.Curve;
            if (c1s == null || c2s == null) return new { ok = false, msg = "LocationCurve is not available." };

            double tolFts = UnitHelper.MmToFt(p.Value<double?>("toleranceMm") ?? 3.0);
            if (!AreaGeom.IntersectCurves(c1s, c2s, tolFts, out var ips))
                return new { ok = false, msg = "No intersection at given tolerance." };

            var a0s = c1s.GetEndPoint(0); var a1s = c1s.GetEndPoint(1);
            bool cutStarts = (a0s.DistanceTo(ips) <= a1s.DistanceTo(ips));
            var newLine1 = cutStarts ? Line.CreateBound(ips, a1s) : Line.CreateBound(a0s, ips);

            using (var tx = new Transaction(doc, "Trim Area Boundary Line"))
            {
                tx.Start();
                lc1s.Curve = newLine1;
                tx.Commit();
            }

            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return new
            {
                ok = true,
                lineId = ce1s.Id.IntegerValue,
                newStart = AreaGeom.XyzToMm(newLine1.GetEndPoint(0)),
                newEnd = AreaGeom.XyzToMm(newLine1.GetEndPoint(1)),
                units = UnitHelper.DefaultUnitsMeta()
            };
        }
    }

    public class ExtendAreaBoundaryLineCommand : IRevitCommandHandler
    {
        public string CommandName => "extend_area_boundary_line";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            Level level; AreaCommon.ResolveAreaPlanView(doc, p, out level);

            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 25);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 100);
                var sw = Stopwatch.StartNew();
                int processed = 0; int nextIndex = startIndex;
                var results = new List<object>();
                using (var tx = new Transaction(doc, "Extend Area Boundary Lines (batch)"))
                {
                    tx.Start();
                    for (int i = startIndex; i < itemsArr.Count; i++)
                    {
                        var it = itemsArr[i] as JObject ?? new JObject();
                        try
                        {
                            var ce1 = doc.GetElement(new ElementId(it.Value<int>("lineId"))) as CurveElement;
                            var ce2 = doc.GetElement(new ElementId(it.Value<int>("targetLineId"))) as CurveElement;
                            if (ce1 == null || ce2 == null || !AreaCommon.IsAreaBoundaryLine(ce1) || !AreaCommon.IsAreaBoundaryLine(ce2))
                            { results.Add(new { ok = false, msg = "boundary line(s) not found." }); processed++; nextIndex = i + 1; continue; }
                            var lc1 = ce1.Location as LocationCurve; var c1 = lc1?.Curve;
                            var lc2 = ce2.Location as LocationCurve; var c2 = lc2?.Curve;
                            if (c1 == null || c2 == null) { results.Add(new { ok = false, msg = "LocationCurve is not available." }); processed++; nextIndex = i + 1; continue; }
                            double tolFt = UnitHelper.MmToFt(it.Value<double?>("toleranceMm") ?? p.Value<double?>("toleranceMm") ?? 3.0);
                            double maxExtendFt = UnitHelper.MmToFt(it.Value<double?>("maxExtendMm") ?? p.Value<double?>("maxExtendMm") ?? 5000.0);
                            var s1 = c1.GetEndPoint(0); var e1 = c1.GetEndPoint(1);
                            var dir = (e1 - s1).Normalize();
                            var extS = s1 - dir * maxExtendFt;
                            var extE = e1 + dir * maxExtendFt;
                            var extLine = Line.CreateBound(extS, extE);
                            if (!AreaGeom.IntersectCurves(extLine, c2, tolFt, out var ip))
                            { results.Add(new { ok = false, msg = "No intersection within maxExtend range." }); processed++; nextIndex = i + 1; continue; }
                            bool extendStart = (s1.DistanceTo(ip) > e1.DistanceTo(ip));
                            var newLine = extendStart ? Line.CreateBound(ip, e1) : Line.CreateBound(s1, ip);
                            lc1.Curve = newLine;
                            results.Add(new { ok = true, lineId = ce1.Id.IntegerValue, newStart = AreaGeom.XyzToMm(newLine.GetEndPoint(0)), newEnd = AreaGeom.XyzToMm(newLine.GetEndPoint(1)) });
                        }
                        catch (Exception ex) { results.Add(new { ok = false, msg = ex.Message }); }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = nextIndex >= itemsArr.Count;
                return new { ok = true, processed, results, completed, nextIndex = completed ? (int?)null : nextIndex, units = UnitHelper.DefaultUnitsMeta() };
            }

            var ce1s = doc.GetElement(new ElementId(p.Value<int>("lineId"))) as CurveElement;
            var ce2s = doc.GetElement(new ElementId(p.Value<int>("targetLineId"))) as CurveElement;
            if (ce1s == null || ce2s == null || !AreaCommon.IsAreaBoundaryLine(ce1s) || !AreaCommon.IsAreaBoundaryLine(ce2s))
                return new { ok = false, msg = "boundary line(s) not found." };

            var lc1s = ce1s.Location as LocationCurve; var c1s = lc1s?.Curve;
            var lc2s = ce2s.Location as LocationCurve; var c2s = lc2s?.Curve;
            if (c1s == null || c2s == null) return new { ok = false, msg = "LocationCurve is not available." };

            double tolFts = UnitHelper.MmToFt(p.Value<double?>("toleranceMm") ?? 3.0);
            double maxExtendFts = UnitHelper.MmToFt(p.Value<double?>("maxExtendMm") ?? 5000.0);
            var s1s = c1s.GetEndPoint(0); var e1s = c1s.GetEndPoint(1);
            var dirs = (e1s - s1s).Normalize();
            var extSs = s1s - dirs * maxExtendFts;
            var extEs = e1s + dirs * maxExtendFts;
            var extLine1 = Line.CreateBound(extSs, extEs);
            if (!AreaGeom.IntersectCurves(extLine1, c2s, tolFts, out var ips))
                return new { ok = false, msg = "No intersection within maxExtend range." };
            bool extendStarts = (s1s.DistanceTo(ips) > e1s.DistanceTo(ips));
            var newLine2 = extendStarts ? Line.CreateBound(ips, e1s) : Line.CreateBound(s1s, ips);

            using (var tx = new Transaction(doc, "Extend Area Boundary Line"))
            {
                tx.Start();
                lc1s.Curve = newLine2;
                tx.Commit();
            }

            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return new
            {
                ok = true,
                lineId = ce1s.Id.IntegerValue,
                newStart = AreaGeom.XyzToMm(newLine2.GetEndPoint(0)),
                newEnd = AreaGeom.XyzToMm(newLine2.GetEndPoint(1)),
                units = UnitHelper.DefaultUnitsMeta()
            };
        }
    }

    public class CleanAreaBoundariesCommand : IRevitCommandHandler
    {
        public string CommandName => "clean_area_boundaries";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            Level level; var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);
            double extendTolFt = UnitHelper.MmToFt(p.Value<double?>("extendToleranceMm") ?? 50.0);
            double mergeTolFt = UnitHelper.MmToFt(p.Value<double?>("mergeToleranceMm") ?? 5.0);
            bool deleteIsolated = p.Value<bool?>("deleteIsolated") ?? true;
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            // Optional staged, time-sliced execution
            string stageParam = (p.Value<string>("stage") ?? "all").Trim().ToLowerInvariant();
            // normalize: all->merge as entry
            string currentStage = (stageParam == "all" || string.IsNullOrEmpty(stageParam)) ? "merge" : stageParam;
            int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
            int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? int.MaxValue);
            int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 0);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var lines = AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList();

            int adjusted = 0, merged = 0, deleted = 0;
            int processed = 0; // counts outer-loop iterations in the current stage
            int nextIndex = startIndex;

            using (var tx = new Transaction(doc, "Clean Area Boundary Lines"))
            {
                tx.Start();

                // A. 重複統合（ステージ: merge）
                if (currentStage == "merge")
                {
                    for (int i = startIndex; i < lines.Count; i++)
                    {
                        var ci = lines[i];
                        if (ci == null) { processed++; nextIndex = i + 1; if (processed >= batchSize) break; if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break; continue; }
                        var lci = ci.Location as LocationCurve; var c1 = lci?.Curve;
                        if (c1 != null)
                        {
                            for (int j = i + 1; j < lines.Count; j++)
                            {
                                var cj = lines[j];
                                if (cj == null) continue;
                                var lcj = cj.Location as LocationCurve; var c2 = lcj?.Curve;
                                if (c2 == null) continue;
                                if (AreaCommon.CurveEquals(c1, c2, mergeTolFt))
                                {
                                    doc.Delete(cj.Id);
                                    lines[j] = null;
                                    merged++;
                                }
                            }
                        }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                }

                // B. 端点スナップ（ステージ: snap）
                if (currentStage == "snap")
                {
                    double shortTolFt = 1e-6;
                    try { shortTolFt = doc?.Application?.ShortCurveTolerance ?? shortTolFt; } catch { }

                    var valid = lines.Where(x => x != null).ToList();
                    for (int i = startIndex; i < valid.Count; i++)
                    {
                        var ci = valid[i];
                        var lci = ci?.Location as LocationCurve; var c1 = lci?.Curve;
                        if (c1 != null)
                        {
                            var ends = new[] { c1.GetEndPoint(0), c1.GetEndPoint(1) };
                            for (int ei = 0; ei < 2; ei++)
                            {
                                var ep = ends[ei];
                                XYZ bestIp = null; double bestDist = double.MaxValue;
                                for (int j = 0; j < valid.Count; j++)
                                {
                                    if (i == j) continue;
                                    var cj = valid[j];
                                    var lcj = cj.Location as LocationCurve; var c2 = lcj?.Curve;
                                    if (c2 == null) continue;
                                    var dir = (ends[1] - ends[0]).Normalize();
                                    var ext = Line.CreateBound(ep - dir * extendTolFt, ep + dir * extendTolFt);
                                    if (AreaGeom.IntersectCurves(ext, c2, mergeTolFt, out var ip))
                                    {
                                        var d = ep.DistanceTo(ip);
                                        if (d < bestDist) { bestDist = d; bestIp = ip; }
                                    }
                                }
                                if (bestIp != null && bestDist <= extendTolFt)
                                {
                                    var other = (ei == 0) ? ends[1] : ends[0];
                                    if (bestIp.DistanceTo(other) <= shortTolFt) continue;
                                    var newLine = (ei == 0) ? Line.CreateBound(bestIp, other) : Line.CreateBound(other, bestIp);
                                    lci.Curve = newLine;
                                    adjusted++;
                                }
                            }
                        }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                }

                // C. 孤立線の削除（ステージ: delete）
                if (currentStage == "delete" && deleteIsolated)
                {
                    var valid = lines.Where(x => x != null).ToList();
                    for (int i = startIndex; i < valid.Count; i++)
                    {
                        var ci = valid[i]; var lci = ci?.Location as LocationCurve; var c1 = lci?.Curve;
                        if (c1 != null)
                        {
                            bool touchesAny = false;
                            for (int j = 0; j < valid.Count && !touchesAny; j++)
                            {
                                if (i == j) continue;
                                var cj = valid[j]; var lcj = cj.Location as LocationCurve; var c2 = lcj?.Curve; if (c2 == null) continue;
                                if (AreaGeom.IntersectCurves(c1, c2, mergeTolFt, out _)) touchesAny = true;
                            }
                            if (!touchesAny)
                            {
                                doc.Delete(ci.Id);
                                deleted++;
                            }
                        }
                        processed++; nextIndex = i + 1;
                        if (processed >= batchSize) break;
                        if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                    }
                }

                tx.Commit();
            }

            if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }

            // Determine next stage when 'all' flow is desired
            string nextStage = null;
            bool completed = false;
            if (stageParam == "all")
            {
                if (currentStage == "merge") nextStage = (nextIndex >= lines.Count) ? "snap" : "merge";
                else if (currentStage == "snap") { var vcount = lines.Count(x => x != null); nextStage = (nextIndex >= vcount) ? (deleteIsolated ? "delete" : null) : "snap"; }
                else if (currentStage == "delete") nextStage = null;
                completed = nextStage == null;
            }
            else
            {
                // Single-stage mode
                int total = (currentStage == "merge") ? lines.Count : lines.Count(x => x != null);
                completed = nextIndex >= total;
                nextStage = completed ? null : currentStage;
            }

            return new
            {
                ok = true,
                viewId = vp.Id.IntegerValue,
                levelId = level.Id.IntegerValue,
                adjusted,
                merged,
                deleted,
                processed,
                completed,
                nextStage,
                nextIndex = completed ? (int?)null : nextIndex,
                units = UnitHelper.DefaultUnitsMeta()
            };
        }
    }

    public class GetAreaBoundaryLinesInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_boundary_lines_in_view";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();
            Level level; var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);

            var list = new List<object>();
            foreach (var ce in AreaCommon.GetAreaBoundaryLinesInView(doc, vp))
            {
                var lc = ce.Location as LocationCurve;
                var c = lc?.Curve; if (c == null) continue;

                list.Add(new
                {
                    elementId = ce.Id.IntegerValue,
                    kind = c.GetType().Name,
                    start = AreaGeom.XyzToMm(c.GetEndPoint(0)),
                    end = AreaGeom.XyzToMm(c.GetEndPoint(1)),
                    lengthMm = Math.Round(UnitHelper.FtToMm(c.ApproximateLength), 3)
                });
            }

            return new { ok = true, viewId = vp.Id.IntegerValue, levelId = level.Id.IntegerValue, total = list.Count, lines = list, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class GetAreaBoundaryIntersectionsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_boundary_intersections";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();
            Level level; var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);
            double tolFt = UnitHelper.MmToFt(p.Value<double?>("toleranceMm") ?? 2.0);

            var lines = AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList();
            var res = new List<object>();

            for (int i = 0; i < lines.Count; i++)
            {
                var li = lines[i]; var lci = (li.Location as LocationCurve); var ci = lci?.Curve; if (ci == null) continue;

                for (int j = i + 1; j < lines.Count; j++)
                {
                    var lj = lines[j]; var lcj = (lj.Location as LocationCurve); var cj = lcj?.Curve; if (cj == null) continue;

                    var comp = ci.Intersect(cj, out IntersectionResultArray ira);
                    if (comp != SetComparisonResult.Overlap || ira == null || ira.Size == 0) continue;

                    var pnt = ira.get_Item(0)?.XYZPoint; if (pnt == null) continue;

                    var pi = ci.Project(pnt); var pj = cj.Project(pnt);
                    if (pi == null || pj == null || pi.Distance > tolFt || pj.Distance > tolFt) continue;

                    res.Add(new { a = li.Id.IntegerValue, b = lj.Id.IntegerValue, point = AreaGeom.XyzToMm(pnt) });
                }
            }

            return new { ok = true, viewId = vp.Id.IntegerValue, levelId = level.Id.IntegerValue, count = res.Count, intersections = res, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class GetAreaBoundaryGapsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_boundary_gaps";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();
            Level level; var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);
            double tolFt = UnitHelper.MmToFt(p.Value<double?>("toleranceMm") ?? 50.0);

            var lines = AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList();
            var gaps = new List<object>();

            for (int i = 0; i < lines.Count; i++)
            {
                var li = lines[i]; var lci = (li.Location as LocationCurve); var ci = lci?.Curve; if (ci == null) continue;
                var ends = new[] { ci.GetEndPoint(0), ci.GetEndPoint(1) };

                foreach (var ep in ends)
                {
                    double best = double.MaxValue; int hitId = 0;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (i == j) continue;
                        var lj = lines[j]; var lcj = (lj.Location as LocationCurve); var cj = lcj?.Curve; if (cj == null) continue;

                        var proj = cj.Project(ep);
                        if (proj == null) continue;
                        if (proj.Distance < best) { best = proj.Distance; hitId = lj.Id.IntegerValue; }
                    }

                    if (best <= tolFt && best > 1e-9)
                        gaps.Add(new { lineId = li.Id.IntegerValue, nearTo = hitId, gapMm = Math.Round(UnitHelper.FtToMm(best), 3), endpoint = AreaGeom.XyzToMm(ep) });
                }
            }

            return new { ok = true, viewId = vp.Id.IntegerValue, levelId = level.Id.IntegerValue, count = gaps.Count, gaps, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class GetAreaBoundaryDuplicatesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_boundary_duplicates";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();
            Level level; var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);
            double tolFt = UnitHelper.MmToFt(p.Value<double?>("mergeToleranceMm") ?? 5.0);

            var lines = AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList();
            var dups = new List<object>();

            for (int i = 0; i < lines.Count; i++)
            {
                var li = lines[i]; var lci = (li.Location as LocationCurve); var ci = lci?.Curve; if (ci == null) continue;

                for (int j = i + 1; j < lines.Count; j++)
                {
                    var lj = lines[j]; var lcj = (lj.Location as LocationCurve); var cj = lcj?.Curve; if (cj == null) continue;

                    if (AreaCommon.CurveEquals(ci, cj, tolFt))
                        dups.Add(new { a = li.Id.IntegerValue, b = lj.Id.IntegerValue });
                }
            }

            return new { ok = true, viewId = vp.Id.IntegerValue, levelId = level.Id.IntegerValue, count = dups.Count, duplicates = dups, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    // ------------------------------------------------------------------------
    // C) Area メトリクス / ジオメトリ / 壁抽出 / 重心
    // ------------------------------------------------------------------------
    public class GetAreaBoundaryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_boundary";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int areaId = p.Value<int?>("areaId") ?? p.Value<int?>("elementId") ?? 0;
            if (areaId <= 0) throw new InvalidOperationException("Parameter 'areaId' is required.");

            var area = doc.GetElement(new ElementId(areaId)) as ArchArea
                       ?? throw new InvalidOperationException("Area not found.");

            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var opts = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };
            var loops = area.GetBoundarySegments(opts);
            if (loops == null || loops.Count == 0)
                return new { ok = true, totalLoops = 0, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), loops = Array.Empty<object>(), units = UnitHelper.DefaultUnitsMeta() };

            var result = new List<object>();
            int idx = 0;
            foreach (var loop in loops)
            {
                var pts = new List<object>();
                var segs = new List<object>();
                foreach (var bs in loop)
                {
                    var c = bs.GetCurve();
                    if (c == null) continue;
                    var p0 = c.GetEndPoint(0);
                    var p1 = c.GetEndPoint(1);
                    pts.Add(AreaGeom.XyzToMm(p0));
                    segs.Add(new { start = AreaGeom.XyzToMm(p0), end = AreaGeom.XyzToMm(p1) });
                }

                result.Add(new { loopIndex = idx++, points = pts, segments = segs });
            }

            return new { ok = true, totalLoops = result.Count, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), loops = result, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class GetAreaBoundaryWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_boundary_walls";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int areaId = p.Value<int?>("areaId") ?? p.Value<int?>("elementId") ?? 0;
            if (areaId <= 0) throw new InvalidOperationException("Parameter 'areaId' is required.");

            var area = doc.GetElement(new ElementId(areaId)) as ArchArea
                       ?? throw new InvalidOperationException("Area not found.");

            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var opts = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };
            var loops = area.GetBoundarySegments(opts);
            if (loops == null || loops.Count == 0)
                return new { ok = true, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), wallIds = new int[0], units = UnitHelper.DefaultUnitsMeta() };
            var wallIds = loops.SelectMany(l => l)
                .Select(bs => bs.ElementId)
                .Where(id => doc.GetElement(id) is Wall)
                .Distinct()
                .Select(id => id.IntegerValue)
                .ToList();

            return new { ok = true, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), wallIds, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class GetAreaCentroidCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_centroid";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int areaId = p.Value<int?>("areaId") ?? p.Value<int?>("elementId") ?? 0;
            if (areaId <= 0) throw new InvalidOperationException("Parameter 'areaId' is required.");

            var area = doc.GetElement(new ElementId(areaId)) as ArchArea
                       ?? throw new InvalidOperationException("Area not found.");

            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var opts = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };
            var loops = area.GetBoundarySegments(opts);
            if (loops == null || loops.Count == 0) return new { ok = false, msg = "Boundary not available." };

            double sumA = 0.0, sumCx = 0.0, sumCy = 0.0;
            foreach (var loop in loops)
            {
                var pts = new List<XYZ>();
                foreach (var seg in loop)
                {
                    var c = seg.GetCurve();
                    var tess = (c is Line) ? new List<XYZ> { c.GetEndPoint(0), c.GetEndPoint(1) } : c.Tessellate().ToList();
                    foreach (var w in tess)
                    {
                        var (x, y, _) = UnitHelper.XyzToMm(w);
                        pts.Add(new XYZ(x, y, 0));
                    }
                }
                if (pts.Count < 3) continue;
                if (!pts[0].IsAlmostEqualTo(pts[pts.Count - 1])) pts.Add(pts[0]);

                double A = 0.0, Cx = 0.0, Cy = 0.0;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    double x0 = pts[i].X, y0 = pts[i].Y;
                    double x1 = pts[i + 1].X, y1 = pts[i + 1].Y;
                    double cross = x0 * y1 - x1 * y0;
                    A += cross; Cx += (x0 + x1) * cross; Cy += (y0 + y1) * cross;
                }
                A *= 0.5;
                if (Math.Abs(A) < 1e-9) continue;
                Cx /= (6.0 * A); Cy /= (6.0 * A);

                sumA += A; sumCx += Cx * A; sumCy += Cy * A;
            }

            if (Math.Abs(sumA) < 1e-9) return new { ok = false, msg = "Failed to compute centroid." };

            double cx = sumCx / sumA; double cy = sumCy / sumA;
            return new { ok = true, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), centroid = new { x = Math.Round(cx, 3), y = Math.Round(cy, 3), z = 0.0, unit = "mm" }, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class GetAreaMetricsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_metrics";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());
            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var boundaryLoc = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr);

            // Batch mode (optional): areaIds = [ ... ]
            var areaIdsArr = (p["areaIds"] as JArray)?.Values<int>().Where(x => x > 0).Distinct().ToList();
            if (areaIdsArr != null && areaIdsArr.Count > 0)
            {
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 200);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerCall") ?? p.Value<int?>("maxMillisPerTx") ?? 0);

                var sw = Stopwatch.StartNew();
                var opts = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = boundaryLoc };
                var items = new List<object>();

                int processed = 0;
                int nextIndex = startIndex;

                for (int i = startIndex; i < areaIdsArr.Count; i++)
                {
                    int id = areaIdsArr[i];
                    var area = doc.GetElement(new ElementId(id)) as ArchArea;
                    if (area != null)
                    {
                        double areaM2 = Math.Round(UnitHelper.ToExternal(area.Area, SpecTypeId.Area) ?? 0.0, 3);

                        double perFt = 0.0;
                        try
                        {
                            var loops = area.GetBoundarySegments(opts);
                            if (loops != null)
                                perFt = loops.SelectMany(loop => loop).Where(bs => bs != null && bs.GetCurve() != null).Select(bs => bs.GetCurve().Length).Sum();
                        }
                        catch { perFt = 0.0; }

                        double perimeterMm = Math.Round(UnitHelper.FtToMm(perFt), 3);
                        var levelName = (doc.GetElement(area.LevelId) as Level)?.Name ?? "";

                        items.Add(new { elementId = area.Id.IntegerValue, areaM2, perimeterMm, level = levelName });
                    }

                    processed++;
                    nextIndex = i + 1;
                    if (processed >= batchSize) break;
                    if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                }

                bool completed = nextIndex >= areaIdsArr.Count;
                return new
                {
                    ok = true,
                    requested = areaIdsArr.Count,
                    processed,
                    items,
                    completed,
                    nextIndex = completed ? (int?)null : nextIndex,
                    boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(),
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }

            // Single mode (backward compatible): areaId
            int areaId = p.Value<int?>("areaId") ?? p.Value<int?>("elementId") ?? 0;
            if (areaId <= 0) throw new InvalidOperationException("Parameter 'areaId' is required.");

            var areaSingle = doc.GetElement(new ElementId(areaId)) as ArchArea
                             ?? throw new InvalidOperationException("Area not found.");

            double areaM2Single = Math.Round(UnitHelper.ToExternal(areaSingle.Area, SpecTypeId.Area) ?? 0.0, 3);

            var optsSingle = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = boundaryLoc };
            var loopsSingle = areaSingle.GetBoundarySegments(optsSingle);
            double perFtSingle = 0.0;
            if (loopsSingle != null)
                perFtSingle = loopsSingle.SelectMany(loop => loop).Where(bs => bs != null && bs.GetCurve() != null).Select(bs => bs.GetCurve().Length).Sum();
            double perimeterMmSingle = Math.Round(UnitHelper.FtToMm(perFtSingle), 3);

            var levelNameSingle = (doc.GetElement(areaSingle.LevelId) as Level)?.Name ?? "";

            return new { ok = true, elementId = areaSingle.Id.IntegerValue, areaM2 = areaM2Single, perimeterMm = perimeterMmSingle, level = levelNameSingle, boundaryLocation = optsSingle.SpatialElementBoundaryLocation.ToString(), units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    public class GetAreaGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_geometry";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int areaId = p.Value<int?>("areaId") ?? p.Value<int?>("elementId") ?? 0;
            if (areaId <= 0) throw new InvalidOperationException("Parameter 'areaId' is required.");

            var area = doc.GetElement(new ElementId(areaId)) as ArchArea
                       ?? throw new InvalidOperationException("Area not found.");

            string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
            var opts = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
            };
            var loops = area.GetBoundarySegments(opts);

            var geometry = new List<object>();
            foreach (var loop in loops ?? new List<IList<BoundarySegment>>())
            {
                var segments = new List<object>();
                foreach (var bs in loop)
                {
                    var c = bs.GetCurve();
                    segments.Add(new
                    {
                        type = c.GetType().Name,
                        start = AreaGeom.XyzToMm(c.GetEndPoint(0)),
                        end = AreaGeom.XyzToMm(c.GetEndPoint(1)),
                        lengthMm = Math.Round(UnitHelper.FtToMm(c.ApproximateLength), 3)
                    });
                }
                geometry.Add(segments);
            }

            return new { ok = true, elementId = area.Id.IntegerValue, boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(), geometry, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    // ------------------------------------------------------------------------
    // D) AreaScheme / AreaPlan
    // ------------------------------------------------------------------------
    /// <summary>
    /// list_area_schemes
    /// Design/AreaAndSpatialContextCommands_Design.md に基づき、
    /// プロジェクト内の AreaScheme 一覧を返す。
    /// </summary>
    public class ListAreaSchemesCommand : IRevitCommandHandler
    {
        public string CommandName => "list_area_schemes";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var messages = new List<string>();

            if (doc == null)
            {
                messages.Add("アクティブドキュメントがありません。");
                return new { ok = false, areaSchemes = Array.Empty<object>(), messages };
            }

            try
            {
                var p = (JObject)(cmd.Params ?? new JObject());
                bool includeCounts = p.Value<bool?>("includeCounts") ?? false;

                var schemes = new FilteredElementCollector(doc)
                    .OfClass(typeof(AreaScheme))
                    .Cast<AreaScheme>()
                    .ToList();

                Dictionary<int, int> counts = null;
                if (includeCounts)
                {
                    var allAreas = new FilteredElementCollector(doc)
                        .OfClass(typeof(SpatialElement))
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null &&
                                    e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Areas)
                        .Cast<SpatialElement>()
                        .OfType<ArchArea>()
                        .ToList();

                    counts = allAreas
                        .GroupBy(a => a.AreaScheme != null ? a.AreaScheme.Id.IntegerValue : -1)
                        .ToDictionary(g => g.Key, g => g.Count());
                }

                var list = new List<object>();
                foreach (var s in schemes)
                {
                    int id = s.Id.IntegerValue;
                    int? areaCount = null;
                    if (includeCounts && counts != null && counts.TryGetValue(id, out var c))
                        areaCount = c;

                    list.Add(new
                    {
                        id,
                        name = s.Name ?? string.Empty,
                        areaCount
                    });
                }

                messages.Add($"{list.Count} AreaSchemes found.");
                return new { ok = true, areaSchemes = list, messages };
            }
            catch (Exception ex)
            {
                messages.Add("Failed to collect AreaSchemes: " + ex.Message);
                return new { ok = false, areaSchemes = Array.Empty<object>(), messages };
            }
        }
    }

    public class GetAreaSchemesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_area_schemes";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = (JObject)(cmd.Params ?? new JObject());
                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? int.MaxValue;
                bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
                string nameContains = (p.Value<string>("nameContains") ?? "").Trim();

                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(AreaScheme))
                    .Cast<AreaScheme>()
                    .ToList();

                if (!string.IsNullOrEmpty(nameContains))
                    all = all.Where(s => (s.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                var ordered = all
                    .Select(s => new { s, name = s.Name ?? "", id = s.Id.IntegerValue })
                    .OrderBy(x => x.name).ThenBy(x => x.id)
                    .Select(x => x.s).ToList();

                int totalCount = ordered.Count;

                if (count == 0)
                    return new { ok = true, totalCount, units = UnitHelper.DefaultUnitsMeta() };

                if (namesOnly)
                {
                    var names = ordered.Skip(skip).Take(count).Select(s => s.Name ?? "").ToList();
                    return new { ok = true, totalCount, names, units = UnitHelper.DefaultUnitsMeta() };
                }

                var list = ordered.Skip(skip).Take(count).Select(s => new
                {
                    schemeId = s.Id.IntegerValue,
                    uniqueId = s.UniqueId,
                    name = s.Name ?? ""
                }).ToList();

                return new { ok = true, totalCount, schemes = list, units = UnitHelper.DefaultUnitsMeta() };
            }
            catch (Exception ex) { return new { ok = false, msg = ex.Message }; }
        }
    }

    /// <summary>
    /// get_areas_by_scheme
    /// Design/AreaAndSpatialContextCommands_Design.md に基づき、
    /// 指定した AreaScheme に属する Area の一覧を返す。
    /// </summary>
    public class GetAreasBySchemeCommand : IRevitCommandHandler
    {
        public string CommandName => "get_areas_by_scheme";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var messages = new List<string>();

            if (doc == null)
            {
                messages.Add("アクティブドキュメントがありません。");
                return new { ok = false, scheme = (object)null, areas = Array.Empty<object>(), messages };
            }

            try
            {
                var p = (JObject)(cmd.Params ?? new JObject());

                int? schemeIdInt = p.Value<int?>("schemeId");
                string schemeName = (p.Value<string>("schemeName") ?? "").Trim();

                AreaScheme scheme = null;

                if (schemeIdInt.HasValue && schemeIdInt.Value > 0)
                {
                    var asId = new ElementId(schemeIdInt.Value);
                    scheme = doc.GetElement(asId) as AreaScheme;
                    if (scheme == null)
                    {
                        messages.Add($"AreaScheme (id={schemeIdInt.Value}) was not found.");
                        return new { ok = false, scheme = (object)null, areas = Array.Empty<object>(), messages };
                    }
                }
                else if (!string.IsNullOrEmpty(schemeName))
                {
                    scheme = new FilteredElementCollector(doc)
                        .OfClass(typeof(AreaScheme))
                        .Cast<AreaScheme>()
                        .FirstOrDefault(s => string.Equals(s.Name ?? string.Empty, schemeName, StringComparison.OrdinalIgnoreCase));

                    if (scheme == null)
                    {
                        messages.Add($"AreaScheme '{schemeName}' was not found.");
                        return new { ok = false, scheme = (object)null, areas = Array.Empty<object>(), messages };
                    }
                }
                else
                {
                    messages.Add("Either schemeId or schemeName is required.");
                    return new { ok = false, scheme = (object)null, areas = Array.Empty<object>(), messages };
                }

                var schemeObj = new
                {
                    id = scheme.Id.IntegerValue,
                    name = scheme.Name ?? string.Empty
                };

                messages.Add($"AreaScheme '{schemeObj.name}' (id={schemeObj.id}) resolved.");

                // levelNames フィルタ
                HashSet<string> levelNameFilter = null;
                if (p["levelNames"] is JArray levelsArr && levelsArr.Count > 0)
                {
                    levelNameFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var jt in levelsArr)
                    {
                        var lv = jt.Value<string>();
                        if (!string.IsNullOrWhiteSpace(lv))
                            levelNameFilter.Add(lv.Trim());
                    }
                }

                // includeParameters
                var includeParams = new List<string>();
                if (p["includeParameters"] is JArray ipArr && ipArr.Count > 0)
                {
                    foreach (var jt in ipArr)
                    {
                        var nm = jt.Value<string>();
                        if (!string.IsNullOrWhiteSpace(nm))
                            includeParams.Add(nm.Trim());
                    }
                }

                var areasAll = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null &&
                                e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Areas)
                    .Cast<SpatialElement>()
                    .OfType<ArchArea>()
                    .Where(a => a.AreaScheme != null && a.AreaScheme.Id == scheme.Id)
                    .ToList();

                var areasOut = new List<object>();

                foreach (var a in areasAll)
                {
                    string levelName = string.Empty;
                    try
                    {
                        var lvl = doc.GetElement(a.LevelId) as Level;
                        levelName = lvl?.Name ?? string.Empty;
                    }
                    catch { }

                    if (levelNameFilter != null)
                    {
                        if (string.IsNullOrEmpty(levelName) || !levelNameFilter.Contains(levelName))
                            continue;
                    }

                    double areaM2 = Math.Round(UnitHelper.InternalToSqm(a.Area), 3);

                    Dictionary<string, object> extraParams = null;
                    if (includeParams.Count > 0)
                    {
                        extraParams = new Dictionary<string, object>();
                        foreach (var paramName in includeParams)
                        {
                            try
                            {
                                var prm = a.LookupParameter(paramName);
                                if (prm == null) continue;
                                extraParams[paramName] = UnitHelper.ParamToSiInfo(prm, 3);
                            }
                            catch
                            {
                                // 個別パラメータ取得エラーは無視
                            }
                        }
                    }

                    areasOut.Add(new
                    {
                        id = a.Id.IntegerValue,
                        number = a.Number ?? string.Empty,
                        name = a.Name ?? string.Empty,
                        levelName,
                        area = areaM2,
                        unit = "m2",
                        extraParams = (object)(extraParams ?? new Dictionary<string, object>())
                    });
                }

                if (areasOut.Count == 0)
                {
                    messages.Add($"AreaScheme '{schemeObj.name}' has no Areas on the requested levels.");
                }
                else
                {
                    messages.Add($"{areasOut.Count} Areas returned for requested levels.");
                }

                return new { ok = true, scheme = schemeObj, areas = areasOut, messages };
            }
            catch (Exception ex)
            {
                messages.Add("get_areas_by_scheme failed: " + ex.Message);
                return new { ok = false, scheme = (object)null, areas = Array.Empty<object>(), messages };
            }
        }
    }

    public class CreateAreaSchemeCommand : IRevitCommandHandler
    {
        public string CommandName => "create_area_scheme";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            return new
            {
                ok = false,
                msg = "AreaScheme の作成は、このRevitバージョン/APIではサポートされていません（UIで作成後、get_area_schemesで取得してください）。",
                units = UnitHelper.DefaultUnitsMeta()
            };
        }
    }

    public class CreateAreaPlanCommand : IRevitCommandHandler
    {
        public string CommandName => "create_area_plan";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            if (!p.TryGetValue("levelId", out var levelIdTok))
                return new { ok = false, msg = "levelId が必要です。" };

            var levelId = new ElementId(levelIdTok.Value<int>());
            var level = doc.GetElement(levelId) as Level;
            if (level == null) return new { ok = false, msg = $"Level not found: {levelId.IntegerValue}" };

            AreaScheme scheme = null;
            if (p.TryGetValue("areaSchemeId", out var schemeIdTok))
            {
                var asId = new ElementId(schemeIdTok.Value<int>());
                scheme = doc.GetElement(asId) as AreaScheme;
                if (scheme == null) return new { ok = false, msg = $"AreaScheme not found: {asId.IntegerValue}" };
            }
            else
            {
                scheme = new FilteredElementCollector(doc)
                    .OfClass(typeof(AreaScheme))
                    .Cast<AreaScheme>()
                    .OrderBy(s => s.Name)
                    .FirstOrDefault();
                if (scheme == null)
                    return new { ok = false, msg = "AreaScheme がプロジェクトに存在しません。" };
            }

            string desiredName = (p.Value<string>("name") ?? "").Trim();

            ViewPlan view = null;
            using (var tx = new Transaction(doc, "Create Area Plan"))
            {
                try
                {
                    tx.Start();
                    view = ViewPlan.CreateAreaPlan(doc, scheme.Id, level.Id);
                    if (!string.IsNullOrEmpty(desiredName)) view.Name = MakeUniqueViewName(doc, desiredName);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Area Plan の作成に失敗: {ex.Message}" };
                }
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                name = view.Name,
                levelId = level.Id.IntegerValue,
                areaSchemeId = scheme.Id.IntegerValue,
                units = UnitHelper.DefaultUnitsMeta()
            };
        }

        private static string MakeUniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int i = 2;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                   .Any(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} ({i})";
                i++;
            }
            return name;
        }
    }

    // ------------------------------------------------------------------------
    // E) 追補コマンド（UnitHelper 統一版）
    // ------------------------------------------------------------------------
    public class AutoAreaBoundariesFromWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "auto_area_boundaries_from_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            try
            {
                Level level;
                var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);

                var cats = new List<BuiltInCategory>();
                if (p.TryGetValue("includeCategories", out var catsTok) && catsTok is JArray arr && arr.Count > 0)
                    cats.AddRange(arr.Values<int>().Select(i => (BuiltInCategory)i));
                if (cats.Count == 0) cats.Add(BuiltInCategory.OST_Walls);

                var bicFilter = new ElementMulticategoryFilter(cats);

                var existing = AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList();
                double mergeTol = UnitHelper.MmToFt(p.Value<double?>("mergeToleranceMm") ?? 5.0);

                int created = 0, skipped = 0, failed = 0, processed = 0;
                var itemsArr = p["items"] as JArray; // elementIds
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 200);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 250);
                var sw = Stopwatch.StartNew();

                List<Element> elems = null;
                if (itemsArr == null)
                {
                    elems = new FilteredElementCollector(doc)
                        .WherePasses(bicFilter)
                        .WhereElementIsNotElementType()
                        .ToElements()
                        .Where(e => { try { return (e as Wall)?.LevelId.IntegerValue == level.Id.IntegerValue; } catch { return false; } })
                        .ToList();
                }

                int nextIndex = startIndex;
                using (var tx = new Transaction(doc, "Auto Area Boundaries from Walls"))
                {
                    tx.Start();
                    if (itemsArr != null && itemsArr.Count > 0)
                    {
                        for (int i = startIndex; i < itemsArr.Count; i++)
                        {
                            try
                            {
                                int id = itemsArr[i].Type == JTokenType.Object ? ((JObject)itemsArr[i]).Value<int>("elementId") : itemsArr[i].Value<int>();
                                var e = doc.GetElement(new ElementId(id));
                                Curve c = null;
                                if (e is Wall w && w.LevelId.IntegerValue == level.Id.IntegerValue && w.Location is LocationCurve lc && lc.Curve != null) c = lc.Curve;
                                else { var lc1 = e?.Location as LocationCurve; if (lc1?.Curve != null) c = lc1.Curve; }
                                if (c == null) { skipped++; }
                                else {
                                    bool dup = existing.Any(ce => AreaCommon.CurveEquals(ce.GeometryCurve, c, mergeTol));
                                    if (dup) { skipped++; }
                                    else { var ce = doc.Create.NewAreaBoundaryLine(vp.SketchPlane, c, vp); existing.Add(ce); created++; }
                                }
                            }
                            catch { failed++; }
                            processed++; nextIndex = i + 1; if (processed >= batchSize) break; if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                        }
                    }
                    else
                    {
                        for (int i = startIndex; i < elems.Count; i++)
                        {
                            try
                            {
                                var e = elems[i];
                                Curve c = null;
                                if (e is Wall w && w.Location is LocationCurve lc && lc.Curve != null) c = lc.Curve;
                                else { var lc1 = e.Location as LocationCurve; if (lc1?.Curve != null) c = lc1.Curve; }
                                if (c == null) { skipped++; }
                                else {
                                    bool dup = existing.Any(ce => AreaCommon.CurveEquals(ce.GeometryCurve, c, mergeTol));
                                    if (dup) { skipped++; }
                                    else { var ce = doc.Create.NewAreaBoundaryLine(vp.SketchPlane, c, vp); existing.Add(ce); created++; }
                                }
                            }
                            catch { failed++; }
                            processed++; nextIndex = i + 1; if (processed >= batchSize) break; if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                        }
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = (itemsArr != null ? nextIndex >= itemsArr.Count : elems == null || nextIndex >= elems.Count);

                return new { ok = true, viewId = vp.Id.IntegerValue, levelId = level.Id.IntegerValue, created, skipped, failed, processed, completed, nextIndex = completed ? (int?)null : nextIndex, units = UnitHelper.DefaultUnitsMeta() };
            }
            catch (Exception ex) { return new { ok = false, msg = ex.Message }; }
        }
    }

    public class CopyAreaBoundariesFromRoomsCommand : IRevitCommandHandler
    {
        public string CommandName => "copy_area_boundaries_from_rooms";

        private static Curve PreferCurveElementButKeepSegmentEndpoints(Document doc, Curve segmentCurve, Curve curveElementCurve)
        {
            if (segmentCurve == null) return curveElementCurve;
            if (curveElementCurve == null) return segmentCurve;

            double shortTolFt = 1e-6;
            try { shortTolFt = doc?.Application?.ShortCurveTolerance ?? shortTolFt; } catch { }

            XYZ p0 = null;
            XYZ p1 = null;
            try
            {
                p0 = segmentCurve.GetEndPoint(0);
                p1 = segmentCurve.GetEndPoint(1);
            }
            catch
            {
                return curveElementCurve;
            }

            var line = curveElementCurve as Line;
            if (line != null)
            {
                try
                {
                    var unbound = Line.CreateUnbound(line.Origin, line.Direction);
                    var r0 = unbound.Project(p0);
                    var r1 = unbound.Project(p1);
                    var q0 = (r0 != null && r0.XYZPoint != null) ? r0.XYZPoint : p0;
                    var q1 = (r1 != null && r1.XYZPoint != null) ? r1.XYZPoint : p1;
                    if (q0 != null && q1 != null && q0.DistanceTo(q1) > shortTolFt)
                        return Line.CreateBound(q0, q1);
                }
                catch { }

                try
                {
                    if (p0 != null && p1 != null && p0.DistanceTo(p1) > shortTolFt)
                        return Line.CreateBound(p0, p1);
                }
                catch { }

                return curveElementCurve;
            }

            return segmentCurve;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            try
            {
                Level level;
                var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);

                var existing = AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList();
                double mergeTol = UnitHelper.MmToFt(p.Value<double?>("mergeToleranceMm") ?? 3.0);
                int created = 0, skipped = 0, roomsProcessed = 0, processed = 0;

                string boundaryLocationStr = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? "Finish";
                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocationStr)
                };

                string boundaryCurveSourceStr = p.Value<string>("boundaryCurveSource") ?? p.Value<string>("boundary_curve_source");
                bool? preferLineElementsOverride = p.Value<bool?>("preferLineElements") ?? p.Value<bool?>("prefer_line_elements");
                var boundaryCurveSource = SpatialUtils.ParseBoundaryCurveSource(boundaryCurveSourceStr, SpatialUtils.BoundaryCurveSource.BoundarySegment);
                if (preferLineElementsOverride.HasValue)
                {
                    boundaryCurveSource = preferLineElementsOverride.Value
                        ? SpatialUtils.BoundaryCurveSource.PreferLineElements
                        : SpatialUtils.BoundaryCurveSource.BoundarySegment;
                }
                bool preferLineElements = boundaryCurveSource == SpatialUtils.BoundaryCurveSource.PreferLineElements;

                var roomIdsFilter = (p["roomIds"] as JArray)?.Values<int>().ToHashSet() ?? new HashSet<int>();
                var itemsArr = p["items"] as JArray; // roomIds or {roomId}
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 50);
                int maxMillis = Math.Max(0, p.Value<int?>("maxMillisPerTx") ?? 200);
                var sw = Stopwatch.StartNew();

                List<Autodesk.Revit.DB.Architecture.Room> roomsAll = null;
                if (itemsArr == null)
                {
                    roomsAll = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Autodesk.Revit.DB.Architecture.Room>()
                        .Where(r => r.LevelId.IntegerValue == level.Id.IntegerValue)
                        .Where(r => roomIdsFilter.Count == 0 || roomIdsFilter.Contains(r.Id.IntegerValue))
                        .ToList();
                }

                int nextIndex = startIndex;
                using (var tx = new Transaction(doc, "Copy Area Boundaries from Rooms"))
                {
                    tx.Start();
                    if (itemsArr != null && itemsArr.Count > 0)
                    {
                        for (int i = startIndex; i < itemsArr.Count; i++)
                        {
                            try
                            {
                                int rid = itemsArr[i].Type == JTokenType.Object ? ((JObject)itemsArr[i]).Value<int>("roomId") : itemsArr[i].Value<int>();
                                var room = doc.GetElement(new ElementId(rid)) as Autodesk.Revit.DB.Architecture.Room;
                                if (room == null || room.LevelId.IntegerValue != level.Id.IntegerValue) { processed++; nextIndex = i + 1; continue; }
                                roomsProcessed++;
                                var loops = room.GetBoundarySegments(opts);
                                if (loops != null)
                                {
                                    foreach (var loop in loops)
                                        foreach (var seg in loop)
                                        {
                                            var segCurve = seg.GetCurve(); if (segCurve == null) continue;
                                            var c = segCurve;
                                            if (preferLineElements)
                                            {
                                                try
                                                {
                                                    var eid = seg.ElementId;
                                                    if (eid != null && eid != ElementId.InvalidElementId)
                                                    {
                                                        var e = doc.GetElement(eid);
                                                        if (e is CurveElement curveEl && curveEl.GeometryCurve != null) c = PreferCurveElementButKeepSegmentEndpoints(doc, segCurve, curveEl.GeometryCurve);
                                                    }
                                                }
                                                catch { }
                                            }
                                            var cFlat = AreaBoundaryMaterialCoreCenterUtil.FlattenCurveToZ(c, level.Elevation, out _);
                                            if (cFlat == null) continue;
                                            bool dup = existing.Any(ce => AreaCommon.CurveEquals(ce.GeometryCurve, cFlat, mergeTol));
                                            if (dup) { skipped++; continue; }
                                            var ce = doc.Create.NewAreaBoundaryLine(vp.SketchPlane, cFlat, vp);
                                            existing.Add(ce);
                                            created++;
                                        }
                                }
                            }
                            catch { /* 未囲い等はスキップ */ }
                            processed++; nextIndex = i + 1; if (processed >= batchSize) break; if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                        }
                    }
                    else
                    {
                        for (int i = startIndex; i < roomsAll.Count; i++)
                        {
                            try
                            {
                                var room = roomsAll[i];
                                roomsProcessed++;
                                var loops = room.GetBoundarySegments(opts);
                                if (loops != null)
                                {
                                    foreach (var loop in loops)
                                        foreach (var seg in loop)
                                        {
                                            var segCurve = seg.GetCurve(); if (segCurve == null) continue;
                                            var c = segCurve;
                                            if (preferLineElements)
                                            {
                                                try
                                                {
                                                    var eid = seg.ElementId;
                                                    if (eid != null && eid != ElementId.InvalidElementId)
                                                    {
                                                        var e = doc.GetElement(eid);
                                                        if (e is CurveElement curveEl && curveEl.GeometryCurve != null) c = PreferCurveElementButKeepSegmentEndpoints(doc, segCurve, curveEl.GeometryCurve);
                                                    }
                                                }
                                                catch { }
                                            }
                                            var cFlat = AreaBoundaryMaterialCoreCenterUtil.FlattenCurveToZ(c, level.Elevation, out _);
                                            if (cFlat == null) continue;
                                            bool dup = existing.Any(ce => AreaCommon.CurveEquals(ce.GeometryCurve, cFlat, mergeTol));
                                            if (dup) { skipped++; continue; }
                                            var ce = doc.Create.NewAreaBoundaryLine(vp.SketchPlane, cFlat, vp);
                                            existing.Add(ce);
                                            created++;
                                        }
                                }
                            }
                            catch { /* 未囲い等はスキップ */ }
                            processed++; nextIndex = i + 1; if (processed >= batchSize) break; if (maxMillis > 0 && sw.ElapsedMilliseconds >= maxMillis) break;
                        }
                    }
                    tx.Commit();
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }
                bool completed = (itemsArr != null ? nextIndex >= itemsArr.Count : roomsAll == null || nextIndex >= roomsAll.Count);

                return new
                {
                    ok = true,
                    viewId = vp.Id.IntegerValue,
                    levelId = level.Id.IntegerValue,
                    boundaryLocation = opts.SpatialElementBoundaryLocation.ToString(),
                    boundaryCurveSource = boundaryCurveSource.ToString(),
                    roomsProcessed,
                    created,
                    skipped,
                    processed,
                    completed,
                    nextIndex = completed ? (int?)null : nextIndex,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
            catch (Exception ex) { return new { ok = false, msg = ex.Message }; }
        }
    }

    public class MergeAreasCommand : IRevitCommandHandler
    {
        public string CommandName => "merge_areas";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();
            bool refreshView = p.Value<bool?>("refreshView") ?? false;

            try
            {
                Level level; var vp = AreaCommon.ResolveAreaPlanView(doc, p, out level);

                var ids = (p["areaIds"] as JArray)?.Values<int>().Distinct().ToList() ?? new List<int>();
                if (ids.Count < 2) return new { ok = false, msg = "areaIds を2つ以上指定してください。" };

                double tolFt = UnitHelper.MmToFt(p.Value<double?>("toleranceMm") ?? 2.0);
                var areas = ids.Select(id => doc.GetElement(new ElementId(id)) as Autodesk.Revit.DB.Area)
                               .Where(a => a != null).ToList();
                if (areas.Count < 2) return new { ok = false, msg = "有効な Area が見つかりません。" };

                var areaCurves = new List<(Autodesk.Revit.DB.Area area, Curve curve, ElementId behindId)>();
                var opts = new SpatialElementBoundaryOptions();

                foreach (var a in areas)
                {
                    var loops = a.GetBoundarySegments(opts);
                    if (loops == null) continue;
                    foreach (var loop in loops)
                        foreach (var seg in loop)
                            if (seg.GetCurve() is Curve c) areaCurves.Add((a, c, seg.ElementId));
                }

                var allBoundaryLines = AreaCommon.GetAreaBoundaryLinesInView(doc, vp).ToList();

                var toDelete = new HashSet<ElementId>();
                for (int i = 0; i < areaCurves.Count; i++)
                {
                    for (int j = i + 1; j < areaCurves.Count; j++)
                    {
                        if (areaCurves[i].area.Id == areaCurves[j].area.Id) continue;
                        var ci = areaCurves[i].curve;
                        var cj = areaCurves[j].curve;
                        if (!AreaCommon.CurveEquals(ci, cj, tolFt)) continue;

                        var target = allBoundaryLines.FirstOrDefault(ce => AreaCommon.CurveEquals(ce.GeometryCurve, ci, tolFt));
                        if (target != null) toDelete.Add(target.Id);
                    }
                }

                int deleted = 0;
                using (var tx = new Transaction(doc, "Merge Areas (delete shared boundaries)"))
                {
                    tx.Start();
                    try
                    {
                        foreach (var eid in toDelete)
                        {
                            var e = doc.GetElement(eid);
                            if (AreaCommon.IsAreaBoundaryLine(e))
                            {
                                doc.Delete(eid);
                                deleted++;
                            }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return new { ok = false, msg = "境界削除に失敗: " + ex.Message, tried = toDelete.Count, deleted };
                    }
                }
                if (refreshView) { try { uiapp?.ActiveUIDocument?.RefreshActiveView(); } catch { } }

                return new
                {
                    ok = true,
                    viewId = vp.Id.IntegerValue,
                    levelId = level.Id.IntegerValue,
                    requestedAreaCount = areas.Count,
                    sharedBoundaryDeleted = deleted,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
            catch (Exception ex) { return new { ok = false, msg = ex.Message }; }
        }
    }
}
