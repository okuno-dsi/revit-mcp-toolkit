// ================================================================
// File: Commands/ViewOps/ShowAllInViewCommand.cs
// Purpose : "show_all_in_view" non-freezing + UI-like unhide (batched) + always Regenerate
// Target  : Revit 2023+ compatible (no GetHiddenElementIds / IsElementHidden)
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.RevitUI;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class ShowAllInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "show_all_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var swAll = Stopwatch.StartNew();
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return new { ok = false, msg = "No active document." };

                var p = (JObject?)(cmd.Params ?? new JObject());

                // ---- required ----
                var viewId = new ElementId(p!.Value<int>("viewId"));
                var view = doc.GetElement(viewId) as View;
                if (view == null) return new { ok = false, msg = $"viewId={viewId.IntegerValue} not found." };

                // ---- fast defaults (軽量パス) ----
                bool detachViewTemplate = p.Value<bool?>("detachViewTemplate") ?? true;
                bool includeTempReset = p.Value<bool?>("includeTempReset") ?? true;
                bool resetHiddenCategories = p.Value<bool?>("resetHiddenCategories") ?? true;
                bool resetFilterVisibility = p.Value<bool?>("resetFilterVisibility") ?? true;
                bool resetWorksetVisibility = p.Value<bool?>("resetWorksetVisibility") ?? true;

                // ---- heavy (任意) ----
                bool unhideElements = p.Value<bool?>("unhideElements") ?? false; // 既定: OFF（重い）
                bool clearElementOverrides = p.Value<bool?>("clearElementOverrides") ?? false; // 既定: OFF（重い）

                // ---- batch controls ----
                int batchSize = Math.Max(50, Math.Min(2000, p.Value<int?>("batchSize") ?? 400));
                int maxMillisPerTx = Math.Max(500, Math.Min(15000, p.Value<int?>("maxMillisPerTx") ?? 3000));
                int startIndex = Math.Max(0, p.Value<int?>("startIndex") ?? 0);

                // For subsequent slices (startIndex>0), skip one-time heavy resets to avoid
                // repeating the same work in every tick. Detach/reset only on the first slice.
                if (startIndex > 0)
                {
                    detachViewTemplate = false;
                    includeTempReset = false;
                    resetHiddenCategories = false;
                    resetFilterVisibility = false;
                    resetWorksetVisibility = false;
                }

                // ---- redraw control ----
                bool refreshView = p.Value<bool?>("refreshView") ?? true;
                bool activateView = p.Value<bool?>("activateView") ?? false; // optionally switch to target view

                var ogsEmpty = new OverrideGraphicSettings();

                int shownCats = 0, shownFilters = 0, shownWorksets = 0;
                int unhiddenElementsCount = 0, clearedElementOverrides = 0;
                bool templateDetached = false;

                using (var tx = new Transaction(doc, "[MCP] Show All in View (fast/batched)"))
                {
                    tx.Start();
                    var sw = Stopwatch.StartNew();

                    // 0) detach template
                    if (detachViewTemplate && view.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        try { view.ViewTemplateId = ElementId.InvalidElementId; templateDetached = true; }
                        catch (Exception ex) { RevitLogger.Warn("ViewTemplate detach failed.", ex); }
                    }
                    if (sw.ElapsedMilliseconds > maxMillisPerTx) goto COMMIT_AND_RETURN_FAST;

                    // 1) temp hide/isolate off
                    if (includeTempReset)
                    {
                        try
                        {
                            if (view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                                view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                        }
                        catch (Exception ex) { RevitLogger.Error("TemporaryViewMode disable failed.", ex); }
                    }
                    if (sw.ElapsedMilliseconds > maxMillisPerTx) goto COMMIT_AND_RETURN_FAST;

                    // 2) categories visible
                    if (resetHiddenCategories)
                    {
                        try
                        {
                            foreach (Category c in view.Document.Settings.Categories)
                            {
                                if (c == null) continue;
                                if (view.CanCategoryBeHidden(c.Id))
                                {
                                    view.SetCategoryHidden(c.Id, false);
                                    shownCats++;
                                }
                            }
                        }
                        catch (Exception ex) { RevitLogger.Error("SetCategoryHidden(false) failed.", ex); }
                    }
                    if (sw.ElapsedMilliseconds > maxMillisPerTx) goto COMMIT_AND_RETURN_FAST;

                    // 3) filters visible + clear OGS
                    if (resetFilterVisibility)
                    {
                        try
                        {
                            foreach (var fid in view.GetFilters())
                            {
                                try
                                {
                                    view.SetFilterVisibility(fid, true);
                                    view.SetFilterOverrides(fid, ogsEmpty);
                                    shownFilters++;
                                }
                                catch (Exception ex) { RevitLogger.Error($"Filter reset failed: {fid.IntegerValue}", ex); }
                            }
                        }
                        catch (Exception ex) { RevitLogger.Error("GetFilters failed.", ex); }
                    }
                    if (sw.ElapsedMilliseconds > maxMillisPerTx) goto COMMIT_AND_RETURN_FAST;

                    // 4) worksets visible
                    if (resetWorksetVisibility && doc.IsWorkshared)
                    {
                        try
                        {
                            var wsCol = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                            foreach (Workset ws in wsCol)
                            {
                                try
                                {
                                    view.SetWorksetVisibility(ws.Id, WorksetVisibility.Visible);
                                    shownWorksets++;
                                }
                                catch (Exception ex) { RevitLogger.Error($"SetWorksetVisibility failed: {ws.Id.IntegerValue}", ex); }
                            }
                        }
                        catch (Exception ex) { RevitLogger.Error("Workset visibility reset failed.", ex); }
                    }
                    if (sw.ElapsedMilliseconds > maxMillisPerTx) goto COMMIT_AND_RETURN_FAST;

                    // 5) heavy path (batched)
                    // Keep backward-compat: if caller omits param, enable unhide by default
                    // (first slice only). For subsequent slices, the flag is respected as-is.
                    if (startIndex == 0)
                    {
                        if (p["unhideElements"] == null || p["unhideElements"]?.Type == JTokenType.Null) unhideElements = true;
                    }
                    if (unhideElements || clearElementOverrides)
                    {
                        // Use a cached document-wide superset because hidden elements are NOT returned by view-limited collectors
                        var allIds = ShowAllIdsCache.GetOrBuild(doc, view, forceRebuild: startIndex == 0);

                        if (startIndex >= allIds.Count) startIndex = 0;
                        int end = Math.Min(allIds.Count, startIndex + batchSize);
                        var batch = allIds.GetRange(startIndex, end - startIndex);

                        // Unhide (UI-like)
                        if (unhideElements && batch.Count > 0)
                        {
                            var skipped = new JArray();
                            var errors = new JArray();
                            unhiddenElementsCount = UnhideElementsLikeUI(view, doc, batch, skipped, errors);
                            if (skipped.Count > 0) RevitLogger.Info($"ShowAll skipped (category hidden): {skipped.Count}");
                            if (errors.Count > 0) RevitLogger.Warn($"ShowAll per-item errors: {errors.Count}");
                        }

                        // Clear element overrides
                        if (clearElementOverrides && batch.Count > 0)
                        {
                            foreach (var id in batch)
                            {
                                try { view.SetElementOverrides(id, ogsEmpty); clearedElementOverrides++; }
                                catch (Exception ex) { RevitLogger.Error($"SetElementOverrides failed: {id.IntegerValue}", ex); }
                            }
                        }

                        int totalCount = allIds.Count;
                        int nextIndex = (end < totalCount) ? end : -1;
                        bool completed = (nextIndex == -1);

                        tx.Commit();

                        // ---- Always Regenerate and Refresh active view (for immediate UI update) ----
                        SafeRegenerate(doc);
                        if (activateView && uidoc != null) { UiHelpers.TryRequestViewChange(uidoc, view); }
                        SafeRefresh(uidoc, view, refreshView);

                        return new
                        {
                            ok = true,
                            completed,
                            nextIndex = completed ? (int?)null : nextIndex,
                            stats = new
                            {
                                templateDetached,
                                shownCats,
                                shownFilters,
                                shownWorksets,
                                unhiddenElements = unhiddenElementsCount,
                                clearedElementOverrides,
                                totalElementsEnumerated = totalCount
                            },
                            hint = completed
                                ? "Done."
                                : $"Continue with startIndex={nextIndex} (batchSize={batchSize}).",
                            elapsedMs = swAll.ElapsedMilliseconds
                        };
                    }

                COMMIT_AND_RETURN_FAST:
                    tx.Commit();
                }

                // ---- Fast path: Always Regenerate / Optional Refresh ----
                SafeRegenerate(doc);
                if (activateView && uidoc != null) { UiHelpers.TryRequestViewChange(uidoc, view); }
                SafeRefresh(uidoc, view, refreshView);

                return new
                {
                    ok = true,
                    completed = true,
                    nextIndex = (int?)null,
                    stats = new
                    {
                        templateDetached,
                        shownCats,
                        shownFilters,
                        shownWorksets,
                        unhiddenElements = unhiddenElementsCount,
                        clearedElementOverrides
                    },
                    hint = "Fast path finished.",
                    elapsedMs = swAll.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = $"Exception: {ex.Message}", detail = ex.ToString() };
            }
            finally { swAll.Stop(); }
        }

        // Lightweight cache to avoid re-enumerating all element ids on every tick.
        // The cache is per-document+view and is rebuilt on the first slice (startIndex==0).
        private static class ShowAllIdsCache
        {
            private static readonly object _gate = new object();
            private static readonly Dictionary<string, WeakReference<List<ElementId>>> _store = new Dictionary<string, WeakReference<List<ElementId>>>();

            private static string Key(Document doc, View view)
                => doc.GetHashCode().ToString() + ":" + (view?.Id.IntegerValue ?? 0).ToString();

            public static List<ElementId> GetOrBuild(Document doc, View view, bool forceRebuild)
            {
                var key = Key(doc, view);
                lock (_gate)
                {
                    if (!forceRebuild && _store.TryGetValue(key, out var wr))
                    {
                        if (wr != null && wr.TryGetTarget(out var cached) && cached != null)
                            return cached;
                    }

                    var fresh = new FilteredElementCollector(doc)
                                    .WhereElementIsNotElementType()
                                    .ToElementIds()
                                    .ToList();

                    _store[key] = new WeakReference<List<ElementId>>(fresh);
                    // Rudimentary cap: trim if map grows too large
                    if (_store.Count > 32)
                    {
                        // remove arbitrary first expired entry
                        var dead = _store.FirstOrDefault(kv => kv.Value == null || !kv.Value.TryGetTarget(out _));
                        if (!string.IsNullOrEmpty(dead.Key)) _store.Remove(dead.Key);
                    }
                    return fresh;
                }
            }
        }

        // ---- UI-equivalent Unhide helper (category gate + bulk + per-item retry) ----
        private static int UnhideElementsLikeUI(View view, Document doc, IEnumerable<ElementId> ids, JArray skipped, JArray errors)
        {
            var toUnhide = new List<ElementId>();

            foreach (var id in ids)
            {
                var e = doc.GetElement(id);
                if (e == null)
                {
                    errors.Add(new JObject { ["elementId"] = id.IntegerValue, ["reason"] = "element not found" });
                    continue;
                }

                // Only target elements that can be hidden in this view and are currently hidden-in-view
                bool canBeHidden = false; try { canBeHidden = e.CanBeHidden(view); } catch { }
                if (!canBeHidden) continue;

                bool isHiddenInView = false; try { isHiddenInView = e.IsHidden(view); } catch { }
                if (!isHiddenInView) continue;

                var cat = e.Category;
                if (cat != null)
                {
                    try
                    {
                        if (view.GetCategoryHidden(cat.Id))
                        {
                            skipped.Add(new JObject
                            {
                                ["elementId"] = id.IntegerValue,
                                ["reason"] = "category hidden in view",
                                ["categoryId"] = cat.Id.IntegerValue,
                                ["categoryName"] = cat.Name
                            });
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        RevitLogger.Info($"GetCategoryHidden failed: cat={cat.Id.IntegerValue}, reason={ex.Message}");
                        // try unhide anyway
                    }
                }

                toUnhide.Add(id);
            }

            int unhidden = 0;
            if (toUnhide.Count > 0)
            {
                try
                {
                    view.UnhideElements(toUnhide);
                    unhidden = toUnhide.Count;
                }
                catch (Exception ex)
                {
                    foreach (var id in toUnhide)
                    {
                        try { view.UnhideElements(new List<ElementId> { id }); unhidden++; }
                        catch (Exception ex1)
                        {
                            errors.Add(new JObject { ["elementId"] = id.IntegerValue, ["reason"] = ex1.Message });
                        }
                    }
                    RevitLogger.Error($"UnhideElements bulk-apply failed. Fallback to per-item. reason={ex.Message}");
                }
            }
            return unhidden;
        }

        // ---- redraw helpers ----
        private static void SafeRegenerate(Document doc)
        {
            try { doc.Regenerate(); } catch { /* no-op */ }
        }

        private static void SafeRefresh(UIDocument? uidoc, View view, bool refreshView)
        {
            if (!refreshView) return;
            try
            {
                // Always refresh the active UI view to reflect changes immediately.
                uidoc?.RefreshActiveView();
            }
            catch { /* no-op */ }
        }
    }
}
