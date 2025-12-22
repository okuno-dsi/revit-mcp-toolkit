#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPAddin.Commands.VisualizationOps
{
    /// <summary>
    /// JSON-RPC: delete_spatial_volume_overlays
    /// create_spatial_volume_overlay で作成した DirectShape の一括削除。
    /// ApplicationId == "RevitMCP" のみ対象。hostIds/hostUniqueIds/directShapeIds で絞り込み可能。
    /// </summary>
    public class DeleteSpatialVolumeOverlaysCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_spatial_volume_overlays";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Err("アクティブドキュメントがありません。");

            var p = (JObject)cmd.Params;

            bool all = p.Value<bool?>("all") ?? false;
            bool dryRun = p.Value<bool?>("dryRun") ?? false;

            var hostIds = p["hostIds"]?.ToObject<List<int>>() ?? new List<int>();
            var hostUniqueIds = p["hostUniqueIds"]?.ToObject<List<string>>() ?? new List<string>();
            var dsIds = p["directShapeIds"]?.ToObject<List<int>>() ?? new List<int>();

            if (!all && hostIds.Count == 0 && hostUniqueIds.Count == 0 && dsIds.Count == 0)
                return Err("削除条件がありません。'all:true' または 'hostIds/hostUniqueIds/directShapeIds' のいずれかを指定してください。");

            var hostIdSet = new HashSet<int>(hostIds);
            var hostUidSet = new HashSet<string>(hostUniqueIds.Where(u => !string.IsNullOrWhiteSpace(u)));
            var dsIdSet = new HashSet<int>(dsIds);

            var willDelete = new List<ElementId>();
            var skipped = new JArray();

            // 対象探索：DirectShape のみ
            var collector = new FilteredElementCollector(doc).OfClass(typeof(DirectShape));
            foreach (var e in collector)
            {
                var ds = (DirectShape)e;
                // MCP 生成物のみ
                if (!string.Equals(ds.ApplicationId, "RevitMCP", StringComparison.OrdinalIgnoreCase))
                {
                    if (dsIdSet.Contains(ds.Id.IntValue()))
                        skipped.Add(Skip(ds.Id, "not overlay"));
                    continue;
                }

                // directShapeIds 指定があれば最優先マッチ
                if (dsIdSet.Count > 0 && dsIdSet.Contains(ds.Id.IntValue()))
                {
                    willDelete.Add(ds.Id);
                    continue;
                }

                // all:true は overlay すべて
                if (all && hostIdSet.Count == 0 && hostUidSet.Count == 0)
                {
                    willDelete.Add(ds.Id);
                    continue;
                }

                // hostIds/hostUniqueIds での照合
                // create 側で ApplicationDataId に Room/Space の UniqueId または ElementId(文字列) を格納している想定
                var dataId = ds.ApplicationDataId ?? string.Empty;

                bool matchByHostId = false;
                if (hostIdSet.Count > 0)
                {
                    if (int.TryParse(dataId, out var parsed))
                        matchByHostId = hostIdSet.Contains(parsed);
                }

                bool matchByHostUid = (hostUidSet.Count > 0) && hostUidSet.Contains(dataId);

                if (matchByHostId || matchByHostUid)
                {
                    willDelete.Add(ds.Id);
                    continue;
                }

                // 条件にかからない → 見送り
                if (!all && (hostIdSet.Count > 0 || hostUidSet.Count > 0))
                    skipped.Add(Skip(ds.Id, "host mismatch"));
            }

            if (dryRun)
            {
                return new JObject
                {
                    ["ok"] = true,
                    ["requested"] = willDelete.Count,
                    ["wouldDelete"] = new JArray(willDelete.Select(x => x.IntValue())),
                    ["skipped"] = skipped
                };
            }

            var deleted = new List<int>();
            var errors = new JArray();

            using (var t = new Transaction(doc, "Delete Spatial Overlays"))
            {
                t.Start();
                foreach (var id in willDelete.Distinct())
                {
                    try
                    {
                        // doc.Delete は関連要素を複数返すことがあるが、ここでは代表として自要素IDを記録
                        doc.Delete(id);
                        deleted.Add(id.IntValue());
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new JObject
                        {
                            ["id"] = id.IntValue(),
                            ["message"] = ex.Message
                        });
                    }
                }
                t.Commit();
            }

            return new JObject
            {
                ["ok"] = true,
                ["requested"] = willDelete.Count,
                ["deleted"] = new JArray(deleted),
                ["skipped"] = skipped,
                ["errors"] = errors
            };
        }

        private static JObject Err(string msg)
            => new JObject { ["ok"] = false, ["msg"] = msg };

        private static JObject Skip(ElementId id, string reason)
            => new JObject { ["id"] = id.IntValue(), ["reason"] = reason };
    }
}

