using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using netDxf;
using netDxf.Tables;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DxfArc = netDxf.Entities.Arc;
using DxfCircle = netDxf.Entities.Circle;
using DxfLine = netDxf.Entities.Line;
using DxfText = netDxf.Entities.Text;

namespace RevitMCPAddin.Commands.DxfOps
{
    /// <summary>
    /// 任意カテゴリの要素から Line / Arc を抽出して mm で返す
    /// </summary>
    public class GetCurvesByCategoryHandler : IRevitCommandHandler
    {
        public string CommandName => "get_curves_by_category";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var categories = p["categories"]?.ToObject<List<string>>();
            if (categories == null || categories.Count == 0)
                return new { ok = false, msg = "categories パラメータが必要です" };

            var doc = uiapp.ActiveUIDocument.Document;

            // Optional paging / shape controls
            int limit = Math.Max(0, p.Value<int?>("limit") ?? int.MaxValue);
            int skip = Math.Max(0, p.Value<int?>("skip") ?? 0);
            bool linesOnly = p.Value<bool?>("linesOnly") ?? false;
            bool arcsOnly = p.Value<bool?>("arcsOnly") ?? false;

            // カテゴリフィルタ作成（安全に個別TryParse）
            var bicList = new List<BuiltInCategory>();
            foreach (var name in categories)
            {
                if (Enum.TryParse<BuiltInCategory>(name, true, out var bic))
                    bicList.Add(bic);
            }
            if (bicList.Count == 0)
                return new { ok = false, msg = "有効なカテゴリがありません（BuiltInCategory名を指定）" };

            var filter = new ElementMulticategoryFilter(bicList);
            var collector = new FilteredElementCollector(doc)
                                .WherePasses(filter)
                                .WhereElementIsNotElementType();

            var result = new List<object>(1024);
            int produced = 0;

            foreach (var elem in collector)
            {
                if (produced >= (skip + limit)) break; // paging guard
                var geom = elem.get_Geometry(new Options { ComputeReferences = false });
                if (geom == null) continue;

                foreach (var geoObj in geom)
                {
                    if (!(geoObj is Curve curve)) continue;
                    if (linesOnly && !(curve is Line)) continue;
                    if (arcsOnly && !(curve is Arc)) continue;

                    if (curve is Line line)
                    {
                        if (produced++ < skip) continue;
                        if (result.Count >= limit) break;
                        var p0 = line.GetEndPoint(0);
                        var p1 = line.GetEndPoint(1);
                        result.Add(new
                        {
                            type = "Line",
                            start = new { x = UnitHelper.FtToMm(p0.X), y = UnitHelper.FtToMm(p0.Y), z = UnitHelper.FtToMm(p0.Z) },
                            end = new { x = UnitHelper.FtToMm(p1.X), y = UnitHelper.FtToMm(p1.Y), z = UnitHelper.FtToMm(p1.Z) }
                        });
                    }
                    else if (curve is Arc arc)
                    {
                        if (produced++ < skip) continue;
                        if (result.Count >= limit) break;
                        var center = arc.Center;
                        var p0 = arc.GetEndPoint(0);
                        var p1 = arc.GetEndPoint(1);
                        double startDeg = Math.Atan2(p0.Y - center.Y, p0.X - center.X) * 180.0 / Math.PI;
                        double endDeg = Math.Atan2(p1.Y - center.Y, p1.X - center.X) * 180.0 / Math.PI;
                        result.Add(new
                        {
                            type = "Arc",
                            center = new { x = UnitHelper.FtToMm(center.X), y = UnitHelper.FtToMm(center.Y), z = UnitHelper.FtToMm(center.Z) },
                            radius = UnitHelper.FtToMm(arc.Radius),
                            startAngle = startDeg,
                            endAngle = endDeg
                        });
                    }
                }
            }

            return new
            {
                ok = true,
                count = result.Count,
                items = result,
                units = new { output = new { Length = "mm", Angle = "deg" }, internal_ = new { Length = "ft", Angle = "rad" } },
                nextSkip = (produced >= (skip + limit)) ? (skip + result.Count) : (int?)null
            };
        }
    }

    /// <summary>
    /// グリッド専用: グリッド線(直線/アーク)＋バブル注記を mm で返す
    /// </summary>
    public class GetGridsWithBubblesHandler : IRevitCommandHandler
    {
        public string CommandName => "get_grids_with_bubbles";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            double defaultRadiusMm = p.Value<double?>("radiusMm") ?? 100.0; // 既定 mm
            string defaultLayer = p.Value<string>("layer") ?? "0";

            var doc = uiapp.ActiveUIDocument.Document;
            var grids = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Grids)
                            .WhereElementIsNotElementType()
                            .Cast<Grid>();

            var list = new List<object>();

            foreach (var g in grids)
            {
                if (!(g.Location is LocationCurve loc)) continue;
                var curve = loc.Curve;
                object geomObj = null;

                if (curve is Line line)
                {
                    var p0 = line.GetEndPoint(0);
                    var p1 = line.GetEndPoint(1);
                    geomObj = new
                    {
                        type = "Line",
                        start = new { x = UnitHelper.FtToMm(p0.X), y = UnitHelper.FtToMm(p0.Y), z = UnitHelper.FtToMm(p0.Z) },
                        end = new { x = UnitHelper.FtToMm(p1.X), y = UnitHelper.FtToMm(p1.Y), z = UnitHelper.FtToMm(p1.Z) }
                    };
                }
                else if (curve is Arc arc)
                {
                    var center = arc.Center;
                    var p0 = arc.GetEndPoint(0);
                    var p1 = arc.GetEndPoint(1);

                    double startDeg = Math.Atan2(p0.Y - center.Y, p0.X - center.X) * 180.0 / Math.PI;
                    double endDeg = Math.Atan2(p1.Y - center.Y, p1.X - center.X) * 180.0 / Math.PI;

                    geomObj = new
                    {
                        type = "Arc",
                        center = new { x = UnitHelper.FtToMm(center.X), y = UnitHelper.FtToMm(center.Y), z = UnitHelper.FtToMm(center.Z) },
                        radius = UnitHelper.FtToMm(arc.Radius),
                        startAngle = startDeg,
                        endAngle = endDeg
                    };
                }
                if (geomObj == null) continue;

                var basePoint = curve.GetEndPoint(0);
                list.Add(new
                {
                    gridId = g.Id.IntValue(),
                    name = g.Name,
                    geometry = geomObj,
                    position = new { x = UnitHelper.FtToMm(basePoint.X), y = UnitHelper.FtToMm(basePoint.Y), z = UnitHelper.FtToMm(basePoint.Z) },
                    radius = defaultRadiusMm,
                    layer = defaultLayer
                });
            }

            return new
            {
                ok = true,
                totalCount = list.Count,
                grids = list,
                units = new { output = new { Length = "mm", Angle = "deg" }, internal_ = new { Length = "ft", Angle = "rad" } }
            };
        }
    }

    /// <summary>
    /// 曲線・グリッド・注釈を DXF へ出力（既定 mm）。inputUnit="ft" で後方互換。
    /// </summary>
    public class ExportCurvesToDxfHandler : IRevitCommandHandler
    {
        public string CommandName => "export_curves_to_dxf";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;

            var curves = p["curves"]?.Type == JTokenType.Array ? p["curves"].ToObject<List<JObject>>() : null;
            var grids = p["grids"]?.Type == JTokenType.Array ? p["grids"].ToObject<List<JObject>>() : null;
            var annots = p["annotations"]?.Type == JTokenType.Array ? p["annotations"].ToObject<List<JObject>>() : null;

            string defaultLayer = p.Value<string>("layerName") ?? "0";
            string outputPath = p.Value<string>("outputFilePath");
            string inputUnit = (p.Value<string>("inputUnit") ?? "mm").Trim().ToLowerInvariant(); // "mm" | "ft" | "internal"
            bool inputIsFt = (inputUnit == "ft" || inputUnit == "internal");

            if ((curves == null || curves.Count == 0) && (grids == null || grids.Count == 0))
                return new { ok = false, msg = "curves または grids パラメータが必要です" };
            if (string.IsNullOrWhiteSpace(outputPath))
                return new { ok = false, msg = "outputFilePath が指定されていません" };

            // ユーティリティ: 入力値 → mm へ（既定: mm、ft指定時のみ変換）
            double AsMm(double v) => inputIsFt ? UnitHelper.FtToMm(v) : v;

            var dxf = new DxfDocument();
            var layerCache = new Dictionary<string, Layer>();
            Layer EnsureLayer(string name)
            {
                if (!layerCache.TryGetValue(name, out var layer))
                {
                    layer = new Layer(name);
                    dxf.Layers.Add(layer);
                    layerCache[name] = layer;
                }
                return layer;
            }

            // 既定レイヤ
            EnsureLayer(defaultLayer);

            // ---- curves ----
            if (curves != null)
            {
                foreach (var item in curves)
                {
                    string lyr = item.Value<string>("layer") ?? defaultLayer;
                    var layer = EnsureLayer(lyr);
                    var type = item.Value<string>("type");

                    if (type == "Line" && item["start"] != null && item["end"] != null)
                    {
                        double x1 = AsMm(item["start"].Value<double>("x"));
                        double y1 = AsMm(item["start"].Value<double>("y"));
                        double x2 = AsMm(item["end"].Value<double>("x"));
                        double y2 = AsMm(item["end"].Value<double>("y"));
                        dxf.Entities.Add(new DxfLine(new netDxf.Vector2(x1, y1), new netDxf.Vector2(x2, y2)) { Layer = layer });
                    }
                    else if (type == "Arc" && item["center"] != null)
                    {
                        double cx = AsMm(item["center"].Value<double>("x"));
                        double cy = AsMm(item["center"].Value<double>("y"));
                        double radius = AsMm(item.Value<double>("radius"));
                        double startDeg = item.Value<double>("startAngle");
                        double endDeg = item.Value<double>("endAngle");
                        dxf.Entities.Add(new DxfArc(new netDxf.Vector2(cx, cy), radius, startDeg, endDeg) { Layer = layer });
                    }
                }
            }

            // ---- grids ----
            if (grids != null)
            {
                foreach (var g in grids)
                {
                    var geom = g["geometry"] as JObject;
                    if (geom == null) continue;

                    string type = geom.Value<string>("type");
                    string lyr = g.Value<string>("layer") ?? defaultLayer;
                    var layer = EnsureLayer(lyr);

                    if (type == "Line")
                    {
                        double x1 = AsMm(geom["start"].Value<double>("x"));
                        double y1 = AsMm(geom["start"].Value<double>("y"));
                        double x2 = AsMm(geom["end"].Value<double>("x"));
                        double y2 = AsMm(geom["end"].Value<double>("y"));
                        dxf.Entities.Add(new DxfLine(new netDxf.Vector2(x1, y1), new netDxf.Vector2(x2, y2)) { Layer = layer });
                    }
                    else if (type == "Arc")
                    {
                        double cx = AsMm(geom["center"].Value<double>("x"));
                        double cy = AsMm(geom["center"].Value<double>("y"));
                        double radius = AsMm(geom.Value<double>("radius"));
                        double startDeg = geom.Value<double>("startAngle");
                        double endDeg = geom.Value<double>("endAngle");
                        dxf.Entities.Add(new DxfArc(new netDxf.Vector2(cx, cy), radius, startDeg, endDeg) { Layer = layer });
                    }

                    // バブル（円＋文字）
                    var pos = g["position"] as JObject;
                    if (pos != null)
                    {
                        double bx = AsMm(pos.Value<double>("x"));
                        double by = AsMm(pos.Value<double>("y"));
                        double r = AsMm(g.Value<double?>("radius") ?? 1.0);
                        dxf.Entities.Add(new DxfCircle(new netDxf.Vector2(bx, by), r) { Layer = layer });
                        dxf.Entities.Add(new DxfText(g.Value<string>("name"), new netDxf.Vector2(bx, by - r * 0.3), Math.Max(0.1, r * 0.8)) { Layer = layer });
                    }
                }
            }

            // ---- annotations（任意）----
            if (annots != null)
            {
                foreach (var a in annots)
                {
                    string lyr = a.Value<string>("layer") ?? defaultLayer;
                    var layer = EnsureLayer(lyr);

                    var pos = a["position"] as JObject;
                    if (pos != null)
                    {
                        double cx = AsMm(pos.Value<double>("x"));
                        double cy = AsMm(pos.Value<double>("y"));
                        double r = AsMm(a.Value<double?>("radius") ?? 1.0);
                        dxf.Entities.Add(new DxfCircle(new netDxf.Vector2(cx, cy), r) { Layer = layer });
                        dxf.Entities.Add(new DxfText(a.Value<string>("text"), new netDxf.Vector2(cx, cy - r * 0.3), Math.Max(0.1, r * 0.8)) { Layer = layer });
                    }
                }
            }

            // 保存
            try
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                dxf.Save(outputPath);
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "DXF保存に失敗: " + ex.Message };
            }

            return new
            {
                ok = true,
                path = outputPath,
                inputUnit = inputUnit,
                dxfUnitAssumption = "mm"
            };
        }
    }
}

