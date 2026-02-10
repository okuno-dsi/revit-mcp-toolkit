// File: Commands/ElementOps/CutGeometryCommands.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    internal static class CutUtil
    {
        public static Element ResolveByKeys(Document doc, JObject p, string idKey, string uidKey)
        {
            if (p.TryGetValue(idKey, out var tok))
            {
                try { return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tok.Value<int>())); } catch { }
            }
            if (p.TryGetValue(uidKey, out var uTok))
            {
                try { return doc.GetElement(uTok.Value<string>()); } catch { }
            }
            return null;
        }

        public static List<Element> ResolveMany(Document doc, JToken tok)
        {
            var list = new List<Element>();
            if (tok == null) return list;
            if (tok is JArray arr)
            {
                foreach (var v in arr)
                {
                    try
                    {
                        if (v.Type == JTokenType.Integer)
                        {
                            var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(v.Value<int>()));
                            if (e != null) list.Add(e);
                        }
                        else if (v.Type == JTokenType.String)
                        {
                            var e = doc.GetElement(v.Value<string>());
                            if (e != null) list.Add(e);
                        }
                    }
                    catch { }
                }
                return list;
            }
            // single value
            try
            {
                if (tok.Type == JTokenType.Integer)
                {
                    var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tok.Value<int>()));
                    if (e != null) list.Add(e);
                }
                else if (tok.Type == JTokenType.String)
                {
                    var e = doc.GetElement(tok.Value<string>());
                    if (e != null) list.Add(e);
                }
            }
            catch { }
            return list;
        }

        public static bool IsAlreadyCut(Document doc, Element cutting, Element cut)
        {
            try
            {
                if (!JoinGeometryUtils.AreElementsJoined(doc, cutting, cut)) return false;
                return JoinGeometryUtils.IsCuttingElementInJoin(doc, cutting, cut);
            }
            catch
            {
                return false;
            }
        }
    }

    public class CutElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "element.cut_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();

            var cutting = CutUtil.ResolveByKeys(doc, p, "cuttingElementId", "cuttingUniqueId")
                       ?? CutUtil.ResolveByKeys(doc, p, "elementId", "uniqueId");

            if (cutting == null)
                return new { ok = false, msg = "cuttingElementId / cuttingUniqueId を指定してください。" };

            var cutList = new List<Element>();
            if (p.TryGetValue("cutElementIds", out var idsTok))
                cutList.AddRange(CutUtil.ResolveMany(doc, idsTok));
            if (p.TryGetValue("cutElementUniqueIds", out var uidsTok))
                cutList.AddRange(CutUtil.ResolveMany(doc, uidsTok));
            if (p.TryGetValue("cutElementId", out var idTok))
                cutList.AddRange(CutUtil.ResolveMany(doc, idTok));
            if (p.TryGetValue("cutElementUniqueId", out var uidTok))
                cutList.AddRange(CutUtil.ResolveMany(doc, uidTok));

            if (cutList.Count == 0)
                return new { ok = false, msg = "cutElementIds / cutElementUniqueIds を指定してください。" };

            bool skipIfAlreadyCut = p.Value<bool?>("skipIfAlreadyCut") ?? true;
            bool skipIfCannotCut = p.Value<bool?>("skipIfCannotCut") ?? true;

            var success = new List<int>();
            var skipped = new List<object>();
            var failed = new List<object>();

            try
            {
                using (var tx = new Transaction(doc, "Cut Elements"))
                {
                    tx.Start();
                    foreach (var cut in cutList)
                    {
                        if (cut == null) continue;
                        var cutId = cut.Id.IntValue();

                        try
                        {
                            if (skipIfAlreadyCut && CutUtil.IsAlreadyCut(doc, cutting, cut))
                            {
                                skipped.Add(new { elementId = cutId, reason = "alreadyCut" });
                                continue;
                            }

                            // Join geometry first (CutGeometryUtils is not available in this API version)
                            try
                            {
                                if (!JoinGeometryUtils.AreElementsJoined(doc, cutting, cut))
                                    JoinGeometryUtils.JoinGeometry(doc, cutting, cut);
                            }
                            catch (Exception jex)
                            {
                                if (skipIfCannotCut)
                                {
                                    skipped.Add(new { elementId = cutId, reason = "cannotJoin", msg = jex.Message });
                                    continue;
                                }
                                throw;
                            }

                            // Ensure cutting element is the cutter
                            try
                            {
                                if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, cutting, cut))
                                    JoinGeometryUtils.SwitchJoinOrder(doc, cutting, cut);
                            }
                            catch { /* ignore if cannot switch */ }
                            success.Add(cutId);
                        }
                        catch (Exception ex)
                        {
                            failed.Add(new { elementId = cutId, msg = ex.Message });
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }

            return new
            {
                ok = failed.Count == 0,
                cuttingElementId = cutting.Id.IntValue(),
                cutCount = cutList.Count,
                successIds = success,
                skipped,
                failed
            };
        }
    }

    public class UncutElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "element.uncut_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();

            var cutting = CutUtil.ResolveByKeys(doc, p, "cuttingElementId", "cuttingUniqueId")
                       ?? CutUtil.ResolveByKeys(doc, p, "elementId", "uniqueId");

            if (cutting == null)
                return new { ok = false, msg = "cuttingElementId / cuttingUniqueId を指定してください。" };

            var cutList = new List<Element>();
            if (p.TryGetValue("cutElementIds", out var idsTok))
                cutList.AddRange(CutUtil.ResolveMany(doc, idsTok));
            if (p.TryGetValue("cutElementUniqueIds", out var uidsTok))
                cutList.AddRange(CutUtil.ResolveMany(doc, uidsTok));
            if (p.TryGetValue("cutElementId", out var idTok))
                cutList.AddRange(CutUtil.ResolveMany(doc, idTok));
            if (p.TryGetValue("cutElementUniqueId", out var uidTok))
                cutList.AddRange(CutUtil.ResolveMany(doc, uidTok));

            if (cutList.Count == 0)
                return new { ok = false, msg = "cutElementIds / cutElementUniqueIds を指定してください。" };

            var success = new List<int>();
            var failed = new List<object>();

            try
            {
                using (var tx = new Transaction(doc, "Uncut Elements"))
                {
                    tx.Start();
                    foreach (var cut in cutList)
                    {
                        if (cut == null) continue;
                        var cutId = cut.Id.IntValue();
                        try
                        {
                            if (JoinGeometryUtils.AreElementsJoined(doc, cutting, cut))
                                JoinGeometryUtils.UnjoinGeometry(doc, cutting, cut);
                            success.Add(cutId);
                        }
                        catch (Exception ex)
                        {
                            failed.Add(new { elementId = cutId, msg = ex.Message });
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }

            return new
            {
                ok = failed.Count == 0,
                cuttingElementId = cutting.Id.IntValue(),
                cutCount = cutList.Count,
                successIds = success,
                failed
            };
        }
    }
}
