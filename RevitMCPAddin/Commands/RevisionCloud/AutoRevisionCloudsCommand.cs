// ================================================================
// File: RevitMCPAddin/Commands/RevisionCloud/AutoRevisionCloudsCommand.cs
// Target: .NET Framework 4.8 / Revit 2023
// Purpose:
//   差分抽出（簡易）＋ レポート ＋ リビジョンクラウド作図を 1 コマンドで実行。
//   入力は filters 方式/ categories+rules 方式 の両方に対応。
//   drawMode: "none" | "each" | "merge"
//   コメント自動記載: cloudOptions.annotate.enabled/template
//   安全実装: JObject混在/オーバーロード差/カテゴリ名ゆらぎ/単位(mm)
//
// JSON 例：
// {
//   "viewId": 401,
//   "compare": {"mode":"open_docs","sourceDocTitle":"Host","baselineDocTitle":"Baseline"}, // （本版は未使用）
//   "categories": {"include":["Structural Frames"], "exclude":[]},
//   "rules": {"moveThresholdMm":50, "bboxDeltaMm":30, "checkTypeChange":true, "paramNames":["Comments","Mark"]},
//   "cloudOptions": {
//     "drawMode":"each", "paddingMm":200, "minCloudSizeMm":150, "mergeNearbyMm":300,
//     "annotate":{"enabled":true, "template":"${changeType}: ${elementCount} item(s) in ${category}"}
//   },
//   "output":{"includeDetectedElementIds":true,"includeChangeDetails":true}
// }
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    public class AutoRevisionCloudsCommand : IRevitCommandHandler
    {
        public string CommandName => "auto_revision_clouds";

        // ===== Unit helpers =====
        private static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        // ===== Safe JObject =====
        private static JObject AsJObject(object any)
        {
            if (any is JObject jo) return jo;
            return JObject.FromObject(any ?? new object());
        }

        // ===== Category name resolver (Enum/ID/表示名/部分一致) =====
        private static HashSet<BuiltInCategory> ResolveCategoryNames(Document doc, IEnumerable<string> names)
        {
            var set = new HashSet<BuiltInCategory>();
            if (names == null) return set;
            var wanted = names.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (wanted.Count == 0) return set;

            // 1) 直接 Enum or 数値
            foreach (var raw in wanted)
            {
                if (Enum.TryParse<BuiltInCategory>(raw, true, out var bic)) { set.Add(bic); continue; }
                if (int.TryParse(raw, out var i)) { try { set.Add((BuiltInCategory)i); } catch { } }
            }

            // 2) 表示名/部分一致
            foreach (Category cat in doc.Settings.Categories)
            {
                if (wanted.Any(w => string.Equals(cat.Name, w, StringComparison.OrdinalIgnoreCase))
                 || wanted.Any(w => cat.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    set.Add((BuiltInCategory)cat.Id.IntValue());

                foreach (Category sub in cat.SubCategories)
                {
                    if (wanted.Any(w => string.Equals(sub.Name, w, StringComparison.OrdinalIgnoreCase))
                     || wanted.Any(w => sub.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                        set.Add((BuiltInCategory)sub.Id.IntValue());
                }
            }
            return set;
        }

        // ===== Template render =====
        private static string RenderTemplate(string template, JObject label)
        {
            if (string.IsNullOrWhiteSpace(template) || label == null) return null;
            string s = template;
            foreach (var prop in label.Properties())
            {
                s = s.Replace("${" + prop.Name + "}", prop.Value?.ToString() ?? "");
            }
            return s;
        }

        // ===== Signed area for curve orientation (XY) =====
        private static double SignedAreaXY(IList<Curve> curves)
        {
            var pts = curves.Select(c => c.GetEndPoint(0)).ToList();
            double s2 = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                s2 += (a.X * b.Y - b.X * a.Y);
            }
            return 0.5 * s2;
        }

        // ===== Safe RevisionCloud.Create wrapper (handles overloads) =====
        private static Autodesk.Revit.DB.RevisionCloud SafeCreateRevisionCloud(Document doc, View view, ElementId revisionId, IList<Curve> curves)
        {
            // CW 統一（RevitはCWが安定）
            if (SignedAreaXY(curves) > 0)
                curves = curves.AsEnumerable().Reverse().Select(c => c.CreateReversed()).ToList();

            var t = typeof(Autodesk.Revit.DB.RevisionCloud);

            // (doc, view, ElementId, IList<Curve>)
            var m1 = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(ElementId), typeof(IList<Curve>) });
            if (m1 != null) return (Autodesk.Revit.DB.RevisionCloud)m1.Invoke(null, new object[] { doc, view, revisionId, curves });

            // (doc, view, IList<Curve>, ElementId)
            var m2 = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<Curve>), typeof(ElementId) });
            if (m2 != null) return (Autodesk.Revit.DB.RevisionCloud)m2.Invoke(null, new object[] { doc, view, curves, revisionId });

            // (doc, view, IList<CurveLoop>, ElementId)
            var m3 = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<CurveLoop>), typeof(ElementId) });
            if (m3 != null)
            {
                var loop = new CurveLoop(); foreach (var c in curves) loop.Append(c);
                return (Autodesk.Revit.DB.RevisionCloud)m3.Invoke(null, new object[] { doc, view, new List<CurveLoop> { loop }, revisionId });
            }

            // (doc, view, IList<Curve>) → 後から Revision を付与
            var m4 = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<Curve>) });
            if (m4 != null)
            {
                var rc = (Autodesk.Revit.DB.RevisionCloud)m4.Invoke(null, new object[] { doc, view, curves });
                if (rc != null)
                {
                    try
                    {
                        var p = rc.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION);
                        if (p != null && !p.IsReadOnly) p.Set(revisionId);
                    }
                    catch { /* ignore */ }
                }
                return rc;
            }
            return null;
        }

        // ===== Helpers: rect(mm) -> curves(ft) =====
        private static IList<Curve> RectToCurvesFt(JObject rectMm)
        {
            var min = rectMm["min"]; var max = rectMm["max"];
            double x0 = MmToFt(min.Value<double>("x"));
            double y0 = MmToFt(min.Value<double>("y"));
            double z = MmToFt(min.Value<double>("z"));
            double x1 = MmToFt(max.Value<double>("x"));
            double y1 = MmToFt(max.Value<double>("y"));
            var p0 = new XYZ(x0, y0, z);
            var p1 = new XYZ(x1, y0, z);
            var p2 = new XYZ(x1, y1, z);
            var p3 = new XYZ(x0, y1, z);
            var curves = new List<Curve>
            {
                Line.CreateBound(p0,p1),
                Line.CreateBound(p1,p2),
                Line.CreateBound(p2,p3),
                Line.CreateBound(p3,p0)
            };
            if (SignedAreaXY(curves) > 0)
                curves = curves.AsEnumerable().Reverse().Select(c => c.CreateReversed()).ToList();
            return curves;
        }

        private static JObject RectFromBBoxMm(BoundingBoxXYZ bb, View v, double padFt, double minFt)
        {
            var min = bb.Min; var max = bb.Max;

            double w = max.X - min.X, h = max.Y - min.Y;
            if (w < minFt) { double d = 0.5 * (minFt - w); min = new XYZ(min.X - d, min.Y, min.Z); max = new XYZ(max.X + d, max.Y, max.Z); }
            if (h < minFt) { double d = 0.5 * (minFt - h); min = new XYZ(min.X, min.Y - d, min.Z); max = new XYZ(max.X, max.Y + d, max.Z); }

            min = new XYZ(min.X - padFt, min.Y - padFt, min.Z);
            max = new XYZ(max.X + padFt, max.Y + padFt, max.Z);

            double zmm = (v is ViewPlan) ? 0.0 : Math.Round(FtToMm((min.Z + max.Z) * 0.5), 3);

            return new JObject
            {
                ["min"] = new JObject { ["x"] = Math.Round(FtToMm(min.X), 3), ["y"] = Math.Round(FtToMm(min.Y), 3), ["z"] = zmm },
                ["max"] = new JObject { ["x"] = Math.Round(FtToMm(max.X), 3), ["y"] = Math.Round(FtToMm(max.Y), 3), ["z"] = zmm }
            };
        }

        private static List<JObject> MergeRectsMm(List<JObject> rects, double mergeNearbyMm)
        {
            var result = new List<JObject>();
            var used = new bool[rects.Count];
            for (int i = 0; i < rects.Count; i++)
            {
                if (used[i]) continue;
                var a = rects[i]; used[i] = true;
                double ax0 = a["min"].Value<double>("x"), ay0 = a["min"].Value<double>("y");
                double ax1 = a["max"].Value<double>("x"), ay1 = a["max"].Value<double>("y");
                double az = a["min"].Value<double>("z");

                for (int j = i + 1; j < rects.Count; j++)
                {
                    if (used[j]) continue;
                    var b = rects[j];
                    double bx0 = b["min"].Value<double>("x"), by0 = b["min"].Value<double>("y");
                    double bx1 = b["max"].Value<double>("x"), by1 = b["max"].Value<double>("y");

                    bool overlapX = !(bx0 > ax1 + mergeNearbyMm || bx1 < ax0 - mergeNearbyMm);
                    bool overlapY = !(by0 > ay1 + mergeNearbyMm || by1 < ay0 - mergeNearbyMm);
                    if (overlapX && overlapY)
                    {
                        ax0 = Math.Min(ax0, bx0); ay0 = Math.Min(ay0, by0);
                        ax1 = Math.Max(ax1, bx1); ay1 = Math.Max(ay1, by1);
                        used[j] = true;
                    }
                }

                result.Add(new JObject
                {
                    ["min"] = new JObject { ["x"] = ax0, ["y"] = ay0, ["z"] = az },
                    ["max"] = new JObject { ["x"] = ax1, ["y"] = ay1, ["z"] = az }
                });
            }
            return result;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            try
            {
                // ===== View =====
                if (!p.TryGetValue("viewId", out var vTok))
                    return new { ok = false, msg = "viewId is required." };
                var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vTok.Value<int>())) as View;
                if (view == null) return new { ok = false, msg = $"View not found: {vTok.Value<int>()}" };

                // ===== Revision resolve/create =====
                ElementId revisionId = ElementId.InvalidElementId;
                if (p.TryGetValue("revisionId", out var ridTok))
                {
                    revisionId = Autodesk.Revit.DB.ElementIdCompat.From(ridTok.Value<int>());
                    if (doc.GetElement(revisionId) as Autodesk.Revit.DB.Revision == null)
                        return new { ok = false, msg = $"Revision not found: {revisionId.IntValue()}" };
                }
                else
                {
                    Autodesk.Revit.DB.Revision rev;
                    using (var txR = new Transaction(doc, "Create Default Revision"))
                    {
                        txR.Start();
                        rev = Autodesk.Revit.DB.Revision.Create(doc);
                        txR.Commit();
                    }
                    revisionId = rev.Id; // ★ 再宣言せず代入だけ
                }

                // ===== Options =====
                var cloudOpt = (JObject?)p["cloudOptions"] ?? new JObject();
                string drawMode = (cloudOpt.Value<string>("drawMode") ?? "merge").Trim().ToLowerInvariant(); // none|each|merge
                double paddingFt = MmToFt(cloudOpt.Value<double?>("paddingMm") ?? 150.0);
                double minFt = MmToFt(cloudOpt.Value<double?>("minCloudSizeMm") ?? 150.0);
                double mergeNearbyMm = cloudOpt.Value<double?>("mergeNearbyMm") ?? 300.0;
                int maxClouds = cloudOpt.Value<int?>("maxClouds") ?? 500;

                var annotate = (JObject?)cloudOpt["annotate"];
                bool annotateEnabled = annotate?.Value<bool?>("enabled") ?? false;
                string annotateTemplate = annotate?.Value<string>("template") ?? "${changeType}: ${elementCount} item(s)";

                var output = (JObject?)p["output"] ?? new JObject();
                bool outElemIds = output.Value<bool?>("includeDetectedElementIds") ?? true;
                bool outDetails = output.Value<bool?>("includeChangeDetails") ?? true;
                int outDetailCap = output.Value<int?>("maxDetailItemsPerCloud") ?? 10;

                // ===== Filters: support both {filters:{...}} and {categories:{...}} =====
                var filters = (JObject?)p["filters"];
                var categoriesObj = (JObject?)p["categories"];
                var rulesObj = (JObject?)p["rules"];

                var includeCats = new HashSet<BuiltInCategory>();
                var excludeCats = new HashSet<BuiltInCategory>();
                if (filters != null)
                {
                    if (filters["includeCategories"] is JArray inc1) includeCats.UnionWith(ResolveCategoryNames(doc, inc1.Values<string>()));
                    if (filters["excludeCategories"] is JArray exc1) excludeCats.UnionWith(ResolveCategoryNames(doc, exc1.Values<string>()));
                }
                if (categoriesObj != null)
                {
                    if (categoriesObj["include"] is JArray inc2) includeCats.UnionWith(ResolveCategoryNames(doc, inc2.Values<string>()));
                    if (categoriesObj["exclude"] is JArray exc2) excludeCats.UnionWith(ResolveCategoryNames(doc, exc2.Values<string>()));
                }

                double moveTolMm = 30, bboxTolMm = 20;
                bool checkTypeChange = true;
                var diffParamNames = new List<string>();
                if (rulesObj != null)
                {
                    moveTolMm = rulesObj.Value<double?>("moveThresholdMm") ?? moveTolMm;
                    bboxTolMm = rulesObj.Value<double?>("bboxDeltaMm") ?? bboxTolMm;
                    checkTypeChange = rulesObj.Value<bool?>("checkTypeChange") ?? checkTypeChange;
                    if (rulesObj["paramNames"] is JArray pn) diffParamNames = pn.Values<string>().Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                }

                // ===== Targets: preferred changes[] (external diff) else collect by filters =====
                var changesIn = ReadChangesArray(p["changes"]);
                var targetIds = new HashSet<ElementId>(ReadElementIds(p["elementIds"] ?? filters?["elementIds"]));

                if (changesIn.Count == 0 && targetIds.Count == 0)
                {
                    var col = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElements();
                    foreach (var e in col)
                    {
                        if (e.Category == null) continue;
                        var bic = (BuiltInCategory)e.Category.Id.IntValue();
                        if (excludeCats.Contains(bic)) continue;
                        if (includeCats.Count > 0 && !includeCats.Contains(bic)) continue;
                        targetIds.Add(e.Id);
                    }
                }

                var cloudsOut = new List<object>();
                var skipped = new List<object>();

                // ===== drawMode: none → report only =====
                if (drawMode == "none")
                {
                    var items = (changesIn.Count > 0)
                        ? BuildReportFromChanges(doc, view, changesIn, paddingFt, minFt, outDetails, outDetailCap, outElemIds, skipped)
                        : BuildReportFromIds(doc, view, targetIds, paddingFt, minFt, outDetails, outElemIds, skipped);

                    return new
                    {
                        ok = true,
                        viewId = view.Id.IntValue(),
                        usedRevisionId = revisionId.IntValue(),
                        summary = Summarize(items),
                        clouds = items,
                        skipped,
                        issues = new object[0]
                    };
                }

                // ===== drawMode: each/merge =====
                int createdCount = 0;
                using (var tx = new Transaction(doc, "Auto Revision Clouds"))
                {
                    tx.Start();

                    if (drawMode == "each")
                    {
                        var items = (changesIn.Count > 0)
                            ? BuildReportFromChanges(doc, view, changesIn, paddingFt, minFt, outDetails, outDetailCap, outElemIds, skipped)
                            : BuildReportFromIds(doc, view, targetIds, paddingFt, minFt, outDetails, outElemIds, skipped);

                        foreach (var it in items.Cast<JObject>())
                        {
                            if (createdCount >= maxClouds) break;
                            var rc = TryCreateCloudFromReportItem(doc, view, revisionId, it, annotateEnabled, annotateTemplate);
                            if (rc != null) { createdCount++; cloudsOut.Add(rc); }
                        }
                    }
                    else // merge
                    {
                        var items = (changesIn.Count > 0)
                            ? BuildReportFromChanges(doc, view, changesIn, paddingFt, minFt, outDetails, outDetailCap, outElemIds, skipped)
                            : BuildReportFromIds(doc, view, targetIds, paddingFt, minFt, outDetails, outElemIds, skipped);

                        var rects = items.Select(it => AsJObject(it)["rect"]).Cast<JObject>().ToList();
                        var mergedRects = MergeRectsMm(rects, mergeNearbyMm);

                        foreach (var rect in mergedRects)
                        {
                            if (createdCount >= maxClouds) break;
                            var rc = TryCreateCloudFromRect(doc, view, revisionId, rect, items, annotateEnabled, annotateTemplate);
                            if (rc != null) { createdCount++; cloudsOut.Add(rc); }
                        }
                    }

                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    viewId = view.Id.IntValue(),
                    usedRevisionId = revisionId.IntValue(),
                    summary = Summarize(cloudsOut),
                    clouds = cloudsOut,
                    skipped,
                    issues = new object[0]
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        // ===== Report builders =====
        private static List<object> BuildReportFromIds(Document doc, View view, HashSet<ElementId> ids, double padFt, double minFt, bool outDetails, bool outElemIds, List<object> skipped)
        {
            var items = new List<object>();
            foreach (var id in ids)
            {
                var e = doc.GetElement(id);
                if (e == null) { skipped.Add(new { elementId = id.IntValue(), reason = "not found" }); continue; }

                var bb = e.get_BoundingBox(view) ?? e.get_BoundingBox(null);
                if (bb == null) { skipped.Add(new { elementId = id.IntValue(), reason = "no bbox" }); continue; }

                var rect = RectFromBBoxMm(bb, view, padFt, minFt);
                var label = new JObject
                {
                    ["changeType"] = "target",
                    ["elementCount"] = 1,
                    ["category"] = e.Category?.Name,
                    ["family"] = (e as FamilyInstance)?.Symbol?.Family?.Name,
                };
                items.Add(new JObject
                {
                    ["reason"] = "target",
                    ["rect"] = rect,
                    ["elementIds"] = outElemIds ? new JArray(id.IntValue()) : null,
                    ["changeDetails"] = outDetails ? new JArray() : null,
                    ["_label"] = label
                });
            }
            return items;
        }

        private static List<object> BuildReportFromChanges(Document doc, View view, List<ChangeGroup> changes, double padFt, double minFt, bool outDetails, int detailCap, bool outElemIds, List<object> skipped)
        {
            var items = new List<object>();
            foreach (var g in changes)
            {
                BoundingBoxXYZ merged = null;
                var okIds = new List<int>();

                foreach (var eid in g.ElementIds)
                {
                    var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                    if (e == null) { skipped.Add(new { elementId = eid, reason = "not found (maybe deleted)" }); continue; }
                    var bb = e.get_BoundingBox(view) ?? e.get_BoundingBox(null);
                    if (bb == null) { skipped.Add(new { elementId = eid, reason = "no bbox" }); continue; }
                    okIds.Add(eid);
                    merged = merged == null ? new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max } :
                        new BoundingBoxXYZ
                        {
                            Min = new XYZ(Math.Min(merged.Min.X, bb.Min.X), Math.Min(merged.Min.Y, bb.Min.Y), Math.Min(merged.Min.Z, bb.Min.Z)),
                            Max = new XYZ(Math.Max(merged.Max.X, bb.Max.X), Math.Max(merged.Max.Y, bb.Max.Y), Math.Max(merged.Max.Z, bb.Max.Z))
                        };
                }
                if (merged == null) continue;

                var rect = RectFromBBoxMm(merged, view, padFt, minFt);

                var details = new JArray();
                if (outDetails && g.Details != null)
                    foreach (var d in g.Details.Take(detailCap)) details.Add(JObject.FromObject(d));

                var label = new JObject
                {
                    ["changeType"] = g.ChangeType ?? "changed",
                    ["elementCount"] = okIds.Count,
                    ["category"] = g.Category,
                    ["family"] = g.Family,
                    ["movedDistanceMm"] = g.MovedDistanceMm,
                    ["typeBefore"] = g.TypeBefore,
                    ["typeAfter"] = g.TypeAfter,
                    ["paramChangesSummary"] = g.ParamChangesSummary
                };

                items.Add(new JObject
                {
                    ["reason"] = g.ChangeType ?? "changed",
                    ["rect"] = rect,
                    ["elementIds"] = outElemIds ? new JArray(okIds) : null,
                    ["changeDetails"] = outDetails ? details : null,
                    ["_label"] = label
                });
            }
            return items;
        }

        // ===== Create clouds from report =====
        private static object TryCreateCloudFromReportItem(Document doc, View view, ElementId revisionId, JObject item, bool annotate, string tmpl)
        {
            var rect = AsJObject(item["rect"]);
            var curves = RectToCurvesFt(rect);
            var rc = SafeCreateRevisionCloud(doc, view, revisionId, curves);
            if (rc == null) return null;

            string commentText = null;
            if (annotate)
            {
                commentText = RenderTemplate(tmpl, AsJObject(item["_label"]));
                TrySetComments(rc, commentText);
            }

            return new
            {
                cloudId = rc.Id.IntValue(),
                mode = "each",
                reason = item.Value<string>("reason"),
                rect,
                elementIds = item["elementIds"],
                changeDetails = item["changeDetails"],
                commentApplied = annotate,
                commentText
            };
        }

        private static object TryCreateCloudFromRect(Document doc, View view, ElementId revisionId, JObject rectMm, List<object> sourceItems, bool annotate, string tmpl)
        {
            var curves = RectToCurvesFt(rectMm);
            var rc = SafeCreateRevisionCloud(doc, view, revisionId, curves);
            if (rc == null) return null;

            var elemIds = new List<int>();
            var labels = new List<JObject>();
            foreach (var o in sourceItems.Cast<object>())
            {
                var jo = AsJObject(o);
                var ids = jo["elementIds"] as JArray;
                if (ids != null) foreach (var id in ids) elemIds.Add(id.Value<int>());
                labels.Add(AsJObject(jo["_label"]));
            }

            string commentText = null;
            if (annotate)
            {
                var changeType = labels.Select(l => l.Value<string>("changeType") ?? "changed")
                                       .GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
                var category = labels.Select(l => l.Value<string>("category")).FirstOrDefault(x => !string.IsNullOrEmpty(x));
                var infoAgg = new JObject { ["changeType"] = changeType, ["elementCount"] = elemIds.Count, ["category"] = category };
                commentText = RenderTemplate(tmpl, infoAgg);
                TrySetComments(rc, commentText);
            }

            return new
            {
                cloudId = rc.Id.IntValue(),
                mode = "merge",
                reason = "merged",
                rect = rectMm,
                elementIds = elemIds.Distinct().ToArray(),
                changeDetails = (object)null,
                commentApplied = annotate,
                commentText
            };
        }

        private static void TrySetComments(Element e, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS) ?? e.LookupParameter("Comments");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(text);
            }
            catch { /* ignore */ }
        }

        private static JObject Summarize(IEnumerable<object> cloudItems)
        {
            int added = 0, deleted = 0, moved = 0, typeChanged = 0, paramChanged = 0, total = 0;
            foreach (var o in cloudItems.Cast<object>())
            {
                total++;
                var reason = (AsJObject(o).Value<string>("reason") ?? "").ToLowerInvariant();
                if (reason.Contains("add")) added++;
                else if (reason.Contains("delete")) deleted++;
                else if (reason.Contains("move")) moved++;
                else if (reason.Contains("type")) typeChanged++;
                else if (reason.Contains("param")) paramChanged++;
            }
            return new JObject
            {
                ["added"] = added,
                ["deleted"] = deleted,
                ["moved"] = moved,
                ["typeChanged"] = typeChanged,
                ["paramChanged"] = paramChanged,
                ["totalClouds"] = total
            };
        }

        // ===== Changes DTO & readers =====
        private class ChangeGroup
        {
            public string ChangeType { get; set; }
            public string Category { get; set; }
            public string Family { get; set; }
            public double? MovedDistanceMm { get; set; }
            public string TypeBefore { get; set; }
            public string TypeAfter { get; set; }
            public string ParamChangesSummary { get; set; }
            public List<int> ElementIds { get; set; } = new List<int>();
            public List<ChangeDetail> Details { get; set; } = new List<ChangeDetail>();
        }
        private class ChangeDetail
        {
            public int ElementId { get; set; }
            public string ChangeType { get; set; }
            public double? MovedDistanceMm { get; set; }
            public string TypeBefore { get; set; }
            public string TypeAfter { get; set; }
            public string ParamName { get; set; }
            public string ParamBefore { get; set; }
            public string ParamAfter { get; set; }
        }
        private static List<ChangeGroup> ReadChangesArray(JToken token)
        {
            var list = new List<ChangeGroup>();
            if (token is JArray arr)
            {
                foreach (var jt in arr.OfType<JObject>())
                {
                    var cg = new ChangeGroup
                    {
                        ChangeType = jt.Value<string>("changeType"),
                        Category = jt.Value<string>("category"),
                        Family = jt.Value<string>("family"),
                        MovedDistanceMm = jt.Value<double?>("movedDistanceMm"),
                        TypeBefore = jt.Value<string>("typeBefore"),
                        TypeAfter = jt.Value<string>("typeAfter"),
                        ParamChangesSummary = jt.Value<string>("paramChangesSummary"),
                        ElementIds = jt["elementIds"] is JArray earr ? earr.Values<int>().ToList() : new List<int>(),
                        Details = ReadDetailArray(jt["details"])
                    };
                    list.Add(cg);
                }
            }
            return list;
        }
        private static List<ChangeDetail> ReadDetailArray(JToken token)
        {
            var list = new List<ChangeDetail>();
            if (token is JArray arr)
            {
                foreach (var jt in arr.OfType<JObject>())
                {
                    list.Add(new ChangeDetail
                    {
                        ElementId = jt.Value<int?>("elementId") ?? 0,
                        ChangeType = jt.Value<string>("changeType"),
                        MovedDistanceMm = jt.Value<double?>("movedDistanceMm"),
                        TypeBefore = jt.Value<string>("typeBefore"),
                        TypeAfter = jt.Value<string>("typeAfter"),
                        ParamName = jt.Value<string>("paramName"),
                        ParamBefore = jt["before"]?.ToString(),
                        ParamAfter = jt["after"]?.ToString()
                    });
                }
            }
            return list;
        }
        private static IList<ElementId> ReadElementIds(JToken token)
        {
            var list = new List<ElementId>();
            if (token is JArray arr) foreach (var t in arr) list.Add(Autodesk.Revit.DB.ElementIdCompat.From(t.Value<int>()));
            return list;
        }
    }
}


