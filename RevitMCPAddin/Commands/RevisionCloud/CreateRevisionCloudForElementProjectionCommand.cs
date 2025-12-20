// ================================================================
// File: RevitMCPAddin/Commands/RevisionCloud/CreateRevisionCloudForElementProjectionCommand.cs
// Target: .NET Framework 4.8 / Revit 2023
// Purpose:
//   指定要素をビュー平面へ投影し、AABB もしくは OBB の矩形を
//   「必ず時計回り(CW)」で作図して Autodesk.Revit.DB.RevisionCloud を生成する。
// JSON 例:
//   {
//     "viewId": 401,
//     "elementId": 123,              // or "uniqueId"
//     "revisionId": 0,               // 省略/0 なら新規 Revision を作成
//     "mode": "obb",                 // "obb"(既定) | "aabb"
//     "widthMm": 120,                // OBB 幅（省略時は minRectSizeMm を幅に流用）
//     "paddingMm": 80,               // 長手/短手の両方向に余白付与
//     "minRectSizeMm": 40,           // 最小寸法（AABBにも使用）
//     "planeSource": "view",         // "view"(既定) | "sketch"
//     "preZoom": "fit|element|",     // 任意：作図前のズーム
//     "restoreZoom": true,           // 任意：作図後にズーム復帰
//     "focusMarginMm": 150,          // 任意：elementズーム時の余白
//     "debug": true
//   }
// Return:
//   { ok, cloudId, usedRevisionId, rect:{min:{x,y,z},max:{x,y,z}}, diag? }
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

namespace RevitMCPAddin.Commands.RevisionCloud
{
    public class CreateRevisionCloudForElementProjectionCommand : IRevitCommandHandler
    {
        public string CommandName => "create_revision_cloud_for_element_projection";

        // ---- Units/Helpers ----
        private static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        private static JObject Jpt(XYZ p) => new JObject { ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z };
        private static JObject Jvec(XYZ v) => new JObject { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z };

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            bool debug = p.Value<bool?>("debug") ?? false;
            var diag = debug ? new JObject() : null;

            try
            {
                // 1) View
                View view = null;
                if (p.TryGetValue("viewId", out var vTok))
                {
                    view = doc.GetElement(new ElementId(vTok.Value<int>())) as View;
                    if (view == null) return Fail("View not found.");
                }
                else
                {
                    view = uidoc.ActiveView ?? throw new InvalidOperationException("Active view is not available.");
                }

                // オプション：ズーム挙動
                string preZoom = (p.Value<string>("preZoom") ?? string.Empty).Trim().ToLowerInvariant(); // "fit" | "element" | ""
                bool restoreZoom = p.Value<bool?>("restoreZoom") ?? false;
                double focusMarginFt = MmToFt(p.Value<double?>("focusMarginMm") ?? 150.0);

                // (BATCH) elementIds：複数要素に雲を作図
                if (p.TryGetValue("elementIds", out var idsTok) && idsTok is JArray arrIds && arrIds.Count > 0)
                {
                    var sp0 = view.SketchPlane;
                    if (sp0 == null) return Fail("Target view has no SketchPlane (use Plan/Elevation/Section/Drafting view).");

                    // 既存/新規 Revision
                    ElementId revisionId0;
                    if (p.TryGetValue("revisionId", out var rTok0))
                    {
                        revisionId0 = new ElementId(rTok0.Value<int>());
                        if (doc.GetElement(revisionId0) as Autodesk.Revit.DB.Revision == null)
                            return Fail($"Revision not found: {revisionId0.IntegerValue}");
                    }
                    else
                    {
                        using (var txR = new Transaction(doc, "Create Default Revision"))
                        {
                            txR.Start();
                            Autodesk.Revit.DB.Revision rev = Autodesk.Revit.DB.Revision.Create(doc);
                            revisionId0 = rev.Id;
                            txR.Commit();
                        }
                    }

                    var created = new List<int>();
                    var failures = new List<object>();

                    // 事前に UI 視図を取得（必要ならアクティブ化）
                    UIView uiView = null;
                    try
                    {
                        if (uidoc.ActiveView?.Id != view.Id) uidoc.ActiveView = view;
                        uiView = uidoc.GetOpenUIViews()?.FirstOrDefault(v => v.ViewId == view.Id)
                                 ?? uidoc.GetOpenUIViews()?.FirstOrDefault();
                        if (preZoom == "fit" && uiView != null) uiView.ZoomToFit();
                    }
                    catch { /* ignore zoom errors */ }

                    foreach (var tId in arrIds)
                    {
                        try
                        {
                            int eid = tId.Value<int>();
                            if (preZoom == "element" && uiView != null)
                            {
                                try
                                {
                                    var el = doc.GetElement(new ElementId(eid));
                                    var bb = el?.get_BoundingBox(view);
                                    if (bb != null)
                                    {
                                        var min = new XYZ(bb.Min.X - focusMarginFt, bb.Min.Y - focusMarginFt, bb.Min.Z);
                                        var max = new XYZ(bb.Max.X + focusMarginFt, bb.Max.Y + focusMarginFt, bb.Max.Z);
                                        uiView.ZoomAndCenterRectangle(min, max);
                                    }
                                }
                                catch { }
                            }
                            int rcid = CreateCloudForSingleElement(doc, view, p, revisionId0, eid);
                            if (rcid > 0) created.Add(rcid);
                            else failures.Add(new { elementId = eid, error = "create failed" });
                        }
                        catch (Exception exEach)
                        {
                            failures.Add(new { elementId = (int?)tId, error = Unwrap(exEach) });
                        }
                    }

                    return new { ok = created.Count > 0, count = created.Count, cloudIds = created, usedRevisionId = revisionId0.IntegerValue, failures };
                }

                // 2) Element
                Element elem = null;
                if (p.TryGetValue("elementId", out var eTok))
                    elem = doc.GetElement(new ElementId(eTok.Value<int>()));
                else if (p.TryGetValue("uniqueId", out var uTok))
                    elem = doc.GetElement(uTok.Value<string>());
                if (elem == null) return Fail("Target element not found (elementId or uniqueId required).");

                // 3) SketchPlane 必須
                var sp = view.SketchPlane;
                if (sp == null) return Fail("Target view has no SketchPlane (use Plan/Elevation/Section/Drafting view).");

                // 4) ビュー/スケッチ座標系
                string planeSource = (p.Value<string>("planeSource") ?? "view").Trim().ToLowerInvariant();
                XYZ origin, ux, uy, uz;
                if (planeSource == "sketch")
                {
                    var pl = sp.GetPlane();
                    origin = pl.Origin;
                    ux = pl.XVec.Normalize(); uy = pl.YVec.Normalize(); uz = pl.Normal.Normalize();
                }
                else
                {
                    origin = view.Origin;
                    ux = view.RightDirection.Normalize(); uy = view.UpDirection.Normalize(); uz = view.ViewDirection.Normalize();
                    planeSource = "view";
                }

                if (debug)
                {
                    var pl = sp.GetPlane();
                    diag["view"] = new JObject
                    {
                        ["id"] = view.Id.IntegerValue,
                        ["name"] = view.Name,
                        ["origin"] = Jpt(view.Origin),
                        ["right"] = Jvec(view.RightDirection),
                        ["up"] = Jvec(view.UpDirection),
                        ["dir"] = Jvec(view.ViewDirection)
                    };
                    diag["plane"] = new JObject
                    {
                        ["origin"] = Jpt(pl.Origin),
                        ["xvec"] = Jvec(pl.XVec),
                        ["yvec"] = Jvec(pl.YVec),
                        ["normal"] = Jvec(pl.Normal)
                    };
                    diag["planeSource"] = planeSource;
                }

                // 5) Options (View 指定)
                var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, View = view };

                // 6) 投影点（UV ft）収集
                var uvPts = new List<UV>();
                UV au = default, bu = default;   // LocationCurve 端点
                bool hasLC = false;

                if (elem.Location is LocationCurve lc && lc.Curve != null)
                {
                    var c = lc.Curve; var a = c.GetEndPoint(0); var b = c.GetEndPoint(1);
                    au = ProjectToUV(a, origin, ux, uy);
                    bu = ProjectToUV(b, origin, ux, uy);
                    uvPts.Add(au); uvPts.Add(bu);
                    hasLC = true;
                }
                else
                {
                    var geo = elem.get_Geometry(opts);
                    if (geo != null)
                    {
                        foreach (var g in geo)
                        {
                            if (g is GeometryInstance gi)
                            {
                                var instT = gi.Transform;
                                foreach (var sub in gi.GetInstanceGeometry())
                                    CollectUVFromGeom(sub, instT, origin, ux, uy, uvPts);
                            }
                            else
                                CollectUVFromGeom(g, Transform.Identity, origin, ux, uy, uvPts);
                        }
                    }
                }

                if (uvPts.Count == 0)
                {
                    // IndependentTag の特別扱い
                    if (elem is IndependentTag tag)
                    {
                        var bb = elem.get_BoundingBox(view);
                        if (bb != null)
                        {
                            var corners = new[]
                            {
                                new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                                new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                                new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                                new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)
                            };
                            foreach (var c in corners) uvPts.Add(ProjectToUV(c, origin, ux, uy));
                        }
                        else
                        {
                            // 明示サイズの受け入れ
                            double tagWft = MmToFt(p.Value<double?>("tagWidthMm") ?? 0.0);
                            double tagHft = MmToFt(p.Value<double?>("tagHeightMm") ?? 0.0);
                            if (tagWft > 0 && tagHft > 0)
                            {
                                var c = tag.TagHeadPosition; // ft
                                var cuv = ProjectToUV(c, origin, ux, uy);
                                double halfW = 0.5 * tagWft, halfH = 0.5 * tagHft;
                                uvPts.Add(new UV(cuv.U - halfW, cuv.V - halfH));
                                uvPts.Add(new UV(cuv.U + halfW, cuv.V - halfH));
                                uvPts.Add(new UV(cuv.U + halfW, cuv.V + halfH));
                                uvPts.Add(new UV(cuv.U - halfW, cuv.V + halfH));
                            }
                            else
                            {
                                return Fail("No projectable geometry for tag; provide tagWidthMm/tagHeightMm or ensure the view provides a valid bounding box.");
                            }
                        }
                    }
                    else
                    {
                        return Fail("No projectable geometry found for the element in this view.");
                    }
                }
                if (debug) diag["uvPointCount"] = uvPts.Count;

                // 7) 生成モード
                string mode = (p.Value<string>("mode") ?? "obb").Trim().ToLowerInvariant();  // "obb" | "aabb"
                double paddingFt = MmToFt(p.Value<double?>("paddingMm") ?? 100.0);
                double minRectFt = MmToFt(p.Value<double?>("minRectSizeMm") ?? 50.0);
                double widthFtUser = MmToFt(p.Value<double?>("widthMm") ?? 0.0);

                // 8) UV 矩形（AABB or OBB）
                List<UV> rectUV;
                if (mode == "aabb" || !hasLC)
                {
                    double minX = uvPts.Min(q => q.U), minY = uvPts.Min(q => q.V);
                    double maxX = uvPts.Max(q => q.U), maxY = uvPts.Max(q => q.V);
                    double w = maxX - minX, h = maxY - minY;
                    if (w < minRectFt) { double d = 0.5 * (minRectFt - w); minX -= d; maxX += d; }
                    if (h < minRectFt) { double d = 0.5 * (minRectFt - h); minY -= d; maxY += d; }
                    minX -= paddingFt; minY -= paddingFt; maxX += paddingFt; maxY += paddingFt;
                    const double eps = 1e-8;
                    if (Math.Abs(maxX - minX) < eps) maxX = minX + eps;
                    if (Math.Abs(maxY - minY) < eps) maxY = minY + eps;
                    rectUV = new List<UV> { new UV(minX, minY), new UV(maxX, minY), new UV(maxX, maxY), new UV(minX, maxY) };
                    if (debug) diag["rectMode"] = "aabb";
                }
                else
                {
                    // OBB: LocationCurve の方向に沿った回転矩形
                    var du = new XYZ(bu.U - au.U, bu.V - au.V, 0.0);
                    if (du.GetLength() < 1e-9) return Fail("Element direction is degenerate in this view.");

                    var udir = du.Normalize();                // 長手
                    var vdir = new XYZ(-udir.Y, udir.X, 0.0); // 短手（左90°）

                    var midU = 0.5 * (au.U + bu.U);
                    var midV = 0.5 * (au.V + bu.V);

                    double halfLen = 0.5 * du.GetLength() + paddingFt;
                    double halfWid = Math.Max(widthFtUser > 0 ? 0.5 * widthFtUser : 0.5 * minRectFt, MmToFt(5)); // 最低幅5mm

                    var c0 = new UV(midU - (udir.X * halfLen + vdir.X * halfWid), midV - (udir.Y * halfLen + vdir.Y * halfWid));
                    var c1 = new UV(midU + (udir.X * halfLen - vdir.X * halfWid), midV + (udir.Y * halfLen - vdir.Y * halfWid));
                    var c2 = new UV(midU + (udir.X * halfLen + vdir.X * halfWid), midV + (udir.Y * halfLen + vdir.Y * halfWid));
                    var c3 = new UV(midU - (udir.X * halfLen - vdir.X * halfWid), midV - (udir.Y * halfLen - vdir.Y * halfWid));
                    rectUV = new List<UV> { c0, c1, c2, c3 };

                    if (debug)
                    {
                        diag["rectMode"] = "obb";
                        diag["obb"] = new JObject
                        {
                            ["halfLenFt"] = halfLen,
                            ["halfWidFt"] = halfWid,
                            ["uDir"] = new JObject { ["x"] = udir.X, ["y"] = udir.Y },
                            ["vDir"] = new JObject { ["x"] = vdir.X, ["y"] = vdir.Y },
                            ["endpointsUV"] = new JArray {
                                new JObject{["u"]=au.U,["v"]=au.V},
                                new JObject{["u"]=bu.U,["v"]=bu.V}
                            }
                        };
                    }
                }

                // 9) 「描画平面で必ず CW」→ World の Line 群
                var curves = BuildCloudLoopCW(rectUV, origin, ux, uy, uz, sp);

                // 10) Autodesk.Revit.DB.Revision の確保
                ElementId revisionId;
                if (p.TryGetValue("revisionId", out var rTok))
                {
                    revisionId = new ElementId(rTok.Value<int>());
                    if (doc.GetElement(revisionId) as Autodesk.Revit.DB.Revision == null)
                        return Fail($"Revision not found: {revisionId.IntegerValue}");
                }
                else
                {
                    using (var txR = new Transaction(doc, "Create Default Revision"))
                    {
                        txR.Start();
                        Autodesk.Revit.DB.Revision rev = Autodesk.Revit.DB.Revision.Create(doc);
                        revisionId = rev.Id;
                        txR.Commit();
                    }
                }

                // 11) 必要ならズーム調整（元ズーム退避/復帰）
                UIView uiViewSingle = null; XYZ zoomA = null, zoomB = null;
                if (!string.IsNullOrEmpty(preZoom))
                {
                    try
                    {
                        if (uidoc.ActiveView?.Id != view.Id) uidoc.ActiveView = view;
                        uiViewSingle = uidoc.GetOpenUIViews()?.FirstOrDefault(v => v.ViewId == view.Id)
                                       ?? uidoc.GetOpenUIViews()?.FirstOrDefault();
                        if (uiViewSingle != null)
                        {
                            if (restoreZoom)
                            {
                                try { if (TryGetZoomCorners(uiViewSingle, out var a, out var b)) { zoomA = a; zoomB = b; } } catch { zoomA = null; zoomB = null; }
                            }
                            if (preZoom == "fit") uiViewSingle.ZoomToFit();
                            else if (preZoom == "element")
                            {
                                var bb = elem.get_BoundingBox(view);
                                if (bb != null)
                                {
                                    var min = new XYZ(bb.Min.X - focusMarginFt, bb.Min.Y - focusMarginFt, bb.Min.Z);
                                    var max = new XYZ(bb.Max.X + focusMarginFt, bb.Max.Y + focusMarginFt, bb.Max.Z);
                                    uiViewSingle.ZoomAndCenterRectangle(min, max);
                                }
                            }
                        }
                    }
                    catch { /* ignore */ }
                }

                // 12) 作図
                Autodesk.Revit.DB.RevisionCloud rc;
                using (var tx = new Transaction(doc, "Create Revision Cloud (Projection)"))
                {
                    tx.Start();
                    rc = SafeCreateRevisionCloud(doc, view, revisionId, curves);
                    tx.Commit();
                }
                if (rc == null) return Fail("RevisionCloud.Create failed (no suitable overload).");

                // 13) ズーム復帰
                if (uiViewSingle != null && restoreZoom && zoomA != null && zoomB != null)
                {
                    try { uiViewSingle.ZoomAndCenterRectangle(zoomA, zoomB); } catch { }
                }

                // AABB を mm で返す（参考）
                double rminx = rectUV.Min(q => q.U), rminy = rectUV.Min(q => q.V);
                double rmaxx = rectUV.Max(q => q.U), rmaxy = rectUV.Max(q => q.V);
                var rect = new
                {
                    min = new { x = Math.Round(FtToMm(rminx), 3), y = Math.Round(FtToMm(rminy), 3), z = 0.0 },
                    max = new { x = Math.Round(FtToMm(rmaxx), 3), y = Math.Round(FtToMm(rmaxy), 3), z = 0.0 }
                };

                if (debug) diag["worldRect"] = new JArray(curves.Select(c => new JObject
                {
                    ["start"] = Jpt(c.GetEndPoint(0)),
                    ["end"] = Jpt(c.GetEndPoint(1))
                }));

                return new
                {
                    ok = true,
                    cloudId = rc.Id.IntegerValue,
                    usedRevisionId = revisionId.IntegerValue,
                    rect,
                    diag = debug ? diag : null
                };
            }
            catch (TargetInvocationException tie)
            {
                return Fail($"Invocation: {Unwrap(tie)}");
            }
            catch (Exception ex)
            {
                return Fail(Unwrap(ex));
            }
        }

        // ===== 投影・収集 =====
        private static UV ProjectToUV(XYZ p, XYZ origin, XYZ ux, XYZ uy)
        {
            var v = p - origin;
            return new UV(v.DotProduct(ux), v.DotProduct(uy)); // ft
        }
        private static void CollectUVFromGeom(GeometryObject g, Transform instT, XYZ origin, XYZ ux, XYZ uy, List<UV> acc)
        {
            switch (g)
            {
                case Solid s when s.Faces.Size > 0:
                    foreach (Face f in s.Faces)
                        foreach (EdgeArray ea in f.EdgeLoops)
                            foreach (Edge edge in ea)
                                foreach (var wp in edge.AsCurve().Tessellate())
                                    acc.Add(ProjectToUV(instT.OfPoint(wp), origin, ux, uy));
                    break;
                case Curve c:
                    foreach (var wp in c.Tessellate())
                        acc.Add(ProjectToUV(instT.OfPoint(wp), origin, ux, uy));
                    break;
                case Mesh m:
                    for (int i = 0; i < m.Vertices.Count; i++)
                    {
                        var v = m.Vertices[i];
                        acc.Add(ProjectToUV(instT.OfPoint(new XYZ(v.X, v.Y, v.Z)), origin, ux, uy));
                    }
                    break;
            }
        }

        // ===== UV ループ符号付き面積（+ = CCW）=====
        private static double SignedAreaUV(IList<UV> uv)
        {
            double a2 = 0;
            for (int i = 0; i < uv.Count; i++)
            {
                var a = uv[i]; var b = uv[(i + 1) % uv.Count];
                a2 += (a.U * b.V - b.U * a.V);
            }
            return 0.5 * a2;
        }

        // ===== 「平面で必ず CW」のワールド曲線作成 =====
        private static IList<Curve> BuildCloudLoopCW(
            IList<UV> rectUV, XYZ origin, XYZ ux, XYZ uy, XYZ uz, SketchPlane sp)
        {
            if (rectUV == null || rectUV.Count < 4)
                throw new ArgumentException("rectUV must have 4 points.");

            // UV の面積が + なら CCW → CW に反転
            if (SignedAreaUV(rectUV) > 0)
            {
                var rev = new List<UV>(rectUV);
                rev.Reverse();
                rectUV = rev;
            }

            // UV -> World （描画平面へ復元）
            var pts = rectUV.Select(q => origin + q.U * ux + q.V * uy).ToArray();

            // 退化回避：隣接一致は法線方向に微小にずらす
            for (int i = 0; i < pts.Length; i++)
            {
                int j = (i + 1) % pts.Length;
                if (pts[i].IsAlmostEqualTo(pts[j]))
                    pts[j] = pts[j] + uz.Multiply(1e-6);
            }

            // 描画平面に正射影（安全側）
            var pl = sp.GetPlane();
            for (int i = 0; i < pts.Length; i++)
            {
                var d = (pts[i] - pl.Origin).DotProduct(pl.Normal);
                if (Math.Abs(d) > 1e-7)
                    pts[i] = pts[i] - pl.Normal.Multiply(d);
            }

            return new List<Curve>
            {
                Line.CreateBound(pts[0], pts[1]),
                Line.CreateBound(pts[1], pts[2]),
                Line.CreateBound(pts[2], pts[3]),
                Line.CreateBound(pts[3], pts[0]),
            };
        }

        // ===== Revit 作図（オーバーロード吸収 & 内側例外露出）=====
        private static Autodesk.Revit.DB.RevisionCloud SafeCreateRevisionCloud(
            Document doc, View view, ElementId revisionId, IList<Curve> curves)
        {
            var t = typeof(Autodesk.Revit.DB.RevisionCloud);
            try
            {
                var m1 = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(ElementId), typeof(IList<Curve>) });
                if (m1 != null) return (Autodesk.Revit.DB.RevisionCloud)m1.Invoke(null, new object[] { doc, view, revisionId, curves });

                var m2 = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<Curve>), typeof(ElementId) });
                if (m2 != null) return (Autodesk.Revit.DB.RevisionCloud)m2.Invoke(null, new object[] { doc, view, curves, revisionId });

                var m3 = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<CurveLoop>), typeof(ElementId) });
                if (m3 != null)
                {
                    var loop = new CurveLoop(); foreach (var c in curves) loop.Append(c);
                    return (Autodesk.Revit.DB.RevisionCloud)m3.Invoke(null, new object[] { doc, view, new List<CurveLoop> { loop }, revisionId });
                }

                var m4 = t.GetMethod("Create", new[] { typeof(Document), typeof(View), typeof(IList<Curve>) });
                if (m4 != null)
                {
                    var rc = (Autodesk.Revit.DB.RevisionCloud)m4.Invoke(null, new object[] { doc, view, curves });
                    try
                    {
                        var par = rc?.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION);
                        if (par != null && !par.IsReadOnly) par.Set(revisionId);
                    }
                    catch { /* ignore */ }
                    return rc;
                }

                return null;
            }
            catch (TargetInvocationException tie)
            {
                throw new InvalidOperationException(Unwrap(tie));
            }
        }

        // ===== 単要素作図（バッチでも使用）=====
        private int CreateCloudForSingleElement(Document doc, View view, JObject p, ElementId revisionId, int elementId)
        {
            var sp = view.SketchPlane; if (sp == null) return 0;

            // ビュー/スケッチ座標系
            string planeSource = (p.Value<string>("planeSource") ?? "view").Trim().ToLowerInvariant();
            XYZ origin, ux, uy, uz;
            if (planeSource == "sketch")
            {
                var pl = sp.GetPlane();
                origin = pl.Origin; ux = pl.XVec.Normalize(); uy = pl.YVec.Normalize(); uz = pl.Normal.Normalize();
            }
            else
            {
                origin = view.Origin; ux = view.RightDirection.Normalize(); uy = view.UpDirection.Normalize(); uz = view.ViewDirection.Normalize();
            }

            var opts = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, View = view };
            var elem = doc.GetElement(new ElementId(elementId)); if (elem == null) return 0;

            var uvPts = new List<UV>(); UV au = default, bu = default; bool hasLC = false;
            if (elem.Location is LocationCurve lc && lc.Curve != null)
            {
                var c = lc.Curve; var a = c.GetEndPoint(0); var b = c.GetEndPoint(1);
                au = ProjectToUV(a, origin, ux, uy); bu = ProjectToUV(b, origin, ux, uy);
                uvPts.Add(au); uvPts.Add(bu); hasLC = true;
            }
            else
            {
                var geo = elem.get_Geometry(opts);
                if (geo != null)
                {
                    foreach (var g in geo)
                    {
                        if (g is GeometryInstance gi)
                        {
                            var instT = gi.Transform;
                            foreach (var sub in gi.GetInstanceGeometry()) CollectUVFromGeom(sub, instT, origin, ux, uy, uvPts);
                        }
                        else CollectUVFromGeom(g, Transform.Identity, origin, ux, uy, uvPts);
                    }
                }
            }
            if (uvPts.Count == 0)
            {
                if (elem is IndependentTag tag)
                {
                    var bb = elem.get_BoundingBox(view);
                    if (bb != null)
                    {
                        var corners = new[] {
                            new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                            new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                            new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                            new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z) };
                        foreach (var c in corners) uvPts.Add(ProjectToUV(c, origin, ux, uy));
                    }
                }
            }
            if (uvPts.Count == 0) return 0;

            string mode = (p.Value<string>("mode") ?? "obb").Trim().ToLowerInvariant();
            double paddingFt = MmToFt(p.Value<double?>("paddingMm") ?? 100.0);
            double minRectFt = MmToFt(p.Value<double?>("minRectSizeMm") ?? 50.0);
            double widthFtUser = MmToFt(p.Value<double?>("widthMm") ?? 0.0);

            List<UV> rectUV;
            if (mode == "aabb" || !hasLC)
            {
                double minX = uvPts.Min(q => q.U), minY = uvPts.Min(q => q.V);
                double maxX = uvPts.Max(q => q.U), maxY = uvPts.Max(q => q.V);
                double w = maxX - minX, h = maxY - minY;
                if (w < minRectFt) { double d = 0.5 * (minRectFt - w); minX -= d; maxX += d; }
                if (h < minRectFt) { double d = 0.5 * (minRectFt - h); minY -= d; maxY += d; }
                minX -= paddingFt; minY -= paddingFt; maxX += paddingFt; maxY += paddingFt;
                const double eps = 1e-8;
                if (Math.Abs(maxX - minX) < eps) maxX = minX + eps;
                if (Math.Abs(maxY - minY) < eps) maxY = minY + eps;
                rectUV = new List<UV> { new UV(minX, minY), new UV(maxX, minY), new UV(maxX, maxY), new UV(minX, maxY) };
            }
            else
            {
                var du = new XYZ(bu.U - au.U, bu.V - au.V, 0.0); if (du.GetLength() < 1e-9) return 0;
                var udir = du.Normalize(); var vdir = new XYZ(-udir.Y, udir.X, 0.0);
                var midU = 0.5 * (au.U + bu.U); var midV = 0.5 * (au.V + bu.V);
                double halfLen = 0.5 * du.GetLength() + paddingFt;
                double halfWid = Math.Max(widthFtUser > 0 ? 0.5 * widthFtUser : 0.5 * minRectFt, MmToFt(5));
                var c0 = new UV(midU - (udir.X * halfLen + vdir.X * halfWid), midV - (udir.Y * halfLen + vdir.Y * halfWid));
                var c1 = new UV(midU + (udir.X * halfLen - vdir.X * halfWid), midV + (udir.Y * halfLen - vdir.Y * halfWid));
                var c2 = new UV(midU + (udir.X * halfLen + vdir.X * halfWid), midV + (udir.Y * halfLen + vdir.Y * halfWid));
                var c3 = new UV(midU - (udir.X * halfLen - vdir.X * halfWid), midV - (udir.Y * halfLen - vdir.Y * halfWid));
                rectUV = new List<UV> { c0, c1, c2, c3 };
            }

            var curves = BuildCloudLoopCW(rectUV, origin, ux, uy, uz, sp);
            Autodesk.Revit.DB.RevisionCloud rc;
            using (var tx = new Transaction(doc, "Create Revision Cloud (Batch Item)"))
            {
                tx.Start();
                rc = SafeCreateRevisionCloud(doc, view, revisionId, curves);
                tx.Commit();
            }
            return rc?.Id?.IntegerValue ?? 0;
        }

        // ===== UIView の現在ズーム矩形を取得（失敗すれば false）=====
        private static bool TryGetZoomCorners(UIView uiView, out XYZ cornerA, out XYZ cornerB)
        {
            cornerA = null; cornerB = null;
            if (uiView == null) return false;
            try
            {
                // ← Revit 2023 では IList<XYZ> を返す
                var pts = uiView.GetZoomCorners();
                if (pts != null && pts.Count >= 2 && pts[0] != null && pts[1] != null)
                {
                    cornerA = pts[0];
                    cornerB = pts[1];
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        // ===== 例外の展開 / エラーレスポンス =====
        private static string Unwrap(Exception ex)
        {
            var list = new List<string>();
            for (var e = ex; e != null; e = e.InnerException) list.Add(e.Message);
            return string.Join(" | ", list);
        }

        private static object Fail(string msg) => new { ok = false, msg };
    }
}
