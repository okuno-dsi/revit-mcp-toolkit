#nullable enable
using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.RevitUI
{
    public class DebugPaneResolveCommand : IRevitCommandHandler
    {
        public string CommandName => "debug_resolve_dockable_pane";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();
            DockablePaneId id;
            var res = UiHelpers.TryResolvePaneId(p, out id);

            return new
            {
                ok = res,
                received = p,
                resolvedGuid = res ? id.Guid.ToString("D") : null,
                hints = new[]{
                    "Accepted keys: pane | builtIn | builtin | name | title | guid",
                    "Try: {\"name\":\"ProjectBrowser\"} or {\"name\":\"Properties\"}",
                    "If ok=false, Router may point to old class or add-in not reloaded"
                }
            };
        }
    }
}
