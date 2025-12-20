// ================================================================
// File: Commands/GroupOps/GroupGetHandlers.cs
// 概要: グループ情報の取得系ハンドラをひとつのファイルに集約
// 対応: .NET Framework 4.8 / C# 8 / Revit 2023 API
// I/O : 入力mm/出力mm（本ファイルは取得系のみのため主に出力mm）
// 返却: { ok: true, ... } / { ok: false, msg: "..." }
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.GroupOps
{
    // ------------------------------------------------------------
    // 共通ユーティリティ（本ファイル内限定）
    // ------------------------------------------------------------
    internal static class GroupGetUtils
    {
        public static double ToMm(double feet) => UnitHelper.FtToMm(feet);

        public static object? ToMm(XYZ? p)
        {
            if (p == null) return null;
            var(x, y, z) = UnitHelper.XyzToMm(p);
            return new { x, y, z };
        }

public static string CategoryKindOf(Group g)
        {
            // Revitのローカライズに依存しないためカテゴリIDで判定
            var bid = (BuiltInCategory)g.Category.Id.IntegerValue;
            // ModelGroup: OST_IOSModelGroups / DetailGroup: OST_IOSDetailGroups
            if (bid == BuiltInCategory.OST_IOSModelGroups) return "ModelGroup";
            if (bid == BuiltInCategory.OST_IOSDetailGroups) return "DetailGroup";
            return g.Category?.Name ?? "Group";
        }

        public static string CategoryKindOf(GroupType gt)
        {
            if (gt.Category != null)
            {
                var bid = (BuiltInCategory)gt.Category.Id.IntegerValue;
                if (bid == BuiltInCategory.OST_IOSModelGroups) return "ModelGroup";
                if (bid == BuiltInCategory.OST_IOSDetailGroups) return "DetailGroup";
            }
            return gt.Category?.Name ?? "GroupType";
        }

        public static bool GetIsMirrored(Element e)
        {
            // FamilyInstance の場合は Mirrored プロパティを利用
            if (e is FamilyInstance fi)
                return fi.Mirrored;

            // Group など他の要素は Revit API が直接フラグを提供していないので常に false
            return false;
        }

        public static ElementId? GetLevelIdSafe(Group g)
        {
            // ModelGroupはLevel拘束あり・DetailGroupはViewSpecific中心
            try
            {
                var levelIdProp = g.LevelId; // 一部でInvalidElementIdの可能性
                if (levelIdProp != null && levelIdProp != ElementId.InvalidElementId) return levelIdProp;
            }
            catch { /* Revitバージョン差異に備えて */ }
            return null;
        }

        public static View? GetOwnerView(Document doc, Group g)
        {
            try
            {
                var vid = g.OwnerViewId;
                if (vid != null && vid != ElementId.InvalidElementId)
                    return doc.GetElement(vid) as View;
            }
            catch { }
            return null;
        }

        public static LocationPoint? GetLocationPoint(Element e)
        {
            var loc = e.Location as LocationPoint;
            return loc;
        }

        public static IList<ElementId> GetMemberIdsSafe(Group g)
        {
            try { return g.GetMemberIds(); }
            catch { return new List<ElementId>(); }
        }

        public static string? GetElementTypeName(Document doc, ElementId elemId)
        {
            var el = doc.GetElement(elemId);
            if (el == null) return null;
            var tid = el.GetTypeId();
            if (tid == null || tid == ElementId.InvalidElementId) return el.Name;
            var et = doc.GetElement(tid) as ElementType;
            return et != null ? et.Name : el.Name;
        }

        public static bool HasLocation(Element e) => e.Location != null;

        public static HashSet<string> GetRequestedFields(JObject p, string key = "fields")
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (p != null && p.TryGetValue(key, out var tok) && tok is JArray arr)
            {
                foreach (var f in arr) if (f.Type == JTokenType.String) set.Add(((string)f)!);
            }
            return set;
        }

        public static IEnumerable<Group> CollectGroups(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Group))
                .Cast<Group>();
        }

        public static IEnumerable<GroupType> CollectGroupTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(GroupType))
                .Cast<GroupType>();
        }
    }

    // ------------------------------------------------------------
    // 1) get_groups : モデル内のグループインスタンス一覧
    // params: { skip?, count?, viewScoped?:bool, fields?:string[] }
    // fields 例: elementId, groupTypeId, groupTypeName, category,
    //            memberCount, origin, levelId, viewId, isViewSpecific,
    //            isMirrored, isPinned
    // ------------------------------------------------------------
    public class GetGroupsHandler : IRevitCommandHandler
    {
        public string CommandName => "get_groups";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = cmd.Params as JObject ?? new JObject();

                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? 100;
                bool viewScoped = p.Value<bool?>("viewScoped") ?? false;
                var fields = GroupGetUtils.GetRequestedFields(p, "fields");

                IEnumerable<Group> src = GroupGetUtils.CollectGroups(doc);

                if (viewScoped)
                {
                    var curView = uiapp.ActiveUIDocument.ActiveView;
                    src = src.Where(g => g.OwnerViewId == curView.Id || g.OwnerViewId == ElementId.InvalidElementId);
                }

                var all = src.ToList();
                var page = all.Skip(skip).Take(count).ToList();

                var groups = page.Select(g =>
                {
                    var location = GroupGetUtils.GetLocationPoint(g);
                    var ownerView = GroupGetUtils.GetOwnerView(doc, g);
                    var levelId = GroupGetUtils.GetLevelIdSafe(g);
                    var members = (fields.Count == 0 || fields.Contains("memberCount")) ? GroupGetUtils.GetMemberIdsSafe(g) : null;

                    return new
                    {
                        elementId = g.Id.IntegerValue,
                        groupTypeId = g.GroupType != null ? g.GroupType.Id.IntegerValue : (int?)null,
                        groupTypeName = g.GroupType?.Name,
                        category = GroupGetUtils.CategoryKindOf(g),
                        memberCount = members?.Count,
                        origin = (fields.Count == 0 || fields.Contains("origin")) ? GroupGetUtils.ToMm(location?.Point) : null,
                        levelId = (fields.Count == 0 || fields.Contains("levelId")) ? (levelId?.IntegerValue) : (int?)null,
                        viewId = (fields.Count == 0 || fields.Contains("viewId")) ? (ownerView?.Id.IntegerValue) : (int?)null,
                        isViewSpecific = (fields.Count == 0 || fields.Contains("isViewSpecific")) ? (ownerView != null) : (bool?)null,
                        isMirrored = (fields.Count == 0 || fields.Contains("isMirrored")) ? GroupGetUtils.GetIsMirrored(g) : (bool?)null,
                        isPinned = (fields.Count == 0 || fields.Contains("isPinned")) ? g.Pinned : (bool?)null,
                    };
                }).ToList();

                return new
                {
                    ok = true,
                    totalCount = all.Count,
                    groups
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 2) get_group_types : グループタイプ一覧
    // params: { categoryFilter?: ["ModelGroup","DetailGroup"], skip?, count? }
    // ------------------------------------------------------------
    public class GetGroupTypesHandler : IRevitCommandHandler
    {
        public string CommandName => "get_group_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = cmd.Params as JObject ?? new JObject();

                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? 100;
                var catFilter = p["categoryFilter"] as JArray;

                var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (catFilter != null)
                {
                    foreach (var x in catFilter.OfType<JValue>().Select(v => v.ToString()))
                        kinds.Add(x);
                }

                var allTypes = GroupGetUtils.CollectGroupTypes(doc).ToList();
                if (kinds.Count > 0)
                {
                    allTypes = allTypes
                        .Where(gt => kinds.Contains(GroupGetUtils.CategoryKindOf(gt)))
                        .ToList();
                }

                var page = allTypes.Skip(skip).Take(count).ToList();

                var groupTypes = page.Select(gt => new
                {
                    typeId = gt.Id.IntegerValue,
                    name = gt.Name,
                    category = GroupGetUtils.CategoryKindOf(gt),
                    instanceCount = (gt is ElementType et)
                        ? new FilteredElementCollector(doc).OfClass(typeof(Group))
                            .Cast<Group>()
                            .Count(g => g.GroupType != null && g.GroupType.Id == et.Id)
                        : 0
                }).ToList();

                return new
                {
                    ok = true,
                    totalCount = allTypes.Count,
                    groupTypes
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 3) get_group_info : 単一グループ詳細
    // params: { elementId:int, includeMembers?:bool, memberFields?:string[], includeOwnerView?:bool }
    // memberFields 例: elementId, category, typeName
    // ------------------------------------------------------------
    public class GetGroupInfoHandler : IRevitCommandHandler
    {
        public string CommandName => "get_group_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)cmd.Params;

                int elementId = p.Value<int>("elementId");
                bool includeMembers = p.Value<bool?>("includeMembers") ?? false;
                bool includeOwnerView = p.Value<bool?>("includeOwnerView") ?? true;
                var memberFields = GroupGetUtils.GetRequestedFields(p, "memberFields");

                var g = doc.GetElement(new ElementId(elementId)) as Group;
                if (g == null) return new { ok = false, msg = $"Group not found: {elementId}" };

                var loc = GroupGetUtils.GetLocationPoint(g);
                var ownerView = includeOwnerView ? GroupGetUtils.GetOwnerView(doc, g) : null;
                var levelId = GroupGetUtils.GetLevelIdSafe(g);

                object? members = null;
                int? memberCount = null;
                if (includeMembers)
                {
                    var ids = GroupGetUtils.GetMemberIdsSafe(g);
                    memberCount = ids.Count;
                    members = ids.Select(id =>
                    {
                        var el = doc.GetElement(id);
                        return new
                        {
                            elementId = id.IntegerValue,
                            category = el?.Category?.Name,
                            typeName = GroupGetUtils.GetElementTypeName(doc, id)
                        };
                    }).ToList();
                }
                else
                {
                    memberCount = GroupGetUtils.GetMemberIdsSafe(g).Count;
                }

                return new
                {
                    ok = true,
                    elementId = g.Id.IntegerValue,
                    groupTypeId = g.GroupType != null ? g.GroupType.Id.IntegerValue : (int?)null,
                    groupTypeName = g.GroupType?.Name,
                    category = GroupGetUtils.CategoryKindOf(g),
                    origin = GroupGetUtils.ToMm(loc?.Point),
                    levelId = levelId?.IntegerValue,
                    viewId = ownerView?.Id.IntegerValue,
                    isViewSpecific = ownerView != null,
                    isMirrored = GroupGetUtils.GetIsMirrored(g),
                    isPinned = g.Pinned,
                    memberCount,
                    members
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 4) get_element_group_membership : 任意要素が属するグループ一覧
    // params: { elementId:int }
    // 実装簡素化のため全グループ走査（単発利用を想定）
    // ------------------------------------------------------------
    public class GetElementGroupMembershipHandler : IRevitCommandHandler
    {
        public string CommandName => "get_element_group_membership";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)cmd.Params;

                int elementId = p.Value<int>("elementId");
                var targetId = new ElementId(elementId);
                var target = doc.GetElement(targetId);
                if (target == null) return new { ok = false, msg = $"Element not found: {elementId}" };

                var memberships = new List<object>();
                foreach (var g in GroupGetUtils.CollectGroups(doc))
                {
                    var mem = GroupGetUtils.GetMemberIdsSafe(g);
                    if (mem.Any(id => id.IntegerValue == elementId))
                    {
                        var ownerView = GroupGetUtils.GetOwnerView(doc, g);
                        memberships.Add(new
                        {
                            groupId = g.Id.IntegerValue,
                            groupTypeId = g.GroupType != null ? g.GroupType.Id.IntegerValue : (int?)null,
                            groupTypeName = g.GroupType?.Name,
                            category = GroupGetUtils.CategoryKindOf(g),
                            isViewSpecific = ownerView != null,
                            viewId = ownerView?.Id.IntegerValue
                        });
                    }
                }

                return new
                {
                    ok = true,
                    elementId = elementId,
                    memberships
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 5) get_groups_in_view : 指定ビューに可視なグループ一覧
    // params: { viewId:int, skip?, count? }
    // ------------------------------------------------------------
    public class GetGroupsInViewHandler : IRevitCommandHandler
    {
        public string CommandName => "get_groups_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)cmd.Params;

                int viewId = p.Value<int>("viewId");
                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? 100;

                var view = doc.GetElement(new ElementId(viewId)) as View;
                if (view == null) return new { ok = false, msg = $"View not found: {viewId}" };

                var all = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Group))
                    .Cast<Group>()
                    .ToList();

                var page = all.Skip(skip).Take(count).ToList();

                var groups = page.Select(g =>
                {
                    var ownerView = GroupGetUtils.GetOwnerView(doc, g);
                    return new
                    {
                        elementId = g.Id.IntegerValue,
                        groupTypeId = g.GroupType != null ? g.GroupType.Id.IntegerValue : (int?)null,
                        groupTypeName = g.GroupType?.Name,
                        category = GroupGetUtils.CategoryKindOf(g),
                        memberCount = GroupGetUtils.GetMemberIdsSafe(g).Count,
                        viewId = ownerView?.Id.IntegerValue,
                        isViewSpecific = ownerView != null
                    };
                }).ToList();

                return new
                {
                    ok = true,
                    totalCount = all.Count,
                    groups
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 6) get_group_members : グループのメンバーElementId一覧
    // params: { groupId:int, categoryFilter?:string[], skip?, count? }
    // ------------------------------------------------------------
    public class GetGroupMembersHandler : IRevitCommandHandler
    {
        public string CommandName => "get_group_members";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)cmd.Params;

                int groupId = p.Value<int>("groupId");
                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? 200;
                var catFilter = p["categoryFilter"] as JArray;

                var g = doc.GetElement(new ElementId(groupId)) as Group;
                if (g == null) return new { ok = false, msg = $"Group not found: {groupId}" };

                var ids = GroupGetUtils.GetMemberIdsSafe(g);
                var elems = ids.Select(id => doc.GetElement(id)).Where(e => e != null);

                if (catFilter != null && catFilter.Count > 0)
                {
                    var set = new HashSet<string>(catFilter.OfType<JValue>().Select(v => v.ToString()), StringComparer.OrdinalIgnoreCase);
                    elems = elems.Where(e => e.Category != null && (set.Contains(e.Category.Name) || set.Contains(((BuiltInCategory)e.Category.Id.IntegerValue).ToString())));
                }

                var list = elems.Select(e => e.Id.IntegerValue).ToList();
                var page = list.Skip(skip).Take(count).ToList();

                return new
                {
                    ok = true,
                    totalCount = list.Count,
                    elementIds = page
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 7) get_group_constraints_report : できない理由の事前可視化（簡易診断）
    // params: { groupId:int }
    // 注: Revit内部の拘束網は複雑なため、ここでは安全側の簡易チェックを実装
    // ------------------------------------------------------------
    public class GetGroupConstraintsReportHandler : IRevitCommandHandler
    {
        public string CommandName => "get_group_constraints_report";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)cmd.Params;

                int groupId = p.Value<int>("groupId");
                var g = doc.GetElement(new ElementId(groupId)) as Group;
                if (g == null) return new { ok = false, msg = $"Group not found: {groupId}" };

                var reasons = new List<string>();

                bool canMove = true;
                bool canRotate = true;
                bool canChangeType = true;

                if (g.Pinned)
                {
                    reasons.Add("Group is pinned.");
                    canMove = false;
                    canRotate = false;
                }

                if (!GroupGetUtils.HasLocation(g))
                {
                    reasons.Add("Group has no Location (cannot transform).");
                    canMove = false;
                    canRotate = false;
                }

                // OwnerView（ビュー依存＝詳細系）への配慮
                var ownerView = GroupGetUtils.GetOwnerView(doc, g);
                if (ownerView != null)
                {
                    // ビュー固有注記要素を含むため、ビュー間移動やレベル付替えは制限される
                    reasons.Add("Group is view-specific (detail group). Movement across views/levels is restricted.");
                }

                // メンバー拘束の簡易診断（ロック・ピン留めの存在）
                var mem = GroupGetUtils.GetMemberIdsSafe(g);
                int pinnedCount = 0;
                foreach (var id in mem)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    if (el.Pinned) pinnedCount++;
                }
                if (pinnedCount > 0)
                {
                    reasons.Add($"Contains pinned members: {pinnedCount}.");
                    // グループ移動時に自動でピン解除しない方針のため制限表示
                    canMove = false;
                    canRotate = false;
                }

                // タイプ変更はカテゴリの互換性や添付関係など多要因
                // ここでは安全側: ModelGroup⇔DetailGroup間の変更は不可
                var cat = GroupGetUtils.CategoryKindOf(g);
                if (cat == "ModelGroup" || cat == "DetailGroup")
                {
                    // 互換タイプが存在するか単純には判断できないため注意喚起
                    reasons.Add("Changing group type may affect member constraints.");
                }

                return new
                {
                    ok = true,
                    groupId = groupId,
                    canMove,
                    canRotate,
                    canChangeType,
                    reasons
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
