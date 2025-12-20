// RevitMCPAddin/Commands/ViewOps/DeleteOrphanElevationMarkersCommand.cs
// Purpose:
//  - Delete "orphan" ElevationMarker elements that no longer own any elevation views.
//  - This happens when an elevation/door-window view is deleted but the marker remains.
//
// Notes:
//  - A single ElevationMarker can host multiple elevation views (slots). We delete the marker
//    only when ALL slots have no live View in the document.
//  - Supports dryRun and chunked transactions to avoid long single transactions.
//
// Target: .NET Framework 4.8 / Revit 2023+ / C# 8
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class DeleteOrphanElevationMarkersCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_orphan_elevation_markers";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null)
                    return ResultUtil.Err("No active document.");

                var p = (JObject)(cmd.Params ?? new JObject());

                bool dryRun = p.Value<bool?>("dryRun") ?? p.Value<bool?>("dry_run") ?? false;
                int viewId = p.Value<int?>("viewId") ?? 0;
                int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 200);
                int detailLimit = Math.Max(0, p.Value<int?>("detailLimit") ?? 200);
                int maxIds = Math.Max(0, p.Value<int?>("maxIds") ?? 2000);
                int limit = Math.Max(0, p.Value<int?>("limit") ?? 0);

                View scopeView = null;
                if (viewId > 0)
                {
                    scopeView = doc.GetElement(new ElementId(viewId)) as View;
                    if (scopeView == null)
                        return ResultUtil.Err($"View not found: {viewId}");
                }

                // Collect ElevationMarker elements (optionally within a view)
                FilteredElementCollector collector;
                try
                {
                    collector = (scopeView != null)
                        ? new FilteredElementCollector(doc, scopeView.Id)
                        : new FilteredElementCollector(doc);
                }
                catch (Exception ex)
                {
                    return ResultUtil.Err("Failed to create element collector: " + ex.Message);
                }

                var markers = collector
                    .OfClass(typeof(ElevationMarker))
                    .Cast<ElevationMarker>()
                    .ToList();

                var orphanIds = new List<int>();
                var orphanDetails = new List<object>();

                foreach (var marker in markers)
                {
                    var scan = ScanMarker(doc, marker);
                    if (!scan.IsOrphan) continue;

                    orphanIds.Add(scan.MarkerId);
                    if (detailLimit > 0 && orphanDetails.Count < detailLimit)
                        orphanDetails.Add(scan.ToAnonObject());
                }

                int orphanTotal = orphanIds.Count;

                if (limit > 0 && orphanIds.Count > limit)
                {
                    orphanIds = orphanIds.Take(limit).ToList();
                }

                // Truncate id arrays in the response to avoid huge payloads by default
                var orphanIdsOut = TruncateIds(orphanIds, maxIds, out bool orphanIdsTruncated);

                if (dryRun)
                {
                    return new
                    {
                        ok = true,
                        dryRun = true,
                        scope = (scopeView != null) ? "view" : "document",
                        viewId = (scopeView != null) ? (int?)scopeView.Id.IntegerValue : null,
                        scannedMarkers = markers.Count,
                        orphanCount = orphanTotal,
                        orphanCountSelected = orphanIds.Count,
                        orphanMarkerIds = orphanIdsOut,
                        orphanMarkerIdsTruncated = orphanIdsTruncated,
                        orphanMarkers = orphanDetails,
                        msg = $"[DryRun] Found {orphanTotal} orphan ElevationMarker(s)."
                    };
                }

                // Delete in chunks to avoid long transaction
                var deleted = new List<int>();
                var failed = new List<object>();

                int processed = 0;
                while (processed < orphanIds.Count)
                {
                    var chunk = orphanIds.Skip(processed).Take(batchSize).ToList();
                    using (var tx = new Transaction(doc, "[MCP] Delete orphan elevation markers"))
                    {
                        tx.Start();
                        foreach (int markerId in chunk)
                        {
                            try
                            {
                                doc.Delete(new ElementId(markerId));
                                deleted.Add(markerId);
                            }
                            catch (Exception ex)
                            {
                                failed.Add(new { markerId, error = ex.Message });
                            }
                        }
                        tx.Commit();
                    }
                    processed += chunk.Count;
                }

                var deletedOut = TruncateIds(deleted, maxIds, out bool deletedIdsTruncated);

                return new
                {
                    ok = true,
                    dryRun = false,
                    scope = (scopeView != null) ? "view" : "document",
                    viewId = (scopeView != null) ? (int?)scopeView.Id.IntegerValue : null,
                    scannedMarkers = markers.Count,
                    orphanCount = orphanTotal,
                    deletedCount = deleted.Count,
                    failedCount = failed.Count,
                    deletedMarkerIds = deletedOut,
                    deletedMarkerIdsTruncated = deletedIdsTruncated,
                    failed,
                    msg = failed.Count == 0
                        ? $"Deleted {deleted.Count} orphan ElevationMarker(s)."
                        : $"Deleted {deleted.Count} orphan ElevationMarker(s). Failed: {failed.Count}."
                };
            }
            catch (Exception ex)
            {
                return ResultUtil.Err(ex.Message);
            }
        }

        private static List<int> TruncateIds(List<int> ids, int maxIds, out bool truncated)
        {
            truncated = false;
            if (maxIds <= 0) return ids;
            if (ids.Count <= maxIds) return ids;
            truncated = true;
            return ids.Take(maxIds).ToList();
        }

        private class MarkerScan
        {
            public int MarkerId;
            public List<int> SlotViewIds = new List<int>(4);
            public List<int> LiveViewIds = new List<int>();
            public List<int> MissingViewIds = new List<int>();
            public bool IsOrphan => LiveViewIds.Count == 0;

            public object ToAnonObject()
            {
                return new
                {
                    markerId = MarkerId,
                    slotViewIds = SlotViewIds,
                    liveViewIds = LiveViewIds,
                    missingViewIds = MissingViewIds
                };
            }
        }

        private static MarkerScan ScanMarker(Document doc, ElevationMarker marker)
        {
            var scan = new MarkerScan
            {
                MarkerId = marker.Id.IntegerValue
            };

            // ElevationMarker supports up to 4 elevation slots (0..3).
            for (int i = 0; i < 4; i++)
            {
                int rawId = 0;
                try
                {
                    var vid = marker.GetViewId(i);
                    rawId = (vid != null) ? vid.IntegerValue : 0;
                }
                catch
                {
                    rawId = 0;
                }

                scan.SlotViewIds.Add(rawId);

                if (rawId <= 0) continue;

                try
                {
                    var v = doc.GetElement(new ElementId(rawId)) as View;
                    if (v != null)
                        scan.LiveViewIds.Add(rawId);
                    else
                        scan.MissingViewIds.Add(rawId);
                }
                catch
                {
                    scan.MissingViewIds.Add(rawId);
                }
            }

            return scan;
        }
    }
}

