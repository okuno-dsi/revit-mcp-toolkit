using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Models; // ★ 追加
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.RevisionCloud
{
    internal static class RevUnits
    {
        public static double ToMm(double ft) => ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        public static double MmToFt(double mm) => ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static object BBoxToMm(BoundingBoxXYZ bb)
        {
            if (bb == null) return null;
            return new
            {
                min = new { x = Math.Round(ToMm(bb.Min.X), 3), y = Math.Round(ToMm(bb.Min.Y), 3), z = Math.Round(ToMm(bb.Min.Z), 3) },
                max = new { x = Math.Round(ToMm(bb.Max.X), 3), y = Math.Round(ToMm(bb.Max.Y), 3), z = Math.Round(ToMm(bb.Max.Z), 3) }
            };
        }
    }

    // ------------------------------
    // list_revisions （DTOで返却）
    // ------------------------------
    public class ListRevisionsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_revisions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            bool includeClouds = p.Value<bool?>("includeClouds") ?? false;
            var cloudFields = p["cloudFields"] is JArray arr
                ? arr.Values<string>().Select(s => s?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;
            bool Want(string k)
            {
                if (cloudFields == null) return true;
                if (cloudFields.Contains(k)) return true;
                // Accept common aliases
                if (string.Equals(k, "ownerViewId", StringComparison.OrdinalIgnoreCase))
                    return cloudFields.Contains("viewId");
                if (string.Equals(k, "ownerViewName", StringComparison.OrdinalIgnoreCase))
                    return cloudFields.Contains("viewName");
                return false;
            }

            // 全Revision
            var revs = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Revision)).Cast<Autodesk.Revit.DB.Revision>()
                        .OrderBy(r => r.SequenceNumber).ToList();

            // すべてのクラウド（必要時のみ集計）
            Dictionary<int, List<Autodesk.Revit.DB.RevisionCloud>> cloudsByRevId = null;
            if (includeClouds)
            {
                var allClouds = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.RevisionCloud)).Cast<Autodesk.Revit.DB.RevisionCloud>().ToList();
                cloudsByRevId = allClouds.GroupBy(rc => rc.RevisionId?.IntegerValue ?? 0)
                                         .ToDictionary(g => g.Key, g => g.ToList());
            }

            var rs = RevisionSettings.GetRevisionSettings(doc);
            string numberingMode = rs.RevisionNumbering.ToString();

            var items = new List<RevisionInfo>();
            foreach (var r in revs)
            {
                int rid = r.Id.IntegerValue;

                // cloudCount & clouds
                int cloudCount = 0;
                List<object> cloudsOut = null;
                if (includeClouds && cloudsByRevId != null && cloudsByRevId.TryGetValue(rid, out var list))
                {
                    cloudCount = list.Count;
                    cloudsOut = new List<object>();
                    foreach (var c in list)
                    {
                        var view = doc.GetElement(c.OwnerViewId) as View;
                        var o = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["elementId"] = c.Id.IntegerValue
                        };
                        if (Want("ownerViewId")) o["ownerViewId"] = c.OwnerViewId.IntegerValue;
                        // Provide alias field for clients expecting 'viewId'
                        if (Want("viewId")) o["viewId"] = c.OwnerViewId.IntegerValue;
                        if (Want("ownerViewName")) o["ownerViewName"] = view?.Name ?? string.Empty;
                        if (Want("viewName")) o["viewName"] = view?.Name ?? string.Empty;
                        if (Want("bbox")) o["bbox"] = RevUnits.BBoxToMm(c.get_BoundingBox(null));
                        if (Want("createdBy")) o["createdBy"] = null; // Worksharing履歴は非対応
                        if (Want("lastChanged")) o["lastChanged"] = null;
                        cloudsOut.Add(o);
                    }
                }
                else
                {
                    // includeClouds=false でも件数は返す（効率優先で簡易カウント）
                    cloudCount = new FilteredElementCollector(doc)
                                 .OfClass(typeof(Autodesk.Revit.DB.RevisionCloud))
                                 .Cast<Autodesk.Revit.DB.RevisionCloud>()
                                 .Count(c => (c.RevisionId?.IntegerValue ?? 0) == rid);
                }

                items.Add(new RevisionInfo
                {
                    revisionId = rid,
                    uniqueId = r.UniqueId,
                    sequenceNumber = r.SequenceNumber,
                    revisionNumber = r.RevisionNumber ?? string.Empty,
                    description = r.Description ?? string.Empty,
                    revisionDate = r.RevisionDate ?? string.Empty,
                    issued = r.Issued,
                    issuedBy = r.IssuedBy ?? string.Empty,
                    issuedTo = r.IssuedTo ?? string.Empty,
                    visibility = r.Visibility.ToString(),
                    numberingSequenceId = r.RevisionNumberingSequenceId?.IntegerValue ?? 0,
                    cloudCount = cloudCount,
                    clouds = includeClouds ? cloudsOut : null
                });
            }

            return new
            {
                ok = true,
                totalCount = items.Count,
                numberingMode,
                revisions = items
            };
        }
    }

    // ------------------------------
    // list_sheet_revisions （DTOで返却）
    // ------------------------------
    public class ListSheetRevisionsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_sheet_revisions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            bool includeDetails = p.Value<bool?>("includeRevisionDetails") ?? true;

            // 対象シート
            var sheetIdList = p["sheetIds"] is JArray a && a.Count > 0 ? a.Values<int>().ToList() : null;
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                         .Where(vs => sheetIdList == null || sheetIdList.Contains(vs.Id.IntegerValue))
                         .OrderBy(vs => vs.SheetNumber).ToList();

            // 詳細参照用辞書
            Dictionary<int, Autodesk.Revit.DB.Revision> revById = null;
            if (includeDetails)
            {
                revById = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Revision)).Cast<Autodesk.Revit.DB.Revision>()
                          .ToDictionary(r => r.Id.IntegerValue, r => r);
            }

            var list = new List<SheetRevisionItem>();
            foreach (var s in sheets)
            {
                var revIds = s.GetAllRevisionIds() ?? new List<ElementId>();
                var ids = revIds.Select(id => id.IntegerValue).ToList();

                List<RevisionBrief> revDetails = null;
                if (includeDetails && revById != null)
                {
                    revDetails = ids.Where(id => revById.ContainsKey(id))
                                    .Select(id =>
                                    {
                                        var r = revById[id];
                                        return new RevisionBrief
                                        {
                                            revisionId = id,
                                            revisionNumber = r.RevisionNumber ?? string.Empty,
                                            sequenceNumber = r.SequenceNumber,
                                            description = r.Description ?? string.Empty,
                                            revisionDate = r.RevisionDate ?? string.Empty,
                                            issued = r.Issued
                                        };
                                    })
                                    .ToList();
                }

                list.Add(new SheetRevisionItem
                {
                    sheetId = s.Id.IntegerValue,
                    sheetNumber = s.SheetNumber,
                    sheetName = s.Name,
                    revisionIds = ids,
                    revisions = includeDetails ? revDetails : null
                });
            }

            return new { ok = true, totalCount = list.Count, sheets = list };
        }
    }

    // ------------------------------
    // get/set revision cloud spacing（既出のまま）
    // ------------------------------
    public class GetRevisionCloudSpacingCommand : IRevitCommandHandler
    {
        public string CommandName => "get_revision_cloud_spacing";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var rs = RevisionSettings.GetRevisionSettings(doc);
            double spacingMm = RevUnits.ToMm(rs.RevisionCloudSpacing);
            return new { ok = true, spacingMm = Math.Round(spacingMm, 3) };
        }
    }
    public class SetRevisionCloudSpacingCommand : IRevitCommandHandler
    {
        public string CommandName => "set_revision_cloud_spacing";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());
            if (!p.TryGetValue("spacingMm", out var tok)) return new { ok = false, msg = "spacingMm が必要です（mm）" };
            double spacingMm = tok.Value<double>();
            if (spacingMm <= 0) return new { ok = false, msg = "spacingMm は正の数で指定してください。" };

            var rs = RevisionSettings.GetRevisionSettings(doc);
            using (var tx = new Transaction(doc, "Set Revision Cloud Spacing"))
            {
                try
                {
                    tx.Start();
                    rs.RevisionCloudSpacing = RevUnits.MmToFt(spacingMm);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "更新に失敗: " + ex.Message };
                }
            }
            double saved = RevUnits.ToMm(RevisionSettings.GetRevisionSettings(doc).RevisionCloudSpacing);
            return new { ok = true, spacingMm = Math.Round(saved, 3) };
        }
    }
}
