// RevitMCPAddin/Commands/ElementOps/Wall/GetWallsCommand.cs
// UnitHelper化: 位置/高さ/厚さの mm 変換を UnitHelper 経由、units メタは Input/Internal を整備
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class GetWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            // Optional view filter to reduce payload and speed up: viewId param
            View view = null;
            try
            {
                int? viewId = p.Value<int?>("viewId");
                if (viewId.HasValue && viewId.Value > 0)
                    view = doc.GetElement(new ElementId(viewId.Value)) as View;
            }
            catch { }

            var collector = (view != null)
                ? new FilteredElementCollector(doc, view.Id)
                : new FilteredElementCollector(doc);
            var allWalls = collector
                .OfClass(typeof(Autodesk.Revit.DB.Wall))
                .Cast<Autodesk.Revit.DB.Wall>()
                .ToList();
            int totalCount = allWalls.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = new { Length = "mm" },
                    internalUnits = new { Length = "ft" }
                };

            var typeIds = allWalls.Select(w => w.GetTypeId().IntegerValue).Distinct();
            var typeNameMap = typeIds.ToDictionary(
                id => id,
                id => doc.GetElement(new ElementId(id))?.Name ?? string.Empty
            );

            var slice = allWalls.Skip(skip).Take(count).ToList();
            var walls = slice.Select(wall =>
            {
                var loc = wall.Location as LocationCurve;
                var curve = loc?.Curve;

                var heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                double heightFt = (heightParam != null) ? heightParam.AsDouble() : 0.0;
                double thicknessFt = wall.Width;

                double heightMm = Math.Round(UnitHelper.InternalToMm(heightFt), 3);
                double thicknessMm = Math.Round(UnitHelper.InternalToMm(thicknessFt), 3);

                object start = null, end = null;
                if (curve != null)
                {
                    var p0 = curve.GetEndPoint(0);
                    var p1 = curve.GetEndPoint(1);
                    var sMm = UnitHelper.XyzToMm(p0);
                    var eMm = UnitHelper.XyzToMm(p1);
                    start = new { x = Math.Round(sMm.x, 3), y = Math.Round(sMm.y, 3), z = Math.Round(sMm.z, 3) };
                    end = new { x = Math.Round(eMm.x, 3), y = Math.Round(eMm.y, 3), z = Math.Round(eMm.z, 3) };
                }

                int eid = wall.Id.IntegerValue;
                int tid = wall.GetTypeId().IntegerValue;
                int levelId = wall.LevelId != null ? wall.LevelId.IntegerValue : 0;

                var typeName = typeNameMap.TryGetValue(tid, out var tn) ? tn : string.Empty;

                return new
                {
                    elementId = eid,
                    id = eid,
                    uniqueId = wall.UniqueId,
                    typeId = tid,
                    typeName,
                    levelId,
                    height = heightMm,
                    thickness = thicknessMm,
                    start,
                    end
                };
            }).ToList();

            var wallsById = walls.ToDictionary(x => x.elementId, x => (object)x);

            return new
            {
                ok = true,
                totalCount,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" },
                walls,
                wallsById
            };
        }
    }
}
