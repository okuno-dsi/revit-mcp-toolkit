// ================================================================
// File: Commands/ElementOps/GetElementInfoHandler.cs
// Purpose : elementId/uniqueId系から要素情報を取得（mm/ft両立）
// Changes :
//  - 単位変換を UnitHelper に統一（FtToMm, XyzToMm など）
//  - coordinates(ft) は後方互換で維持し、coordinatesMm(mm) を追加
//  - bboxMm / 各種オフセット(mm) を UnitHelper で算出
//  - (NEW) includeVisual/viewId を受け取り、visualOverride フラグを付与
// Target  : .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RevitMCPAddin.Commands.ElementOps
{
    /// <summary>
    /// elementId/elementIds または uniqueId/uniqueIds から
    /// カテゴリ・ファミリ名・タイプ名・レベル・座標（既存ft + 追加mm）を返す。
    /// "rich": true の場合は以下を追加:
    ///  - identity: uniqueId, className, categoryId, typeId, levelId
    ///  - location: locationKind(point/curve/none), curveType, bboxMm
    ///  - flags: isPinned, isMirrored (FamilyInstance), viewSpecific, ownerViewId
    ///  - group: isInGroup, groupId, groupName
    ///  - link: isLinkInstance, linkTypeId, linkDocTitle
    ///  - host: hostId, hostCategory
    ///  - phase: phaseCreated, phaseDemolished
    ///  - workset/designOption: worksetId, worksetName, designOptionId, designOptionName
    ///  - constraints: 壁/柱用の拘束情報（レベル・オフセット・高さ ※mm）
    ///  - nextActions: 推奨MCPコマンドのヒント配列（文字列）
    /// 既存の feet 座標は後方互換で維持、mm併記。
    ///
    /// (NEW)
    ///  - params: includeVisual?:bool, viewId?:int
    ///  - output: visualOverride: boolean | null  ※ includeVisual==true の時のみ付与
    /// </summary>
    public class GetElementInfoHandler : IRevitCommandHandler
    {
        public string CommandName => "get_element_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (JObject)cmd.Params;
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };

                bool rich = p.Value<bool?>("rich") ?? false;
                bool includeGeometry = p.Value<bool?>("includeGeometry") ?? false;

                // (NEW) 追加パラメータ
                bool includeVisual = p.Value<bool?>("includeVisual") ?? false;
                int? visualViewId = p.Value<int?>("viewId");
                View visualView = null;
                if (includeVisual)
                {
                    visualView = ResolveViewForVisual(doc, visualViewId);
                }

                // 入力解決
                var ids = new List<int>();
                // elementIds / elementId
                if (p.TryGetValue("elementIds", out var arrIdsToken))
                {
                    var arrIds = arrIdsToken.ToObject<int[]>();
                    if (arrIds != null && arrIds.Length > 0) ids.AddRange(arrIds);
                }
                else if (p.TryGetValue("elementId", out var singleIdToken))
                {
                    ids.Add(singleIdToken.Value<int>());
                }
                // uniqueIds / uniqueId
                if (p.TryGetValue("uniqueIds", out var arrUidsToken))
                {
                    var arrUids = arrUidsToken.ToObject<string[]>();
                    if (arrUids != null)
                    {
                        foreach (var uid in arrUids.Where(s => !string.IsNullOrWhiteSpace(s)))
                        {
                            var e = doc.GetElement(uid);
                            if (e != null) ids.Add(e.Id.IntValue());
                        }
                    }
                }
                else if (p.TryGetValue("uniqueId", out var singleUidToken))
                {
                    var uid = singleUidToken.Value<string>();
                    if (!string.IsNullOrWhiteSpace(uid))
                    {
                        var e = doc.GetElement(uid);
                        if (e != null) ids.Add(e.Id.IntValue());
                    }
                }

                if (ids.Count == 0)
                    return new { ok = false, msg = "elementId/elementIds もしくは uniqueId/uniqueIds を指定してください" };

                ids = ids.Distinct().ToList();

                var result = new JArray();

                foreach (int id in ids)
                {
                    var elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id));
                    if (elem == null) continue;

                    // 既存（後方互換）
                    string category = elem.Category?.Name ?? "";

                    string familyName, typeName;
                    int? typeId = null;

                    if (elem is FamilyInstance fi)
                    {
                        var sym = fi.Symbol;
                        familyName = sym?.Family?.Name ?? "";
                        typeName = sym?.Name ?? "";
                        if (sym != null) typeId = sym.Id.IntValue();
                    }
                    else
                    {
                        familyName = elem.GetType().Name;
                        typeName = elem.Name ?? "";

                        var et = doc.GetElement(elem.GetTypeId()) as ElementType;
                        if (et != null) typeId = et.Id.IntValue();
                    }

                    string levelName = "";
                    int? levelId = null;

                    if (elem is FamilyInstance fi2 && fi2.LevelId != ElementId.InvalidElementId)
                    {
                        levelId = fi2.LevelId.IntValue();
                        var lvl = doc.GetElement(fi2.LevelId) as Level;
                        levelName = lvl?.Name ?? "";
                    }
                    else
                    {
                        var lp = elem.LookupParameter("Level") ?? elem.LookupParameter("Reference Level");
                        if (lp != null)
                            levelName = lp.AsValueString() ?? lp.AsString() ?? "";
                    }

                    // 位置（ft → mm も併記）
                    double x = 0, y = 0, z = 0;
                    string locationKind = "none";
                    string curveType = null;

                    if (elem.Location is LocationPoint lpnt && lpnt.Point != null)
                    {
                        x = lpnt.Point.X; y = lpnt.Point.Y; z = lpnt.Point.Z;
                        locationKind = "point";
                    }
                    else if (elem.Location is LocationCurve lcrv && lcrv.Curve != null)
                    {
                        var st = lcrv.Curve.GetEndPoint(0);
                        x = st.X; y = st.Y; z = st.Z;
                        locationKind = "curve";
                        curveType = lcrv.Curve.GetType().Name;
                    }

                    var info = new JObject
                    {
                        ["elementId"] = id,
                        ["category"] = category,
                        ["familyName"] = familyName,
                        ["typeName"] = typeName,
                        ["level"] = levelName,
                        // 旧互換: ft のまま
                        ["coordinates"] = new JObject
                        {
                            ["x"] = x,
                            ["y"] = y,
                            ["z"] = z
                        },
                        // 追加: mm 併記（UnitHelper）
                        ["coordinatesMm"] = new JObject
                        {
                            ["x"] = Math.Round(UnitHelper.FtToMm(x), 3),
                            ["y"] = Math.Round(UnitHelper.FtToMm(y), 3),
                            ["z"] = Math.Round(UnitHelper.FtToMm(z), 3)
                        }
                    };

                    if (includeGeometry)
                    {
                        var geomSummary = BuildGeometrySummaryJson(doc, elem);
                        if (geomSummary != null) info["geometrySummary"] = geomSummary;
                        var geomShape = BuildGeometryShapeJson(elem);
                        if (geomShape != null) info["geometryShape"] = geomShape;
                    }

                    // (NEW) ビュー基準の visualOverride フラグ（任意）
                    if (includeVisual)
                    {
                        bool? hasOv = null;
                        try
                        {
                            if (visualView != null)
                                hasOv = VisualOverrideChecker.HasVisualOverride(visualView, elem.Id);
                        }
                        catch
                        {
                            hasOv = null;
                        }
                        if (hasOv.HasValue)
                        {
                            info["visualOverride"] = hasOv.Value;           // bool → JToken へ暗黙変換OK
                        }
                        else
                        {
                            info["visualOverride"] = JValue.CreateNull();   // null を明示
                        }
                    }

                    if (rich)
                    {
                        // identity
                        TryAdd(info, "uniqueId", elem.UniqueId);
                        TryAdd(info, "className", elem.GetType().Name);
                        if (elem.Category != null)
                            TryAdd(info, "categoryId", elem.Category.Id.IntValue());
                        if (typeId.HasValue)
                            TryAdd(info, "typeId", typeId.Value);
                        if (levelId.HasValue)
                            TryAdd(info, "levelId", levelId.Value);

                        // location
                        TryAdd(info, "locationKind", locationKind);
                        if (!string.IsNullOrEmpty(curveType))
                            TryAdd(info, "curveType", curveType);

                        // bbox (mm, UnitHelper)
                        var bb = elem.get_BoundingBox(null);
                        if (bb != null)
                        {
                            info["bboxMm"] = new JObject
                            {
                                ["min"] = MmPt(bb.Min),
                                ["max"] = MmPt(bb.Max)
                            };
                        }

                        // flags
                        info["isPinned"] = elem.Pinned;
                        if (elem is FamilyInstance fi3) info["isMirrored"] = fi3.Mirrored;

                        // group
                        var grpId = elem.GroupId;
                        if (grpId != null && grpId != ElementId.InvalidElementId)
                        {
                            var grp = doc.GetElement(grpId) as Group;
                            info["isInGroup"] = true;
                            info["groupId"] = grpId.IntValue();
                            info["groupName"] = grp?.Name ?? "";
                        }
                        else
                        {
                            info["isInGroup"] = false;
                        }

                        // link
                        if (elem is RevitLinkInstance rli)
                        {
                            info["isLinkInstance"] = true;
                            info["linkTypeId"] = rli.GetTypeId().IntValue();
                            var ldoc = rli.GetLinkDocument();
                            if (ldoc != null) info["linkDocTitle"] = ldoc.Title;
                        }
                        else info["isLinkInstance"] = false;

                        // host / owner view
                        if (elem is FamilyInstance fi4 && fi4.Host != null)
                        {
                            info["hostId"] = fi4.Host.Id.IntValue();
                            info["hostCategory"] = fi4.Host.Category?.Name ?? "";
                        }
                        info["viewSpecific"] = elem.ViewSpecific;
                        if (elem.ViewSpecific) info["ownerViewId"] = elem.OwnerViewId.IntValue();

                        // phases
                        info["phaseCreated"] = GetPhaseName(elem, BuiltInParameter.PHASE_CREATED, doc);
                        info["phaseDemolished"] = GetPhaseName(elem, BuiltInParameter.PHASE_DEMOLISHED, doc);

                        // workset
                        var wsId = elem.WorksetId;
                        if (wsId != null && wsId.IntValue() > 0)
                        {
                            info["worksetId"] = wsId.IntValue();
                            try
                            {
                                var ws = doc.GetWorksetTable()?.GetWorkset(wsId);
                                if (ws != null) info["worksetName"] = ws.Name;
                            }
                            catch { /* ignore */ }
                        }

                        // design option
                        var doParam = elem.get_Parameter(BuiltInParameter.DESIGN_OPTION_ID);
                        if (doParam != null && doParam.StorageType == StorageType.ElementId)
                        {
                            var doId = doParam.AsElementId();
                            if (doId != ElementId.InvalidElementId)
                            {
                                info["designOptionId"] = doId.IntValue();
                                var dop = doc.GetElement(doId) as DesignOption;
                                if (dop != null) info["designOptionName"] = dop.Name;
                            }
                        }

                        // element-specific constraints（mm）
                        var constraints = new JObject();

                        if (elem is Autodesk.Revit.DB.Wall w)
                        {
                            var baseL = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId();
                            var topL = w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId();

                            var baseOff = w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0.0;
                            var topOff = w.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.AsDouble() ?? 0.0;
                            var unconn = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble();

                            var wallObj = new JObject();

                            if (baseL != null && baseL != ElementId.InvalidElementId)
                            {
                                wallObj["baseLevelId"] = baseL.IntValue();
                                wallObj["baseLevelName"] = (doc.GetElement(baseL) as Level)?.Name ?? "";
                            }
                            if (topL != null && topL != ElementId.InvalidElementId)
                            {
                                wallObj["topLevelId"] = topL.IntValue();
                                wallObj["topLevelName"] = (doc.GetElement(topL) as Level)?.Name ?? "";
                            }

                            wallObj["baseOffsetMm"] = Math.Round(UnitHelper.InternalToMm(baseOff), 3);
                            wallObj["topOffsetMm"] = Math.Round(UnitHelper.InternalToMm(topOff), 3);
                            if (unconn.HasValue) wallObj["unconnectedHeightMm"] = Math.Round(UnitHelper.InternalToMm(unconn.Value), 3);

                            constraints["wall"] = wallObj;
                        }

                        if (elem is FamilyInstance cf && (IsArchitecturalColumn(cf) || IsStructuralColumn(cf)))
                        {
                            var baseL = cf.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId();
                            var topL = cf.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId();
                            var baseOff = cf.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0.0;
                            var topOff = cf.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0.0;

                            var colObj = new JObject();
                            if (baseL != null && baseL != ElementId.InvalidElementId)
                            {
                                colObj["baseLevelId"] = baseL.IntValue();
                                colObj["baseLevelName"] = (doc.GetElement(baseL) as Level)?.Name ?? "";
                            }
                            if (topL != null && topL != ElementId.InvalidElementId)
                            {
                                colObj["topLevelId"] = topL.IntValue();
                                colObj["topLevelName"] = (doc.GetElement(topL) as Level)?.Name ?? "";
                            }
                            colObj["baseOffsetMm"] = Math.Round(UnitHelper.InternalToMm(baseOff), 3);
                            colObj["topOffsetMm"] = Math.Round(UnitHelper.InternalToMm(topOff), 3);

                            constraints["column"] = colObj;
                        }

                        if (constraints.Properties().Any())
                            info["constraints"] = constraints;

                        // next actions (ヒント)
                        info["nextActions"] = SuggestNextActions(elem, info);
                    }

                    result.Add(info);
                }

                return new { ok = true, count = result.Count, elements = result };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        private static View ResolveViewForVisual(Document doc, int? viewId)
        {
            try
            {
                if (viewId.HasValue && viewId.Value > 0)
                {
                    var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId.Value)) as View;
                    if (v != null && !v.IsTemplate) return v;
                }
                return doc.ActiveView;
            }
            catch { return doc.ActiveView; }
        }

        private static void TryAdd(JObject obj, string name, object value)
        {
            if (value == null) return;
            if (value is string s && string.IsNullOrWhiteSpace(s)) return;
            obj[name] = JToken.FromObject(value);
        }

        private static JObject MmPt(XYZ p)
        {
            var mm = UnitHelper.XyzToMm(p);
            return new JObject
            {
                ["x"] = Math.Round(mm.x, 3),
                ["y"] = Math.Round(mm.y, 3),
                ["z"] = Math.Round(mm.z, 3)
            };
        }

        private static string GetPhaseName(Element e, BuiltInParameter bip, Document doc)
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var pid = p.AsElementId();
                    var ph = doc.GetElement(pid) as Phase;
                    return ph?.Name ?? null;
                }
            }
            catch { }
            return null;
        }

        private static bool IsArchitecturalColumn(FamilyInstance fi)
        {
            try { return fi.Symbol?.Family?.FamilyCategory?.Id.IntValue() == (int)BuiltInCategory.OST_Columns; }
            catch { return false; }
        }

        private static bool IsStructuralColumn(FamilyInstance fi)
        {
            try { return fi.Symbol?.Family?.FamilyCategory?.Id.IntValue() == (int)BuiltInCategory.OST_StructuralColumns; }
            catch { return false; }
        }

        private static JArray SuggestNextActions(Element elem, JObject info)
        {
            var hints = new List<string>();

            // 汎用
            if (info["bboxMm"] != null && (info["viewSpecific"]?.Value<bool>() ?? false) == false)
            {
                hints.Add("set_section_box_by_elements");
            }
            hints.Add("get_bounding_box");

            // グループ
            if (info["isInGroup"]?.Value<bool>() == true)
            {
                hints.Add("get_group_info");
                hints.Add("get_group_members");
                hints.Add("get_element_group_membership");
            }

            // 壁
            if (elem is Autodesk.Revit.DB.Wall)
            {
                hints.Add("get_wall_parameters");
                hints.Add("update_wall_parameter");
                hints.Add("change_wall_type");
                hints.Add("get_wall_faces");
            }

            // 窓・ドア
            if (elem is FamilyInstance fi)
            {
                var catId = fi.Category?.Id.IntValue() ?? 0;
                if (catId == (int)BuiltInCategory.OST_Windows)
                    hints.Add("get_window_parameters");
                if (catId == (int)BuiltInCategory.OST_Doors)
                    hints.Add("get_door_parameters");
            }

            // ワークセット/オプション
            if (info["worksetId"] != null) hints.Add("get_element_workset");
            if (info["designOptionId"] != null) hints.Add("get_open_documents");

            // リンク
            if (info["isLinkInstance"]?.Value<bool>() == true)
            {
                hints.Add("get_open_documents");
            }

            return new JArray(hints.Distinct());
        }

        private static JObject? BuildGeometrySummaryJson(Document doc, Element element)
        {
            try
            {
                Options opt = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Coarse
                };

                GeometryElement? geo = element.get_Geometry(opt);
                if (geo == null)
                    return null;

                int solidCount = 0;
                double vol = 0.0;
                double area = 0.0;

                foreach (var obj in geo)
                {
                    if (obj is Solid solid && solid.Volume > 0)
                    {
                        solidCount++;
                        vol += solid.Volume;
                        area += solid.SurfaceArea;
                    }
                }

                if (solidCount == 0)
                    return new JObject { ["hasSolid"] = false };

                double volM3 = UnitUtils.ConvertFromInternalUnits(vol, UnitTypeId.CubicMeters);
                double areaM2 = UnitUtils.ConvertFromInternalUnits(area, UnitTypeId.SquareMeters);

                return new JObject
                {
                    ["hasSolid"] = true,
                    ["solidCount"] = solidCount,
                    ["approxVolume"] = Math.Round(volM3, 3),
                    ["approxSurfaceArea"] = Math.Round(areaM2, 3)
                };
            }
            catch
            {
                return null;
            }
        }

        private static JObject? BuildGeometryShapeJson(Element element)
        {
            try
            {
                if (element == null) return null;
                var catId = element.Category?.Id?.IntValue() ?? 0;
                bool isColumn = catId == (int)BuiltInCategory.OST_StructuralColumns
                                || catId == (int)BuiltInCategory.OST_Columns;
                if (!isColumn) return null;

                double diaFt;
                string src;
                if (TryDetectCircularByGeometry(element, out diaFt, out src))
                {
                    return new JObject
                    {
                        ["shape"] = "circular",
                        ["diameterMm"] = Math.Round(UnitHelper.FtToMm(diaFt), 3),
                        ["source"] = src ?? "geom"
                    };
                }

                // fallback: bbox-based rectangular
                var bb = element.get_BoundingBox(null);
                if (bb != null)
                {
                    var w = Math.Abs(bb.Max.X - bb.Min.X);
                    var d = Math.Abs(bb.Max.Y - bb.Min.Y);
                    if (w > 1e-6 && d > 1e-6)
                    {
                        return new JObject
                        {
                            ["shape"] = "rectangular",
                            ["widthMm"] = Math.Round(UnitHelper.FtToMm(w), 3),
                            ["depthMm"] = Math.Round(UnitHelper.FtToMm(d), 3),
                            ["source"] = "bbox"
                        };
                    }
                }

                return new JObject
                {
                    ["shape"] = "unknown",
                    ["source"] = "geom"
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool TryDetectCircularByGeometry(Element element, out double diameterFt, out string source)
        {
            diameterFt = 0.0;
            source = null;
            try
            {
                var opt = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Fine
                };

                var ge = element.get_Geometry(opt);
                if (ge == null) return false;

                double bestDia = 0.0;
                string bestSrc = null;

                bool Traverse(GeometryElement geo, Transform t)
                {
                    foreach (var obj in geo)
                    {
                        if (obj is GeometryInstance gi)
                        {
                            var instGe = gi.GetInstanceGeometry();
                            if (instGe != null && Traverse(instGe, t.Multiply(gi.Transform))) return true;
                        }
                        else if (obj is Solid solid && solid.Faces != null && solid.Faces.Size > 0)
                        {
                            foreach (Autodesk.Revit.DB.Face face in solid.Faces)
                            {
                                var cyl = face as Autodesk.Revit.DB.CylindricalFace;
                                if (cyl != null)
                                {
                                    var axis = cyl.Axis;
                                    double r = TryGetCylindricalFaceRadiusFt(cyl);
                                    if (IsAxisVertical(axis) && r > 1e-6)
                                    {
                                        double d = r * 2.0;
                                        if (d > bestDia)
                                        {
                                            bestDia = d;
                                            bestSrc = "geom:cylindrical_face";
                                        }
                                        continue;
                                    }
                                }

                                var pf = face as Autodesk.Revit.DB.PlanarFace;
                                if (pf == null) continue;
                                if (!IsAxisVertical(pf.FaceNormal)) continue;

                                try
                                {
                                    var loops = pf.GetEdgesAsCurveLoops();
                                    foreach (var loop in loops)
                                    {
                                        double loopDia;
                                        if (TryGetCircularLoopDiameter(loop, out loopDia))
                                        {
                                            if (loopDia > bestDia)
                                            {
                                                bestDia = loopDia;
                                                bestSrc = "geom:planar_loop_arcs";
                                            }
                                        }
                                    }
                                }
                                catch { /* ignore */ }
                            }
                        }
                    }
                    return false;
                }

                Traverse(ge, Transform.Identity);
                if (bestDia > 1e-6)
                {
                    diameterFt = bestDia;
                    source = bestSrc;
                    return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool TryGetCircularLoopDiameter(CurveLoop loop, out double diameterFt)
        {
            diameterFt = 0.0;
            if (loop == null) return false;
            XYZ center = null;
            double radius = 0.0;
            double angleSum = 0.0;
            int arcCount = 0;
            double tolFt = UnitHelper.MmToFt(2.0);

            foreach (Curve c in loop)
            {
                var arc = c as Arc;
                if (arc == null) return false;
                if (arc.Radius <= 1e-6) return false;
                if (center == null)
                {
                    center = arc.Center;
                    radius = arc.Radius;
                }
                else
                {
                    if (center.DistanceTo(arc.Center) > tolFt) return false;
                    if (Math.Abs(arc.Radius - radius) > tolFt) return false;
                }
                angleSum += arc.Length / arc.Radius;
                arcCount++;
            }

            if (arcCount == 0) return false;
            double twoPi = Math.PI * 2.0;
            if (Math.Abs(angleSum - twoPi) > 0.6) return false;

            diameterFt = radius * 2.0;
            return true;
        }

        private static bool IsAxisVertical(XYZ axis)
        {
            if (axis == null) return false;
            XYZ a;
            try { a = axis.Normalize(); }
            catch { return false; }
            return Math.Abs(Math.Abs(a.Z) - 1.0) <= 0.1;
        }

        private static double TryGetCylindricalFaceRadiusFt(CylindricalFace cyl)
        {
            if (cyl == null) return 0.0;
            try
            {
                var prop = cyl.GetType().GetProperty("Radius");
                if (prop != null && prop.PropertyType == typeof(double))
                {
                    var v = prop.GetValue(cyl, null);
                    if (v is double d) return d;
                }
            }
            catch { /* ignore */ }

            try
            {
                var m = cyl.GetType().GetMethod("get_Radius", new[] { typeof(int) });
                if (m != null)
                {
                    var rv = m.Invoke(cyl, new object[] { 0 });
                    if (rv is double d) return d;
                    if (rv is XYZ v) return v.GetLength();
                }
            }
            catch { /* ignore */ }

            return 0.0;
        }

        // ============================================================
        // (NEW) 内部ヘルパー: ビュー内の要素に Visual Override があるか判定
        //  - まず反射で View.AreGraphicsOverridesApplied(ElementId) を試みる
        //  - 使えなければ GetElementOverrides() → 既定値比較で判定
        //  - 2023 の型/プロパティ差に配慮してベストエフォート
        // ============================================================
        private static class VisualOverrideChecker
        {
            private static readonly MethodInfo AreAppliedMI =
                typeof(View).GetMethod("AreGraphicsOverridesApplied", new[] { typeof(ElementId) });

            public static bool HasVisualOverride(View view, ElementId elementId)
            {
                // 高速経路（存在すれば）
                try
                {
                    if (AreAppliedMI != null)
                    {
                        var obj = AreAppliedMI.Invoke(view, new object[] { elementId });
                        if (obj is bool applied) return applied;
                    }
                }
                catch { /* ignore and fallback */ }

                // フォールバック：OverrideGraphicSettings で差分を確認
                try
                {
                    var ogs = view.GetElementOverrides(elementId); // OverrideGraphicSettings
                    return !IsDefault(ogs);
                }
                catch
                {
                    return false;
                }
            }

            private static bool IsDefault(OverrideGraphicSettings ogs)
            {
                if (ogs == null) return true;

                // 2023で確実に参照できる項目中心に判定（透明度・パターンID）
                if (SafeGetTransparency(ogs) != 0) return false;

                if (SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceForegroundPatternId)) != 0) return false;
                if (SafeGetId(ogs, nameof(OverrideGraphicSettings.SurfaceBackgroundPatternId)) != 0) return false;
                if (SafeGetId(ogs, nameof(OverrideGraphicSettings.CutForegroundPatternId)) != 0) return false;
                if (SafeGetId(ogs, nameof(OverrideGraphicSettings.CutBackgroundPatternId)) != 0) return false;

                // 色getterは環境差があるため、判定には使わない
                return true;
            }

            private static int SafeGetTransparency(OverrideGraphicSettings ogs)
            {
                try { return ogs.Transparency; } catch { return 0; }
            }

            private static int SafeGetId(OverrideGraphicSettings ogs, string propName)
            {
                try
                {
                    var pi = typeof(OverrideGraphicSettings).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (pi == null) return 0;
                    var id = pi.GetValue(ogs) as ElementId;
                    return id?.IntValue() ?? 0;
                }
                catch { return 0; }
            }
        }
    }
}


