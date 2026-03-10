#nullable enable
// ================================================================
// File: Commands/Area/AreaCalcCommands.cs
// Purpose: KSG area-calc MVP commands (numbering / edge extraction / write / run-all)
// Target : Revit 2024 / .NET Framework 4.8 / C# 8
// ================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using ArchArea = Autodesk.Revit.DB.Area;

namespace RevitMCPAddin.Commands.Area
{
    internal sealed class AreaCalcSettings
    {
        public string BoundaryLocation { get; set; } = "Finish";
        public double RowToleranceMm { get; set; } = 200.0;
        public string NumberPrefix { get; set; } = "";
        public string NumberSuffix { get; set; } = "";
        public int NumberStart { get; set; } = 1;
        public int NumberStep { get; set; } = 1;
        public int NumberPad { get; set; } = 2;
        public double LengthOffsetMm { get; set; } = 250.0;
        public double LengthOffsetStepMm { get; set; } = 120.0;
        public int LengthDigits { get; set; } = 2;
        public int AreaDigits { get; set; } = 4;
        public double DiffWarnThresholdM2 { get; set; } = 1.0;
        public bool PlotEdgeText { get; set; } = true;
        public bool LengthTextCenterAtSegment { get; set; } = true;
        public string LengthTextHorizontalAlignment { get; set; } = "Center";
        public bool WriteValues { get; set; } = true;
        public bool DoNumbering { get; set; } = true;
        public bool RefreshView { get; set; } = true;
        public string ParamNo { get; set; } = "KSG_No";
        public string ParamFormula { get; set; } = "KSG_Formula";
        public string ParamAreaCalc { get; set; } = "KSG_AreaCalc";
        public string ParamShapeSummary { get; set; } = "KSG_ShapeSummary";
        public string ParamStatus { get; set; } = "KSG_Status";

        public static AreaCalcSettings FromParams(JObject p)
        {
            var s = new AreaCalcSettings();
            if (p == null) return s;

            s.BoundaryLocation = p.Value<string>("boundaryLocation") ?? p.Value<string>("boundary_location") ?? s.BoundaryLocation;
            s.RowToleranceMm = p.Value<double?>("rowToleranceMm") ?? s.RowToleranceMm;
            s.NumberPrefix = p.Value<string>("numberPrefix") ?? s.NumberPrefix;
            s.NumberSuffix = p.Value<string>("numberSuffix") ?? s.NumberSuffix;
            s.NumberStart = p.Value<int?>("numberStart") ?? s.NumberStart;
            s.NumberStep = p.Value<int?>("numberStep") ?? s.NumberStep;
            s.NumberPad = p.Value<int?>("numberPad") ?? s.NumberPad;
            s.LengthOffsetMm = p.Value<double?>("lengthOffsetMm") ?? s.LengthOffsetMm;
            s.LengthOffsetStepMm = p.Value<double?>("lengthOffsetStepMm") ?? s.LengthOffsetStepMm;
            s.LengthDigits = p.Value<int?>("lengthDigits") ?? s.LengthDigits;
            s.AreaDigits = p.Value<int?>("areaDigits") ?? s.AreaDigits;
            s.DiffWarnThresholdM2 = p.Value<double?>("diffWarnThresholdM2") ?? s.DiffWarnThresholdM2;
            s.PlotEdgeText = p.Value<bool?>("plotEdgeText") ?? s.PlotEdgeText;
            s.LengthTextCenterAtSegment = p.Value<bool?>("lengthTextCenterAtSegment")
                                       ?? p.Value<bool?>("lengthTextCenterAtLineCenter")
                                       ?? s.LengthTextCenterAtSegment;
            s.LengthTextHorizontalAlignment = p.Value<string>("lengthTextHorizontalAlignment")
                                           ?? p.Value<string>("lengthTextHAlign")
                                           ?? s.LengthTextHorizontalAlignment;
            s.WriteValues = p.Value<bool?>("writeValues") ?? s.WriteValues;
            s.DoNumbering = p.Value<bool?>("doNumbering") ?? s.DoNumbering;
            s.RefreshView = p.Value<bool?>("refreshView") ?? s.RefreshView;

            s.ParamNo = p.Value<string>("paramNo") ?? s.ParamNo;
            s.ParamFormula = p.Value<string>("paramFormula") ?? s.ParamFormula;
            s.ParamAreaCalc = p.Value<string>("paramAreaCalc") ?? s.ParamAreaCalc;
            s.ParamShapeSummary = p.Value<string>("paramShapeSummary") ?? s.ParamShapeSummary;
            s.ParamStatus = p.Value<string>("paramStatus") ?? s.ParamStatus;

            if (s.RowToleranceMm <= 0) s.RowToleranceMm = 200.0;
            if (s.NumberStep <= 0) s.NumberStep = 1;
            if (s.NumberPad < 0) s.NumberPad = 0;
            if (s.LengthDigits < 0) s.LengthDigits = 2;
            if (s.AreaDigits < 0) s.AreaDigits = 4;
            if (s.DiffWarnThresholdM2 < 0) s.DiffWarnThresholdM2 = 1.0;
            if (s.LengthOffsetMm < 0) s.LengthOffsetMm = 0;
            if (s.LengthOffsetStepMm < 0) s.LengthOffsetStepMm = 0;
            var align = (s.LengthTextHorizontalAlignment ?? "Center").Trim();
            if (!align.Equals("left", StringComparison.OrdinalIgnoreCase)
                && !align.Equals("center", StringComparison.OrdinalIgnoreCase)
                && !align.Equals("right", StringComparison.OrdinalIgnoreCase))
            {
                align = "Center";
            }
            s.LengthTextHorizontalAlignment = align;

            return s;
        }
    }

    internal sealed class AreaCalcEdge
    {
        public int LoopIndex { get; set; }
        public int SegmentIndex { get; set; }
        public Curve Curve { get; set; } = null!;
        public double LengthMmRaw { get; set; }
        public double LengthMm { get; set; }
        public XYZ MidFt { get; set; } = XYZ.Zero;
        public XYZ DirectionFt { get; set; } = XYZ.BasisX;
        public XYZ NormalFt { get; set; } = XYZ.BasisX;
        public string Kind { get; set; } = "Line";
    }

    internal sealed class AreaCalcTriangle
    {
        public int LoopIndex { get; set; }
        public int TriangleIndex { get; set; }
        public int AIndex { get; set; }
        public int BIndex { get; set; }
        public int CIndex { get; set; }
        public double AreaMm2 { get; set; }
    }

    internal sealed class AreaCalcModel
    {
        public ArchArea Area { get; set; } = null!;
        public int AreaId => Area.Id.IntValue();
        public string LevelName { get; set; } = "";
        public double RevitAreaM2 { get; set; }
        public double CalcAreaM2 { get; set; }
        public double DiffM2 { get; set; }
        public double PerimeterMm { get; set; }
        public string Formula { get; set; } = "";
        public string ShapeSummary { get; set; } = "";
        public string Status { get; set; } = "OK";
        public double CentroidXmm { get; set; }
        public double CentroidYmm { get; set; }
        public List<AreaCalcEdge> Edges { get; } = new List<AreaCalcEdge>();
        public List<AreaCalcTriangle> Triangles { get; } = new List<AreaCalcTriangle>();
        public int TriangleCount { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    internal static class AreaCalcUtil
    {
        public static List<ArchArea> ResolveTargetAreas(Document doc, UIApplication uiapp, JObject p)
        {
            var outAreas = new List<ArchArea>();

            var ids = (p["areaIds"] as JArray)?.Values<int>().Where(x => x > 0).Distinct().ToList();
            if (ids != null && ids.Count > 0)
            {
                foreach (var id in ids)
                {
                    var a = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as ArchArea;
                    if (a != null) outAreas.Add(a);
                }
                return outAreas;
            }

            bool fromSelection = p.Value<bool?>("fromSelection") ?? true;
            if (fromSelection)
            {
                var uidoc = uiapp.ActiveUIDocument;
                if (uidoc != null)
                {
                    var selIds = uidoc.Selection.GetElementIds();
                    foreach (var id in selIds)
                    {
                        var a = doc.GetElement(id) as ArchArea;
                        if (a != null) outAreas.Add(a);
                    }
                    if (outAreas.Count > 0) return outAreas;
                }
            }

            View? targetView = null;
            int viewId = p.Value<int?>("viewId") ?? 0;
            if (viewId > 0)
            {
                targetView = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            }
            if (targetView == null) targetView = uiapp.ActiveUIDocument?.ActiveView;

            if (targetView != null)
            {
                var inView = new FilteredElementCollector(doc, targetView.Id)
                    .OfCategory(BuiltInCategory.OST_Areas)
                    .WhereElementIsNotElementType()
                    .Cast<ArchArea>()
                    .ToList();
                if (inView.Count > 0) return inView;
            }

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType()
                .Cast<ArchArea>()
                .ToList();
        }

        public static SpatialElementBoundaryOptions BuildBoundaryOptions(string boundaryLocation)
        {
            return new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialUtils.ParseBoundaryLocation(boundaryLocation ?? "Finish")
            };
        }

        public static List<AreaCalcModel> Analyze(Document doc, List<ArchArea> areas, AreaCalcSettings s)
        {
            var models = new List<AreaCalcModel>();
            var opts = BuildBoundaryOptions(s.BoundaryLocation);

            foreach (var area in areas)
            {
                var m = new AreaCalcModel { Area = area };
                try
                {
                    m.LevelName = (doc.GetElement(area.LevelId) as Level)?.Name ?? "";
                    m.RevitAreaM2 = RoundDown(UnitHelper.ToExternal(area.Area, SpecTypeId.Area) ?? 0.0, s.AreaDigits);

                    var loops = area.GetBoundarySegments(opts);
                    if (loops == null || loops.Count == 0)
                    {
                        m.Status = "ERROR";
                        m.Warnings.Add("Boundary loop not found.");
                        models.Add(m);
                        continue;
                    }

                    double signedAreaMm2Total = 0.0;
                    double cxWeighted = 0.0;
                    double cyWeighted = 0.0;
                    int lineCount = 0;
                    int arcCount = 0;
                    int triangleCount = 0;

                    for (int li = 0; li < loops.Count; li++)
                    {
                        var loop = loops[li];
                        if (loop == null || loop.Count == 0) continue;

                        var loopPts = new List<XYZ>();
                        int si = 0;
                        foreach (var bs in loop)
                        {
                            var c = bs.GetCurve();
                            if (c == null) { si++; continue; }
                            if (c is Arc) arcCount++; else lineCount++;

                            if (c is Line)
                            {
                                var p0 = c.GetEndPoint(0);
                                var p1 = c.GetEndPoint(1);
                                AddPointNoDup(loopPts, p0);
                                AddPointNoDup(loopPts, p1);
                            }
                            else
                            {
                                var tess = c.Tessellate();
                                if (tess == null || tess.Count == 0)
                                {
                                    var p0 = c.GetEndPoint(0);
                                    var p1 = c.GetEndPoint(1);
                                    AddPointNoDup(loopPts, p0);
                                    AddPointNoDup(loopPts, p1);
                                }
                                else
                                {
                                    foreach (var tp in tess) AddPointNoDup(loopPts, tp);
                                }
                            }

                            var lenMmRaw = UnitHelper.FtToMm(c.Length);
                            var lenMm = RoundDown(lenMmRaw, s.LengthDigits);
                            var pStart = c.GetEndPoint(0);
                            var pEnd = c.GetEndPoint(1);
                            var dir = TryGetCurveDirection(c, pStart, pEnd);
                            var n = new XYZ(-dir.Y, dir.X, 0);
                            if (n.GetLength() > 1e-9) n = n.Normalize(); else n = XYZ.BasisX;
                            m.Edges.Add(new AreaCalcEdge
                            {
                                LoopIndex = li,
                                SegmentIndex = si,
                                Curve = c,
                                LengthMmRaw = lenMmRaw,
                                LengthMm = lenMm,
                                MidFt = c.Evaluate(0.5, true),
                                DirectionFt = dir,
                                NormalFt = n,
                                Kind = c is Arc ? "Arc" : "Line"
                            });

                            m.PerimeterMm += lenMmRaw;
                            si++;
                        }

                        EnsureClosed(loopPts);
                        if (loopPts.Count < 4) continue;

                        // Shoelace for loop orientation/centroid (kept for robustness and diagnostics).
                        double a = 0.0;
                        double cx = 0.0;
                        double cy = 0.0;
                        for (int i = 0; i < loopPts.Count - 1; i++)
                        {
                            var p0 = loopPts[i];
                            var p1 = loopPts[i + 1];
                            var x0 = UnitHelper.FtToMm(p0.X);
                            var y0 = UnitHelper.FtToMm(p0.Y);
                            var x1 = UnitHelper.FtToMm(p1.X);
                            var y1 = UnitHelper.FtToMm(p1.Y);
                            var cross = x0 * y1 - x1 * y0;
                            a += cross;
                            cx += (x0 + x1) * cross;
                            cy += (y0 + y1) * cross;
                        }
                        a *= 0.5;
                        if (Math.Abs(a) > 1e-6)
                        {
                            cx /= (6.0 * a);
                            cy /= (6.0 * a);
                            cxWeighted += cx * a;
                            cyWeighted += cy * a;
                        }

                        // Triangle decomposition (ear clipping). Rectangle loops become 2 triangles
                        // using existing vertices only (no extra vertices are inserted for lines).
                        var loopOpenPts = ToOpenLoop(loopPts);
                        double loopSignedMm2ForCalc = a;
                        if (loopOpenPts.Count >= 3)
                        {
                            List<Tuple<int, int, int>> triIndexes;
                            string triErr;
                            if (TryTriangulatePolygon(loopOpenPts, out triIndexes, out triErr))
                            {
                                double triAbsSumMm2 = 0.0;
                                for (int ti = 0; ti < triIndexes.Count; ti++)
                                {
                                    var tri = triIndexes[ti];
                                    var triMm2 = TriangleAbsAreaMm2(loopOpenPts[tri.Item1], loopOpenPts[tri.Item2], loopOpenPts[tri.Item3]);
                                    triAbsSumMm2 += triMm2;
                                    m.Triangles.Add(new AreaCalcTriangle
                                    {
                                        LoopIndex = li,
                                        TriangleIndex = ti,
                                        AIndex = tri.Item1,
                                        BIndex = tri.Item2,
                                        CIndex = tri.Item3,
                                        AreaMm2 = RoundDown(triMm2, 2)
                                    });
                                }

                                var sign = (a < 0.0) ? -1.0 : 1.0;
                                loopSignedMm2ForCalc = sign * triAbsSumMm2;
                                triangleCount += triIndexes.Count;
                            }
                            else if (!string.IsNullOrWhiteSpace(triErr))
                            {
                                m.Warnings.Add("Triangulation fallback(loop=" + li.ToString(CultureInfo.InvariantCulture) + "): " + triErr);
                            }
                        }

                        signedAreaMm2Total += loopSignedMm2ForCalc;
                    }

                    m.PerimeterMm = RoundDown(m.PerimeterMm, s.LengthDigits);
                    var absMm2 = Math.Abs(signedAreaMm2Total);
                    m.CalcAreaM2 = RoundDown(absMm2 / 1_000_000.0, s.AreaDigits);

                    if (Math.Abs(signedAreaMm2Total) > 1e-6)
                    {
                        m.CentroidXmm = cxWeighted / signedAreaMm2Total;
                        m.CentroidYmm = cyWeighted / signedAreaMm2Total;
                    }
                    else
                    {
                        var loc = area.Location as LocationPoint;
                        if (loc != null)
                        {
                            m.CentroidXmm = UnitHelper.FtToMm(loc.Point.X);
                            m.CentroidYmm = UnitHelper.FtToMm(loc.Point.Y);
                        }
                    }

                    m.DiffM2 = RoundDown(Math.Abs(m.RevitAreaM2 - m.CalcAreaM2), s.AreaDigits);
                    m.ShapeSummary = "loops=" + m.Edges.Select(e => e.LoopIndex).Distinct().Count().ToString(CultureInfo.InvariantCulture)
                                   + ", lines=" + lineCount.ToString(CultureInfo.InvariantCulture)
                                   + ", arcs=" + arcCount.ToString(CultureInfo.InvariantCulture)
                                   + ", triangles=" + triangleCount.ToString(CultureInfo.InvariantCulture);
                    m.TriangleCount = triangleCount;
                    m.Formula = triangleCount > 0
                        ? "A=ΣTri(" + triangleCount.ToString(CultureInfo.InvariantCulture) + ")/1e6"
                        : "A=shoelace(" + m.ShapeSummary + ")/1e6";
                    if (m.DiffM2 > s.DiffWarnThresholdM2) m.Status = "WARN";
                }
                catch (Exception ex)
                {
                    m.Status = "ERROR";
                    m.Warnings.Add(ex.Message);
                }

                models.Add(m);
            }

            return models;
        }

        public static void ApplyNumbering(List<AreaCalcModel> models, AreaCalcSettings s)
        {
            if (models.Count == 0) return;

            var sorted = models
                .OrderBy(m => Math.Round(m.CentroidYmm / s.RowToleranceMm, MidpointRounding.AwayFromZero))
                .ThenBy(m => m.CentroidXmm)
                .ThenBy(m => m.AreaId)
                .ToList();

            int n = s.NumberStart;
            foreach (var m in sorted)
            {
                var numCore = s.NumberPad > 0 ? n.ToString("D" + s.NumberPad.ToString(CultureInfo.InvariantCulture)) : n.ToString(CultureInfo.InvariantCulture);
                var no = (s.NumberPrefix ?? "") + numCore + (s.NumberSuffix ?? "");
                TrySetString(m.Area, s.ParamNo, no, m.Warnings);
                n += s.NumberStep;
            }
        }

        public static int PlotEdgeTexts(Document doc, View view, List<AreaCalcModel> models, AreaCalcSettings s)
        {
            if (view == null) return 0;

            var textTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            int created = 0;
            var occupancy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var noteOpts = BuildLengthTextOptions(textTypeId, s);

            foreach (var m in models)
            {
                foreach (var e in m.Edges)
                {
                    try
                    {
                        var mid = e.MidFt;
                        XYZ pt;
                        if (s.LengthTextCenterAtSegment)
                        {
                            // Keep the text insertion point at line-segment center so text follows scale changes stably.
                            pt = mid;
                        }
                        else
                        {
                            var baseOffFt = UnitHelper.MmToFt(s.LengthOffsetMm);
                            var stepOffFt = UnitHelper.MmToFt(s.LengthOffsetStepMm);
                            var cellKey = Math.Round(UnitHelper.FtToMm(mid.X), 0).ToString(CultureInfo.InvariantCulture)
                                       + "," + Math.Round(UnitHelper.FtToMm(mid.Y), 0).ToString(CultureInfo.InvariantCulture);
                            int k = 0;
                            occupancy.TryGetValue(cellKey, out k);
                            occupancy[cellKey] = k + 1;
                            var off = baseOffFt + (k * stepOffFt);
                            pt = new XYZ(mid.X + e.NormalFt.X * off, mid.Y + e.NormalFt.Y * off, mid.Z);
                        }

                        var txt = e.LengthMm.ToString("0.##", CultureInfo.InvariantCulture) + " mm";
                        var note = TextNote.Create(doc, view.Id, pt, txt, noteOpts);
                        TryRotateTextNoteAlongEdge(doc, view, note, e.DirectionFt, m.Warnings, e.LoopIndex, e.SegmentIndex);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        m.Warnings.Add("TextNote failed(loop=" + e.LoopIndex.ToString(CultureInfo.InvariantCulture) + ",seg=" + e.SegmentIndex.ToString(CultureInfo.InvariantCulture) + "): " + ex.Message);
                    }
                }
            }
            return created;
        }

        public static void WriteValues(List<AreaCalcModel> models, AreaCalcSettings s)
        {
            foreach (var m in models)
            {
                TrySetString(m.Area, s.ParamFormula, m.Formula, m.Warnings);
                TrySetDouble(m.Area, s.ParamAreaCalc, m.CalcAreaM2, m.Warnings);
                TrySetString(m.Area, s.ParamShapeSummary, m.ShapeSummary, m.Warnings);
                TrySetString(m.Area, s.ParamStatus, m.Status, m.Warnings);
            }
        }

        public static JObject BuildSummary(List<AreaCalcModel> models, int createdTextCount, AreaCalcSettings s)
        {
            var items = new JArray();
            foreach (var m in models)
            {
                var edgeArr = new JArray();
                foreach (var e in m.Edges)
                {
                    edgeArr.Add(new JObject
                    {
                        ["loopIndex"] = e.LoopIndex,
                        ["segmentIndex"] = e.SegmentIndex,
                        ["kind"] = e.Kind,
                        ["lengthMm"] = e.LengthMm
                    });
                }

                var warnArr = new JArray();
                foreach (var w in m.Warnings) warnArr.Add(w);

                items.Add(new JObject
                {
                    ["areaId"] = m.AreaId,
                    ["level"] = m.LevelName,
                    ["revitAreaM2"] = m.RevitAreaM2,
                    ["calcAreaM2"] = m.CalcAreaM2,
                    ["diffM2"] = m.DiffM2,
                    ["perimeterMm"] = m.PerimeterMm,
                    ["centroidMm"] = new JObject
                    {
                        ["x"] = Math.Round(m.CentroidXmm, 3),
                        ["y"] = Math.Round(m.CentroidYmm, 3)
                    },
                    ["status"] = m.Status,
                    ["shapeSummary"] = m.ShapeSummary,
                    ["formula"] = m.Formula,
                    ["triangleCount"] = m.TriangleCount,
                    ["edges"] = edgeArr,
                    ["warnings"] = warnArr
                });
            }

            var warnCount = models.Sum(m => m.Warnings.Count) + models.Count(m => m.DiffM2 > s.DiffWarnThresholdM2);
            return new JObject
            {
                ["ok"] = true,
                ["count"] = models.Count,
                ["textNotesCreated"] = createdTextCount,
                ["warnCount"] = warnCount,
                ["items"] = items,
                ["settings"] = new JObject
                {
                    ["boundaryLocation"] = s.BoundaryLocation,
                    ["rowToleranceMm"] = s.RowToleranceMm,
                    ["lengthDigits"] = s.LengthDigits,
                    ["areaDigits"] = s.AreaDigits,
                    ["diffWarnThresholdM2"] = s.DiffWarnThresholdM2,
                    ["lengthTextCenterAtSegment"] = s.LengthTextCenterAtSegment,
                    ["lengthTextHorizontalAlignment"] = s.LengthTextHorizontalAlignment
                }
            };
        }

        private static TextNoteOptions BuildLengthTextOptions(ElementId textTypeId, AreaCalcSettings s)
        {
            var opts = new TextNoteOptions(textTypeId);
            var t = (s.LengthTextHorizontalAlignment ?? "Center").Trim().ToLowerInvariant();
            switch (t)
            {
                case "left":
                    opts.HorizontalAlignment = HorizontalTextAlignment.Left;
                    break;
                case "right":
                    opts.HorizontalAlignment = HorizontalTextAlignment.Right;
                    break;
                default:
                    opts.HorizontalAlignment = HorizontalTextAlignment.Center;
                    break;
            }
            return opts;
        }

        private static List<XYZ> ToOpenLoop(List<XYZ> closedLoopPts)
        {
            var outPts = new List<XYZ>();
            if (closedLoopPts == null || closedLoopPts.Count == 0) return outPts;

            foreach (var p in closedLoopPts)
            {
                AddPointNoDup(outPts, p);
            }

            if (outPts.Count > 1 && outPts[0].DistanceTo(outPts[outPts.Count - 1]) <= 1e-7)
            {
                outPts.RemoveAt(outPts.Count - 1);
            }

            return outPts;
        }

        private static bool TryTriangulatePolygon(List<XYZ> polygonOpenPts, out List<Tuple<int, int, int>> triangles, out string error)
        {
            triangles = new List<Tuple<int, int, int>>();
            error = "";
            if (polygonOpenPts == null || polygonOpenPts.Count < 3)
            {
                error = "insufficient vertices";
                return false;
            }

            var pts = new List<XYZ>();
            foreach (var p in polygonOpenPts) AddPointNoDup(pts, p);
            if (pts.Count < 3)
            {
                error = "degenerate vertices";
                return false;
            }

            var signed = SignedAreaMm2(pts);
            if (Math.Abs(signed) <= 1e-6)
            {
                error = "near-zero area polygon";
                return false;
            }

            var idx = new List<int>(Enumerable.Range(0, pts.Count));
            if (signed < 0.0) idx.Reverse(); // normalize to CCW for ear clipping

            int guard = 0;
            int guardMax = Math.Max(16, pts.Count * pts.Count * 4);
            while (idx.Count > 3 && guard < guardMax)
            {
                bool foundEar = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i - 1 + idx.Count) % idx.Count];
                    int ib = idx[i];
                    int ic = idx[(i + 1) % idx.Count];
                    if (!IsConvexCcw(pts[ia], pts[ib], pts[ic])) continue;

                    bool containsAny = false;
                    for (int j = 0; j < idx.Count; j++)
                    {
                        int ip = idx[j];
                        if (ip == ia || ip == ib || ip == ic) continue;
                        if (PointInOrOnTriangle(pts[ip], pts[ia], pts[ib], pts[ic]))
                        {
                            containsAny = true;
                            break;
                        }
                    }
                    if (containsAny) continue;

                    triangles.Add(Tuple.Create(ia, ib, ic));
                    idx.RemoveAt(i);
                    foundEar = true;
                    break;
                }

                if (!foundEar)
                {
                    error = "ear clipping stalled";
                    return false;
                }
                guard++;
            }

            if (idx.Count == 3)
            {
                triangles.Add(Tuple.Create(idx[0], idx[1], idx[2]));
            }

            if (triangles.Count == 0)
            {
                error = "no triangle generated";
                return false;
            }

            return true;
        }

        private static double SignedAreaMm2(List<XYZ> openPts)
        {
            if (openPts == null || openPts.Count < 3) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < openPts.Count; i++)
            {
                int j = (i + 1) % openPts.Count;
                var x0 = UnitHelper.FtToMm(openPts[i].X);
                var y0 = UnitHelper.FtToMm(openPts[i].Y);
                var x1 = UnitHelper.FtToMm(openPts[j].X);
                var y1 = UnitHelper.FtToMm(openPts[j].Y);
                sum += (x0 * y1 - x1 * y0);
            }
            return 0.5 * sum;
        }

        private static double TriangleAbsAreaMm2(XYZ a, XYZ b, XYZ c)
        {
            var ax = UnitHelper.FtToMm(a.X);
            var ay = UnitHelper.FtToMm(a.Y);
            var bx = UnitHelper.FtToMm(b.X);
            var by = UnitHelper.FtToMm(b.Y);
            var cx = UnitHelper.FtToMm(c.X);
            var cy = UnitHelper.FtToMm(c.Y);
            var cross = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
            return 0.5 * Math.Abs(cross);
        }

        private static bool IsConvexCcw(XYZ a, XYZ b, XYZ c)
        {
            var ax = UnitHelper.FtToMm(a.X);
            var ay = UnitHelper.FtToMm(a.Y);
            var bx = UnitHelper.FtToMm(b.X);
            var by = UnitHelper.FtToMm(b.Y);
            var cx = UnitHelper.FtToMm(c.X);
            var cy = UnitHelper.FtToMm(c.Y);
            var cross = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
            return cross > 1e-6;
        }

        private static bool PointInOrOnTriangle(XYZ p, XYZ a, XYZ b, XYZ c)
        {
            var px = UnitHelper.FtToMm(p.X);
            var py = UnitHelper.FtToMm(p.Y);
            var ax = UnitHelper.FtToMm(a.X);
            var ay = UnitHelper.FtToMm(a.Y);
            var bx = UnitHelper.FtToMm(b.X);
            var by = UnitHelper.FtToMm(b.Y);
            var cx = UnitHelper.FtToMm(c.X);
            var cy = UnitHelper.FtToMm(c.Y);

            double c1 = ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));
            double c2 = ((cx - bx) * (py - by)) - ((cy - by) * (px - bx));
            double c3 = ((ax - cx) * (py - cy)) - ((ay - cy) * (px - cx));
            const double eps = 1e-6;
            return c1 >= -eps && c2 >= -eps && c3 >= -eps;
        }

        private static XYZ TryGetCurveDirection(Curve c, XYZ pStart, XYZ pEnd)
        {
            try
            {
                var der = c.ComputeDerivatives(0.5, true);
                if (der != null && der.BasisX != null && der.BasisX.GetLength() > 1e-9) return der.BasisX.Normalize();
            }
            catch
            {
                // Fallback to end points.
            }

            var v = pEnd - pStart;
            if (v.GetLength() > 1e-9) return v.Normalize();
            return XYZ.BasisX;
        }

        private static void TryRotateTextNoteAlongEdge(
            Document doc,
            View view,
            TextNote note,
            XYZ edgeDirFt,
            List<string> warnings,
            int loopIndex,
            int segmentIndex)
        {
            try
            {
                if (note == null || view == null || edgeDirFt == null) return;
                if (edgeDirFt.GetLength() <= 1e-9) return;

                var viewNormal = view.ViewDirection;
                if (viewNormal == null || viewNormal.GetLength() <= 1e-9) viewNormal = XYZ.BasisZ;
                else viewNormal = viewNormal.Normalize();

                // Use direction projected onto view plane, then rotate from view-right axis.
                var projected = edgeDirFt - viewNormal.Multiply(edgeDirFt.DotProduct(viewNormal));
                if (projected.GetLength() <= 1e-9) return;
                projected = projected.Normalize();

                var right = view.RightDirection;
                if (right == null || right.GetLength() <= 1e-9) right = XYZ.BasisX;
                else right = right.Normalize();

                var up = view.UpDirection;
                if (up == null || up.GetLength() <= 1e-9) up = XYZ.BasisY;
                else up = up.Normalize();

                var x = projected.DotProduct(right);
                var y = projected.DotProduct(up);
                if (Math.Abs(x) < 1e-9 && Math.Abs(y) < 1e-9) return;

                var angle = Math.Atan2(y, x);
                if (Math.Abs(angle) <= 1e-9) return;

                var axis = Line.CreateBound(note.Coord, note.Coord + viewNormal);
                ElementTransformUtils.RotateElement(doc, note.Id, axis, angle);
            }
            catch (Exception ex)
            {
                warnings.Add(
                    "TextNote rotate failed(loop="
                    + loopIndex.ToString(CultureInfo.InvariantCulture)
                    + ",seg="
                    + segmentIndex.ToString(CultureInfo.InvariantCulture)
                    + "): "
                    + ex.Message);
            }
        }

        private static void TrySetString(Element e, string paramName, string value, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return;
            var p = e.LookupParameter(paramName);
            if (p == null)
            {
                warnings.Add("Parameter missing: " + paramName);
                return;
            }
            if (p.IsReadOnly)
            {
                warnings.Add("Parameter readonly: " + paramName);
                return;
            }
            try
            {
                if (p.StorageType == StorageType.String) p.Set(value ?? "");
                else p.Set(value ?? "");
            }
            catch (Exception ex)
            {
                warnings.Add("Parameter set failed(" + paramName + "): " + ex.Message);
            }
        }

        private static void TrySetDouble(Element e, string paramName, double value, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(paramName)) return;
            var p = e.LookupParameter(paramName);
            if (p == null)
            {
                warnings.Add("Parameter missing: " + paramName);
                return;
            }
            if (p.IsReadOnly)
            {
                warnings.Add("Parameter readonly: " + paramName);
                return;
            }

            try
            {
                if (p.StorageType == StorageType.Double)
                {
                    p.Set(value);
                    return;
                }

                if (p.StorageType == StorageType.String)
                {
                    p.Set(value.ToString("0.####", CultureInfo.InvariantCulture));
                    return;
                }

                warnings.Add("Parameter type unsupported: " + paramName);
            }
            catch (Exception ex)
            {
                warnings.Add("Parameter set failed(" + paramName + "): " + ex.Message);
            }
        }

        private static void AddPointNoDup(List<XYZ> pts, XYZ p)
        {
            if (pts.Count == 0) { pts.Add(p); return; }
            var q = pts[pts.Count - 1];
            if (q.DistanceTo(p) > 1e-7) pts.Add(p);
        }

        private static void EnsureClosed(List<XYZ> pts)
        {
            if (pts.Count < 2) return;
            if (pts[0].DistanceTo(pts[pts.Count - 1]) > 1e-7) pts.Add(pts[0]);
        }

        private static double RoundDown(double v, int digits)
        {
            var p = Math.Pow(10.0, digits);
            if (p <= 0) return v;
            return Math.Floor(v * p) / p;
        }
    }

    [RpcCommand("area.calc_edge_lengths",
        Category = "Area",
        Kind = "read",
        Summary = "Analyze selected areas and return edge lengths/formula/area diff summary.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"area.calc_edge_lengths\", \"params\":{ \"fromSelection\":true, \"boundaryLocation\":\"Finish\" } }")]
    public sealed class AreaCalcEdgeLengthsCommand : IRevitCommandHandler
    {
        public string CommandName => "area.calc_edge_lengths|calc_area_edge_lengths";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();
            var settings = AreaCalcSettings.FromParams(p);
            var areas = AreaCalcUtil.ResolveTargetAreas(doc, uiapp, p);
            if (areas.Count == 0) return ResultUtil.Err("対象Areaが見つかりません。", "NO_AREAS");

            var models = AreaCalcUtil.Analyze(doc, areas, settings);
            var summary = AreaCalcUtil.BuildSummary(models, 0, settings);
            return ResultUtil.Ok(summary);
        }
    }

    [RpcCommand("area.calc_numbering",
        Category = "Area",
        Kind = "write",
        Summary = "Apply left-bottom numbering to target areas and write KSG_No.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"area.calc_numbering\", \"params\":{ \"fromSelection\":true, \"numberPrefix\":\"A-\", \"numberStart\":1, \"numberPad\":2 } }")]
    public sealed class AreaCalcNumberingCommand : IRevitCommandHandler
    {
        public string CommandName => "area.calc_numbering|calc_area_numbering";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");
            var p = cmd.Params as JObject ?? new JObject();
            var settings = AreaCalcSettings.FromParams(p);
            var areas = AreaCalcUtil.ResolveTargetAreas(doc, uiapp, p);
            if (areas.Count == 0) return ResultUtil.Err("対象Areaが見つかりません。", "NO_AREAS");

            var models = AreaCalcUtil.Analyze(doc, areas, settings);
            using (var tx = new Transaction(doc, "KSG Area Numbering"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                AreaCalcUtil.ApplyNumbering(models, settings);
                tx.Commit();
            }

            if (settings.RefreshView) { try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return ResultUtil.Ok(AreaCalcUtil.BuildSummary(models, 0, settings));
        }
    }

    [RpcCommand("area.calc_write_values",
        Category = "Area",
        Kind = "write",
        Summary = "Write formula/area/status values to KSG parameters for target areas.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"area.calc_write_values\", \"params\":{ \"fromSelection\":true, \"paramFormula\":\"KSG_Formula\", \"paramAreaCalc\":\"KSG_AreaCalc\" } }")]
    public sealed class AreaCalcWriteValuesCommand : IRevitCommandHandler
    {
        public string CommandName => "area.calc_write_values|calc_area_write_values";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");
            var p = cmd.Params as JObject ?? new JObject();
            var settings = AreaCalcSettings.FromParams(p);
            var areas = AreaCalcUtil.ResolveTargetAreas(doc, uiapp, p);
            if (areas.Count == 0) return ResultUtil.Err("対象Areaが見つかりません。", "NO_AREAS");

            var models = AreaCalcUtil.Analyze(doc, areas, settings);
            using (var tx = new Transaction(doc, "KSG Area Write Values"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                AreaCalcUtil.WriteValues(models, settings);
                tx.Commit();
            }

            if (settings.RefreshView) { try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return ResultUtil.Ok(AreaCalcUtil.BuildSummary(models, 0, settings));
        }
    }

    [RpcCommand("area.calc_run_all",
        Category = "Area",
        Kind = "write",
        Importance = "high",
        Summary = "Run KSG area-calc MVP flow: analyze, numbering, edge plotting, write values, diff check.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"area.calc_run_all\", \"params\":{ \"fromSelection\":true, \"plotEdgeText\":true, \"writeValues\":true, \"doNumbering\":true } }")]
    public sealed class AreaCalcRunAllCommand : IRevitCommandHandler
    {
        public string CommandName => "area.calc_run_all|calc_area_run_all";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");
            var p = cmd.Params as JObject ?? new JObject();
            var settings = AreaCalcSettings.FromParams(p);
            var areas = AreaCalcUtil.ResolveTargetAreas(doc, uiapp, p);
            if (areas.Count == 0) return ResultUtil.Err("対象Areaが見つかりません。", "NO_AREAS");

            var models = AreaCalcUtil.Analyze(doc, areas, settings);
            int createdText = 0;

            using (var tg = new TransactionGroup(doc, "KSG Area Calc Run All"))
            {
                tg.Start();

                if (settings.DoNumbering)
                {
                    using (var txN = new Transaction(doc, "KSG Numbering"))
                    {
                        txN.Start();
                        TxnUtil.ConfigureProceedWithWarnings(txN);
                        AreaCalcUtil.ApplyNumbering(models, settings);
                        txN.Commit();
                    }
                }

                if (settings.PlotEdgeText)
                {
                    var view = ResolveTargetView(doc, uiapp, p);
                    if (view != null)
                    {
                        using (var txT = new Transaction(doc, "KSG Edge Text"))
                        {
                            txT.Start();
                            TxnUtil.ConfigureProceedWithWarnings(txT);
                            createdText = AreaCalcUtil.PlotEdgeTexts(doc, view, models, settings);
                            txT.Commit();
                        }
                    }
                    else
                    {
                        foreach (var m in models) m.Warnings.Add("Target view not resolved for edge text.");
                    }
                }

                if (settings.WriteValues)
                {
                    using (var txW = new Transaction(doc, "KSG Write Values"))
                    {
                        txW.Start();
                        TxnUtil.ConfigureProceedWithWarnings(txW);
                        AreaCalcUtil.WriteValues(models, settings);
                        txW.Commit();
                    }
                }

                tg.Assimilate();
            }

            if (settings.RefreshView) { try { uiapp.ActiveUIDocument?.RefreshActiveView(); } catch { } }
            return ResultUtil.Ok(AreaCalcUtil.BuildSummary(models, createdText, settings));
        }

        private static View? ResolveTargetView(Document doc, UIApplication uiapp, JObject p)
        {
            var viewId = p.Value<int?>("viewId") ?? 0;
            if (viewId > 0)
            {
                return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View;
            }
            return uiapp.ActiveUIDocument?.ActiveView;
        }
    }
}
