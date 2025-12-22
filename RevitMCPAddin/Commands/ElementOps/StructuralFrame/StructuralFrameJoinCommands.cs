// File: Commands/ElementOps/StructuralFrame/StructuralFrameJoinCommands.cs
// Purpose: Control Structural Framing "join at end" flags (DisallowJoinAtEnd) via MCP.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.StructuralFrame
{
    /// <summary>
    /// Disallow join at one or both ends of structural framing elements (beams/braces).
    /// CommandName: disallow_structural_frame_join_at_end
    /// </summary>
    public class DisallowStructuralFrameJoinAtEndCommand : IRevitCommandHandler
    {
        public string CommandName => "disallow_structural_frame_join_at_end";

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
                        if (v > 0) ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(v));
                    }
                }
            }
            else
            {
                Element target = null;
                var eid = p.Value<int?>("elementId") ?? 0;
                var uid = p.Value<string>("uniqueId");
                if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);

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
            int skipped = 0;
            int notFraming = 0;
            int failed = 0;

            using (var tx = new Transaction(doc, "Disallow Structural Frame Join At End"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                foreach (var id in ids)
                {
                    try
                    {
                        var fi = doc.GetElement(id) as FamilyInstance;
                        if (fi == null)
                        {
                            skipped++;
                            continue;
                        }

                        // Structural framing のみ対象 (Beam / Brace)
                        try
                        {
                            if (fi.StructuralType != StructuralType.Beam &&
                                fi.StructuralType != StructuralType.Brace)
                            {
                                notFraming++;
                                continue;
                            }
                        }
                        catch
                        {
                            notFraming++;
                            continue;
                        }

                        processed++;

                        foreach (var idx in ends)
                        {
                            try
                            {
                                // Some API builds do not expose IsJoinAllowedAtEnd; call DisallowJoinAtEnd directly.
                                StructuralFramingUtils.DisallowJoinAtEnd(fi, idx);
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
                    return new { ok = false, msg = "Transaction failed while disallowing joins at end." };
                }
            }

            return new
            {
                ok = true,
                requested,
                processed,
                changedEnds,
                skipped,
                notFraming,
                failed
            };
        }
    }
}


