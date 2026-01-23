// ================================================================
// File: RevitMCPAddin/Commands/EgressPathCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: Waypoint-guided egress path (PathOfTravel) utilities.
// Notes  :
//  - PathOfTravel runs in a plan view plane; Z is snapped to view level.
//  - Input/Output coordinates are mm (consistent with this project).
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Egress
{
    internal static class EgressJson
    {
        public static bool TryGetViewPlan(UIApplication uiapp, JObject p, out ViewPlan view, out string msg)
        {
            view = null!;
            msg = "";

            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) { msg = "アクティブドキュメントがありません。"; return false; }

            View v;
            var viewId = p.Value<int?>("viewId");
            if (viewId.HasValue && viewId.Value > 0)
            {
                v = doc.GetElement(ElementIdCompat.From(viewId.Value)) as View;
                if (v == null) { msg = $"viewId={viewId.Value} のビューが見つかりません。"; return false; }
            }
            else
            {
                v = doc.ActiveView;
                if (v == null) { msg = "アクティブビューがありません。"; return false; }
            }

            if (v.IsTemplate) { msg = "ビュー テンプレートには実行できません。"; return false; }
            if (!(v is ViewPlan vp))
            {
                msg = "このコマンドは平面ビュー(ViewPlan: Floor/Ceiling plan)でのみ実行できます。";
                return false;
            }

            // PathOfTravel is designed for floor plan views; allow ceiling plans as best-effort.
            if (vp.ViewType != ViewType.FloorPlan && vp.ViewType != ViewType.CeilingPlan)
            {
                msg = $"このビュー種別では実行できません: ViewType={vp.ViewType}";
                return false;
            }

            view = vp;
            return true;
        }

        public static bool TryParsePointMm(JToken? tok, out XYZ ptFt, out string msg)
        {
            ptFt = null!;
            msg = "";
            try
            {
                if (!(tok is JObject o)) { msg = "点は {x,y,z} のオブジェクトで指定してください。"; return false; }
                if (!o.ContainsKey("x") || !o.ContainsKey("y")) { msg = "点は x,y が必要です。"; return false; }
                var x = (double)o["x"]!;
                var y = (double)o["y"]!;
                var z = o.ContainsKey("z") ? (double)o["z"]! : 0.0;
                ptFt = new XYZ(UnitHelper.MmToFt(x), UnitHelper.MmToFt(y), UnitHelper.MmToFt(z));
                return true;
            }
            catch
            {
                msg = "点の数値解釈に失敗しました（mm）。";
                return false;
            }
        }

        public static JObject PointToMm(XYZ p)
        {
            return new JObject
            {
                ["x"] = Math.Round(UnitHelper.FtToMm(p.X), 3),
                ["y"] = Math.Round(UnitHelper.FtToMm(p.Y), 3),
                ["z"] = Math.Round(UnitHelper.FtToMm(p.Z), 3),
            };
        }
    }

    internal static class EgressGeom
    {
        public static XYZ SnapZ(XYZ p, double zFt) => new XYZ(p.X, p.Y, zFt);

        public static double PolylineLengthFt(IList<XYZ> pts)
        {
            if (pts == null || pts.Count < 2) return 0.0;
            double sum = 0.0;
            for (int i = 1; i < pts.Count; i++)
                sum += pts[i - 1].DistanceTo(pts[i]);
            return sum;
        }

        public static string FormatLenLabel(string fmt, double lenM)
        {
            var s = (fmt ?? string.Empty);
            if (string.IsNullOrWhiteSpace(s)) s = "{len_m:0.00} m";

            // Replace first {len_m[:format]} token (simple, non-regex).
            var idx = s.IndexOf("{len_m", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return s;
            var end = s.IndexOf("}", idx, StringComparison.Ordinal);
            if (end < 0) return s;

            var token = s.Substring(idx, end - idx + 1);
            string numFmt = "0.00";
            var colon = token.IndexOf(':');
            if (colon >= 0 && token.EndsWith("}", StringComparison.Ordinal))
            {
                numFmt = token.Substring(colon + 1, token.Length - colon - 2);
                if (string.IsNullOrWhiteSpace(numFmt)) numFmt = "0.00";
            }

            var repl = lenM.ToString(numFmt, CultureInfo.InvariantCulture);
            return s.Replace(token, repl);
        }
    }

    // ------------------------------------------------------------
    // route.find_shortest_paths (analysis-only)
    // ------------------------------------------------------------
    [RpcCommand("route.find_shortest_paths",
        Aliases = new[] { "revit.route.findShortestPaths" },
        Category = "Route",
        Tags = new[] { "Route", "PathOfTravel", "Analysis" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Summary = "Find shortest travel polylines in a plan view (PathOfTravel.FindShortestPaths). Input/Output points are mm.",
        Requires = new[] { "start,end|starts,ends" },
        Constraints = new[]
        {
            "Works only in ViewPlan (Floor/Ceiling plan).",
            "Points are in mm; Z is snapped to the view's level elevation.",
            "Analysis-only: does not create elements."
        })]
    public class FindShortestPathsCommand : IRevitCommandHandler
    {
        public string CommandName => "route.find_shortest_paths";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = cmd.Params as JObject ?? new JObject();

            if (!EgressJson.TryGetViewPlan(uiapp, p, out var view, out var why))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("INVALID_VIEW", why), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var zView = (view.GenLevel != null ? view.GenLevel.Elevation : 0.0);

            var starts = new List<XYZ>();
            var ends = new List<XYZ>();

            if (p["start"] != null || p["end"] != null)
            {
                if (!EgressJson.TryParsePointMm(p["start"], out var s0, out var em1))
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("INVALID_START", em1), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                if (!EgressJson.TryParsePointMm(p["end"], out var e0, out var em2))
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("INVALID_END", em2), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                starts.Add(EgressGeom.SnapZ(s0, zView));
                ends.Add(EgressGeom.SnapZ(e0, zView));
            }
            else
            {
                if (!(p["starts"] is JArray sa) || sa.Count == 0)
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_STARTS", "starts[] が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                if (!(p["ends"] is JArray ea) || ea.Count == 0)
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_ENDS", "ends[] が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

                foreach (var t in sa)
                {
                    if (!EgressJson.TryParsePointMm(t, out var s, out var em)) continue;
                    starts.Add(EgressGeom.SnapZ(s, zView));
                }
                foreach (var t in ea)
                {
                    if (!EgressJson.TryParsePointMm(t, out var e, out var em)) continue;
                    ends.Add(EgressGeom.SnapZ(e, zView));
                }
                if (starts.Count == 0 || ends.Count == 0)
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("INVALID_POINTS", "starts/ends の解釈に失敗しました。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);
            }

            IList<IList<XYZ>> polylines;
            try
            {
                polylines = PathOfTravel.FindShortestPaths(view, starts, ends);
            }
            catch (Exception ex)
            {
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("FIND_SHORTEST_PATHS_FAILED", ex.Message), uiapp, cmd.Command, sw.ElapsedMilliseconds);
            }

            var items = new JArray();
            double bestLenM = 0.0;
            int bestIndex = -1;

            if (polylines != null)
            {
                for (int i = 0; i < polylines.Count; i++)
                {
                    var pts = polylines[i];
                    var lenFt = EgressGeom.PolylineLengthFt(pts);
                    var lenM = UnitUtils.ConvertFromInternalUnits(lenFt, UnitTypeId.Meters);
                    if (lenM > bestLenM) { bestLenM = lenM; bestIndex = i; }

                    var ptsArr = new JArray();
                    if (pts != null)
                    {
                        foreach (var q in pts)
                            ptsArr.Add(EgressJson.PointToMm(q));
                    }

                    items.Add(new JObject
                    {
                        ["index"] = i,
                        ["lengthM"] = Math.Round(lenM, 3),
                        ["points"] = ptsArr
                    });
                }
            }

            var ok = (bestIndex >= 0 && bestLenM > 0.0);
            var payload = new JObject
            {
                ["ok"] = ok,
                ["msg"] = ok ? "ok" : "NoPathOfTravel: could not find a route in the given view.",
                ["data"] = new JObject
                {
                    ["count"] = polylines != null ? polylines.Count : 0,
                    ["bestIndex"] = bestIndex,
                    ["bestLengthM"] = Math.Round(bestLenM, 3),
                    ["items"] = items
                }
            };
            if (!ok)
            {
                payload["code"] = "NO_PATH";
                payload["nextActions"] = new JArray(
                    "Check crop region and view visibility (PathOfTravel uses visible geometry).",
                    "Ensure start/end points are not inside walls/obstacles.",
                    "Try a different viewId on the same level."
                );
            }
            return RpcResultEnvelope.StandardizePayload(payload, uiapp, cmd.Command, sw.ElapsedMilliseconds);
        }
    }

    // ------------------------------------------------------------
    // path.waypoints.get
    // ------------------------------------------------------------
    [RpcCommand("path.waypoints.get",
        Aliases = new[] { "revit.path.waypoints.get" },
        Category = "Route",
        Tags = new[] { "PathOfTravel", "Waypoints", "Read" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Summary = "Get waypoints of a PathOfTravel element (mm).",
        Requires = new[] { "pathId" },
        Constraints = new[]
        {
            "pathId must be a PathOfTravel element id."
        })]
    public class GetPathWaypointsCommand : IRevitCommandHandler
    {
        public string CommandName => "path.waypoints.get";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = cmd.Params as JObject ?? new JObject();

            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("NO_DOC", "アクティブドキュメントがありません。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var pathId = p.Value<int?>("pathId") ?? p.Value<int?>("elementId");
            if (!pathId.HasValue || pathId.Value <= 0)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_PATH_ID", "pathId が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var el = doc.GetElement(ElementIdCompat.From(pathId.Value));
            if (!(el is PathOfTravel path))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("NOT_PATH_OF_TRAVEL", $"pathId={pathId.Value} は PathOfTravel ではありません。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            IList<XYZ> wps;
            try { wps = path.GetWaypoints(); }
            catch (Exception ex)
            {
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("GET_WAYPOINTS_FAILED", ex.Message), uiapp, cmd.Command, sw.ElapsedMilliseconds);
            }

            var arr = new JArray();
            if (wps != null)
            {
                foreach (var q in wps)
                    arr.Add(EgressJson.PointToMm(q));
            }

            var payload = new JObject
            {
                ["ok"] = true,
                ["msg"] = "ok",
                ["data"] = new JObject
                {
                    ["pathId"] = path.Id.IntValue(),
                    ["waypoints"] = arr,
                    ["count"] = wps != null ? wps.Count : 0
                }
            };
            return RpcResultEnvelope.StandardizePayload(payload, uiapp, cmd.Command, sw.ElapsedMilliseconds);
        }
    }

    // ------------------------------------------------------------
    // path.waypoints.set
    // ------------------------------------------------------------
    [RpcCommand("path.waypoints.set",
        Aliases = new[] { "revit.path.waypoints.set" },
        Category = "Route",
        Tags = new[] { "PathOfTravel", "Waypoints", "Write" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Summary = "Replace all waypoints of a PathOfTravel element (mm) and optionally update the path.",
        Requires = new[] { "pathId", "waypoints[]" },
        Constraints = new[]
        {
            "pathId must be a PathOfTravel element id.",
            "Z is snapped to the owner view level when possible."
        })]
    public class SetPathWaypointsCommand : IRevitCommandHandler
    {
        public string CommandName => "path.waypoints.set";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = cmd.Params as JObject ?? new JObject();

            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("NO_DOC", "アクティブドキュメントがありません。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var pathId = p.Value<int?>("pathId") ?? p.Value<int?>("elementId");
            if (!pathId.HasValue || pathId.Value <= 0)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_PATH_ID", "pathId が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var el = doc.GetElement(ElementIdCompat.From(pathId.Value));
            if (!(el is PathOfTravel path))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("NOT_PATH_OF_TRAVEL", $"pathId={pathId.Value} は PathOfTravel ではありません。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var wpTok = p["waypoints"] as JArray;
            if (wpTok == null)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_WAYPOINTS", "waypoints[] が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            // Determine snap Z from owner view level when possible.
            double zView = 0.0;
            try
            {
                var ov = doc.GetElement(path.OwnerViewId) as ViewPlan;
                if (ov?.GenLevel != null) zView = ov.GenLevel.Elevation;
            }
            catch { /* ignore */ }

            var newWps = new List<XYZ>();
            foreach (var t in wpTok)
            {
                if (!EgressJson.TryParsePointMm(t, out var pt, out var em)) continue;
                newWps.Add(EgressGeom.SnapZ(pt, zView));
            }

            bool doUpdate = p.Value<bool?>("update") != false;

            using (var tx = new Transaction(doc, "PathOfTravel: Set Waypoints"))
            {
                tx.Start();
                try
                {
                    // Remove existing waypoints (from tail)
                    var cur = path.GetWaypoints();
                    if (cur != null)
                    {
                        for (int i = cur.Count - 1; i >= 0; i--)
                            path.RemoveWaypoint(i);
                    }

                    for (int i = 0; i < newWps.Count; i++)
                        path.InsertWaypoint(newWps[i], i);

                    PathOfTravelCalculationStatus st = PathOfTravelCalculationStatus.Success;
                    if (doUpdate)
                        st = path.Update();

                    tx.Commit();

                    var payload = new JObject
                    {
                        ["ok"] = true,
                        ["msg"] = "ok",
                        ["data"] = new JObject
                        {
                            ["pathId"] = path.Id.IntValue(),
                            ["waypointsApplied"] = newWps.Count,
                            ["updated"] = doUpdate,
                            ["status"] = st.ToString()
                        }
                    };
                    return RpcResultEnvelope.StandardizePayload(payload, uiapp, cmd.Command, sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("SET_WAYPOINTS_FAILED", ex.Message), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                }
            }
        }
    }

    // ------------------------------------------------------------
    // path.update
    // ------------------------------------------------------------
    [RpcCommand("path.update",
        Aliases = new[] { "revit.path.update" },
        Category = "Route",
        Tags = new[] { "PathOfTravel", "Update", "Write" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Summary = "Recalculate an existing PathOfTravel element.",
        Requires = new[] { "pathId" })]
    public class UpdatePathOfTravelCommand : IRevitCommandHandler
    {
        public string CommandName => "path.update";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = cmd.Params as JObject ?? new JObject();

            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("NO_DOC", "アクティブドキュメントがありません。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var pathId = p.Value<int?>("pathId") ?? p.Value<int?>("elementId");
            if (!pathId.HasValue || pathId.Value <= 0)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_PATH_ID", "pathId が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var el = doc.GetElement(ElementIdCompat.From(pathId.Value));
            if (!(el is PathOfTravel path))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("NOT_PATH_OF_TRAVEL", $"pathId={pathId.Value} は PathOfTravel ではありません。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            using (var tx = new Transaction(doc, "PathOfTravel: Update"))
            {
                tx.Start();
                try
                {
                    var st = path.Update();
                    tx.Commit();
                    var payload = new JObject
                    {
                        ["ok"] = true,
                        ["msg"] = "ok",
                        ["data"] = new JObject
                        {
                            ["pathId"] = path.Id.IntValue(),
                            ["status"] = st.ToString()
                        }
                    };
                    return RpcResultEnvelope.StandardizePayload(payload, uiapp, cmd.Command, sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("PATH_UPDATE_FAILED", ex.Message), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                }
            }
        }
    }

    internal static class EgressDoorUtil
    {
        public static bool TryGetFamilyInstanceCenterAndFacing(Document doc, int id, out FamilyInstance fi, out XYZ centerFt, out XYZ facing, out string msg)
        {
            fi = null!;
            centerFt = null!;
            facing = null!;
            msg = "";

            var el = doc.GetElement(ElementIdCompat.From(id));
            if (!(el is FamilyInstance inst)) { msg = $"elementId={id} は FamilyInstance ではありません。"; return false; }
            fi = inst;

            XYZ c = null;
            try
            {
                var lp = inst.Location as LocationPoint;
                if (lp?.Point != null) c = lp.Point;
            }
            catch { /* ignore */ }
            if (c == null)
            {
                try
                {
                    var bb = inst.get_BoundingBox(null);
                    if (bb != null) c = (bb.Min + bb.Max) * 0.5;
                }
                catch { /* ignore */ }
            }
            if (c == null) { msg = $"elementId={id} の中心点を取得できません。"; return false; }
            centerFt = c;

            XYZ f = XYZ.BasisY;
            try
            {
                var fo = inst.FacingOrientation;
                if (fo != null && fo.GetLength() > 1e-9) f = fo.Normalize();
            }
            catch { /* ignore */ }
            facing = f;
            return true;
        }

        public static bool TryComputeDoorApproachPointBestEffort(
            ViewPlan view,
            int doorId,
            XYZ startOnLevel,
            double offsetMm,
            out XYZ endOnLevel,
            out string msg)
        {
            endOnLevel = null!;
            msg = "";

            var doc = view.Document;

            if (!TryGetFamilyInstanceCenterAndFacing(doc, doorId, out var fi, out var center, out var facing, out var em))
            {
                msg = em;
                return false;
            }

            var zView = (view.GenLevel != null ? view.GenLevel.Elevation : 0.0);
            var offFt = UnitHelper.MmToFt(offsetMm);
            if (offFt < UnitHelper.MmToFt(50.0)) offFt = UnitHelper.MmToFt(300.0);

            var c0 = EgressGeom.SnapZ(center, zView);
            var p1 = EgressGeom.SnapZ(new XYZ(center.X + facing.X * offFt, center.Y + facing.Y * offFt, zView), zView);
            var p2 = EgressGeom.SnapZ(new XYZ(center.X - facing.X * offFt, center.Y - facing.Y * offFt, zView), zView);

            // Prefer point that yields a valid shortest path from start.
            try
            {
                var starts = new List<XYZ> { startOnLevel };

                var poly0 = PathOfTravel.FindShortestPaths(view, starts, new List<XYZ> { c0 });
                if (poly0 != null && poly0.Count > 0 && poly0[0] != null && poly0[0].Count >= 2) { endOnLevel = c0; return true; }

                var poly1 = PathOfTravel.FindShortestPaths(view, starts, new List<XYZ> { p1 });
                if (poly1 != null && poly1.Count > 0 && poly1[0] != null && poly1[0].Count >= 2) { endOnLevel = p1; return true; }

                var poly2 = PathOfTravel.FindShortestPaths(view, starts, new List<XYZ> { p2 });
                if (poly2 != null && poly2.Count > 0 && poly2[0] != null && poly2[0].Count >= 2) { endOnLevel = p2; return true; }
            }
            catch { /* ignore */ }

            // Fallback to center
            endOnLevel = c0;
            return true;
        }
    }

    internal static class EgressRoomUtil
    {
        public static double ComputeRoomZProbeFt(Autodesk.Revit.DB.Architecture.Room room, Level? level)
        {
            double zProbeFt = 0.0;
            try
            {
                var bb = room.get_BoundingBox(null);
                if (bb != null) zProbeFt = (bb.Min.Z + bb.Max.Z) * 0.5;
            }
            catch { /* ignore */ }
            if (Math.Abs(zProbeFt) < 1e-9)
            {
                try
                {
                    var lp = room.Location as LocationPoint;
                    if (lp?.Point != null) zProbeFt = lp.Point.Z;
                }
                catch { /* ignore */ }
            }
            if (Math.Abs(zProbeFt) < 1e-9)
            {
                try { zProbeFt = (level != null ? level.Elevation : 0.0) + UnitHelper.MmToFt(1000.0); } catch { /* ignore */ }
            }
            return zProbeFt;
        }

        public static bool IsPointInRoomSafe(Autodesk.Revit.DB.Architecture.Room room, XYZ p)
        {
            try { return room.IsPointInRoom(p); } catch { return false; }
        }

        public static List<Curve> TryGetRoomBoundaryCurves(Autodesk.Revit.DB.Architecture.Room room)
        {
            try
            {
                var opt = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                var loops = room.GetBoundarySegments(opt);
                var curves = new List<Curve>();
                if (loops == null) return curves;
                foreach (var loop in loops)
                {
                    if (loop == null) continue;
                    foreach (var seg in loop)
                    {
                        try
                        {
                            var c = seg?.GetCurve();
                            if (c != null) curves.Add(c);
                        }
                        catch { /* ignore */ }
                    }
                }
                return curves;
            }
            catch
            {
                return new List<Curve>();
            }
        }

        public static double MinDistanceToCurvesFt(XYZ ptOnPlane, List<Curve> curves)
        {
            if (curves == null || curves.Count == 0) return double.MaxValue;
            double min = double.MaxValue;
            foreach (var c in curves)
            {
                if (c == null) continue;
                try
                {
                    var pr = c.Project(ptOnPlane);
                    if (pr == null) continue;
                    var q = pr.XYZPoint;
                    if (q == null) continue;
                    var d = q.DistanceTo(ptOnPlane);
                    if (d < min) min = d;
                }
                catch { /* ignore */ }
            }
            return min;
        }

        public static bool TryComputeRoomDoorApproachPoints(
            ViewPlan view,
            Autodesk.Revit.DB.Architecture.Room room,
            int roomDoorId,
            double offsetMm,
            out XYZ insideOnLevel,
            out XYZ outsideOnLevel,
            out string warnOrErr)
        {
            insideOnLevel = null!;
            outsideOnLevel = null!;
            warnOrErr = "";

            var doc = view.Document;

            if (!EgressDoorUtil.TryGetFamilyInstanceCenterAndFacing(doc, roomDoorId, out var fi, out var center, out var facing, out var em))
            {
                warnOrErr = "roomDoorId: " + em;
                return false;
            }

            var zView = (view.GenLevel != null ? view.GenLevel.Elevation : 0.0);
            var zProbe = ComputeRoomZProbeFt(room, doc.GetElement(room.LevelId) as Level);

            var offFt = UnitHelper.MmToFt(offsetMm);
            if (offFt < UnitHelper.MmToFt(50.0)) offFt = UnitHelper.MmToFt(200.0);

            var plusProbe = new XYZ(center.X + facing.X * offFt, center.Y + facing.Y * offFt, zProbe);
            var minusProbe = new XYZ(center.X - facing.X * offFt, center.Y - facing.Y * offFt, zProbe);

            bool plusIn = IsPointInRoomSafe(room, plusProbe);
            bool minusIn = IsPointInRoomSafe(room, minusProbe);

            var plusLevel = EgressGeom.SnapZ(new XYZ(plusProbe.X, plusProbe.Y, zView), zView);
            var minusLevel = EgressGeom.SnapZ(new XYZ(minusProbe.X, minusProbe.Y, zView), zView);

            if (plusIn && !minusIn)
            {
                insideOnLevel = plusLevel;
                outsideOnLevel = minusLevel;
                return true;
            }
            if (!plusIn && minusIn)
            {
                insideOnLevel = minusLevel;
                outsideOnLevel = plusLevel;
                return true;
            }

            // Ambiguous: choose one as inside (fallback to no-constraint).
            insideOnLevel = plusLevel;
            outsideOnLevel = minusLevel;
            warnOrErr = "roomDoorId の内外判定が曖昧でした（両側が Room か、両側が Room 外）。向き・部屋配置を確認してください。";
            return true;
        }

        public static bool TryComputeMostRemotePointInRoom(
            ViewPlan view,
            Autodesk.Revit.DB.Architecture.Room room,
            XYZ roomDoorInsideOnLevel,
            double clearanceMm,
            double gridMm,
            int maxSamples,
            int maxSolverCandidates,
            out XYZ bestOnLevel,
            out double bestLenM,
            out JObject debug)
        {
            bestOnLevel = null!;
            bestLenM = 0.0;
            debug = new JObject();

            var doc = view.Document;
            var level = doc.GetElement(room.LevelId) as Level;
            var zView = (view.GenLevel != null ? view.GenLevel.Elevation : (level != null ? level.Elevation : 0.0));
            var zProbe = ComputeRoomZProbeFt(room, level);

            var bb = room.get_BoundingBox(view);
            if (bb == null) bb = room.get_BoundingBox(null);
            if (bb == null) return false;

            double clearanceFt = UnitHelper.MmToFt(clearanceMm);
            if (clearanceFt < UnitHelper.MmToFt(50.0)) clearanceFt = UnitHelper.MmToFt(300.0);

            double gridFt = UnitHelper.MmToFt(gridMm);
            if (gridFt < UnitHelper.MmToFt(150.0)) gridFt = UnitHelper.MmToFt(600.0);

            // Auto adjust grid when too many samples.
            try
            {
                var width = Math.Max(0.0, bb.Max.X - bb.Min.X);
                var height = Math.Max(0.0, bb.Max.Y - bb.Min.Y);
                var nx = Math.Max(1, (int)Math.Ceiling(width / gridFt));
                var ny = Math.Max(1, (int)Math.Ceiling(height / gridFt));
                var est = (long)nx * (long)ny;
                if (maxSamples < 100) maxSamples = 100;
                if (est > maxSamples)
                {
                    var scale = Math.Sqrt((double)est / (double)maxSamples);
                    gridFt = gridFt * scale;
                }
            }
            catch { /* ignore */ }

            var boundaryCurves = TryGetRoomBoundaryCurves(room);

            var candidates = new List<XYZ>();
            int tested = 0;
            for (double x = bb.Min.X; x <= bb.Max.X; x += gridFt)
            {
                for (double y = bb.Min.Y; y <= bb.Max.Y; y += gridFt)
                {
                    tested++;
                    var probe = new XYZ(x, y, zProbe);
                    if (!IsPointInRoomSafe(room, probe)) continue;
                    var onLevel = new XYZ(x, y, zView);
                    if (boundaryCurves.Count > 0)
                    {
                        var d = MinDistanceToCurvesFt(onLevel, boundaryCurves);
                        if (d < clearanceFt) continue;
                    }
                    candidates.Add(onLevel);
                    if (candidates.Count >= maxSamples) break;
                }
                if (candidates.Count >= maxSamples) break;
            }

            // If many candidates, keep farthest by Euclidean distance to the room door point.
            if (maxSolverCandidates < 20) maxSolverCandidates = 20;
            var solverCandidates = candidates;
            if (candidates.Count > maxSolverCandidates)
            {
                solverCandidates = candidates
                    .OrderByDescending(q => q.DistanceTo(roomDoorInsideOnLevel))
                    .Take(maxSolverCandidates)
                    .ToList();
            }

            double bestLenFt = 0.0;
            XYZ best = null;
            int evaluated = 0;

            foreach (var c in solverCandidates)
            {
                evaluated++;
                IList<IList<XYZ>> poly;
                try
                {
                    poly = PathOfTravel.FindShortestPaths(view, new List<XYZ> { c }, new List<XYZ> { roomDoorInsideOnLevel });
                }
                catch
                {
                    continue;
                }

                if (poly == null || poly.Count == 0) continue;
                var pts = poly[0];
                if (pts == null || pts.Count < 2) continue;

                var lenFt = EgressGeom.PolylineLengthFt(pts);
                if (lenFt > bestLenFt)
                {
                    bestLenFt = lenFt;
                    best = c;
                }
            }

            debug["gridMmEffective"] = Math.Round(UnitHelper.FtToMm(gridFt), 1);
            debug["bboxMm"] = new JObject
            {
                ["min"] = EgressJson.PointToMm(bb.Min),
                ["max"] = EgressJson.PointToMm(bb.Max)
            };
            debug["testedGridPoints"] = tested;
            debug["candidatesInRoom"] = candidates.Count;
            debug["solverCandidates"] = solverCandidates.Count;
            debug["solverEvaluated"] = evaluated;

            if (best == null || bestLenFt <= 0.0) return false;

            bestOnLevel = best;
            bestLenM = UnitUtils.ConvertFromInternalUnits(bestLenFt, UnitTypeId.Meters);
            return true;
        }
    }

    internal static class EgressTagUtil
    {
        private const int PathTagCategoryId = -2000834; // OST_PathOfTravelTags

        public static bool TryResolveTagTypeFromSelection(UIApplication uiapp, out ElementId tagTypeId, out string msg)
        {
            tagTypeId = ElementId.InvalidElementId;
            msg = "";
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) { msg = "アクティブドキュメントがありません。"; return false; }

                var selIds = uidoc.Selection?.GetElementIds();
                if (selIds == null || selIds.Count == 0)
                {
                    msg = "移動経路タグが選択されていません。タグを選択してください。";
                    return false;
                }

                var el = doc.GetElement(selIds.First());
                if (el == null)
                {
                    msg = "選択要素が取得できません。";
                    return false;
                }

                // If an IndependentTag instance is selected, use its type.
                if (el is IndependentTag)
                {
                    tagTypeId = el.GetTypeId();
                    return tagTypeId != ElementId.InvalidElementId;
                }

                // If a tag type (ElementType) is selected, use it directly.
                if (el is ElementType)
                {
                    tagTypeId = el.Id;
                    return tagTypeId != ElementId.InvalidElementId;
                }

                msg = "選択要素がタグではありません。移動経路用のタグを選択してください。";
                return false;
            }
            catch (Exception ex)
            {
                msg = "タグの解決に失敗: " + ex.Message;
                return false;
            }
        }

        public static bool TryResolveTagTypeFromProject(UIApplication uiapp, out ElementId tagTypeId, out string msg)
        {
            tagTypeId = ElementId.InvalidElementId;
            msg = "";
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "アクティブドキュメントがありません。"; return false; }

                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .Where(s => s.Category != null && s.Category.Id.IntValue() == PathTagCategoryId)
                    .ToList();

                if (types.Count == 0)
                {
                    msg = "移動経路タグタイプがロードされていません。";
                    return false;
                }

                var ordered = types
                    .Select(s => new
                    {
                        sym = s,
                        fam = s.Family?.Name ?? "",
                        name = s.Name ?? ""
                    })
                    .OrderByDescending(x => ScoreTagName(x.fam, x.name))
                    .ThenBy(x => x.fam)
                    .ThenBy(x => x.name)
                    .ToList();

                var chosen = ordered.First().sym;
                tagTypeId = chosen.Id;

                if (types.Count > 1)
                {
                    msg = $"移動経路タグタイプを自動選択しました: {chosen.Family?.Name ?? ""} / {chosen.Name ?? ""}";
                }
                return tagTypeId != ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                msg = "タグの自動選択に失敗: " + ex.Message;
                return false;
            }
        }

        private static int ScoreTagName(string familyName, string typeName)
        {
            var a = (familyName ?? "") + " " + (typeName ?? "");
            int score = 0;
            if (a.IndexOf("避難", StringComparison.OrdinalIgnoreCase) >= 0) score += 20;
            if (a.IndexOf("経路", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
            if (a.IndexOf("長さ", StringComparison.OrdinalIgnoreCase) >= 0) score += 5;
            return score;
        }
    }

    // ------------------------------------------------------------
    // egress.create_waypoint_guided_path (design alias: revit.egress.createWaypointGuided)
    // ------------------------------------------------------------
    [RpcCommand("egress.create_waypoint_guided_path",
        Aliases = new[] { "revit.egress.createWaypointGuided", "egress.create_waypoint_guided" },
        Category = "Route",
        Tags = new[] { "Egress", "PathOfTravel", "Waypoints", "Create" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Summary = "Create a waypoint-guided egress PathOfTravel in a plan view. Supports roomMostRemoteToDoor mode.",
        Requires = new[] { "mode", "targetDoorId", "roomId|start", "roomDoorId(optional)" },
        Constraints = new[]
        {
            "Works only in ViewPlan (Floor/Ceiling plan).",
            "Coordinates are in mm; Z is snapped to the view's level elevation.",
            "mode: 'roomMostRemoteToDoor' or 'pointToDoor'.",
            "waypoints[] are optional; 2–5 points recommended.",
            "PathOfTravel uses visible geometry of the view; crop/visibility can affect results.",
            "To create a PathOfTravel tag, set createTag=true and provide tagTypeId or select an existing tag and set tagTypeFromSelection=true."
        })]
    public class CreateWaypointGuidedEgressPathCommand : IRevitCommandHandler
    {
        public string CommandName => "egress.create_waypoint_guided_path";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = cmd.Params as JObject ?? new JObject();

            if (!EgressJson.TryGetViewPlan(uiapp, p, out var view, out var why))
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("INVALID_VIEW", why), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            var doc = uiapp.ActiveUIDocument.Document;

            var mode = (p.Value<string>("mode") ?? "roomMostRemoteToDoor").Trim();
            if (!string.Equals(mode, "roomMostRemoteToDoor", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(mode, "pointToDoor", StringComparison.OrdinalIgnoreCase))
            {
                return RpcResultEnvelope.StandardizePayload(
                    RpcResultEnvelope.Fail("INVALID_MODE", "mode は 'roomMostRemoteToDoor' または 'pointToDoor' を指定してください。"),
                    uiapp, cmd.Command, sw.ElapsedMilliseconds);
            }

            int targetDoorId = p.Value<int?>("targetDoorId") ?? 0;
            if (targetDoorId <= 0)
                return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_TARGET_DOOR", "targetDoorId が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

            double clearanceMm = p.Value<double?>("clearanceMm") ?? 450.0;
            if (clearanceMm < 0) clearanceMm = Math.Abs(clearanceMm);

            double doorApproachOffsetMm = p.Value<double?>("doorApproachOffsetMm") ?? Math.Max(200.0, Math.Min(600.0, clearanceMm));
            double gridMm = p.Value<double?>("gridSpacingMm") ?? 600.0;
            int maxSamples = p.Value<int?>("maxSamples") ?? 2000;
            int maxSolverCandidates = p.Value<int?>("maxSolverCandidates") ?? 200;
            bool forcePassThroughRoomDoor = p.Value<bool?>("forcePassThroughRoomDoor") != false;

            var zView = (view.GenLevel != null ? view.GenLevel.Elevation : 0.0);

            XYZ startOnLevel;
            XYZ endOnLevel;
            XYZ? roomDoorOutsideOnLevel = null;
            var warnings = new JArray();
            var debug = new JObject();

            if (string.Equals(mode, "pointToDoor", StringComparison.OrdinalIgnoreCase))
            {
                if (!EgressJson.TryParsePointMm(p["start"], out var s, out var em))
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_START", "pointToDoor では start が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                startOnLevel = EgressGeom.SnapZ(s, zView);

                if (!EgressDoorUtil.TryComputeDoorApproachPointBestEffort(view, targetDoorId, startOnLevel, doorApproachOffsetMm, out endOnLevel, out var em2))
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("INVALID_TARGET_DOOR", em2), uiapp, cmd.Command, sw.ElapsedMilliseconds);
            }
            else
            {
                // roomMostRemoteToDoor
                int roomId = p.Value<int?>("roomId") ?? 0;
                if (roomId <= 0)
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_ROOM", "roomMostRemoteToDoor では roomId が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                int roomDoorId = p.Value<int?>("roomDoorId") ?? 0;
                if (roomDoorId <= 0)
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MISSING_ROOM_DOOR", "roomMostRemoteToDoor では roomDoorId が必要です。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

                var elRoom = doc.GetElement(ElementIdCompat.From(roomId));
                if (!(elRoom is Autodesk.Revit.DB.Architecture.Room room))
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("NOT_ROOM", $"roomId={roomId} は Room ではありません。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

                if (!EgressRoomUtil.TryComputeRoomDoorApproachPoints(view, room, roomDoorId, doorApproachOffsetMm, out var doorInside, out var doorOutside, out var doorWarn))
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("ROOM_DOOR_POINT_FAILED", doorWarn), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                if (!string.IsNullOrWhiteSpace(doorWarn)) warnings.Add(doorWarn);

                roomDoorOutsideOnLevel = doorOutside;

                if (!EgressRoomUtil.TryComputeMostRemotePointInRoom(view, room, doorInside, clearanceMm, gridMm, maxSamples, maxSolverCandidates, out startOnLevel, out var bestLenM, out var samplingDbg))
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("MOST_REMOTE_FAILED", "Room 内で最遠点を算出できませんでした。部屋境界/表示/障害物/作図位置を確認してください。"), uiapp, cmd.Command, sw.ElapsedMilliseconds);

                debug["roomMostRemote"] = new JObject
                {
                    ["roomId"] = room.Id.IntValue(),
                    ["roomName"] = room.Name ?? "",
                    ["mostRemotePoint"] = EgressJson.PointToMm(startOnLevel),
                    ["doorPointInside"] = EgressJson.PointToMm(doorInside),
                    ["distanceM"] = Math.Round(bestLenM, 3),
                    ["sampling"] = samplingDbg
                };

                if (!EgressDoorUtil.TryComputeDoorApproachPointBestEffort(view, targetDoorId, startOnLevel, doorApproachOffsetMm, out endOnLevel, out var em3))
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("INVALID_TARGET_DOOR", em3), uiapp, cmd.Command, sw.ElapsedMilliseconds);
            }

            // Assemble waypoint list (optional)
            var wpList = new List<XYZ>();
            if (forcePassThroughRoomDoor && roomDoorOutsideOnLevel != null)
                wpList.Add(roomDoorOutsideOnLevel);

            if (p["waypoints"] is JArray wps)
            {
                foreach (var t in wps)
                {
                    if (!EgressJson.TryParsePointMm(t, out var pt, out var em)) continue;
                    wpList.Add(EgressGeom.SnapZ(pt, zView));
                }
            }

            bool createLabel = p.Value<bool?>("createLabel") == true;
            string labelFormat = p.Value<string>("labelFormat") ?? "{len_m:0.00} m";
            double labelOffsetMm = p.Value<double?>("labelOffsetMm") ?? 300.0;
            bool createTag = p.Value<bool?>("createTag") ?? false;
            int tagTypeIdInt = p.Value<int?>("tagTypeId") ?? 0;
            bool tagTypeFromSelection = p.Value<bool?>("tagTypeFromSelection") ?? true;
            bool tagAddLeader = p.Value<bool?>("tagAddLeader") ?? true;
            string tagOrientation = p.Value<string>("tagOrientation") ?? "Horizontal";
            double tagOffsetMm = p.Value<double?>("tagOffsetMm") ?? 300.0;

            var statusCreate = PathOfTravelCalculationStatus.Success;
            var statusUpdate = PathOfTravelCalculationStatus.Success;
            int pathIdInt = 0;
            double lengthM = 0.0;
            int labelIdInt = 0;
            int tagIdInt = 0;

            using (var tx = new Transaction(doc, "Egress: Create Waypoint Guided Path"))
            {
                tx.Start();
                try
                {
                    var path = PathOfTravel.Create(view, startOnLevel, endOnLevel, out statusCreate);
                    if (path == null)
                    {
                        tx.RollBack();
                        var fail = RpcResultEnvelope.Fail("NO_PATH", "NoPathOfTravel: could not create a route in the given view.", new { status = statusCreate.ToString() });
                        fail["nextActions"] = new JArray(
                            "Check crop region and view visibility (PathOfTravel uses visible geometry).",
                            "Ensure start/end points are not inside walls/obstacles.",
                            "Try a different viewId on the same level."
                        );
                        return RpcResultEnvelope.StandardizePayload(fail, uiapp, cmd.Command, sw.ElapsedMilliseconds);
                    }

                    for (int i = 0; i < wpList.Count; i++)
                        path.InsertWaypoint(wpList[i], i);

                    statusUpdate = path.Update();
                    if (statusUpdate != PathOfTravelCalculationStatus.Success
                        && statusUpdate != PathOfTravelCalculationStatus.ResultAffectedByCrop)
                    {
                        tx.RollBack();
                        return RpcResultEnvelope.StandardizePayload(
                            RpcResultEnvelope.Fail("NO_PATH", "NoPathOfTravel: could not find a route after applying waypoints.", new { status = statusUpdate.ToString() }),
                            uiapp, cmd.Command, sw.ElapsedMilliseconds);
                    }

                    // length
                    try
                    {
                        var curves = path.GetCurves();
                        double lenFt = 0.0;
                        if (curves != null)
                        {
                            foreach (var c in curves) if (c != null) lenFt += c.Length;
                        }
                        lengthM = UnitUtils.ConvertFromInternalUnits(lenFt, UnitTypeId.Meters);
                    }
                    catch { /* ignore */ }

                    // label
                    if (createLabel)
                    {
                        try
                        {
                            var txt = EgressGeom.FormatLenLabel(labelFormat, lengthM);
                            var typeId = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).FirstElementId();
                            if (typeId != null && typeId != ElementId.InvalidElementId)
                            {
                                var mid = path.PathMidpoint;
                                var offFt = UnitHelper.MmToFt(labelOffsetMm);
                                var pt = mid + view.RightDirection * offFt;
                                var tn = TextNote.Create(doc, view.Id, pt, txt, typeId);
                                if (tn != null) labelIdInt = tn.Id.IntValue();
                            }
                            else
                            {
                                warnings.Add("TextNoteType が見つからずラベルを作成できませんでした。");
                            }
                        }
                        catch (Exception ex)
                        {
                            warnings.Add("ラベル作成に失敗: " + ex.Message);
                        }
                    }

                    // tag (PathOfTravel tag)
                    if (createTag)
                    {
                        try
                        {
                            ElementId tagTypeId = ElementId.InvalidElementId;
                            if (tagTypeIdInt > 0) tagTypeId = ElementIdCompat.From(tagTypeIdInt);
                            if (tagTypeId == ElementId.InvalidElementId && tagTypeFromSelection)
                            {
                                if (!EgressTagUtil.TryResolveTagTypeFromSelection(uiapp, out tagTypeId, out var tagMsg))
                                {
                                    // Fallback to project-wide auto selection
                                    if (!EgressTagUtil.TryResolveTagTypeFromProject(uiapp, out tagTypeId, out var autoMsg))
                                    {
                                        tx.RollBack();
                                        return RpcResultEnvelope.StandardizePayload(
                                            RpcResultEnvelope.Fail("TAG_TYPE_NOT_FOUND", string.IsNullOrWhiteSpace(tagMsg)
                                                ? "移動経路用のタグが見つかりません。タグファミリをロードするか、既存の移動経路タグを選択してください。"
                                                : tagMsg),
                                            uiapp, cmd.Command, sw.ElapsedMilliseconds);
                                    }
                                    if (!string.IsNullOrWhiteSpace(autoMsg)) warnings.Add(autoMsg);
                                }
                                else if (!string.IsNullOrWhiteSpace(tagMsg))
                                {
                                    warnings.Add(tagMsg);
                                }
                            }
                            if (tagTypeId == ElementId.InvalidElementId)
                            {
                                if (EgressTagUtil.TryResolveTagTypeFromProject(uiapp, out var autoId, out var autoMsg2))
                                {
                                    tagTypeId = autoId;
                                    if (!string.IsNullOrWhiteSpace(autoMsg2)) warnings.Add(autoMsg2);
                                }
                            }
                            if (tagTypeId == ElementId.InvalidElementId)
                            {
                                tx.RollBack();
                                return RpcResultEnvelope.StandardizePayload(
                                    RpcResultEnvelope.Fail("TAG_TYPE_NOT_FOUND", "移動経路用のタグが見つかりません。タグファミリをロードするか、既存の移動経路タグを選択してください。"),
                                    uiapp, cmd.Command, sw.ElapsedMilliseconds);
                            }

                            var mid = path.PathMidpoint;
                            var offFt = UnitHelper.MmToFt(tagOffsetMm);
                            var head = mid + view.RightDirection * offFt;
                            var orientation = tagOrientation.Equals("Vertical", StringComparison.OrdinalIgnoreCase)
                                ? TagOrientation.Vertical
                                : TagOrientation.Horizontal;
                            var reference = new Reference(path);
                            var tag = IndependentTag.Create(doc, tagTypeId, view.Id, reference, tagAddLeader, orientation, head);
                            if (tag != null) tagIdInt = tag.Id.IntValue();
                        }
                        catch (Exception ex)
                        {
                            warnings.Add("タグ作成に失敗: " + ex.Message);
                        }
                    }

                    tx.Commit();
                    pathIdInt = path.Id.IntValue();
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return RpcResultEnvelope.StandardizePayload(RpcResultEnvelope.Fail("CREATE_EGRESS_FAILED", ex.Message), uiapp, cmd.Command, sw.ElapsedMilliseconds);
                }
            }

            var ok = statusUpdate == PathOfTravelCalculationStatus.Success || statusUpdate == PathOfTravelCalculationStatus.ResultAffectedByCrop;
            var msgOut = ok ? "ok" : "NoPathOfTravel";
            if (statusUpdate == PathOfTravelCalculationStatus.ResultAffectedByCrop)
                warnings.Add("ResultAffectedByCrop: crop region may have affected the route.");

            var data = new JObject
            {
                ["pathId"] = pathIdInt,
                ["statusCreate"] = statusCreate.ToString(),
                ["status"] = statusUpdate.ToString(),
                ["lengthM"] = Math.Round(lengthM, 3),
                ["startPoint"] = EgressJson.PointToMm(startOnLevel),
                ["endPoint"] = EgressJson.PointToMm(endOnLevel),
                ["waypointsApplied"] = wpList.Count,
                ["labelId"] = labelIdInt,
                ["tagId"] = tagIdInt,
                ["mode"] = mode,
                ["targetDoorId"] = targetDoorId
            };
            if (debug.Count > 0) data["debug"] = debug;

            var payloadOut = new JObject
            {
                ["ok"] = ok,
                ["msg"] = msgOut,
                ["data"] = data,
                ["warnings"] = warnings
            };
            return RpcResultEnvelope.StandardizePayload(payloadOut, uiapp, cmd.Command, sw.ElapsedMilliseconds);
        }
    }
}
