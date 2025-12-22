// File: RevitMCPAddin/Commands/ElementOps/CurtainWall/GetCurtainWallTypesCommand.cs
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    /// <summary>
    /// カーテンウォールタイプ一覧（WallType.Kind == Curtain）
    /// filters: typeName, nameContains
    /// </summary>
    public class GetCurtainWallTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_curtain_wall_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            // 既存ページング（互換）
            int skipLegacy = p.Value<int?>("skip") ?? 0;
            int countLegacy = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // shape/paging + 軽量
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? countLegacy);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? skipLegacy);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            string typeName = p.Value<string>("typeName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(t => t.Kind == WallKind.Curtain)
                .ToList();

            IEnumerable<WallType> q = all;

            if (!string.IsNullOrWhiteSpace(typeName))
                q = q.Where(t => string.Equals(t.Name ?? "", typeName, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(t => (t.Name ?? "").IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0);

            var filtered = q.ToList();
            int totalCount = filtered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount };

            var ordered = filtered.Select(t => new { t, name = t.Name ?? "", id = t.Id.IntValue() })
                                  .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.t).ToList();

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(t => t.Name ?? "").ToList();
                return new { ok = true, totalCount, names };
            }

            if (idsOnly)
            {
                var ids = ordered.Skip(skip).Take(limit).Select(t => t.Id.IntValue()).ToList();
                return new { ok = true, totalCount, typeIds = ids };
            }

            var types = ordered.Skip(skip).Take(limit).Select(t => new
            {
                typeId = t.Id.IntValue(),
                uniqueId = t.UniqueId,
                typeName = t.Name ?? ""
            }).ToList();

            return new { ok = true, totalCount, types };
        }
    }
}

