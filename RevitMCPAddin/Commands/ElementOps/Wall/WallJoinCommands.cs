// File: Commands/ElementOps/Wall/WallJoinCommands.cs
// Purpose: Wall join controls (Disallow) exposed via MCP.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    /// <summary>
    /// Disallow wall joins at one or both ends of walls.
    /// CommandName: disallow_wall_join_at_end
    /// </summary>
    public class DisallowWallJoinAtEndCommand : IRevitCommandHandler
    {
        public string CommandName => "disallow_wall_join_at_end";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();

            // elementIds (array) 優先、その次に elementId / uniqueId
            var ids = new List<ElementId>();
            var idArr = p["elementIds"] as JArray;
            if (idArr != null && idArr.Count > 0)
            {
                foreach (var t in idArr)
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        var v = t.Value<int>();
                        if (v > 0) ids.Add(new ElementId(v));
                    }
                }
            }
            else
            {
                Autodesk.Revit.DB.Wall target = null;
                var eid = p.Value<int?>("elementId") ?? 0;
                var uid = p.Value<string>("uniqueId");
                if (eid > 0) target = doc.GetElement(new ElementId(eid)) as Autodesk.Revit.DB.Wall;
                else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid) as Autodesk.Revit.DB.Wall;

                if (target != null)
                    ids.Add(target.Id);
            }

            if (ids.Count == 0)
                return new { ok = false, msg = "elementId / uniqueId / elementIds のいずれかを指定してください。" };

            // endIndex / ends: 0/1 のみ有効。未指定なら両端 (0,1) を対象
            var ends = new List<int>();
            var endsArr = p["ends"] as JArray;
            if (endsArr != null && endsArr.Count > 0)
            {
                foreach (var t in endsArr)
                {
                    if (t.Type == JTokenType.Integer)
                    {
                        var idx = t.Value<int>();
                        if (idx == 0 || idx == 1) ends.Add(idx);
                    }
                }
            }
            else if (p.TryGetValue("endIndex", out var endTok))
            {
                var idx = endTok.Value<int>();
                if (idx == 0 || idx == 1) ends.Add(idx);
            }

            if (ends.Count == 0)
            {
                ends.Add(0);
                ends.Add(1);
            }

            int requested = ids.Count;
            int processed = 0;
            int changedEnds = 0;
            int notWall = 0;
            int failed = 0;

            using (var tx = new Transaction(doc, "Disallow Wall Join At End"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                foreach (var id in ids)
                {
                    try
                    {
                        var wall = doc.GetElement(id) as Autodesk.Revit.DB.Wall;
                        if (wall == null)
                        {
                            notWall++;
                            continue;
                        }

                        processed++;

                        foreach (var idx in ends)
                        {
                            try
                            {
                                // Some API builds only expose DisallowWallJoinAtEnd; call it directly.
                                WallUtils.DisallowWallJoinAtEnd(wall, idx);
                                changedEnds++;
                            }
                            catch
                            {
                                failed++;
                            }
                        }
                    }
                    catch
                    {
                        failed++;
                    }
                }

                try { tx.Commit(); }
                catch
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Transaction failed while disallowing wall joins at end." };
                }
            }

            return new
            {
                ok = true,
                requested,
                processed,
                changedEnds,
                notWall,
                failed
            };
        }
    }

    /// <summary>
    /// Placeholder for wall join type operations (Miter / Butt).
    /// Currently not supported in this Revit build; always returns ok:false.
    /// CommandName: set_wall_join_type | set_wall_miter_joint
    /// </summary>
    public class SetWallJoinTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "set_wall_join_type|set_wall_miter_joint";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            // Revit API in this environment does not expose WallJoinType / WallUtils.SetWallJoinType publicly,
            // so we cannot implement this safely. Return a clear error instead of failing at runtime.
            return new
            {
                ok = false,
                msg = "Wall join type (Miter/Butt) editing is not available in this Revit version/build via MCP. Please adjust join type in the Revit UI."
            };
        }
    }
}

