// ================================================================
// File: RevitUI/ViewWindowCommands.cs
// Purpose : Manage model view windows (list/activate/open/tile/close-inactive)
// Target  : .NET Framework 4.8 / Revit 2023+ / C# 8
// Notes   : - 個別ビューの「閉じる」APIは無いため CloseInactiveViews を使用
//           - ビューを「開く/アクティブ化」= UIDocument.RequestViewChange(View)
//           - PostCommand は環境差で void 返しの場合があるため try/catch→成功判定
//           - PostableCommand 名称差 (TileWindows/WindowTile) を候補リストで吸収
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.RevitUI;

namespace RevitMCPAddin.RevitUI
{
    public class ListOpenViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_open_views";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return new { ok = false, msg = "No active document." };

            var doc = uidoc.Document;
            var uivs = uidoc.GetOpenUIViews();
            var activeId = doc.ActiveView?.Id?.IntegerValue ?? -1;

            var items = new List<object>();
            foreach (var uiv in uivs)
            {
                try
                {
                    var vid = uiv.ViewId;
                    if (doc.GetElement(vid) is View v)
                    {
                        items.Add(new
                        {
                            viewId = vid.IntegerValue,
                            uniqueId = v.UniqueId,
                            name = v.Name,
                            viewType = v.ViewType.ToString(),
                            isActive = (vid.IntegerValue == activeId),
                            canBePrinted = v.CanBePrinted
                        });
                    }
                }
                catch { /* skip */ }
            }
            return new { ok = true, count = items.Count, views = items };
        }
    }

    public class ActivateViewCommand : IRevitCommandHandler
    {
        public string CommandName => "activate_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return new { ok = false, msg = "No active document." };
            var doc = uidoc.Document;
            var p = cmd.Params as JObject ?? new JObject();
            // Optional execution guard
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            View? target = null;

            JToken jVid;
            if (p.TryGetValue("viewId", out jVid))
            {
                target = doc.GetElement(new ElementId((int)jVid)) as View;
            }
            else if (p.TryGetValue("uniqueId", out var jUid))
            {
                target = doc.GetElement(jUid.ToString()) as View;
            }
            else if (p.TryGetValue("name", out var jName))
            {
                var name = jName.ToString();
                target = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            if (target == null) return new { ok = false, msg = "Target view not found." };

            var ok = UiHelpers.TryRequestViewChange(uidoc, target);
            return new { ok = ok, viewId = target.Id.IntegerValue, name = target.Name };
        }
    }

    public class OpenViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "open_views";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return new { ok = false, msg = "No active document." };
            var doc = uidoc.Document;
            var p = cmd.Params as JObject ?? new JObject();
            // Optional execution guard
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            var byIds = p["viewIds"] != null ? p["viewIds"].ToObject<List<int>>() : new List<int>();
            var byUids = p["uniqueIds"] != null ? p["uniqueIds"].ToObject<List<string>>() : new List<string>();
            var byNames = p["names"] != null ? p["names"].ToObject<List<string>>() : new List<string>();

            var opened = new List<object>();
            View? last = null;

            IEnumerable<View> Resolve()
            {
                foreach (var i in byIds)
                {
                    var v1 = doc.GetElement(new ElementId(i)) as View;
                    if (v1 != null && !v1.IsTemplate) yield return v1;
                }

                foreach (var u in byUids)
                {
                    var v2 = doc.GetElement(u) as View;
                    if (v2 != null && !v2.IsTemplate) yield return v2;
                }

                if (byNames.Any())
                {
                    var all = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();
                    foreach (var n in byNames)
                        foreach (var v3 in all.Where(v => v.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                            yield return v3;
                }
            }

            foreach (var v in Resolve())
            {
                var ok = UiHelpers.TryRequestViewChange(uidoc, v);
                opened.Add(new { ok = ok, viewId = v.Id.IntegerValue, name = v.Name });
                if (ok) last = v;
            }

            return new
            {
                ok = true,
                opened = opened,
                activated = last != null ? new { viewId = last.Id.IntegerValue, name = last.Name } : null
            };
        }
    }

    public class CloseInactiveViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "close_inactive_views";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var ok = UiCommandHelpers.TryPostByNames(uiapp,
                "CloseInactiveViews", "CloseInactiveWindows"
            );
            return new { ok = ok };
        }
    }

    public class CloseViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "close_views";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return new { ok = false, msg = "No active document." };
            var doc = uidoc.Document;

            var p = cmd.Params as JObject ?? new JObject();

            var open = uidoc.GetOpenUIViews() ?? new List<UIView>();
            var openIds = new HashSet<int>(open.Select(u => u.ViewId.IntegerValue));

            // Build target set to close
            var toClose = new HashSet<int>();

            // By viewIds
            var arrIds = p["viewIds"] as JArray;
            if (arrIds != null)
            {
                foreach (var t in arrIds)
                {
                    try
                    {
                        int id = (int)t;
                        if (openIds.Contains(id)) toClose.Add(id);
                    }
                    catch { }
                }
            }

            // Build maps for open views (uniqueId/name)
            var openMap = new Dictionary<int, View>();
            foreach (var u in open)
            {
                try { if (doc.GetElement(u.ViewId) is View v) openMap[u.ViewId.IntegerValue] = v; } catch { }
            }

            // By uniqueIds
            var arrUids = p["uniqueIds"] as JArray;
            if (arrUids != null)
            {
                var uids = arrUids.Select(x => x?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in openMap)
                {
                    var v = kv.Value;
                    var uid = v?.UniqueId ?? string.Empty;
                    if (uids.Contains(uid)) toClose.Add(kv.Key);
                }
            }

            // By names (case-insensitive; matches open views only)
            var arrNames = p["names"] as JArray;
            if (arrNames != null)
            {
                var names = arrNames.Select(x => x?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in openMap)
                {
                    var v = kv.Value;
                    var nm = v?.Name ?? string.Empty;
                    if (names.Contains(nm)) toClose.Add(kv.Key);
                }
            }

            // If nothing to close from current open set
            if (toClose.Count == 0)
            {
                return new { ok = true, closedCount = 0, requested = 0, keptIds = openIds.ToArray(), note = "No matching open views to close." };
            }

            // Compute keep set
            var keepIds = new HashSet<int>(openIds);
            foreach (var id in toClose) keepIds.Remove(id);

            // Ensure we keep at least one view (Revit cannot leave zero open)
            bool keptActiveDueToLimit = false;
            int activeId = doc.ActiveView?.Id?.IntegerValue ?? -1;
            if (keepIds.Count == 0)
            {
                // Prefer keeping the current active view even if requested to close
                if (openIds.Contains(activeId))
                {
                    toClose.Remove(activeId);
                    keepIds.Add(activeId);
                    keptActiveDueToLimit = true;
                }
                else
                {
                    // Fallback: keep the first open view
                    int fallback = openIds.First();
                    toClose.Remove(fallback);
                    keepIds.Add(fallback);
                    keptActiveDueToLimit = true;
                }
            }

            // 1) Activate one keep view as baseline
            int baselineId = keepIds.Contains(activeId) && activeId > 0 ? activeId : keepIds.First();
            try
            {
                var v0 = doc.GetElement(new ElementId(baselineId)) as View;
                if (v0 != null)
                {
                    UiCommandHelpers.RequestViewChangeSmart(uiapp, uidoc, v0, 150);
                }
            }
            catch { }

            // 2) Post Close Inactive Views (keeps only baseline open)
            bool posted = UiEventPump.Instance.InvokeSmart(uiapp, app => UiCommandHelpers.TryPostByNames(app, "CloseInactiveViews", "CloseInactiveWindows"));

            // 3) Re-open the rest of keep views
            foreach (var id in keepIds)
            {
                if (id == baselineId) continue;
                try
                {
                    var v = doc.GetElement(new ElementId(id)) as View;
                    if (v != null)
                        UiCommandHelpers.RequestViewChangeSmart(uiapp, uidoc, v, 120);
                }
                catch { }
            }

            // 4) Summarize
            var afterOpen = uidoc.GetOpenUIViews() ?? new List<UIView>();
            var afterIds = new HashSet<int>(afterOpen.Select(u => u.ViewId.IntegerValue));
            var actuallyClosed = openIds.Where(id => !afterIds.Contains(id)).ToArray();

            return new
            {
                ok = posted,
                requested = toClose.ToArray(),
                keptIds = keepIds.ToArray(),
                baselineId,
                keptActiveDueToLimit,
                closedCount = actuallyClosed.Length,
                closedIds = actuallyClosed
            };
        }
    }

    public class TileWindowsCommand : IRevitCommandHandler
    {
        public string CommandName => "tile_windows";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var steps = new List<object>();

            // 1) 開いている UI ビュー枚数を確認
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return new { ok = false, msg = "No active document." };

            var uivs = uidoc.GetOpenUIViews();
            int countBefore = (uivs != null) ? uivs.Count : 0;
            steps.Add(new { step = "check-open-views", openCount = countBefore });

            // 2) 足りなければもう1枚開く（最初に見つかった別View名/IDでOK）
            if (countBefore < 2)
            {
                try
                {
                    var doc = uidoc.Document;
                    // アクティブでない一般ビューを一つ探す（テンプレート/スケジュール等は避ける）
                    var another = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate && v.Id.IntegerValue != doc.ActiveView.Id.IntegerValue);

                    if (another != null)
                    {
                        // UIスレッドで開く
                        bool opened = UiCommandHelpers.RequestViewChangeSmart(uiapp, uidoc, another, 200);
                        steps.Add(new { step = "open-another-view", ok = opened, viewId = another.Id.IntegerValue, name = another.Name });

                    }
                    else
                    {
                        steps.Add(new { step = "open-another-view", ok = false, reason = "no candidate view" });
                    }
                }
                catch (Exception ex)
                {
                    steps.Add(new { step = "open-another-view", ok = false, ex = ex.Message });
                }
            }

            // 3) タイル実行（候補を順に試す）
            bool posted = false;
            try
            {
                // A) PostableCommand の列挙名（揺れ対策）
                posted = UiEventPump.Instance.InvokeSmart(uiapp, app =>
                    UiCommandHelpers.TryPostByNames(app, "TileWindows", "WindowTile")
                );

                // B) 直接 CommandId 候補（環境依存）
                if (!posted)
                {
                    posted = UiEventPump.Instance.InvokeSmart(uiapp, app =>
                        UiCommandHelpers.TryPostByIds(app,
                            "ID_WINDOW_TILE",         // 代表的
                            "ID_VIEW_WINDOW_TILE",    // 代替候補
                            "ID_TILE_WINDOWS"         // 代替候補
                        )
                    );
                }
                steps.Add(new { step = "post-tile", ok = posted });
            }
            catch (Exception ex)
            {
                steps.Add(new { step = "post-tile", ok = false, ex = ex.Message });
            }

            // 4) 結果まとめ
            return new
            {
                ok = posted,
                attempts = steps
            };
        }
    }

    public class ArrangeViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "arrange_views";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var mode = (p.Value<string>("mode") ?? "tile").ToLowerInvariant(); // "tile" | "tabbed" | "ids"
            var steps = new List<object>();
            bool ok = false;

            try
            {
                if (mode == "tile")
                {
                    ok = UiEventPump.Instance.InvokeSmart(uiapp, app =>
                        UiCommandHelpers.TryPostByNames(app, "TileWindows", "WindowTile")
                        || UiCommandHelpers.TryPostByIds(app, "ID_WINDOW_TILE", "ID_VIEW_WINDOW_TILE", "ID_TILE_WINDOWS")
                    );
                    steps.Add(new { step = "tile-defaults", ok });
                }
                else if (mode == "tabbed")
                {
                    // タブ表示に戻す候補（環境差あり：当たりIDが分かったらここに固定で追記）
                    ok = UiEventPump.Instance.InvokeSmart(uiapp, app =>
                        UiCommandHelpers.TryPostByIds(app,
                            "ID_VIEW_TABBED",           // 仮候補
                            "ID_WINDOW_TABBED",         // 仮候補
                            "ID_VIEW_WINDOW_TABBED"     // 仮候補
                        )
                    );
                    steps.Add(new { step = "tabbed-defaults", ok });
                }
                else if (mode == "ids")
                {
                    var ids = (p["commandIds"] as JArray)?.ToObject<string[]>() ?? Array.Empty<string>();
                    ok = ids.Length > 0 && UiEventPump.Instance.InvokeSmart(uiapp, app => UiCommandHelpers.TryPostByIds(app, ids));
                    steps.Add(new { step = "post-custom-ids", ok, count = ids.Length });
                }
                else
                {
                    steps.Add(new { step = "invalid-mode", mode });
                }
            }
            catch (Exception ex)
            {
                steps.Add(new { step = "exception", ex = ex.Message });
            }

            return new { ok, attempts = steps };
        }
    }


    public class CloseViewsExceptCommand : IRevitCommandHandler
    {
        public string CommandName => "close_views_except";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return new { ok = false, msg = "No active document." };
            var doc = uidoc.Document;

            var p = cmd.Params as JObject ?? new JObject();

            var open = uidoc.GetOpenUIViews() ?? new List<UIView>();
            var openIds = new HashSet<int>(open.Select(u => u.ViewId.IntegerValue));

            // Keep set (views to remain open)
            var keepIds = new HashSet<int>();

            // ViewIds
            var arrIds = p["viewIds"] as JArray;
            if (arrIds != null)
            {
                foreach (var t in arrIds)
                {
                    try
                    {
                        int id = (int)t;
                        if (openIds.Contains(id)) keepIds.Add(id);
                    }
                    catch { }
                }
            }

            // Build open map for uniqueId/name lookup
            var openMap = new Dictionary<int, View>();
            foreach (var u in open)
            {
                try { if (doc.GetElement(u.ViewId) is View v) openMap[u.ViewId.IntegerValue] = v; } catch { }
            }

            // UniqueIds
            var arrUids = p["uniqueIds"] as JArray;
            if (arrUids != null)
            {
                var uids = arrUids.Select(x => x?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in openMap)
                {
                    var v = kv.Value;
                    var uid = v?.UniqueId ?? string.Empty;
                    if (uids.Contains(uid)) keepIds.Add(kv.Key);
                }
            }

            // Names
            var arrNames = p["names"] as JArray;
            if (arrNames != null)
            {
                var names = arrNames.Select(x => x?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in openMap)
                {
                    var v = kv.Value;
                    var nm = v?.Name ?? string.Empty;
                    if (names.Contains(nm)) keepIds.Add(kv.Key);
                }
            }

            // If keepIds empty, enforce at least one (active or first)
            bool keptActiveDueToLimit = false;
            int activeId = doc.ActiveView?.Id?.IntegerValue ?? -1;
            if (keepIds.Count == 0)
            {
                if (openIds.Contains(activeId))
                {
                    keepIds.Add(activeId);
                    keptActiveDueToLimit = true;
                }
                else if (openIds.Count > 0)
                {
                    keepIds.Add(openIds.First());
                    keptActiveDueToLimit = true;
                }
            }

            // toClose = open - keep
            var toClose = new HashSet<int>(openIds);
            foreach (var id in keepIds) toClose.Remove(id);

            // Baseline view to keep open during CloseInactive
            int baselineId = keepIds.Contains(activeId) && activeId > 0 ? activeId : keepIds.First();
            try
            {
                var v0 = doc.GetElement(new ElementId(baselineId)) as View;
                if (v0 != null)
                {
                    UiCommandHelpers.RequestViewChangeSmart(uiapp, uidoc, v0, 150);
                }
            }
            catch { }

            // Close others via CloseInactiveViews
            bool posted = UiEventPump.Instance.InvokeSmart(uiapp, app => UiCommandHelpers.TryPostByNames(app, "CloseInactiveViews", "CloseInactiveWindows"));

            // Re-open remaining keep views (besides baseline)
            foreach (var id in keepIds)
            {
                if (id == baselineId) continue;
                try
                {
                    var v = doc.GetElement(new ElementId(id)) as View;
                    if (v != null)
                        UiCommandHelpers.RequestViewChangeSmart(uiapp, uidoc, v, 120);
                }
                catch { }
            }

            // Summarize
            var afterOpen = uidoc.GetOpenUIViews() ?? new List<UIView>();
            var afterIds = new HashSet<int>(afterOpen.Select(u => u.ViewId.IntegerValue));
            var actuallyClosed = openIds.Where(id => !afterIds.Contains(id)).ToArray();

            return new
            {
                ok = posted,
                keepRequested = keepIds.ToArray(),
                baselineId,
                keptActiveDueToLimit,
                closedCount = actuallyClosed.Length,
                closedIds = actuallyClosed
            };
        }
    }
}
