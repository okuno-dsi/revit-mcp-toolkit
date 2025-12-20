// ================================================================
// File   : RevitUI/DockablePaneCommands.cs
// Purpose: DockablePane の列挙 / 表示 / 非表示（UIスレッド実行 + モード選択）
// Target : .NET Framework 4.8 / C# 8 / Revit 2023+
// Notes  : mode="api"(既定) | "toggle" | "both"
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.RevitUI
{
    public class ListDockablePanesCommand : IRevitCommandHandler
    {
        public string CommandName => "list_dockable_panes";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var panes = new List<object>();

            if (UiHelpers.TryGetBuiltInPaneId("ProjectBrowser", out var pb))
                panes.Add(new { builtIn = "ProjectBrowser", name = "ProjectBrowser", title = "Project Browser", guid = pb.Guid.ToString("D"), resolvable = true });
            else
                panes.Add(new { builtIn = "ProjectBrowser", name = "ProjectBrowser", title = "Project Browser", guid = (string)null, resolvable = false });

            if (UiHelpers.TryGetBuiltInPaneId("Properties", out var prop))
                panes.Add(new { builtIn = "Properties", name = "Properties", title = "Properties", guid = prop.Guid.ToString("D"), resolvable = true });
            else
                panes.Add(new { builtIn = "Properties", name = "Properties", title = "Properties", guid = (string)null, resolvable = false });

            return new { ok = true, count = panes.Count, panes = panes };
        }
    }

    public class ShowDockablePaneCommand : IRevitCommandHandler
    {
        public string CommandName => "show_dockable_pane";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var mode = (p.Value<string>("mode") ?? "api").ToLowerInvariant();

            if (!UiHelpers.TryResolvePaneId(p, out var paneId))
                return new { ok = false, msg = "Pane not resolved. Use {name|pane|builtIn|builtin|title} or non-empty {guid}.", received = p };

            var steps = new List<object>();
            bool ok = false;

            try
            {
                // 1) API（ExternalEventで UI スレッド実行）
                if (mode == "api" || mode == "both")
                {
                    ok = UiEventPump.Instance.InvokeSmart(uiapp, app =>
                    {
                        var pane = UiCommandHelpers.PaneResolveUtil.TryGetPane(app, paneId);
                        if (pane == null) throw new InvalidOperationException("DockablePane not found");
                        pane.Show();

                        // 検出は参考値にする（失敗しても例外にはしない）
                        bool visible = UiCommandHelpers.TryGetVisible(pane, true);
                        return true; // ← 呼べたら成功扱い（環境により visible は嘘をつくため）
                    });
                    steps.Add(new { step = "API Show()", ok });
                }

                // 2) toggle フォールバック
                if (!ok && (mode == "toggle" || mode == "both"))
                {
                    var toggled = PaneToggleHelper.TryToggleBuiltInPane(uiapp, p);
                    steps.Add(new { step = "UI toggle", toggled });
                    ok = toggled;
                }

                return new { ok, guid = paneId.Guid.ToString("D"), attempts = steps };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message, guid = paneId.Guid.ToString("D"), attempts = steps };
            }
        }
    }

    public class HideDockablePaneCommand : IRevitCommandHandler
    {
        public string CommandName => "hide_dockable_pane";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var mode = (p.Value<string>("mode") ?? "api").ToLowerInvariant();

            if (!UiHelpers.TryResolvePaneId(p, out var paneId))
                return new { ok = false, msg = "Pane not resolved. Use {name|pane|builtIn|builtin|title} or non-empty {guid}.", received = p };

            var steps = new List<object>();
            bool ok = false;

            try
            {
                // 1) API（ExternalEventで UI スレッド実行）
                if (mode == "api" || mode == "both")
                {
                    ok = UiEventPump.Instance.InvokeSmart(uiapp, app =>
                    {
                        var pane = UiCommandHelpers.PaneResolveUtil.TryGetPane(app, paneId);
                        if (pane == null) throw new InvalidOperationException("DockablePane not found");
                        pane.Hide();

                        // 検出は参考値
                        bool visible = UiCommandHelpers.TryGetVisible(pane, true);
                        return true; // ← 呼べたら成功扱い
                    });
                    steps.Add(new { step = "API Hide()", ok });
                }

                // 2) toggle フォールバック
                if (!ok && (mode == "toggle" || mode == "both"))
                {
                    var toggled = PaneToggleHelper.TryToggleBuiltInPane(uiapp, p);
                    steps.Add(new { step = "UI toggle", toggled });
                    ok = toggled;
                }

                return new { ok, guid = paneId.Guid.ToString("D"), attempts = steps };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message, guid = paneId.Guid.ToString("D"), attempts = steps };
            }
        }
    }

    /// <summary>
    /// 「検出してトグル」するバージョン（Router が参照しているなら必要）
    /// </summary>
    public class ToggleDockablePaneCommand : IRevitCommandHandler
    {
        public string CommandName => "toggle_dockable_pane";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            var mode = (p.Value<string>("mode") ?? "both").ToLowerInvariant();

            if (!UiHelpers.TryResolvePaneId(p, out var paneId))
                return new { ok = false, msg = "Pane not resolved. Use {name|pane|builtIn|builtin|title} or non-empty {guid}.", received = p };

            var steps = new List<object>();
            bool ok = false;

            try
            {
                // 可視状態を UI スレッドで取得（取れなければ null）
                bool? shown = UiEventPump.Instance.InvokeSmart(uiapp, app =>
                {
                    try
                    {
                        var pane = app.GetDockablePane(paneId);
                        return (bool?)UiCommandHelpers.TryGetVisible(pane, true);
                    }
                    catch { return null; }
                });
                steps.Add(new { step = "probe", hasState = shown.HasValue, shown = shown ?? false });

                // 1) API トグル
                if ((mode == "api" || mode == "both") && shown.HasValue)
                {
                    ok = UiEventPump.Instance.InvokeSmart(uiapp, app =>
                    {
                        var pane = app.GetDockablePane(paneId);
                        if (shown.Value) pane.Hide(); else pane.Show();
                        UiCommandHelpers.UiDelay(120);
                        // 恐らくトグルできた前提で true を返す（検出が不安定な環境があるため）
                        return true;
                    });
                    steps.Add(new { step = "API Toggle()", ok });
                }

                // 2) UI トグル
                if (!ok && (mode == "toggle" || mode == "both"))
                {
                    var toggled = PaneToggleHelper.TryToggleBuiltInPane(uiapp, p);
                    steps.Add(new { step = "UI toggle", toggled });
                    ok = toggled;
                }

                return new { ok, guid = paneId.Guid.ToString("D"), attempts = steps };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message, guid = paneId.Guid.ToString("D"), attempts = steps };
            }
        }
    }

    // ---- Helpers -------------------------------------------------

    internal static class PaneToggleCommandIds
    {
        public static readonly string[] ProjectBrowser = new[]
        {
            "ID_UI_TOGGLE_PROJECT_BROWSER",
            "ID_VIEW_USERINTERFACE_PROJECTBROWSER",
            "ID_PROJECTBROWSER",
            "ID_USER_INTERFACE_PROJECT_BROWSER",
            "ID_VIEW_USERINTERFACE_TOGGLEPROJECTBROWSER",
            "ID_TOGGLE_PROJECT_BROWSER",
            "ID_TOGGLEPROJECTBROWSER"
        };

        public static readonly string[] Properties = new[]
        {
            "ID_UI_TOGGLE_PROPERTIES",
            "ID_VIEW_USERINTERFACE_PROPERTIES",
            "ID_PROPERTIES",
            "ID_USER_INTERFACE_PROPERTIES_PALETTE",
            "ID_VIEW_USERINTERFACE_TOGGLEPROPERTIES",
            "ID_TOGGLE_PROPERTIES",
            "ID_TOGGLEPROPERTIES"
        };
    }

    internal static class PaneToggleHelper
    {
        /// <summary>
        /// UI トグルを実行。p に "toggleIds":[ "ID_...", ... ] があればそれを最優先で試行。
        /// </summary>
        public static bool TryToggleBuiltInPane(UIApplication uiapp, JObject p)
        {
            var arr = p["toggleIds"] as JArray;
            if (arr != null && arr.Count > 0)
            {
                var ids = arr.ToObject<string[]>();
                if (ids != null && ids.Length > 0)
                    return UiEventPump.Instance.InvokeSmart(uiapp, app => UiCommandHelpers.TryPostByIds(app, ids));
            }

            var tok = (p["pane"] ?? p["builtIn"] ?? p["builtin"] ?? p["name"] ?? p["title"])?.ToString() ?? "";
            bool isPB = tok.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0
                     || tok.IndexOf("browser", StringComparison.OrdinalIgnoreCase) >= 0;

            var defaults = isPB ? PaneToggleCommandIds.ProjectBrowser : PaneToggleCommandIds.Properties;
            return UiEventPump.Instance.Invoke(app => UiCommandHelpers.TryPostByIds(app, defaults));
        }
    }
}
