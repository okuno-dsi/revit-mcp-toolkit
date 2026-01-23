#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Spatial
{
    /// <summary>
    /// JSON-RPC: get_spatial_context_for_elements
    /// 複数要素について、代表点および曲線サンプリング点ごとの Room / Space / Area をまとめて取得する。
    /// - elementIds は必須
    /// - include を省略した場合は room/space/zone/area/areaScheme をすべて対象とする
    /// - 曲線要素については 0.0 / 0.5 / 1.0 (端点＋中点) をデフォルトでサンプリングする
    /// </summary>
    public class GetSpatialContextForElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_spatial_context_for_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd.Params as JObject ?? new JObject();

            var idsToken = p["elementIds"] as JArray;
            if (idsToken == null || idsToken.Count == 0)
            {
                return new { ok = false, msg = "elementIds を配列で指定してください。" };
            }

            var elementIds = idsToken
                .Select(t => t.Type == JTokenType.Integer ? (int?)t.Value<int>() : null)
                .Where(v => v.HasValue && v.Value > 0)
                .Select(v => Autodesk.Revit.DB.ElementIdCompat.From(v!.Value))
                .ToList();

            if (elementIds.Count == 0)
            {
                return new { ok = false, msg = "有効な elementIds がありません ( > 0 )。" };
            }

            string phaseName = p.Value<string>("phaseName") ?? string.Empty;
            string mode = (p.Value<string>("mode") ?? "3d").Trim().ToLowerInvariant();
            bool bboxFootprintProbe = p.Value<bool?>("bboxFootprintProbe") ?? true;
            bool requireSameLevel = p.Value<bool?>("requireSameLevel") ?? p.Value<bool?>("sameLevelOnly") ?? true;

            // include フィルタ (省略時は全て)
            var includeToken = p["include"] as JArray;
            var includeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (includeToken != null && includeToken.Count > 0)
            {
                foreach (var jt in includeToken)
                {
                    var s = jt.Value<string>();
                    if (!string.IsNullOrWhiteSpace(s))
                        includeSet.Add(s.Trim());
                }
            }
            bool includeAll = includeSet.Count == 0;
            bool Include(string key) => includeAll || includeSet.Contains(key);

            // 曲線サンプリング用の既定パラメータ (0, 0.5, 1)
            var defaultCurveSamples = new[] { 0.0, 0.5, 1.0 };
            var samplesToken = p["curveSamples"] as JArray;
            double[] curveSamples = defaultCurveSamples;
            if (samplesToken != null && samplesToken.Count > 0)
            {
                var tmp = samplesToken
                    .Select(t => t.Type == JTokenType.Float || t.Type == JTokenType.Integer
                        ? (double?)t.Value<double>()
                        : null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .Where(v => v >= 0.0 - 1e-9 && v <= 1.0 + 1e-9)
                    .Distinct()
                    .OrderBy(v => v)
                    .ToArray();
                if (tmp.Length > 0) curveSamples = tmp;
            }

            int maxElements = p.Value<int?>("maxElements") ?? int.MaxValue;

            var results = new List<object>();
            var globalMessages = new List<string>();

            int processed = 0;

            foreach (var eid in elementIds)
            {
                if (processed >= maxElements) break;

                var element = doc.GetElement(eid);
                if (element == null) continue;

                processed++;

                var localMessages = new List<string>();
                var elementLevelId = GetElementLevelId(element);

                // 代表点を 1 点取得 (Location または BoundingBox)
                var refPt = SpatialUtils.GetReferencePoint(doc, element, out var refMsg);
                if (!string.IsNullOrEmpty(refMsg)) localMessages.Add(refMsg);

                var samples = new List<object>();

                void AddSample(string kind, XYZ pt)
                {
                    if (pt == null) return;

                    var ptMm = UnitHelper.XyzToMm(pt);
                    var ptObj = new
                    {
                        x = Math.Round(ptMm.x, 3),
                        y = Math.Round(ptMm.y, 3),
                        z = Math.Round(ptMm.z, 3)
                    };

                    object? roomObj = null;
                    var spacesArr = new List<object>();
                    var areasArr = new List<object>();

                    // Room
                    if (Include("room"))
                    {
                        var room = SpatialUtils.TryGetRoomWithVerticalProbe(doc, element, pt, phaseName, out var phaseUsed, out var roomMsg, bboxFootprintProbe);
                        if (!string.IsNullOrEmpty(roomMsg)) localMessages.Add(roomMsg);

                        // フォールバック: レベル一致要求時にヒットしない場合、XYは据え置きでZを要素レベル標高に投影して判定
                        if (room == null && requireSameLevel && elementLevelId != ElementId.InvalidElementId)
                        {
                            var lvl = doc.GetElement(elementLevelId) as Level;
                            if (lvl != null)
                            {
                                var projPt = new XYZ(pt.X, pt.Y, lvl.Elevation);
                                room = SpatialUtils.TryGetRoomAtPoint(doc, projPt, phaseName, out phaseUsed, out roomMsg);
                                if (!string.IsNullOrEmpty(roomMsg)) localMessages.Add(roomMsg + " (projected Z to level elevation)");
                                if (room != null && room.LevelId != elementLevelId)
                                {
                                    // レベル不一致なら無視
                                    room = null;
                                }
                            }
                        }

                        if (room != null && (!requireSameLevel || elementLevelId == ElementId.InvalidElementId || room.LevelId == elementLevelId))
                        {
                            string levelName = string.Empty;
                            try
                            {
                                var lvl = doc.GetElement(room.LevelId) as Level;
                                levelName = lvl?.Name ?? string.Empty;
                            }
                            catch { }

                            roomObj = new
                            {
                                id = room.Id.IntValue(),
                                name = room.Name ?? string.Empty,
                                number = room.Number ?? string.Empty,
                                phase = phaseUsed?.Name ?? string.Empty,
                                levelName
                            };
                        }
                    }

                    // Space & Zone
                    if (Include("space") || Include("zone"))
                    {
                        var spaces = SpatialUtils.GetSpacesAtPoint(doc, pt, phaseName, out var spMsg);
                        if (!string.IsNullOrEmpty(spMsg)) localMessages.Add(spMsg);

                        foreach (var s in spaces)
                        {
                            string spaceLevelName = string.Empty;
                            try
                            {
                                var lvl = doc.GetElement(s.LevelId) as Level;
                                spaceLevelName = lvl?.Name ?? string.Empty;
                            }
                            catch { }

                            object? zoneObj = null;
                            if (Include("zone"))
                            {
                                try
                                {
                                    var z = s.Zone;
                                    if (z != null)
                                    {
                                        zoneObj = new
                                        {
                                            id = z.Id.IntValue(),
                                            name = z.Name ?? string.Empty
                                        };
                                    }
                                }
                                catch
                                {
                                    // ignore
                                }
                            }

                            spacesArr.Add(new
                            {
                                id = s.Id.IntValue(),
                                name = s.Name ?? string.Empty,
                                number = s.Number ?? string.Empty,
                                levelName = spaceLevelName,
                                zone = zoneObj
                            });
                        }
                    }

                    // Area & AreaScheme
                    if (Include("area") || Include("areaScheme"))
                    {
                        var areas = SpatialUtils.GetAreasAtPoint(doc, pt, element, out var schemes, out var areaMsg);
                        if (!string.IsNullOrEmpty(areaMsg)) localMessages.Add(areaMsg);

                        foreach (var a in areas)
                        {
                            AreaScheme scheme = null;
                            try { scheme = a.AreaScheme; } catch { scheme = null; }

                            object? schemeObj = null;
                            if (scheme != null)
                            {
                                schemeObj = new
                                {
                                    id = scheme.Id.IntValue(),
                                    name = scheme.Name ?? string.Empty
                                };
                            }

                            areasArr.Add(new
                            {
                                id = a.Id.IntValue(),
                                name = a.Name ?? string.Empty,
                                number = a.Number ?? string.Empty,
                                areaScheme = schemeObj
                            });
                        }
                    }

                    // 少なくともどれか一つの空間要素に属する場合のみサンプルとして残す
                    bool anyHit = (roomObj != null) || spacesArr.Count > 0 || areasArr.Count > 0;
                    if (!anyHit) return;

                    samples.Add(new
                    {
                        kind,
                        point = ptObj,
                        room = roomObj,
                        spaces = spacesArr,
                        areas = areasArr
                    });
                }

                // 代表点
                if (refPt != null)
                {
                    AddSample("reference", refPt);
                }

                // 位置情報に応じて追加サンプリング
                if (element.Location is LocationCurve lc && lc.Curve != null)
                {
                    var curve = lc.Curve;
                    foreach (var t in curveSamples)
                    {
                        XYZ pt;
                        try
                        {
                            pt = curve.Evaluate(t, true);
                        }
                        catch
                        {
                            continue;
                        }

                        string kind = Math.Abs(t) < 1e-9 ? "curveStart"
                            : Math.Abs(t - 1.0) < 1e-9 ? "curveEnd"
                            : Math.Abs(t - 0.5) < 1e-9 ? "curveMid"
                            : "curveParam";

                        AddSample(kind, pt);
                    }
                }
                else if (element.Location is LocationPoint lp && lp.Point != null)
                {
                    AddSample("location", lp.Point);
                }
                // BoundingBox ベースの要素は代表点だけに依存

                if (samples.Count == 0)
                {
                    // この要素はどの空間要素にも属していないとみなして結果から除外
                    globalMessages.AddRange(localMessages);
                    continue;
                }

                string categoryName = element.Category?.Name ?? string.Empty;

                results.Add(new
                {
                    elementId = eid.IntValue(),
                    category = categoryName,
                    samples
                });

                globalMessages.AddRange(localMessages);
            }

            bool any = results.Count > 0;
            if (!any && globalMessages.Count == 0)
            {
                globalMessages.Add("指定された要素は、サンプリング点ベースではいずれの Room / Space / Area にも属していない可能性があります。");
            }

            return new
            {
                ok = any,
                totalCount = results.Count,
                elements = results,
                messages = globalMessages
            };
        }

        private static ElementId GetElementLevelId(Element e)
        {
            if (e == null) return ElementId.InvalidElementId;

            try
            {
                if (e is SpatialElement se && se.LevelId != ElementId.InvalidElementId)
                    return se.LevelId;
            }
            catch { }

            try
            {
                if (e.LevelId != ElementId.InvalidElementId)
                    return e.LevelId;
            }
            catch { }

            try
            {
                var p = e.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                        return id;
                }
            }
            catch { }

            return ElementId.InvalidElementId;
        }
    }
}


