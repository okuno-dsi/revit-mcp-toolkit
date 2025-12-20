// File: Commands/ElementOps/Wall/RejoinWallsCommand.cs
// Purpose: Rejoin walls (allow join at ends, set join type, optional geometry join)

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    /// <summary>
    /// Rejoin multiple walls:
    ///  - Allow wall join at both ends
    ///  - Set join type (Butt / Miter / SquareOff) when possible
    ///  - Optionally reorder join priority and re-run geometry joins
    /// CommandName: rejoin_walls
    /// </summary>
    public class RejoinWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "rejoin_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();

            // wall_ids: long[] / int[]
            var idTokens = p["wall_ids"] as JArray ?? p["elementIds"] as JArray;
            var wallIds = new List<ElementId>();
            if (idTokens != null)
            {
                foreach (var t in idTokens)
                {
                    try
                    {
                        // accept both int and long
                        var v = t.Type == JTokenType.Integer ? t.Value<long>() : 0L;
                        if (v != 0)
                            wallIds.Add(new ElementId(unchecked((int)v)));
                    }
                    catch { }
                }
            }

            if (wallIds.Count == 0)
            {
                return new { ok = false, msg = "wall_ids (or elementIds) must contain at least one id." };
            }

            // join_type: "butt" | "miter" | "square"
            string joinTypeStr = (p.Value<string>("join_type") ?? string.Empty).Trim();
            var joinType = ParseJoinType(joinTypeStr);

            // prefer_wall_id (for Butt / SquareOff)
            ElementId preferId = ElementId.InvalidElementId;
            try
            {
                var preferTok = p["prefer_wall_id"] ?? p["preferWallId"];
                if (preferTok != null && preferTok.Type == JTokenType.Integer)
                {
                    var v = preferTok.Value<long>();
                    if (v != 0)
                        preferId = new ElementId(unchecked((int)v));
                }
            }
            catch { }

            bool alsoJoinGeometry = p.Value<bool?>("also_join_geometry") ?? p.Value<bool?>("alsoJoinGeometry") ?? false;

            int requested = wallIds.Count;
            int processed = 0;
            int notWall = 0;
            int noLocation = 0;
            int joinTypeSetCount = 0;
            int reorderCount = 0;
            int geomJoinCount = 0;
            int failed = 0;

            var perWall = new List<object>();

            using (var tx = new Transaction(doc, "MCP: Rejoin Walls"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                foreach (var id in wallIds)
                {
                    try
                    {
                        var wall = doc.GetElement(id) as Autodesk.Revit.DB.Wall;
                        if (wall == null)
                        {
                            notWall++;
                            perWall.Add(new { elementId = id.IntegerValue, status = "skip", reason = "not Wall" });
                            continue;
                        }

                        var lc = wall.Location as LocationCurve;
                        if (lc == null)
                        {
                            noLocation++;
                            perWall.Add(new { elementId = id.IntegerValue, status = "skip", reason = "no LocationCurve" });
                            continue;
                        }

                        processed++;
                        int localJoinTypeSet = 0;
                        int localReorder = 0;
                        int localGeomJoined = 0;

                        // 1) Allow join at both ends (ignore failures)
                        SafeAllowJoin(wall, 0);
                        SafeAllowJoin(wall, 1);

                        // 2) Set join type for both ends when there is an existing wall join
                        if (TrySetJoinType(lc, joinType, 0)) localJoinTypeSet++;
                        if (TrySetJoinType(lc, joinType, 1)) localJoinTypeSet++;

                        // 3) For Butt / SquareOff, reorder to prefer a given wall (if supplied)
                        if ((joinType == Autodesk.Revit.DB.JoinType.Abut || joinType == Autodesk.Revit.DB.JoinType.SquareOff) &&
                            preferId != ElementId.InvalidElementId)
                        {
                            if (TryReorderJoin(doc, lc, 0, preferId)) localReorder++;
                            if (TryReorderJoin(doc, lc, 1, preferId)) localReorder++;
                        }

                        // 4) Optionally join geometry with neighbors at both ends
                        if (alsoJoinGeometry)
                        {
                            localGeomJoined = JoinNeighborWallsByGeometry(doc, wall);
                        }

                        joinTypeSetCount += localJoinTypeSet;
                        reorderCount += localReorder;
                        geomJoinCount += localGeomJoined;

                        perWall.Add(new
                        {
                            elementId = id.IntegerValue,
                            status = "ok",
                            joinTypeSetAtEnds = localJoinTypeSet,
                            reorderedEnds = localReorder,
                            geometryJoins = localGeomJoined
                        });
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        perWall.Add(new { elementId = id.IntegerValue, status = "error", reason = ex.Message });
                    }
                }

                try { doc.Regenerate(); }
                catch { }

                try { tx.Commit(); }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Transaction failed in rejoin_walls: " + ex.Message };
                }
            }

            RevitLogger.Info("[rejoin_walls] " + string.Join(", ", perWall.Select(x => x.ToString())));

            return new
            {
                ok = true,
                requested,
                processed,
                notWall,
                noLocation,
                failed,
                joinType = joinType.ToString(),
                preferWallId = preferId != ElementId.InvalidElementId ? preferId.IntegerValue : (int?)null,
                alsoJoinGeometry,
                joinTypeSetCount,
                reorderCount,
                geometryJoinCount = geomJoinCount,
                results = perWall
            };
        }

        private static Autodesk.Revit.DB.JoinType ParseJoinType(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Autodesk.Revit.DB.JoinType.Abut;
            switch (s.Trim().ToLowerInvariant())
            {
                case "miter":
                    return Autodesk.Revit.DB.JoinType.Miter;
                case "square":
                case "squareoff":
                    return Autodesk.Revit.DB.JoinType.SquareOff;
                case "butt":
                default:
                    return Autodesk.Revit.DB.JoinType.Abut;
            }
        }

        private static void SafeAllowJoin(Autodesk.Revit.DB.Wall w, int end)
        {
            try { WallUtils.AllowWallJoinAtEnd(w, end); }
            catch { /* ignore */ }
        }

        private static bool TrySetJoinType(LocationCurve lc, Autodesk.Revit.DB.JoinType jt, int end)
        {
            try
            {
                // ElementsAtJoin はパラメータ付きプロパティなので、明示的な accessor を使う
                var arr = lc.get_ElementsAtJoin(end);
                if (arr != null && arr.Size > 0)
                {
                    // JoinType もパラメータ付きプロパティなので、setter を直接呼ぶ
                    lc.set_JoinType(end, jt);
                    return true;
                }
            }
            catch
            {
                // ignore (some combinations / API versions may not support all join types)
            }
            return false;
        }

        private static bool TryReorderJoin(Document doc, LocationCurve lc, int end, ElementId prefer)
        {
            try
            {
                var arr = lc.get_ElementsAtJoin(end);
                if (arr == null || arr.Size == 0) return false;

                var elems = arr.Cast<Element>().ToList();
                var preferElem = elems.FirstOrDefault(e => e.Id == prefer);
                if (preferElem == null) return false;

                var newOrder = new ElementArray();
                newOrder.Append(preferElem);
                foreach (var e in elems)
                {
                    if (e.Id != prefer) newOrder.Append(e);
                }

                lc.set_ElementsAtJoin(end, newOrder);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int JoinNeighborWallsByGeometry(Document doc, Autodesk.Revit.DB.Wall w)
        {
            int count = 0;
            try
            {
                var lc = w.Location as LocationCurve;
                if (lc == null) return 0;
                foreach (var end in new[] { 0, 1 })
                {
                    var arr = lc.get_ElementsAtJoin(end);
                    if (arr == null || arr.Size == 0) continue;

                    foreach (Element neighbor in arr)
                    {
                        try
                        {
                            JoinGeometryUtils.JoinGeometry(doc, w, neighbor);
                            count++;
                        }
                        catch
                        {
                            // ignore individual failures
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
            return count;
        }
    }
}
