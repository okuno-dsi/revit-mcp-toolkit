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
    /// JSON-RPC: get_spatial_context_for_element
    /// 指定した要素が属している空間コンテキスト（Room, Space, Zone, Area, AreaScheme）を返す。
    /// </summary>
    public class GetSpatialContextForElementCommand : IRevitCommandHandler
    {
        public string CommandName => "get_spatial_context_for_element";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd.Params as JObject ?? new JObject();

            int? elementIdInt = p.Value<int?>("elementId");
            if (!elementIdInt.HasValue || elementIdInt.Value <= 0)
            {
                return new { ok = false, msg = "elementId を指定してください (>0)。" };
            }

            string phaseName = p.Value<string>("phaseName") ?? string.Empty;
            string mode = (p.Value<string>("mode") ?? "3d").Trim().ToLowerInvariant(); // 現状は参照のみ
            bool bboxFootprintProbe = p.Value<bool?>("bboxFootprintProbe") ?? true;

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

            var elementId = Autodesk.Revit.DB.ElementIdCompat.From(elementIdInt.Value);
            var element = doc.GetElement(elementId);
            if (element == null)
            {
                return new { ok = false, elementId = elementIdInt.Value, msg = "指定された elementId の要素が見つかりません。" };
            }

            var messages = new List<string>();

            // 代表点
            var refPt = SpatialUtils.GetReferencePoint(doc, element, out var refMsg);
            if (!string.IsNullOrEmpty(refMsg)) messages.Add(refMsg);

            if (refPt == null)
            {
                return new
                {
                    ok = false,
                    elementId = elementIdInt.Value,
                    referencePoint = (object?)null,
                    room = (object?)null,
                    spaces = Array.Empty<object>(),
                    areas = Array.Empty<object>(),
                    areaSchemes = Array.Empty<object>(),
                    messages
                };
            }

            // 代表点 (mm) 出力用
            var refMm = UnitHelper.XyzToMm(refPt);
            var referencePoint = new
            {
                x = Math.Round(refMm.x, 3),
                y = Math.Round(refMm.y, 3),
                z = Math.Round(refMm.z, 3)
            };

            object? roomObj = null;
            var spacesArr = new List<object>();
            var areasArr = new List<object>();
            var schemesArr = new List<object>();

            // Room
            if (Include("room"))
            {
                var room = SpatialUtils.TryGetRoomWithVerticalProbe(doc, element, refPt, phaseName, out var phaseUsed, out var roomMsg, bboxFootprintProbe);
                if (!string.IsNullOrEmpty(roomMsg)) messages.Add(roomMsg);

                if (room != null)
                {
                    var levelName = string.Empty;
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
                var spaces = SpatialUtils.GetSpacesAtPoint(doc, refPt, phaseName, out var spaceMsg);
                if (!string.IsNullOrEmpty(spaceMsg)) messages.Add(spaceMsg);

                // 代表点からのヒットが無い場合でも、要素自身が Space であればフォールバックとして追加
                if (spaces.Count == 0 && element is Autodesk.Revit.DB.Mechanical.Space selfSpace)
                {
                    spaces = new List<Autodesk.Revit.DB.Mechanical.Space> { selfSpace };
                    messages.Add("Space 判定: 代表点からのヒットはありませんでしたが、要素自身が Space なのでコンテキストに含めました。");
                }

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
                            // Zone 取得失敗は無視
                        }
                    }

                    spacesArr.Add(new
                    {
                        id = s.Id.IntValue(),
                        name = s.Name ?? string.Empty,
                        number = s.Number ?? string.Empty,
                        phase = string.Empty,
                        levelName = spaceLevelName,
                        zone = zoneObj
                    });
                }
            }

            // Area & AreaScheme
            if (Include("area") || Include("areaScheme"))
            {
                var areas = SpatialUtils.GetAreasAtPoint(doc, refPt, element, out var schemes, out var areaMsg);
                if (!string.IsNullOrEmpty(areaMsg)) messages.Add(areaMsg);

                // 代表点からのヒットが無い場合でも、要素自身が Area であればフォールバックとして追加
                if (areas.Count == 0 && element is Autodesk.Revit.DB.Area selfArea)
                {
                    areas = new List<Autodesk.Revit.DB.Area> { selfArea };
                    if (schemes == null) schemes = new List<AreaScheme>();
                    try
                    {
                        var scheme = selfArea.AreaScheme;
                        if (scheme != null && !schemes.Any(s => s.Id == scheme.Id))
                        {
                            schemes.Add(scheme);
                        }
                    }
                    catch { /* ignore */ }

                    messages.Add("Area 判定: 代表点からのヒットはありませんでしたが、要素自身が Area なのでコンテキストに含めました。");
                }

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

                if (Include("areaScheme") && areasArr.Count > 0)
                {
                    foreach (var s in areasArr
                        .Select(a => (dynamic)a)
                        .Select(a => a.areaScheme)
                        .Where(x => x != null))
                    {
                        int sid = (int)s.id;
                        if (!schemesArr.Any(o => ((dynamic)o).id == sid))
                        {
                            schemesArr.Add(s);
                        }
                    }
                }
            }

            bool anyContext =
                roomObj != null ||
                spacesArr.Count > 0 ||
                areasArr.Count > 0;

            if (!anyContext && messages.Count == 0)
            {
                messages.Add("Room / Space / Area のいずれにも属していない可能性があります。");
            }

            return new
            {
                ok = anyContext,
                elementId = elementIdInt.Value,
                referencePoint,
                room = roomObj,
                spaces = spacesArr,
                areas = areasArr,
                areaSchemes = schemesArr,
                messages
            };
        }
    }
}


