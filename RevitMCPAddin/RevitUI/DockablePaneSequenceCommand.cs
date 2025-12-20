#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.RevitUI
{
    public class DockablePaneSequenceCommand : IRevitCommandHandler
    {
        public string CommandName => "dockable_pane_sequence";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = cmd.Params as JObject ?? new JObject();

            // C#8対応: target-typed new() を使わない
            var steps = p["steps"] != null
                ? p["steps"].ToObject<List<JObject>>()
                : new List<JObject>();

            bool continueOnError = p.Value<bool?>("continueOnError") ?? true;

            var results = new List<object>();
            foreach (var s in steps)
            {
                string op = (s.Value<string>("op") ?? "").ToLowerInvariant();

                DockablePaneId paneId;
                if (!UiHelpers.TryResolvePaneId(s, out paneId))
                {
                    var r = new { op = op, ok = false, msg = "pane id not resolved (builtIn or guid required)" };
                    results.Add(r);
                    if (!continueOnError) return new { ok = false, results = results };
                    continue;
                }

                try
                {
                    var pane = uiapp.GetDockablePane(paneId);
                    if (op == "show") { pane.Show(); results.Add(new { op = op, ok = true }); }
                    else if (op == "hide") { pane.Hide(); results.Add(new { op = op, ok = true }); }
                    else
                    {
                        results.Add(new { op = op, ok = false, msg = "unsupported op (use 'show' or 'hide')" });
                        if (!continueOnError) return new { ok = false, results = results };
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new { op = op, ok = false, msg = ex.Message });
                    if (!continueOnError) return new { ok = false, results = results };
                }
            }

            return new { ok = true, results = results };
        }
    }
}
