// RevitMCPAddin/Commands/ElementOps/FloorOps/GetFloorsCommand.cs
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class GetFloorsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_floors";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            // 既存のページング（後方互換）
            int skipLegacy = p.Value<int?>("skip") ?? 0;
            int countLegacy = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // 新しい shape/paging
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? countLegacy);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? skipLegacy);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeLocation = p.Value<bool?>("includeLocation") ?? true;
            int viewId = p.Value<int?>("viewId") ?? 0; // NEW: limit to view when specified

            // filters 略…

            // Collector（viewId 指定時はビュー内に限定）
            var fec = viewId > 0 ? new FilteredElementCollector(doc, new ElementId(viewId))
                                  : new FilteredElementCollector(doc);
            fec = fec.OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType();

            // まずは ID のみ列挙（軽量化）
            var allIds = fec.ToElementIds().Select(eid => eid.IntegerValue).ToList();
            int totalCount = allIds.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };

            // idsOnly: ページング後に ID 群のみ返す
            if (idsOnly)
            {
                var pageIds = (limit == int.MaxValue && skip == 0) ? allIds : allIds.Skip(skip).Take(limit).ToList();
                return new { ok = true, totalCount, elementIds = pageIds };
            }

            // namesOnly: ページングした要素のみ詳細取得
            var sliceIds = (limit == int.MaxValue && skip == 0) ? allIds : allIds.Skip(skip).Take(limit).ToList();
            if (namesOnly)
            {
                var names = new List<string>(sliceIds.Count);
                foreach (var id in sliceIds)
                {
                    var fl = doc.GetElement(new ElementId(id)) as Autodesk.Revit.DB.Floor;
                    if (fl == null) { names.Add(""); continue; }
                    names.Add(string.IsNullOrEmpty(fl.Name) ? (fl.FloorType?.Name ?? "") : fl.Name);
                }
                return new { ok = true, totalCount, names, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
            }

            // フル出力：ページングした ID 群のみ詳細取得
            var list = sliceIds.Select(id =>
            {
                var fl = doc.GetElement(new ElementId(id)) as Autodesk.Revit.DB.Floor;
                if (fl == null) return null;

                // 位置：BB中心（mm）
                object location = null;
                if (includeLocation)
                {
                    var bb = fl.get_BoundingBox(null);
                    if (bb != null)
                    {
                        var center = (bb.Min + bb.Max) / 2.0;
                        var mm = UnitHelper.XyzToMm(center);
                        location = new { x = System.Math.Round(mm.x, 3), y = System.Math.Round(mm.y, 3), z = System.Math.Round(mm.z, 3) };
                    }
                }

                var lv = doc.GetElement(fl.LevelId) as Level;

                return new
                {
                    elementId = fl.Id.IntegerValue,
                    uniqueId = fl.UniqueId,
                    typeId = fl.FloorType?.Id.IntegerValue,
                    typeName = fl.FloorType?.Name ?? "",
                    familyName = fl.FloorType?.FamilyName ?? "",
                    levelId = fl.LevelId.IntegerValue,
                    levelName = lv?.Name ?? "",
                    location
                };
            })
            .Where(x => x != null)
            .ToList();

            return new { ok = true, totalCount, floors = list, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };
        }
    }
}
