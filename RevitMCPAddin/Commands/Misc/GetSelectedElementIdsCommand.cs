using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Misc
{
    public class GetSelectedElementIdsCommand : IRevitCommandHandler
    {
        // 呼び出し用メソッド名
        public string CommandName => "get_selected_element_ids";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            // options: retry { maxWaitMs, pollMs }, fallbackToStash, maxAgeMs
            int maxWaitMs = 0, pollMs = 150, maxAgeMs = 2000;
            bool fallbackToStash = false;
            try
            {
                var p = cmd?.Params as JObject;
                var t1 = p?.SelectToken("retry.maxWaitMs");
                var t2 = p?.SelectToken("retry.pollMs");
                var t3 = p?.SelectToken("fallbackToStash");
                var t4 = p?.SelectToken("maxAgeMs");

                var mw = t1 != null ? t1.ToObject<int?>() : null;
                var pm = t2 != null ? t2.ToObject<int?>() : null;
                var fb = t3 != null ? t3.ToObject<bool?>() : null;
                var ma = t4 != null ? t4.ToObject<int?>() : null;

                maxWaitMs = Math.Max(0, mw ?? 0);
                pollMs = Math.Min(1000, Math.Max(50, pm ?? 150));
                fallbackToStash = fb ?? false;
                maxAgeMs = Math.Max(0, ma ?? 2000);
            }
            catch { }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Collections.Generic.List<int> ids = null;
            while (true)
            {
                ids = uidoc.Selection.GetElementIds().Select(x => x.IntegerValue).ToList();
                if (ids.Count > 0) break;
                if (sw.ElapsedMilliseconds >= maxWaitMs || maxWaitMs <= 0) break;
                try { System.Threading.Thread.Sleep(pollMs); } catch { break; }
            }

            string source = "live";
            if (ids.Count == 0 && fallbackToStash)
            {
                var snap = SelectionStash.GetSnapshot();
                var age = (DateTime.UtcNow - snap.ObservedUtc).TotalMilliseconds;
                if (snap.Ids != null && snap.Ids.Length > 0 && (maxAgeMs <= 0 || age <= maxAgeMs))
                {
                    ids = new System.Collections.Generic.List<int>(snap.Ids);
                    source = "stash";
                }
            }

            // Update stash if we obtained a fresh live selection
            if (source == "live")
            {
                string docPath = string.Empty, docTitle = string.Empty; int viewId = 0;
                try { docPath = doc.PathName ?? string.Empty; } catch { }
                try { docTitle = doc.Title ?? string.Empty; } catch { }
                try { viewId = uidoc.ActiveView?.Id?.IntegerValue ?? 0; } catch { }
                SelectionStash.Set(ids, docPath, docTitle, viewId);
            }

            var snapOut = SelectionStash.GetSnapshot();
            return new
            {
                ok = true,
                elementIds = ids ?? new System.Collections.Generic.List<int>(),
                count = ids?.Count ?? 0,
                source,
                observedAtUtc = snapOut.ObservedUtc,
                revision = snapOut.Revision,
                docPath = snapOut.DocPath,
                docTitle = snapOut.DocTitle,
                activeViewId = snapOut.ActiveViewId,
                hash = snapOut.Hash
            };
        }
    }
}
