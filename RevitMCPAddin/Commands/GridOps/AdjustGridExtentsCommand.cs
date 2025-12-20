// ================================================================
// File: Commands/GridOps/AdjustGridExtentsCommand.cs  (UnitHelper 統一版)
// Target: Revit 2023 / .NET Framework 4.8
// CommandName: "adjust_grid_extents"
//
// 変更点（要旨）:
// - 距離変換は UnitHelper に一本化（MmToFt/FtToMm）
// - 返却 units の明示は従来フォーマット維持（mm / internal ft）
// - ロジック/振る舞いは元実装と同一（dryRun, scopeBox detach 等）
// ================================================================

using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.GridOps
{
    public class AdjustGridExtentsCommand : IRevitCommandHandler
    {
        public string CommandName => "adjust_grid_extents";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;
            var p = (JObject)cmd.Params ?? new JObject();

            if (doc == null) return Fail("NO_DOCUMENT", "Active document not found.");

            // --- Params ---
            var modeRaw = (p.Value<string>("mode") ?? "both");
            var mode = NormalizeMode(modeRaw);                // "model" | "views" | "both"
            bool doZ = (mode == "model" || mode == "both");
            bool doXY = (mode == "views" || mode == "both");

            var includeLinks = p.Value<bool?>("includeLinkedModels") ?? false;
            var detachScope = p.Value<bool?>("detachScopeBoxForAdjustment") ?? false;
            var skipPinned = p.Value<bool?>("skipPinned") ?? true;
            var dryRun = p.Value<bool?>("dryRun") ?? false;

            var offsetsMm = p["offsetsMm"] != null ? p["offsetsMm"].ToObject<OffsetsMm>() : OffsetsMm.Default();

            // 対象ビュー（views/both の時のみ）
            var views = doXY ? ResolveViews(doc, p["targets"]) : new List<ViewPlan>();
            if (doXY && views.Count == 0) return Fail("NO_VIEWS", "No plan views matched by filter or specified viewIds.");

            // 全グリッド
            var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().ToList();
            if (grids.Count == 0) return Fail("NO_GRIDS", "No grids found in the active document.");

            // ---- 実行 ----
            ModelZResult zFt = null; // 内部計算は ft
            if (doZ) zFt = ComputeAndApplyModelZ(doc, grids, offsetsMm, skipPinned, dryRun);

            var xyFt = new List<ViewXYResult>(); // 内部は ft
            if (doXY)
            {
                var catNames = (p["includeCategoriesForBounds"] is JArray a)
                    ? a.Values<string>().ToList() : new List<string>();
                var includeCats = ResolveCategories(catNames);
                var worldFt = ComputeWorldBounds(doc, includeLinks, includeCats);  // ft

                foreach (var v in views)
                    xyFt.Add(ApplyXYInView(doc, v, grids, worldFt, offsetsMm, skipPinned, detachScope, dryRun));
            }

            // ---- 返却整形（mm表示）----
            var total = grids.Count;
            var lin = grids.Count(g => g.Curve is Line);
            var curved = total - lin;

            ModelZOut modelZ = null;
            if (doZ && zFt != null)
            {
                modelZ = new ModelZOut
                {
                    applied = zFt.applied,
                    zBottomMm = UnitHelper.FtToMm(zFt.zBottomFt),
                    zTopMm = UnitHelper.FtToMm(zFt.zTopFt),
                    updatedCount = zFt.updatedCount,
                    skipped = zFt.skipped
                };
            }

            var viewsXY = new List<ViewXYOut>();
            if (doXY)
            {
                foreach (var vr in xyFt)
                {
                    viewsXY.Add(new ViewXYOut
                    {
                        viewId = vr.viewId,
                        bboxXYMm = new BBox2Mm
                        {
                            left = UnitHelper.FtToMm(vr.bboxXYFt.left),
                            right = UnitHelper.FtToMm(vr.bboxXYFt.right),
                            down = UnitHelper.FtToMm(vr.bboxXYFt.down),
                            up = UnitHelper.FtToMm(vr.bboxXYFt.up)
                        },
                        updatedCount = vr.updatedCount,
                        skipped = vr.skipped
                    });
                }
            }

            var response = new
            {
                ok = true,
                summary = new { gridsTotal = total, gridsLinear = lin, gridsCurved = curved },
                modelZ,
                viewsXY,
                units = new
                {
                    output = new { length = "mm" },
                    internalUnits = new { length = "ft" }
                },
                msg = BuildMessage(doZ, doXY)
            };

            // ログ（JSON文字列）
            var logObj = new
            {
                cmd = CommandName,
                ok = true,
                modeNormalized = mode,
                offsetsMm,
                docTitle = doc.Title,
                viewIds = views.Select(v => v.Id.IntegerValue).ToArray(),
                summary = new { total, lin, curved },
                modelZ = modelZ,
                viewsXY = viewsXY
            };
            RevitLogger.Info(JsonConvert.SerializeObject(logObj));

            return response;
        }

        // ===================== Z(Model) =====================
        private ModelZResult ComputeAndApplyModelZ(Document doc, List<Grid> grids,
            OffsetsMm offsets, bool skipPinned, bool dryRun)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            if (levels.Count == 0)
            {
                return new ModelZResult
                {
                    applied = false,
                    updatedCount = 0,
                    skipped = new SkipInfo(),
                    zBottomFt = 0,
                    zTopFt = 0,
                    msg = "No levels found."
                };
            }

            double minZ = levels.Min(l => l.Elevation);
            double maxZ = levels.Max(l => l.Elevation);

            double zBottom = minZ - UnitHelper.MmToFt(offsets.bottom);
            double zTop = maxZ + UnitHelper.MmToFt(offsets.top);

            var skipped = new SkipInfo();
            int updated = 0;

            if (!dryRun)
            {
                using (var t = new Transaction(doc, "[MCP] Adjust Grid Z extents"))
                {
                    t.Start();
                    foreach (var g in grids)
                    {
                        try
                        {
                            if (skipPinned && g.Pinned) { skipped.pinned++; continue; }
                            g.SetVerticalExtents(zBottom, zTop);
                            updated++;
                        }
                        catch { skipped.errors++; }
                    }
                    t.Commit();
                }
            }
            else
            {
                foreach (var g in grids)
                {
                    if (skipPinned && g.Pinned) { skipped.pinned++; continue; }
                    updated++;
                }
            }

            return new ModelZResult
            {
                applied = !dryRun,
                zBottomFt = zBottom,
                zTopFt = zTop,
                updatedCount = updated,
                skipped = skipped
            };
        }

        // ===================== XY(ViewSpecific) =====================
        private ViewXYResult ApplyXYInView(Document doc, ViewPlan view, List<Grid> grids,
            WorldBounds worldFt, OffsetsMm offsets, bool skipPinned, bool detachScope, bool dryRun)
        {
            var res = new ViewXYResult
            {
                viewId = view.Id.IntegerValue,
                updatedCount = 0,
                skipped = new SkipInfo()
            };

            // worldFt + mmオフセット → ftで端点決定（UnitHelper）
            var left = worldFt.left - UnitHelper.MmToFt(offsets.left);
            var right = worldFt.right + UnitHelper.MmToFt(offsets.right);
            var down = worldFt.down - UnitHelper.MmToFt(offsets.down);
            var up = worldFt.up + UnitHelper.MmToFt(offsets.up);

            var candidates = grids.Where(g => g.Curve is Line).ToList();
            var curvedCount = grids.Count - candidates.Count;
            if (curvedCount > 0) res.skipped.curved += curvedCount;

            if (!dryRun)
            {
                using (var t = new Transaction(doc, $"[MCP] Adjust Grid XY in View {view.Id.IntegerValue}"))
                {
                    t.Start();

                    foreach (var g in candidates)
                    {
                        try
                        {
                            if (skipPinned && g.Pinned) { res.skipped.pinned++; continue; }

                            // ビューで非表示ならスキップ（必要に応じて）
                            try
                            {
                                if (g.IsHidden(view))
                                {
                                    res.skipped.errors++; // 理由: hidden in view
                                    continue;
                                }
                            }
                            catch { /* IsHidden 例外は無視 */ }

                            // ScopeBox 一時解除（必要なら）
                            ElementId originalScope = ElementId.InvalidElementId;
                            Parameter scopeParam = g.get_Parameter(BuiltInParameter.DATUM_VOLUME_OF_INTEREST);
                            bool scopeDetached = false;

                            if (scopeParam != null && scopeParam.AsElementId() != ElementId.InvalidElementId)
                            {
                                if (!detachScope) { res.skipped.scopeBoxManaged++; continue; }
                                originalScope = scopeParam.AsElementId();
                                scopeParam.Set(ElementId.InvalidElementId);
                                scopeDetached = true;
                            }

                            var line = (Line)g.Curve;
                            var dir = line.Direction;
                            var pOnLine = line.GetEndPoint(0);

                            bool xDominant = Math.Abs(dir.X) >= Math.Abs(dir.Y);
                            Line newLine;

                            if (xDominant)
                            {
                                var y = pOnLine.Y; var z = pOnLine.Z;
                                newLine = Line.CreateBound(new XYZ(left, y, z), new XYZ(right, y, z));
                            }
                            else
                            {
                                var x = pOnLine.X; var z = pOnLine.Z;
                                newLine = Line.CreateBound(new XYZ(x, down, z), new XYZ(x, up, z));
                            }

                            // ★ ビュー固有エクステントに切替えてから、2D線分を設定
                            g.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
                            g.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);
                            g.SetCurveInView(DatumExtentType.ViewSpecific, view, newLine);

                            // ScopeBox 復帰
                            if (scopeDetached && originalScope != ElementId.InvalidElementId)
                                scopeParam.Set(originalScope);

                            res.updatedCount++;
                        }
                        catch
                        {
                            res.skipped.errors++;
                        }
                    }

                    t.Commit();
                }
            }
            else
            {
                foreach (var g in candidates)
                {
                    if (skipPinned && g.Pinned) { res.skipped.pinned++; continue; }

                    Parameter scopeParam = g.get_Parameter(BuiltInParameter.DATUM_VOLUME_OF_INTEREST);
                    if (scopeParam != null && scopeParam.AsElementId() != ElementId.InvalidElementId && !detachScope)
                    { res.skipped.scopeBoxManaged++; continue; }

                    // ビュー非表示なら dry-run でもスキップ計上
                    try
                    {
                        if (g.IsHidden(view)) { res.skipped.errors++; continue; }
                    }
                    catch { /* ignore */ }

                    res.updatedCount++;
                }
            }

            res.bboxXYFt = new BBox2 { left = left, right = right, down = down, up = up };
            return res;
        }

        // ===================== 外形BBox（world, ft） =====================
        private WorldBounds ComputeWorldBounds(Document doc, bool includeLinks, List<BuiltInCategory> includeCategories)
        {
            var init = new WorldBounds
            {
                left = double.PositiveInfinity,
                right = double.NegativeInfinity,
                down = double.PositiveInfinity,
                up = double.NegativeInfinity
            };

            Action<BoundingBoxXYZ, Transform> accumulate = (bb, tf) =>
            {
                if (bb == null) return;
                var min = tf.OfPoint(bb.Min);
                var max = tf.OfPoint(bb.Max);
                var minx = Math.Min(min.X, max.X);
                var maxx = Math.Max(min.X, max.X);
                var miny = Math.Min(min.Y, max.Y);
                var maxy = Math.Max(min.Y, max.Y);
                if (minx < init.left) init.left = minx;
                if (maxx > init.right) init.right = maxx;
                if (miny < init.down) init.down = miny;
                if (maxy > init.up) init.up = maxy;
            };

            if (includeCategories == null || includeCategories.Count == 0)
                includeCategories = DefaultBoundCats;

            // 現行ドキュメント
            foreach (var bic in includeCategories)
            {
                try
                {
                    var col = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().ToElements();
                    foreach (var e in col) accumulate(e.get_BoundingBox(null), Transform.Identity);
                }
                catch { }
            }

            // リンク
            if (includeLinks)
            {
                var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();
                foreach (var l in links)
                {
                    var linkDoc = l.GetLinkDocument();
                    if (linkDoc == null) continue;
                    var tf = l.GetTotalTransform(); // Revit 2023

                    foreach (var bic in includeCategories)
                    {
                        try
                        {
                            var col = new FilteredElementCollector(linkDoc).OfCategory(bic).WhereElementIsNotElementType().ToElements();
                            foreach (var e in col) accumulate(e.get_BoundingBox(null), tf);
                        }
                        catch { }
                    }
                }
            }

            // フォールバック：ActiveView.CropBox のみ
            if (!IsFinite(init.left) || !IsFinite(init.right) || !IsFinite(init.down) || !IsFinite(init.up))
            {
                var fallback = doc.ActiveView?.CropBox;
                if (fallback != null) accumulate(fallback, Transform.Identity);
                if (!IsFinite(init.left) || !IsFinite(init.right) || !IsFinite(init.down) || !IsFinite(init.up))
                { init.left = -10; init.right = 10; init.down = -10; init.up = 10; }
            }
            return init;
        }

        // ===================== View 解決 =====================
        private List<ViewPlan> ResolveViews(Document doc, JToken targetsToken)
        {
            var result = new List<ViewPlan>();
            if (targetsToken == null)
            {
                if (doc.ActiveView is ViewPlan vp) result.Add(vp);
                return result;
            }

            var tgt = (JObject)targetsToken;

            if (tgt.TryGetValue("viewIds", out var arr) && arr is JArray)
            {
                foreach (var idVal in arr.Values<int>())
                {
                    var v = doc.GetElement(new ElementId(idVal)) as ViewPlan;
                    if (v != null && IsSupportedPlanKind(v)) result.Add(v);
                }
            }

            if (tgt.TryGetValue("viewFilter", out var vfTok) && vfTok is JObject vf)
            {
                var kinds = vf["planKinds"] is JArray jk ? jk.Values<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
                                                         : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool filterByKinds = kinds.Count > 0;

                var plans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>();
                foreach (var v in plans)
                {
                    if (v.IsTemplate) continue;
                    if (!v.ViewType.ToString().Contains("Plan")) continue;
                    if (filterByKinds && !IsKindMatched(v, kinds)) continue;
                    if (!result.Contains(v)) result.Add(v);
                }
            }

            if (result.Count == 0 && doc.ActiveView is ViewPlan avp && IsSupportedPlanKind(avp))
                result.Add(avp);

            return result;
        }

        private bool IsSupportedPlanKind(ViewPlan v)
        {
            // EngineeringPlan を構造平面として扱う
            var vt = v.ViewType;
            return vt == ViewType.FloorPlan || vt == ViewType.EngineeringPlan || vt == ViewType.CeilingPlan;
        }

        private bool IsKindMatched(ViewPlan v, HashSet<string> kinds)
        {
            if (kinds.Count == 0) return true;
            string vt = v.ViewType.ToString(); // "FloorPlan" / "CeilingPlan" / "EngineeringPlan"
            if (kinds.Contains("FloorPlan") && vt == "FloorPlan") return true;
            if (kinds.Contains("StructuralPlan") && vt == "EngineeringPlan") return true; // 構造平面=EngineeringPlan扱い
            if (kinds.Contains("CeilingPlan") && vt == "CeilingPlan") return true;
            return false;
        }

        // ===================== カテゴリ解決 =====================
        private List<BuiltInCategory> ResolveCategories(List<string> names)
        {
            if (names == null || names.Count == 0) return DefaultBoundCats;

            var list = new List<BuiltInCategory>();
            foreach (var n in names)
            {
                if (CategoryMap.TryGetValue(n.Trim(), out var bic))
                    list.Add(bic);
            }
            if (list.Count == 0) list = DefaultBoundCats;
            return list;
        }

        // ===================== 辞書 / 定数 =====================
        private static readonly List<BuiltInCategory> DefaultBoundCats = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation, // 単数
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Curtain_Systems       // アンダースコア
        };

        private static readonly Dictionary<string, BuiltInCategory> CategoryMap =
            new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
        {
            { "Walls", BuiltInCategory.OST_Walls },
            { "Floors", BuiltInCategory.OST_Floors },
            { "Columns", BuiltInCategory.OST_Columns },
            { "StructuralColumns", BuiltInCategory.OST_StructuralColumns },
            { "StructuralFraming", BuiltInCategory.OST_StructuralFraming },
            { "StructuralFoundation", BuiltInCategory.OST_StructuralFoundation },
            { "GenericModel", BuiltInCategory.OST_GenericModel },
            { "Roofs", BuiltInCategory.OST_Roofs },
            { "CurtainSystem", BuiltInCategory.OST_Curtain_Systems }
        };

        // ===================== Utils / DTO =====================
        private static string NormalizeMode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "both";
            var s = raw.Trim().ToLowerInvariant();
            if (s == "modelz" || s == "z" || s == "3d" || s == "global") return "model";
            if (s == "xy" || s == "plan" || s == "viewsxy") return "views";
            if (s == "both" || s == "all") return "both";
            if (s == "model" || s == "views" || s == "both") return s;
            return "both";
        }

        private static string BuildMessage(bool didZ, bool didXY)
        {
            if (didZ && didXY) return "grid extents adjusted (model Z + selected views XY)";
            if (didZ) return "grid extents adjusted (model Z)";
            if (didXY) return "grid extents adjusted (selected views XY)";
            return "no changes applied";
        }

        private static object Fail(string code, string msg)
        {
            var payload = JsonConvert.SerializeObject(new { cmd = "adjust_grid_extents", ok = false, code, msg });
            RevitLogger.Info(payload);
            return new { ok = false, msg, error = new { code } };
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // --- 入出力DTO（内部ft→返却mmに変換） ---
        private class OffsetsMm
        {
            public double top { get; set; }
            public double bottom { get; set; }
            public double left { get; set; }
            public double right { get; set; }
            public double down { get; set; }
            public double up { get; set; }
            public static OffsetsMm Default() => new OffsetsMm { top = 2000, bottom = 4000, left = 5000, right = 4000, down = 5000, up = 4000 };
        }

        private class WorldBounds { public double left, right, down, up; }

        private class BBox2 { public double left, right, down, up; }

        private class SkipInfo
        {
            public int curved { get; set; }
            public int pinned { get; set; }
            public int scopeBoxManaged { get; set; }
            public int errors { get; set; }
        }

        private class ModelZResult
        {
            public bool applied { get; set; }
            public double zBottomFt { get; set; }
            public double zTopFt { get; set; }
            public int updatedCount { get; set; }
            public SkipInfo skipped { get; set; }
            public string msg { get; set; }
        }

        private class ViewXYResult
        {
            public int viewId { get; set; }
            public BBox2 bboxXYFt { get; set; }
            public int updatedCount { get; set; }
            public SkipInfo skipped { get; set; }
        }

        // --- 返却用（mm） ---
        private class BBox2Mm { public double left, right, down, up; }

        private class ModelZOut
        {
            public bool applied { get; set; }
            public double zBottomMm { get; set; }
            public double zTopMm { get; set; }
            public int updatedCount { get; set; }
            public SkipInfo skipped { get; set; }
        }

        private class ViewXYOut
        {
            public int viewId { get; set; }
            public BBox2Mm bboxXYMm { get; set; }
            public int updatedCount { get; set; }
            public SkipInfo skipped { get; set; }
        }
    }
}
