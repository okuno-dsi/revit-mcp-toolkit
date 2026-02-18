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

            // options: retry { maxWaitMs, pollMs }, fallbackToStash, maxAgeMs, allowCrossDoc, allowCrossView
            int maxWaitMs = 0, pollMs = 150, maxAgeMs = 2000;
            bool fallbackToStash = true;
            bool allowCrossDoc = false;
            bool allowCrossView = false;
            string liveError = string.Empty;
            try
            {
                var p = cmd?.Params as JObject;
                var t1 = p?.SelectToken("retry.maxWaitMs");
                var t2 = p?.SelectToken("retry.pollMs");
                var t3 = p?.SelectToken("fallbackToStash");
                var t4 = p?.SelectToken("maxAgeMs");
                var t5 = p?.SelectToken("allowCrossDoc");
                var t6 = p?.SelectToken("allowCrossView");

                var mw = t1 != null ? t1.ToObject<int?>() : null;
                var pm = t2 != null ? t2.ToObject<int?>() : null;
                var fb = t3 != null ? t3.ToObject<bool?>() : null;
                var ma = t4 != null ? t4.ToObject<int?>() : null;
                var acd = t5 != null ? t5.ToObject<bool?>() : null;
                var acv = t6 != null ? t6.ToObject<bool?>() : null;

                maxWaitMs = Math.Max(0, mw ?? 0);
                pollMs = Math.Min(1000, Math.Max(50, pm ?? 150));
                fallbackToStash = fb ?? false;
                maxAgeMs = Math.Max(0, ma ?? 2000);
                allowCrossDoc = acd ?? false;
                allowCrossView = acv ?? false;
            }
            catch { }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            System.Collections.Generic.List<int> ids = null;
            while (true)
            {
                try
                {
                    ids = uidoc.Selection.GetElementIds().Select(x => x.IntValue()).ToList();
                    if (ids.Count > 0) break;
                }
                catch (Exception ex)
                {
                    liveError = ex.Message;
                    break;
                }
                if (sw.ElapsedMilliseconds >= maxWaitMs || maxWaitMs <= 0) break;
                try { System.Threading.Thread.Sleep(pollMs); } catch { break; }
            }

            string source = "live";
            if ((ids == null || ids.Count == 0) && fallbackToStash)
            {
                var snap = SelectionStash.GetLastNonEmptySnapshot();
                var age = (DateTime.UtcNow - snap.ObservedUtc).TotalMilliseconds;
                bool docMatch = allowCrossDoc
                                || string.IsNullOrWhiteSpace(snap.DocPath)
                                || string.Equals(snap.DocPath, doc.PathName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(snap.DocTitle, doc.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                bool viewMatch = allowCrossView
                                 || snap.ActiveViewId == 0
                                 || snap.ActiveViewId == (uidoc.ActiveView?.Id?.IntValue() ?? 0);
                if (snap.Ids != null && snap.Ids.Length > 0 && (maxAgeMs <= 0 || age <= maxAgeMs) && docMatch && viewMatch)
                {
                    ids = new System.Collections.Generic.List<int>(snap.Ids);
                    source = "stash";
                }
            }

            // Update stash if we obtained a fresh live selection
            if (source == "live" && ids != null)
            {
                string docPath = string.Empty, docTitle = string.Empty; int viewId = 0;
                try { docPath = doc.PathName ?? string.Empty; } catch { }
                try { docTitle = doc.Title ?? string.Empty; } catch { }
                try { viewId = uidoc.ActiveView?.Id?.IntValue() ?? 0; } catch { }
                SelectionStash.Set(ids, docPath, docTitle, viewId, AppServices.CurrentDocKey);
            }

            var browserElementIds = new System.Collections.Generic.List<int>();
            var modelElementIds = new System.Collections.Generic.List<int>();
            var missingElementIds = new System.Collections.Generic.List<int>();
            foreach (var idInt in (ids ?? new System.Collections.Generic.List<int>()))
            {
                var eid = Autodesk.Revit.DB.ElementIdCompat.From(idInt);
                var e = doc.GetElement(eid);
                if (e == null)
                {
                    missingElementIds.Add(idInt);
                    continue;
                }

                // Project Browser items are typically view/sheet/schedule/family/type.
                bool browserLike = (e is Autodesk.Revit.DB.Family)
                                   || (e is ElementType)
                                   || (e is View);
                if (browserLike) browserElementIds.Add(idInt);
                else modelElementIds.Add(idInt);
            }

            string selectionKind = "Unknown";
            if (browserElementIds.Count > 0 && modelElementIds.Count == 0) selectionKind = "ProjectBrowser";
            else if (browserElementIds.Count == 0 && modelElementIds.Count > 0) selectionKind = "Model";
            else if (browserElementIds.Count > 0 && modelElementIds.Count > 0) selectionKind = "Mixed";
            else if ((ids?.Count ?? 0) == 0)
            {
                var av = uidoc.ActiveView;
                if (av != null && av.ViewType == ViewType.ProjectBrowser) selectionKind = "ProjectBrowserNonElementOrNone";
                else selectionKind = "None";
            }

            var snapOut = SelectionStash.GetSnapshot();
            return new
            {
                ok = true,
                elementIds = ids ?? new System.Collections.Generic.List<int>(),
                count = ids?.Count ?? 0,
                selectionKind,
                isProjectBrowserActive = (uidoc.ActiveView != null && uidoc.ActiveView.ViewType == ViewType.ProjectBrowser),
                browserElementIds,
                modelElementIds,
                missingElementIds,
                classificationCounts = new
                {
                    browser = browserElementIds.Count,
                    model = modelElementIds.Count,
                    missing = missingElementIds.Count
                },
                source,
                selectionSource = source,
                liveError = string.IsNullOrWhiteSpace(liveError) ? null : liveError,
                observedAtUtc = snapOut.ObservedUtc,
                revision = snapOut.Revision,
                docPath = snapOut.DocPath,
                docTitle = snapOut.DocTitle,
                docKey = snapOut.DocKey,
                activeViewId = snapOut.ActiveViewId,
                hash = snapOut.Hash
            };
        }
    }
}

