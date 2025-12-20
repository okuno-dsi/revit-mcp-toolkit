// ================================================================
// File: Commands/Rooms/FindRoomPlaceableRegionsCommand.cs
// Desc: 指定レベルの「部屋配置可能な回路(PlanCircuit)」を列挙し、
//       面積・幾何重心(centroid)・ラベル点(labelPoint)・境界を返す
// API:  method = "find_room_placeable_regions"
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rooms
{
    public class FindRoomPlaceableRegionsCommand : IRevitCommandHandler
    {
        public string CommandName => "find_room_placeable_regions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null)
                    return new { ok = false, msg = "No active document.", code = "NO_DOCUMENT" };

                var p = (JObject)(cmd.Params ?? new JObject());
                int levelId = Get(p, "levelId", 0);
                if (levelId == 0)
                    return new { ok = false, msg = "levelId required.", code = "ARG_MISSING" };

                bool onlyEmpty = Get(p, "onlyEmpty", true);
                string coordUnits = (Get(p, "coordUnits", "mm") ?? "mm").Trim().ToLowerInvariant();
                bool includeLoops = Get(p, "includeLoops", false);
                bool includeLabelPoint = Get(p, "includeLabelPoint", true);
                bool debug = Get(p, "debug", false);

                var level = doc.GetElement(new ElementId(levelId)) as Level;
                if (level == null)
                    return new { ok = false, msg = $"levelId={levelId} is not a Level.", code = "BAD_LEVEL" };

                var regions = new List<object>();

                int totalCircuits = 0;
                int attempted = 0;
                int success = 0;

                int skipHasRoom = 0;
                int skipOpenOrCreateFail = 0;
                int skipNoBoundary = 0;
                int skipTooSmall = 0;

                var skipDetails = new List<object>();

                using (var outer = new Transaction(doc, "[MCP] Find Room Placeable Regions"))
                {
                    outer.Start();

                    var topo = doc.get_PlanTopology(level);
                    if (topo == null)
                    {
                        outer.RollBack();
                        return new { ok = false, msg = "PlanTopology not available. Check phase/design option.", code = "NO_TOPOLOGY" };
                    }

                    int idx = 0;
                    foreach (PlanCircuit circuit in topo.Circuits)
                    {
                        totalCircuits++;

                        bool hasRoom = SafeHasRoomLocated(circuit);
                        if (onlyEmpty && hasRoom)
                        {
                            skipHasRoom++;
                            if (debug) skipDetails.Add(new { circuitIndex = idx, reason = "HAS_ROOM" });
                            idx++;
                            continue;
                        }

                        attempted++;

                        XYZ centroidFt = null;
                        XYZ labelFt = null;
                        double areaFt2 = 0.0;
                        List<List<XYZ>> loopsFt = null;

                        using (var t = new SubTransaction(doc))
                        {
                            t.Start();
                            try
                            {
                                Autodesk.Revit.DB.Architecture.Room tmp = null;
                                try
                                {
                                    // ここで“開放回路/不整合フェーズ/設計オプション”などは例外になりやすい
                                    tmp = doc.Create.NewRoom(null, circuit);
                                }
                                catch (Exception exCreate)
                                {
                                    skipOpenOrCreateFail++;
                                    if (debug) skipDetails.Add(new { circuitIndex = idx, reason = "ROOM_CREATE_FAIL", ex = exCreate.Message });
                                    t.RollBack();
                                    idx++;
                                    continue;
                                }

                                if (tmp == null)
                                {
                                    skipOpenOrCreateFail++;
                                    if (debug) skipDetails.Add(new { circuitIndex = idx, reason = "ROOM_CREATE_NULL" });
                                    t.RollBack();
                                    idx++;
                                    continue;
                                }

                                if (includeLabelPoint)
                                {
                                    var lp = tmp.Location as LocationPoint;
                                    if (lp != null) labelFt = lp.Point;
                                }

                                var opts = new SpatialElementBoundaryOptions
                                {
                                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                                };
                                var segs = tmp.GetBoundarySegments(opts);
                                if (segs == null || segs.Count == 0)
                                {
                                    skipNoBoundary++;
                                    if (debug) skipDetails.Add(new { circuitIndex = idx, reason = "NO_BOUNDARY" });
                                    t.RollBack();
                                    idx++;
                                    continue;
                                }

                                loopsFt = ConvertBoundaryToLoops(segs);
                                (areaFt2, centroidFt) = ComputeCompositeAreaAndCentroid(loopsFt);

                                if (areaFt2 <= 1e-10 || centroidFt == null)
                                {
                                    skipTooSmall++;
                                    if (debug) skipDetails.Add(new { circuitIndex = idx, reason = "AREA_TOO_SMALL_OR_NO_CENTROID", areaFt2 });
                                    t.RollBack();
                                    idx++;
                                    continue;
                                }

                                t.RollBack();
                            }
                            catch (Exception ex)
                            {
                                try { t.RollBack(); } catch { }
                                skipOpenOrCreateFail++;
                                if (debug) skipDetails.Add(new { circuitIndex = idx, reason = "EXCEPTION", ex = ex.Message });
                                idx++;
                                continue;
                            }
                        }

                        string outUnits = ToUnitKey(coordUnits);
                        var centroidOut = ConvertPointFromInternal(centroidFt, outUnits);
                        var labelOut = (labelFt != null) ? ConvertPointFromInternal(labelFt, outUnits) : null;

                        List<List<object>> loopsOut = null;
                        if (includeLoops && loopsFt != null)
                        {
                            loopsOut = new List<List<object>>();
                            foreach (var loop in loopsFt)
                            {
                                var arr = new List<object>();
                                foreach (var pt in loop)
                                    arr.Add(ConvertPointFromInternal(pt, outUnits));
                                loopsOut.Add(arr);
                            }
                        }

                        double areaM2 = UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);

                        regions.Add(new
                        {
                            circuitIndex = idx,
                            isClosed = true,
                            hasRoom = hasRoom,
                            area_m2 = Math.Round(areaM2, 6),
                            centroid = centroidOut,
                            labelPoint = labelOut,
                            loops = loopsOut
                        });

                        success++;
                        idx++;
                    }

                    outer.RollBack();
                }

                var result = new
                {
                    ok = true,
                    levelId = levelId,
                    regions = regions
                };

                if (debug)
                {
                    return new
                    {
                        ok = true,
                        levelId = levelId,
                        regions = regions,
                        diagnostics = new
                        {
                            totalCircuits,
                            attempted,
                            success,
                            skipped = new
                            {
                                hasRoom = skipHasRoom,
                                createFailOrOpen = skipOpenOrCreateFail,
                                noBoundary = skipNoBoundary,
                                tooSmall = skipTooSmall
                            },
                            skipDetails
                        }
                    };
                }

                return result;
            }
            catch (Exception ex)
            {
                RevitLogger.Error($"find_room_placeable_regions error: {ex}");
                return new { ok = false, msg = ex.Message, code = "EXCEPTION" };
            }
        }        
        // ---------- Helpers ----------

        private static T Get<T>(JObject p, string key, T def)
        {
            try
            {
                var tok = p[key];
                if (tok == null || tok.Type == JTokenType.Null) return def;
                return tok.Value<T>();
            }
            catch { return def; }
        }

        private static bool SafeHasRoomLocated(PlanCircuit c)
        {
            try { return c.IsRoomLocated; }
            catch { return false; }
        }

        private static List<List<XYZ>> ConvertBoundaryToLoops(IList<IList<BoundarySegment>> segs)
        {
            // segs は複数ループ（外周+孔）を含む。各 IList<BoundarySegment> が1ループ。
            var loops = new List<List<XYZ>>();
            if (segs == null) return loops;

            foreach (var loopSegs in segs)
            {
                var pts = new List<XYZ>();
                XYZ first = null;
                XYZ prev = null;

                foreach (var s in loopSegs)
                {
                    var c = s.GetCurve();
                    // 境界は通常直線が多いが、円弧などもあり得る → Tessellate でポリライン化
                    var tess = c.Tessellate();
                    for (int i = 0; i < tess.Count; i++)
                    {
                        var p = tess[i];
                        if (pts.Count == 0)
                        {
                            pts.Add(p);
                            first = p;
                            prev = p;
                        }
                        else
                        {
                            if (!AlmostEqual(prev, p))
                            {
                                pts.Add(p);
                                prev = p;
                            }
                        }
                    }
                }

                // 閉合確保
                if (pts.Count >= 3 && !AlmostEqual(pts[0], pts[pts.Count - 1]))
                    pts.Add(pts[0]);

                if (pts.Count >= 4)
                    loops.Add(pts);
            }

            return loops;
        }

        private static bool AlmostEqual(XYZ a, XYZ b, double tol = 1e-8)
        {
            return a.DistanceTo(b) <= tol;
        }

        /// <summary>
        /// 複数ループ（外周＋孔）から合成面積(ft^2)と幾何重心(ft)を算出。
        /// Shoelace + 穴は負符号で加算。
        /// </summary>
        private static (double areaFt2, XYZ centroidFt) ComputeCompositeAreaAndCentroid(List<List<XYZ>> loops)
        {
            double A = 0.0;     // 符号付き面積合計
            double Cx6A = 0.0;  // 6A*Cx 合計
            double Cy6A = 0.0;  // 6A*Cy 合計
            double z = (loops.Count > 0 && loops[0].Count > 0) ? loops[0][0].Z : 0.0;

            foreach (var loop in loops)
            {
                var (a, cx6a, cy6a) = PolygonAreaCentroid(loop);
                A += a;
                Cx6A += cx6a;
                Cy6A += cy6a;
            }

            if (Math.Abs(A) < 1e-12)
                return (0.0, new XYZ(0, 0, z));

            double Cx = Cx6A / (6.0 * A);
            double Cy = Cy6A / (6.0 * A);

            return (Math.Abs(A), new XYZ(Cx, Cy, z));
        }

        /// <summary>
        /// Shoelace公式。閉ループ（最後点=最初点）前提。
        /// 戻り: (符号付き面積A, 6*A*Cx, 6*A*Cy)
        /// </summary>
        private static (double A, double Cx6A, double Cy6A) PolygonAreaCentroid(List<XYZ> pts)
        {
            double A = 0.0;
            double Cx6A = 0.0;
            double Cy6A = 0.0;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p = pts[i];
                var q = pts[i + 1];
                double cross = p.X * q.Y - q.X * p.Y;
                A += cross;
                Cx6A += (p.X + q.X) * cross;
                Cy6A += (p.Y + q.Y) * cross;
            }
            A *= 0.5;
            return (A, Cx6A, Cy6A);
        }

        private static string ToUnitKey(string s)
        {
            switch ((s ?? "mm").Trim().ToLowerInvariant())
            {
                case "ft": return "ft";
                case "m": return "m";
                default: return "mm";
            }
        }

        private static object PointOut(XYZ p, string units)
        {
            return ConvertPointFromInternal(p, units);
        }

        private static object ConvertPointFromInternal(XYZ p, string units)
        {
            double x = p.X, y = p.Y, z = p.Z; // internal ft
            if (units == "mm")
            {
                x = UnitUtils.ConvertFromInternalUnits(x, UnitTypeId.Millimeters);
                y = UnitUtils.ConvertFromInternalUnits(y, UnitTypeId.Millimeters);
                z = UnitUtils.ConvertFromInternalUnits(z, UnitTypeId.Millimeters);
            }
            else if (units == "m")
            {
                x = UnitUtils.ConvertFromInternalUnits(x, UnitTypeId.Meters);
                y = UnitUtils.ConvertFromInternalUnits(y, UnitTypeId.Meters);
                z = UnitUtils.ConvertFromInternalUnits(z, UnitTypeId.Meters);
            }
            // ft はそのまま
            return new { x, y, z, units };
        }
    }
}

