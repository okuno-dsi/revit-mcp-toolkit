#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // IRevitCommandHandler, RequestCommand, ResultUtil, RevitLogger

namespace RevitMCPAddin.Commands.VisualizationOps
{
    // =====================================================================
    // 共通ユーティリティ
    // =====================================================================
    internal static class SolarUtil
    {
        // Logging（Warn が無い環境向けフォールバック）
        public static void Info(string msg) => RevitLogger.Info(msg);
        public static void Warn(string msg) => RevitLogger.Info("[WARN] " + msg);

        // 度⇄ラジアン自動判別（Revit SiteLocation はラジアン前提）
        public static double ToRadLat(double v) => Math.Abs(v) <= Math.PI / 2.0 ? v : v * Math.PI / 180.0;
        public static double ToRadLon(double v) => Math.Abs(v) <= Math.PI ? v : v * Math.PI / 180.0;
        public static double ToDegLat(double v) => Math.Abs(v) <= Math.PI / 2.0 ? v * 180.0 / Math.PI : v;
        public static double ToDegLon(double v) => Math.Abs(v) <= Math.PI ? v * 180.0 / Math.PI : v;

        // 太陽ベクトル（高度/方位→XYZ）※簡易。必要に応じて厳密計算に差し替え
        public static XYZ SunVectorFromAltAzDeg(double altDeg, double azDeg)
        {
            double alt = altDeg * Math.PI / 180.0;
            double az = azDeg * Math.PI / 180.0;
            var v = new XYZ(Math.Cos(alt) * Math.Cos(az),
                            Math.Cos(alt) * Math.Sin(az),
                            Math.Sin(alt));
            return v.Normalize();
        }

        // ビュー個別の要素オーバーライド（塗り＋透過）
        public static void ApplyElementOverride(Document doc, View view, Element el, int r, int g, int b, int transparency)
        {
            var ogs = new OverrideGraphicSettings();
            var solid = GetSolidFillPatternId(doc);
            if (solid != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternId(solid);
                ogs.SetSurfaceForegroundPatternColor(new Color((byte)r, (byte)g, (byte)b));
            }
            ogs.SetSurfaceTransparency(Math.Max(0, Math.Min(100, transparency)));
            view.SetElementOverrides(el.Id, ogs);
        }

        public static ElementId GetSolidFillPatternId(Document doc)
        {
            var fpe = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
            return fpe?.Id ?? ElementId.InvalidElementId;
        }

        // CSV（汎用・安全）
        public static void WriteCsv(string path, IEnumerable<string> headers, IEnumerable<IEnumerable<object>> rows)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            using var w = new StreamWriter(path, false, System.Text.Encoding.UTF8);
            w.WriteLine(string.Join(",", headers));
            foreach (var r in rows)
            {
                var cells = r.Select(v => v is string s ? $"\"{s.Replace("\"", "\"\"")}\""
                                                         : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
                w.WriteLine(string.Join(",", cells));
            }
        }

        public static double Clamp(double x, double lo, double hi) => Math.Max(lo, Math.Min(hi, x));

        public static (int r, int g, int b)[] BuildHeatPalette(int bins)
        {
            var list = new List<(int, int, int)>();
            for (int i = 0; i < bins; i++)
            {
                double t = bins == 1 ? 0.0 : (double)i / (bins - 1);
                int r = (int)(0 + (255 - 0) * t);
                int g = (int)(90 + (60 - 90) * t);
                int b = (int)(255 + (0 - 255) * t);
                list.Add((r, g, b));
            }
            return list.ToArray();
        }

        // 影/サンパスのON（ヒューリスティック）
        public static void TryTurnOnSunAndShadow(View view, bool enableShadows, bool showSunPath)
        {
            if (!(enableShadows || showSunPath)) return;
            foreach (Parameter p in view.Parameters)
            {
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                string name = p.Definition?.Name ?? string.Empty;

                if (enableShadows && (
                    name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.Contains("日影") || name.Contains("影")))
                { try { p.Set(1); } catch { } }

                if (showSunPath && (
                    name.IndexOf("sun path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("sun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.Contains("サンパス") || name.Contains("太陽")))
                { try { p.Set(1); } catch { } }
            }
        }

        public static bool GetToggleState(View view, params string[] keywords)
        {
            foreach (Parameter p in view.Parameters)
            {
                if (p == null || p.StorageType != StorageType.Integer) continue;
                string name = p.Definition?.Name ?? string.Empty;
                foreach (var kw in keywords)
                {
                    if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        try { return p.AsInteger() == 1; } catch { }
                }
            }
            return false;
        }
    }

    internal class ElementIdComparer : IEqualityComparer<Element>
    {
        public bool Equals(Element? x, Element? y) => x?.Id.IntegerValue == y?.Id.IntegerValue;
        public int GetHashCode(Element obj) => obj.Id.IntegerValue.GetHashCode();
    }

    // =====================================================================
    // 事前準備：ユニーク名3Dビュー作成＋テンプレ解除＋詳細Lv＋カテゴリ表示＋影/サンパスON
    // =====================================================================
    public class PrepareSunstudyViewCommand : IRevitCommandHandler
    {
        public string CommandName => "prepare_sunstudy_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");
            var p = cmd.Params as JObject ?? new JObject();

            string baseName = p.Value<string>("baseName") ?? "SunStudy 3D";
            string detailLevel = p.Value<string>("detailLevel") ?? "Fine"; // "Fine"|"Medium"|"Coarse"
            bool showRooms = p["showRooms"]?.ToObject<bool>() ?? true;
            bool showSpaces = p["showSpaces"]?.ToObject<bool>() ?? true;
            bool makeSectionBoxTight = p["makeSectionBoxTight"]?.ToObject<bool>() ?? false;
            int? sectionPaddingMm = p["sectionPaddingMm"]?.ToObject<int?>();

            // 影/サンパスフラグ（要求に応じてON）
            bool enableShadows = p["enableShadows"]?.ToObject<bool>() ?? false;
            bool showSunPath = p["showSunPath"]?.ToObject<bool>() ?? false;

            View3D? view3D = null;

            using (var t = new Transaction(doc, "[MCP] Prepare Sunstudy 3D View"))
            {
                t.Start();

                var vftId = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional)?.Id
                    ?? ElementId.InvalidElementId;

                if (vftId == ElementId.InvalidElementId)
                    return ResultUtil.Err("3D ViewFamilyType が見つかりません。");

                // ユニーク名作成
                var existing = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string name = baseName;
                if (existing.Contains(name))
                {
                    int i = 1;
                    while (existing.Contains($"{baseName} ({i})")) i++;
                    name = $"{baseName} ({i})";
                }

                // ビュー作成
                view3D = View3D.CreateIsometric(doc, vftId);
                view3D.Name = name;

                // テンプレ解除 & 詳細レベル
                view3D.ViewTemplateId = ElementId.InvalidElementId;
                view3D.DetailLevel = ToDetailLevel(detailLevel);

                // Rooms / Spaces ON
                SetCategoryVisible(doc, view3D, BuiltInCategory.OST_Rooms, showRooms);
                SetCategoryVisible(doc, view3D, BuiltInCategory.OST_MEPSpaces, showSpaces);

                // 影/サンパス ON（ヒューリスティック）
                SolarUtil.TryTurnOnSunAndShadow(view3D, enableShadows, showSunPath);

                // セクションボックス
                if (makeSectionBoxTight)
                {
                    view3D.IsSectionBoxActive = true;
                    var bbox = ComputeActiveDocBoundingBox(doc, view3D);
                    if (bbox != null)
                    {
                        if (sectionPaddingMm.HasValue)
                            bbox = InflateBBox(bbox, MmToFt(sectionPaddingMm.Value),
                                                     MmToFt(sectionPaddingMm.Value),
                                                     MmToFt(sectionPaddingMm.Value));
                        view3D.SetSectionBox(bbox);
                    }
                }

                // 反映（Tx内）
                doc.Regenerate();

                t.Commit();
            }

            if (view3D == null) return ResultUtil.Err("3Dビューの作成に失敗しました。");

            // 状態を返却（ベストエフォートで読み取り）
            bool shadowsOn = SolarUtil.GetToggleState(view3D, "shadow", "日影", "影");
            bool sunPathOn = SolarUtil.GetToggleState(view3D, "sun path", "sun", "サンパス", "太陽");

            return ResultUtil.Ok(new
            {
                viewId = view3D.Id.IntegerValue,
                viewName = view3D.Name,
                settings = new
                {
                    detailLevel = view3D.DetailLevel.ToString(),
                    roomsVisible = IsCategoryVisible(doc, view3D, BuiltInCategory.OST_Rooms),
                    spacesVisible = IsCategoryVisible(doc, view3D, BuiltInCategory.OST_MEPSpaces),
                    sectionBoxActive = view3D.IsSectionBoxActive,
                    shadowsOn,
                    sunPathOn
                }
            });
        }

        private static ViewDetailLevel ToDetailLevel(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "coarse": return ViewDetailLevel.Coarse;
                case "medium": return ViewDetailLevel.Medium;
                case "fine": return ViewDetailLevel.Fine;
                default: return ViewDetailLevel.Fine;
            }
        }

        private static void SetCategoryVisible(Document doc, View view, BuiltInCategory bic, bool visible)
        {
            var cat = doc.Settings.Categories.get_Item(bic);
            if (cat != null && view.CanCategoryBeHidden(cat.Id))
                view.SetCategoryHidden(cat.Id, !visible);
        }

        private static bool IsCategoryVisible(Document doc, View view, BuiltInCategory bic)
        {
            var cat = doc.Settings.Categories.get_Item(bic);
            if (cat == null) return false;
            try { return !view.GetCategoryHidden(cat.Id); } catch { return true; }
        }

        private static BoundingBoxXYZ? ComputeActiveDocBoundingBox(Document doc, View view)
        {
            var bb = new BoundingBoxXYZ
            {
                Min = new XYZ(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
                Max = new XYZ(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity)
            };
            bool any = false;

            var ids = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElementIds();
            foreach (var id in ids)
            {
                var e = doc.GetElement(id);
                var ebb = e?.get_BoundingBox(view);
                if (ebb == null) continue;

                any = true;
                bb.Min = new XYZ(Math.Min(bb.Min.X, ebb.Min.X), Math.Min(bb.Min.Y, ebb.Min.Y), Math.Min(bb.Min.Z, ebb.Min.Z));
                bb.Max = new XYZ(Math.Max(bb.Max.X, ebb.Max.X), Math.Max(bb.Max.Y, ebb.Max.Y), Math.Max(bb.Max.Z, ebb.Max.Z));
            }
            return any ? bb : null;
        }

        private static BoundingBoxXYZ InflateBBox(BoundingBoxXYZ src, double dx, double dy, double dz)
        {
            var bb = new BoundingBoxXYZ();
            bb.Min = new XYZ(src.Min.X - dx, src.Min.Y - dy, src.Min.Z - dz);
            bb.Max = new XYZ(src.Max.X + dx, src.Max.Y + dy, src.Max.Z + dz);
            bb.Enabled = src.Enabled;
            bb.Transform = src.Transform;
            return bb;
        }

        private static double MmToFt(double mm) => mm / 304.8;
    }

    // =====================================================================
    // 単時刻の簡易スコア可視化（堅牢化）
    // =====================================================================
    public class SimulateSunlightCommand : IRevitCommandHandler
    {
        public string CommandName => "simulate_sunlight";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = (JObject)cmd.Params;

            // 対象ビュー
            var view = ResolveView(doc, uidoc, (int?)p["viewId"]);
            if (view == null) return ResultUtil.Err("viewId が無効です。");

            // 入力
            var dto = DateTimeOffset.Parse((string?)p["datetime"] ?? DateTimeOffset.Now.ToString("o"));
            var dtUtc = dto.UtcDateTime;

            var loc = p["location"] as JObject;
            double lat = loc?["latitude"]?.ToObject<double>() ?? 35.0;
            double lon = loc?["longitude"]?.ToObject<double>() ?? 135.0;

            // SiteLocation + Sun 時刻（Tx内で設定→Regenerate→Commit）
            using (var t = new Transaction(doc, "[MCP] Sunlight Setup"))
            {
                t.Start();

                var site = doc.ActiveProjectLocation.GetSiteLocation();
                site.Latitude = SolarUtil.ToRadLat(lat);
                site.Longitude = SolarUtil.ToRadLon(lon);

                var sas = view.SunAndShadowSettings;
                if (sas != null) sas.StartDateAndTime = dtUtc;

                doc.Regenerate(); // Tx内で反映（Tx外のRegenerate禁止対策）
                t.Commit();
            }

            // ターゲット選択
            var targetsReq = p["targets"]?.ToObject<List<string>>() ?? new List<string> { "Rooms" };
            var rooms = targetsReq.Contains("Rooms", StringComparer.OrdinalIgnoreCase)
                       ? new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().ToElements()
                       : Enumerable.Empty<Element>();
            var spaces = targetsReq.Contains("Spaces", StringComparer.OrdinalIgnoreCase)
                       ? new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_MEPSpaces).WhereElementIsNotElementType().ToElements()
                       : Enumerable.Empty<Element>();

            // 太陽角（簡易）
            double sunAltDeg = 30.0, sunAzDeg = 165.0;
            var sun = SolarUtil.SunVectorFromAltAzDeg(sunAltDeg, sunAzDeg);

            // スコア算出（FAST近似）
            var rand = new Random(0xA17);
            var entries = new List<JObject>();
            foreach (var e in rooms.Concat(spaces))
            {
                var name = e.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? e.Name ?? e.Id.IntegerValue.ToString();
                var bb = e.get_BoundingBox(view);
                if (bb == null)
                {
                    entries.Add(new JObject { ["elementId"] = e.Id.IntegerValue, ["name"] = name, ["level"] = "", ["areaM2"] = 0.0, ["score"] = 0.0, ["class"] = "N/A" });
                    continue;
                }
                double sizeX = bb.Max.X - bb.Min.X, sizeY = bb.Max.Y - bb.Min.Y;
                double areaM2 = Math.Max(0, sizeX * sizeY) * 0.09290304;

                var normals = new[] { XYZ.BasisX, -XYZ.BasisX, XYZ.BasisY, -XYZ.BasisY };
                double facadeFactor = normals.Select(n => Math.Max(0.0, n.Normalize().DotProduct(sun))).DefaultIfEmpty(0.0).Max();
                double openingRatio = SolarUtil.Clamp(0.15 + 0.05 * rand.NextDouble(), 0.10, 0.35);
                double score = SolarUtil.Clamp(facadeFactor * openingRatio, 0.0, 1.0);

                entries.Add(new JObject
                {
                    ["elementId"] = e.Id.IntegerValue,
                    ["name"] = name,
                    ["level"] = e.get_Parameter(BuiltInParameter.ROOM_LEVEL_ID)?.AsValueString() ?? "",
                    ["areaM2"] = Math.Round(areaM2, 3),
                    ["score"] = Math.Round(score, 3),
                    ["class"] = ""
                });
            }

            // ビン分け
            var scores = entries.Select(j => j.Value<double>("score")).DefaultIfEmpty(0).ToList();
            double min = scores.Min(), max = scores.Max(); int bins = Math.Max(3, (int?)p["bins"] ?? 7);
            string[] classLabels = Enumerable.Range(0, bins).Select(i => $"S{i + 1}").ToArray();
            foreach (var j in entries)
            {
                double sc = j.Value<double>("score");
                int k = (max - min) < 1e-9 ? 0 : Math.Min(bins - 1, (int)Math.Floor(((sc - min) / (max - min + 1e-9)) * bins));
                j["class"] = classLabels[k];
            }

            // 可視化（visualOverride）
            string mode = (string?)p["mode"] ?? "visualOverride";
            if (string.Equals(mode, "visualOverride", StringComparison.OrdinalIgnoreCase))
            {
                var colors = SolarUtil.BuildHeatPalette(bins);
                using (var t = new Transaction(doc, "[MCP] Sunlight Visual Override"))
                {
                    t.Start();
                    foreach (var j in entries)
                    {
                        int id = j.Value<int>("elementId");
                        int k = Array.IndexOf(classLabels, j.Value<string>("class"));
                        var c = colors[Math.Max(0, k)];
                        int tr = (int)(20 + 40.0 * (double)k / Math.Max(1, bins - 1));
                        var el = doc.GetElement(new ElementId(id));
                        if (el == null) continue;
                        SolarUtil.ApplyElementOverride(doc, view, el, c.r, c.g, c.b, tr);
                    }
                    t.Commit();
                }
            }
            else
            {
                // ColorFill 方式：クラス（S1..）を Department/Comments へ一時書込み（適用は別コマンド）
                using (var t = new Transaction(doc, "[MCP] Sunlight ColorFill Stamp"))
                {
                    t.Start();
                    foreach (var j in entries)
                    {
                        var el = doc.GetElement(new ElementId(j.Value<int>("elementId")));
                        var param = el?.LookupParameter("Department") ?? el?.LookupParameter("Comments");
                        if (param != null && !param.IsReadOnly) param.Set(j.Value<string>("class"));
                    }
                    t.Commit();
                }
            }

            // レポート（任意）
            var report = p["report"] as JObject;
            bool exportCsv = report?["exportCsv"]?.ToObject<bool>() ?? false;
            bool exportJson = report?["exportJson"]?.ToObject<bool>() ?? false;
            string csvPath = report?["csvPath"]?.ToString() ?? Path.Combine(Path.GetTempPath(), "sunlight_report.csv");
            string jsonPath = report?["jsonPath"]?.ToString() ?? Path.Combine(Path.GetTempPath(), "sunlight_report.json");

            if (exportCsv)
            {
                var rows = entries.Select(j => new object[]{ j.Value<int>("elementId"), j.Value<string>("name"), j.Value<string>("level"),
                                                             j.Value<double>("areaM2"), j.Value<double>("score"), j.Value<string>("class") });
                SolarUtil.WriteCsv(csvPath, new[] { "elementId", "name", "level", "areaM2", "score", "class" }, rows);
            }
            if (exportJson)
            {
                var root = new JObject
                {
                    ["ok"] = true,
                    ["datetime"] = dto.ToString("o"),
                    ["location"] = new JObject { ["lat"] = SolarUtil.ToDegLat(lat), ["lon"] = SolarUtil.ToDegLon(lon) },
                    ["sunAltDeg"] = sunAltDeg,
                    ["sunAzDeg"] = sunAzDeg,
                    ["bins"] = bins,
                    ["min"] = min,
                    ["max"] = max,
                    ["items"] = JArray.FromObject(entries)
                };
                File.WriteAllText(jsonPath, root.ToString());
            }

            SolarUtil.Info($"[Sunlight] targets={entries.Count}, csv={csvPath}, json={jsonPath}");
            return ResultUtil.Ok(new
            {
                viewId = view.Id.IntegerValue,
                mode,
                targetCount = entries.Count,
                report = new { csvPath, jsonPath },
                stats = new { min, max, avg = scores.DefaultIfEmpty(0).Average() }
            });
        }

        private static View? ResolveView(Document doc, UIDocument? uidoc, int? viewId)
        {
            if (viewId.HasValue) return doc.GetElement(new ElementId(viewId.Value)) as View;
            return uidoc?.ActiveView;
        }
    }
}
