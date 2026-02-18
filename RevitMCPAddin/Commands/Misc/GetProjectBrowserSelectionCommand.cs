using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Misc
{
    public class GetProjectBrowserSelectionCommand : IRevitCommandHandler
    {
        public string CommandName => "get_project_browser_selection";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            // Same retry/fallback behavior as get_selected_element_ids for stability.
            int maxWaitMs = 0, pollMs = 150, maxAgeMs = 2000;
            bool fallbackToStash = true;
            bool allowCrossDoc = false;
            bool allowCrossView = true; // Browser selection can be independent from active view.
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
                fallbackToStash = fb ?? true;
                maxAgeMs = Math.Max(0, ma ?? 2000);
                allowCrossDoc = acd ?? false;
                allowCrossView = acv ?? true;
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

            if (source == "live" && ids != null)
            {
                string docPath = string.Empty, docTitle = string.Empty; int viewId = 0;
                try { docPath = doc.PathName ?? string.Empty; } catch { }
                try { docTitle = doc.Title ?? string.Empty; } catch { }
                try { viewId = uidoc.ActiveView?.Id?.IntValue() ?? 0; } catch { }
                SelectionStash.Set(ids, docPath, docTitle, viewId, AppServices.CurrentDocKey);
            }

            var families = new JArray();
            var familyTypes = new JArray();
            var views = new JArray();
            var sheets = new JArray();
            var schedules = new JArray();
            var others = new JArray();
            var missing = new JArray();

            foreach (var idInt in (ids ?? new System.Collections.Generic.List<int>()))
            {
                var eid = Autodesk.Revit.DB.ElementIdCompat.From(idInt);
                var e = doc.GetElement(eid);
                if (e == null)
                {
                    missing.Add(new JObject { ["elementId"] = idInt, ["reason"] = "not_found" });
                    continue;
                }

                var item = BuildItem(doc, e);

                if (e is Autodesk.Revit.DB.Family)
                    families.Add(item);
                else if (e is ViewSheet)
                    sheets.Add(item);
                else if (e is ViewSchedule)
                    schedules.Add(item);
                else if (e is View)
                    views.Add(item);
                else if (e is ElementType)
                    familyTypes.Add(item);
                else
                    others.Add(item);
            }

            return new
            {
                ok = true,
                source,
                selectionSource = source,
                liveError = string.IsNullOrWhiteSpace(liveError) ? null : liveError,
                count = ids?.Count ?? 0,
                families,
                familyTypes,
                views,
                sheets,
                schedules,
                others,
                missing,
                counts = new
                {
                    families = families.Count,
                    familyTypes = familyTypes.Count,
                    views = views.Count,
                    sheets = sheets.Count,
                    schedules = schedules.Count,
                    others = others.Count,
                    missing = missing.Count
                },
                msg = (ids == null || ids.Count == 0)
                    ? "No selected elements. In Project Browser, only element-backed rows are retrievable."
                    : "OK"
            };
        }

        private static JObject BuildItem(Document doc, Element e)
        {
            var jo = new JObject
            {
                ["elementId"] = e.Id.IntValue(),
                ["uniqueId"] = e.UniqueId ?? string.Empty,
                ["name"] = e.Name ?? string.Empty,
                ["className"] = e.GetType().Name,
                ["category"] = e.Category?.Name ?? string.Empty,
            };

            var typeId = e.GetTypeId();
            if (e is ElementType et)
            {
                jo["typeId"] = et.Id.IntValue();
                jo["typeName"] = et.Name ?? string.Empty;
                jo["familyName"] = TryGetFamilyName(et);
            }
            else if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                jo["typeId"] = typeId.IntValue();
                var te = doc.GetElement(typeId) as ElementType;
                jo["typeName"] = te?.Name ?? string.Empty;
                jo["familyName"] = TryGetFamilyName(te);
            }

            if (e is View v)
            {
                jo["viewType"] = v.ViewType.ToString();
                jo["isTemplate"] = v.IsTemplate;
            }

            return jo;
        }

        private static string TryGetFamilyName(ElementType et)
        {
            if (et == null) return string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(et.FamilyName)) return et.FamilyName;
            }
            catch { }
            try
            {
                var p = et.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
                if (p != null)
                {
                    var s = p.AsString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
