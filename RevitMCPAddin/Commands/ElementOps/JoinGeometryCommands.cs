// File: Commands/ElementOps/JoinGeometryCommands.cs
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    internal static class JoinUtil
    {
        public static Element ResolveByKeys(Document doc, JObject p, string idKey, string uidKey)
        {
            if (p.TryGetValue(idKey, out var aTok))
            {
                try { return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(aTok.Value<int>())); } catch { }
            }
            if (p.TryGetValue(uidKey, out var uTok))
            {
                try { return doc.GetElement(uTok.Value<string>()); } catch { }
            }
            return null;
        }

        public static (Element a, Element b) ResolvePair(Document doc, JObject p)
        {
            var a = ResolveByKeys(doc, p, "elementIdA", "uniqueIdA");
            var b = ResolveByKeys(doc, p, "elementIdB", "uniqueIdB");
            return (a, b);
        }
    }

    public class JoinElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "join_elements";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();
            var (a, b) = JoinUtil.ResolvePair(doc, p);
            if (a == null || b == null) return new { ok = false, msg = "elementIdA/uniqueIdA  elementIdB/uniqueIdB w肵ĂB" };

            try
            {
                using (var tx = new Transaction(doc, "Join Elements"))
                {
                    tx.Start();
                    JoinGeometryUtils.JoinGeometry(doc, a, b);
                    tx.Commit();
                }
                return new { ok = true, a = a.Id.IntValue(), b = b.Id.IntValue() };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    public class UnjoinElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "unjoin_elements";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();
            var (a, b) = JoinUtil.ResolvePair(doc, p);
            if (a == null || b == null) return new { ok = false, msg = "elementIdA/uniqueIdA  elementIdB/uniqueIdB w肵ĂB" };

            try
            {
                using (var tx = new Transaction(doc, "Unjoin Elements"))
                {
                    tx.Start();
                    if (JoinGeometryUtils.AreElementsJoined(doc, a, b))
                        JoinGeometryUtils.UnjoinGeometry(doc, a, b);
                    tx.Commit();
                }
                return new { ok = true, a = a.Id.IntValue(), b = b.Id.IntValue() };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    public class AreElementsJoinedCommand : IRevitCommandHandler
    {
        public string CommandName => "are_elements_joined";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();
            var (a, b) = JoinUtil.ResolvePair(doc, p);
            if (a == null || b == null) return new { ok = false, msg = "elementIdA/uniqueIdA  elementIdB/uniqueIdB w肵ĂB" };
            bool joined = false;
            try { joined = JoinGeometryUtils.AreElementsJoined(doc, a, b); } catch { }
            return new { ok = true, joined, a = a.Id.IntValue(), b = b.Id.IntValue() };
        }
    }

    public class SwitchJoinOrderCommand : IRevitCommandHandler
    {
        public string CommandName => "switch_join_order";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();
            var (a, b) = JoinUtil.ResolvePair(doc, p);
            if (a == null || b == null) return new { ok = false, msg = "elementIdA/uniqueIdA  elementIdB/uniqueIdB w肵ĂB" };

            try
            {
                using (var tx = new Transaction(doc, "Switch Join Order"))
                {
                    tx.Start();
                    if (!JoinGeometryUtils.AreElementsJoined(doc, a, b))
                    {
                        JoinGeometryUtils.JoinGeometry(doc, a, b);
                    }
                    JoinGeometryUtils.SwitchJoinOrder(doc, a, b);
                    tx.Commit();
                }
                return new { ok = true, a = a.Id.IntValue(), b = b.Id.IntValue() };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    public class GetJoinedElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_joined_elements";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            var p = cmd.Params as JObject ?? new JObject();
            var a = JoinUtil.ResolveByKeys(doc, p, "elementId", "uniqueId");
            if (a == null) return new { ok = false, msg = "elementId ܂ uniqueId w肵ĂB" };

            var joinedIds = new List<int>();
            var dependentIds = new List<int>();
            var subComponentIds = new List<int>();
            int? hostId = null;
            int? superComponentId = null;
            int? groupId = null;
            bool isPinned = false;
            bool isInGroup = false;

            try
            {
                // Geometry join information
                var ids = JoinGeometryUtils.GetJoinedElements(doc, a);
                if (ids != null)
                {
                    foreach (var id in ids)
                        joinedIds.Add(id.IntValue());
                }
            }
            catch
            {
                // ignore geometry join errors; keep joinedIds as empty
            }

            try
            {
                // Host / family relationships（主に FamilyInstance 向け）
                if (a is FamilyInstance fi)
                {
                    if (fi.Host != null)
                        hostId = fi.Host.Id.IntValue();
                    if (fi.SuperComponent != null)
                        superComponentId = fi.SuperComponent.Id.IntValue();

                    var subs = fi.GetSubComponentIds();
                    if (subs != null)
                    {
                        foreach (var sid in subs)
                            subComponentIds.Add(sid.IntValue());
                    }
                }

                // Group / pin 状態
                isPinned = a.Pinned;
                if (a.GroupId != null && a.GroupId != ElementId.InvalidElementId)
                {
                    isInGroup = true;
                    groupId = a.GroupId.IntValue();
                }

                // 依存要素（寸法・タグなど）
                var deps = a.GetDependentElements(null);
                if (deps != null)
                {
                    foreach (var did in deps)
                        dependentIds.Add(did.IntValue());
                }
            }
            catch
            {
                // any failure here should not break the command; keep what we have
            }

            // 推奨アクション（コマンド）と注意コメント
            var suggestedCommands = new List<object>();
            var notes = new List<string>();

            if (joinedIds.Count > 0)
            {
                suggestedCommands.Add(new
                {
                    kind = "geometryJoin",
                    command = "unjoin_elements",
                    description = "指定した2要素間のジオメトリ結合を解除します（elementIdA/elementIdB が必要です）。"
                });
            }

            if (isPinned)
            {
                suggestedCommands.Add(new
                {
                    kind = "pin",
                    command = "unpin_element",
                    description = "この要素のピン留めを解除します。"
                });
            }

            if (hostId.HasValue)
            {
                notes.Add("ホスト要素との関係があります。再ホスト／ホスト解除は Revit UI で行うことを推奨します。");
            }

            if (isInGroup || groupId.HasValue)
            {
                notes.Add("グループに所属しています。グループ解除や要素のグループからの切り離しは Revit UI で慎重に行ってください。");
            }

            if (dependentIds.Count > 0)
            {
                notes.Add("寸法・タグなど依存要素があります。削除や編集は Revit UI 上で確認しながら行ってください。");
            }

            return new
            {
                ok = true,
                elementId = a.Id.IntValue(),
                joinedIds,
                hostId,
                superComponentId,
                subComponentIds,
                isPinned,
                isInGroup,
                groupId,
                dependentIds,
                suggestedCommands,
                notes
            };
        }
    }
}


