// ================================================================
// File   : Core/SpatialUtils.cs
// Purpose: Shared helpers for spatial context (Room/Space/Area)
// Target : .NET Framework 4.8 / Revit 2024
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;

namespace RevitMCPAddin.Core
{
    public static class SpatialUtils
    {
        public enum BoundaryCurveSource
        {
            BoundarySegment = 0,
            PreferLineElements = 1
        }

        /// <summary>
        /// Parse SpatialElementBoundaryOptions.SpatialElementBoundaryLocation from user-friendly strings.
        /// - Finish: interior finish (内法)
        /// - Center: wall centerline (壁芯)
        /// - CoreCenter: core centerline (コア芯/躯体芯)
        /// - CoreBoundary: core boundary face (コア境界面)
        /// </summary>
        public static SpatialElementBoundaryLocation ParseBoundaryLocation(
            string? boundaryLocation,
            SpatialElementBoundaryLocation defaultValue = SpatialElementBoundaryLocation.Finish)
        {
            if (string.IsNullOrWhiteSpace(boundaryLocation)) return defaultValue;

            var key = boundaryLocation.Trim().ToLowerInvariant();
            switch (key)
            {
                case "finish":
                case "inside":
                case "interior":
                case "inner":
                case "innerface":
                case "inner_face":
                    return SpatialElementBoundaryLocation.Finish;

                case "center":
                case "centre":
                case "centerline":
                case "centreline":
                case "wallcenter":
                case "wall_center":
                case "wallcentre":
                case "wall_centre":
                case "wallcenterline":
                case "wall_centerline":
                    return SpatialElementBoundaryLocation.Center;

                case "core":
                case "corecenter":
                case "core_center":
                case "corecentre":
                case "core_centre":
                case "corecenterline":
                case "core_centerline":
                    return SpatialElementBoundaryLocation.CoreCenter;

                case "coreboundary":
                case "core_boundary":
                case "coreface":
                case "core_face":
                case "corefaceexterior":
                case "coreface_exterior":
                case "core_outside":
                case "coreoutside":
                    return SpatialElementBoundaryLocation.CoreBoundary;

                default:
                    return defaultValue;
            }
        }

        /// <summary>
        /// Parse boundary curve source for boundary-copy workflows.
        /// - BoundarySegment: use BoundarySegment.GetCurve() (calculated boundary)
        /// - PreferLineElements: when BoundarySegment.ElementId is a CurveElement, copy its GeometryCurve instead
        /// </summary>
        public static BoundaryCurveSource ParseBoundaryCurveSource(
            string? boundaryCurveSource,
            BoundaryCurveSource defaultValue = BoundaryCurveSource.BoundarySegment)
        {
            if (string.IsNullOrWhiteSpace(boundaryCurveSource)) return defaultValue;

            var key = boundaryCurveSource.Trim().ToLowerInvariant();
            switch (key)
            {
                case "segment":
                case "segments":
                case "boundarysegment":
                case "boundary_segment":
                case "boundarysegments":
                case "boundary_segments":
                case "calculated":
                case "computed":
                case "computed_boundary":
                case "computedboundary":
                    return BoundaryCurveSource.BoundarySegment;

                case "line":
                case "lines":
                case "lineelement":
                case "line_element":
                case "lineelements":
                case "line_elements":
                case "curveelement":
                case "curve_element":
                case "curveelements":
                case "curve_elements":
                case "preferlineelements":
                case "prefer_line_elements":
                case "prefercurveelements":
                case "prefer_curve_elements":
                case "線要素":
                case "線要素準拠":
                    return BoundaryCurveSource.PreferLineElements;

                default:
                    return defaultValue;
            }
        }

        /// <summary>
        /// 要素から代表点（Location or BoundingBox 中心）を取得する。
        /// 失敗した場合は null を返し、message に理由を設定する。
        /// </summary>
        public static XYZ? GetReferencePoint(Document doc, Element element, out string message)
        {
            message = string.Empty;
            if (doc == null) { message = "Document is null."; return null; }
            if (element == null) { message = "Element is null."; return null; }

            try
            {
                // 1) LocationPoint
                if (element.Location is LocationPoint lp && lp.Point != null)
                {
                    return lp.Point;
                }

                // 2) LocationCurve の中点
                if (element.Location is LocationCurve lc && lc.Curve != null)
                {
                    var c = lc.Curve;
                    var mid = c.Evaluate(0.5, true);
                    return mid;
                }

                // 3) BoundingBox 中心
                var bb = element.get_BoundingBox(null);
                if (bb != null)
                {
                    var center = (bb.Min + bb.Max) * 0.5;
                    return center;
                }

                message = "要素に Location も有効な BoundingBox もないため、代表点を決定できません。";
                return null;
            }
            catch (Exception ex)
            {
                message = "代表点の取得に失敗しました: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// pt（内部単位 ft）を含む Room を取得する。phaseName が指定されている場合は Phase を優先。
        /// </summary>
        public static Room? TryGetRoomAtPoint(Document doc, XYZ pt, string? phaseName, out Phase? phaseUsed, out string message)
        {
            message = string.Empty;
            phaseUsed = null;
            if (doc == null) { message = "Document is null."; return null; }

            try
            {
                Room? room = null;

                if (!string.IsNullOrWhiteSpace(phaseName))
                {
                    var phase = FindPhaseByName(doc, phaseName!);
                    if (phase != null)
                    {
                        room = doc.GetRoomAtPoint(pt, phase);
                        phaseUsed = phase;
                        if (room != null)
                        {
                            message = $"Room was resolved using phase '{phase.Name}'.";
                            return room;
                        }
                        message = $"No Room found at point for phase '{phase.Name}'.";
                        return null;
                    }

                    // phaseName が指定されているが見つからなかった場合は情報だけ残す
                    message = $"Phase '{phaseName}' was not found. Falling back to final project phase.";
                }

                // phaseName 未指定または Phase 解決に失敗した場合は final phase
                room = doc.GetRoomAtPoint(pt);
                if (room != null)
                {
                    // final phase は API 仕様上の内部決定に委ねる
                    message = "Room was resolved using the final project phase.";
                    return room;
                }

                if (string.IsNullOrEmpty(message))
                    message = "No Room found at the reference point.";

                return null;
            }
            catch (Exception ex)
            {
                message = "Room 解決中に例外が発生しました: " + ex.Message;
                return null;
            }
        }

        private static Phase? FindPhaseByName(Document doc, string phaseName)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .FirstOrDefault(ph => string.Equals(ph.Name ?? string.Empty, phaseName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 要素の BoundingBox に基づいて Z 方向に 1 回だけプローブし、
        /// 要素の縦方向のどこかで Room に入っていないかを判定する。
        /// - まず BoundingBox の Z 中間高さで TryGetRoomAtPoint を試す。
        /// - そこで見つからなかった場合は、従来どおり basePt そのもので TryGetRoomAtPoint を呼び出す。
        /// </summary>
        public static Room? TryGetRoomWithVerticalProbe(Document doc, Element element, XYZ basePt, string? phaseName, out Phase? phaseUsed, out string message, bool bboxFootprintProbe = true)
        {
            message = string.Empty;
            phaseUsed = null;
            if (doc == null)
            {
                message = "Document is null.";
                return null;
            }
            if (element == null)
            {
                message = "Element is null.";
                return null;
            }

            try
            {
                var bb = element.get_BoundingBox(null);
                if (bb != null)
                {
                    double zMin = bb.Min.Z;
                    double zMax = bb.Max.Z;
                    if (zMax > zMin + 1e-6)
                    {
                        var midZ = 0.5 * (zMin + zMax);

                        // 1) Base XY at mid-height
                        var midPt = new XYZ(basePt.X, basePt.Y, midZ);
                        var roomMid = TryGetRoomAtPoint(doc, midPt, phaseName, out phaseUsed, out var msgMid);
                        if (!string.IsNullOrEmpty(msgMid)) message = msgMid;
                        if (roomMid != null)
                        {
                            return roomMid;
                        }

                        // 2) If base point is outside in XY, also probe the element bbox footprint at mid-height.
                        //    This helps cases where the element crosses the room boundary but its representative point is outside.
                        if (bboxFootprintProbe)
                        {
                            if (TryGetBboxWorldMinMaxXY(bb, out double minX, out double minY, out double maxX, out double maxY))
                            {
                                double eps = UnitHelper.MmToFt(1.0);
                                double x0 = minX + eps;
                                double x1 = maxX - eps;
                                double y0 = minY + eps;
                                double y1 = maxY - eps;
                                if (x1 < x0) { x0 = minX; x1 = maxX; }
                                if (y1 < y0) { y0 = minY; y1 = maxY; }

                                double xMid = 0.5 * (minX + maxX);
                                double yMid = 0.5 * (minY + maxY);

                                var probePts = new[]
                                {
                                    new XYZ(xMid, yMid, midZ),
                                    new XYZ(x0, y0, midZ),
                                    new XYZ(x1, y0, midZ),
                                    new XYZ(x1, y1, midZ),
                                    new XYZ(x0, y1, midZ),
                                    new XYZ(xMid, y0, midZ),
                                    new XYZ(x1, yMid, midZ),
                                    new XYZ(xMid, y1, midZ),
                                    new XYZ(x0, yMid, midZ),
                                };

                                foreach (var pt in probePts)
                                {
                                    var r = TryGetRoomAtPoint(doc, pt, phaseName, out var ph, out var msg);
                                    if (r == null) continue;
                                    phaseUsed = ph;
                                    if (!string.IsNullOrEmpty(msg))
                                    {
                                        message = msg + " (bbox footprint probe at mid-height)";
                                    }
                                    else
                                    {
                                        message = "Room was resolved using bbox footprint probe at mid-height.";
                                    }
                                    return r;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 何か問題があった場合は、そのままベースポイント判定にフォールバック
            }

            // 従来どおり basePt そのもので判定
            var roomBase = TryGetRoomAtPoint(doc, basePt, phaseName, out phaseUsed, out var msgBase);
            if (!string.IsNullOrEmpty(msgBase))
            {
                message = msgBase;
            }
            return roomBase;
        }

        private static bool TryGetBboxWorldMinMaxXY(BoundingBoxXYZ bb, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = double.PositiveInfinity;
            minY = double.PositiveInfinity;
            maxX = double.NegativeInfinity;
            maxY = double.NegativeInfinity;

            if (bb == null) return false;

            try
            {
                var tr = bb.Transform ?? Transform.Identity;
                var corners = new[]
                {
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
                }.Select(c => tr.OfPoint(c));

                foreach (var p in corners)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }

                return (minX < maxX && minY < maxY);
            }
            catch
            {
                minX = minY = double.PositiveInfinity;
                maxX = maxY = double.NegativeInfinity;
                return false;
            }
        }

        /// <summary>
        /// pt（ft）の位置を含む Space を列挙する。phaseName が指定されている場合は Space.Phase と比較。
        /// </summary>
        public static List<Space> GetSpacesAtPoint(Document doc, XYZ pt, string? phaseName, out string message)
        {
            message = string.Empty;
            var result = new List<Space>();
            if (doc == null) { message = "Document is null."; return result; }

            try
            {
                if (!string.IsNullOrWhiteSpace(phaseName))
                {
                    // 現時点では Space 側の Phase フィルタは厳密には行わず、情報メッセージのみとする。
                    var phase = FindPhaseByName(doc, phaseName!);
                    if (phase == null)
                        message = $"Phase '{phaseName}' was not found. Space phase filter is ignored.";
                    else
                        message = $"Space resolution uses all spaces (phase '{phaseName}' not strictly filtered).";
                }

                // Revit 2024 以降では、Space/Area は SpatialElement ベースとして扱うのが安全。
                var spaces = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && e.Category.Id.IntValue() == (int)BuiltInCategory.OST_MEPSpaces)
                    .Cast<SpatialElement>()
                    .OfType<Space>();

                foreach (var s in spaces)
                {
                    if (s == null) continue;

                    bool inside = false;
                    try { inside = s.IsPointInSpace(pt); } catch { }
                    if (inside)
                    {
                        result.Add(s);
                    }
                }

                if (result.Count == 0)
                {
                    if (string.IsNullOrEmpty(message))
                        message = "No Space found at the reference point.";
                }
                else
                {
                    message = $"Found {result.Count} Space(s) at the reference point.";
                }

                return result;
            }
            catch (Exception ex)
            {
                message = "Space 解決中に例外が発生しました: " + ex.Message;
                return result;
            }
        }

        /// <summary>
        /// pt（ft）の XY と要素のレベルを用いて、同レベルの Area を BoundingBox ベースで近似的に判定する。
        /// 厳密なポリゴン内判定ではないことに注意。
        /// </summary>
        public static List<Area> GetAreasAtPoint(Document doc, XYZ pt, Element element, out List<AreaScheme> areaSchemes, out string message)
        {
            message = string.Empty;
            areaSchemes = new List<AreaScheme>();
            var result = new List<Area>();
            if (doc == null || element == null)
            {
                message = "Document または Element が null です。";
                return result;
            }

            ElementId levelId = ElementId.InvalidElementId;
            try
            {
                if (element is FamilyInstance fi && fi.LevelId != ElementId.InvalidElementId)
                {
                    levelId = fi.LevelId;
                }
                else if (element.LevelId != ElementId.InvalidElementId)
                {
                    levelId = element.LevelId;
                }
                else
                {
                    var pLevel = element.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    if (pLevel != null && pLevel.StorageType == StorageType.ElementId)
                        levelId = pLevel.AsElementId();
                }
            }
            catch { /* ignore */ }

            if (levelId == ElementId.InvalidElementId)
            {
                message = "要素のレベルを特定できなかったため、Area 判定をスキップしました。";
                return result;
            }

            try
            {
                var areasSameLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && e.Category.Id.IntValue() == (int)BuiltInCategory.OST_Areas)
                    .Cast<SpatialElement>()
                    .OfType<Area>()
                    .Where(a => a != null && a.LevelId == levelId)
                    .ToList();

                foreach (var a in areasSameLevel)
                {
                    BoundingBoxXYZ? bb = null;
                    try { bb = a.get_BoundingBox(null); } catch { }
                    if (bb == null) continue;

                    if (pt.X >= bb.Min.X && pt.X <= bb.Max.X &&
                        pt.Y >= bb.Min.Y && pt.Y <= bb.Max.Y)
                    {
                        result.Add(a);
                        var scheme = a.AreaScheme;
                        if (scheme != null && !areaSchemes.Any(s => s.Id == scheme.Id))
                        {
                            areaSchemes.Add(scheme);
                        }
                    }
                }

                if (result.Count == 0)
                {
                    message = "同レベルの Area による包含は見つかりませんでした（BoundingBox ベースの近似判定）。";
                }
                else
                {
                    message = $"同レベルの Area に {result.Count} 件ヒットしました（BoundingBox ベースの近似判定）。";
                }
            }
            catch (Exception ex)
            {
                message = "Area 解決中に例外が発生しました: " + ex.Message;
            }

            return result;
        }
    }
}

