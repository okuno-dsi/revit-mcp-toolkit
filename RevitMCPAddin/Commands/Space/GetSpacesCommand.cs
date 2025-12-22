// ================================================================
// File: Commands/Space/GetSpacesCommand.cs (UnitHelper完全統一版 - 修正版)
// Target: Revit 2023 / .NET Framework 4.8 / C# 8
// 変更点:
//  - Definition.GetDataTypeId() -> Definition.GetDataType() に修正
//  - 取得失敗時のフォールバックと null セーフティ
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using SpaceElem = Autodesk.Revit.DB.Mechanical.Space;

namespace RevitMCPAddin.Commands.Space
{
    public class GetSpacesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_spaces";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument?.Document;
                if (doc == null) return ResultUtil.Err("No active document.");
                var p = (JObject)(cmd.Params ?? new JObject());

                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? int.MaxValue;

                int? levelIdFilter = p.Value<int?>("levelId");
                string nameContains = (p.Value<string>("nameContains") ?? "").Trim();
                string numberContains = (p.Value<string>("numberContains") ?? "").Trim();

                double? areaMinM2 = p["areaMinM2"]?.Type switch
                {
                    JTokenType.Float => p.Value<double>("areaMinM2"),
                    JTokenType.Integer => Convert.ToDouble(p.Value<int>("areaMinM2")),
                    _ => (double?)null
                };
                double? areaMaxM2 = p["areaMaxM2"]?.Type switch
                {
                    JTokenType.Float => p.Value<double>("areaMaxM2"),
                    JTokenType.Integer => Convert.ToDouble(p.Value<int>("areaMaxM2")),
                    _ => (double?)null
                };

                bool includeParameters = p.Value<bool?>("includeParameters") ?? false;
                bool includeCenter = p.Value<bool?>("includeCenter") ?? true;

                string orderBy = (p.Value<string>("orderBy") ?? "number").ToLowerInvariant(); // id|name|number|area|level
                bool desc = p.Value<bool?>("desc") ?? false;

                var allSpaces = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .Cast<SpatialElement>()
                    .OfType<SpaceElem>()
                    .ToList();

                IEnumerable<SpaceElem> q = allSpaces;

                if (levelIdFilter.HasValue)
                    q = q.Where(s => s.LevelId.IntValue() == levelIdFilter.Value);

                if (!string.IsNullOrEmpty(nameContains))
                    q = q.Where(s => (s.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!string.IsNullOrEmpty(numberContains))
                    q = q.Where(s => (s.Number ?? "").IndexOf(numberContains, StringComparison.OrdinalIgnoreCase) >= 0);

                Func<SpaceElem, double> areaM2Func = s => Math.Round(UnitHelper.InternalToSqm(s.Area), 6);
                if (areaMinM2.HasValue) q = q.Where(s => areaM2Func(s) >= areaMinM2.Value - 1e-9);
                if (areaMaxM2.HasValue) q = q.Where(s => areaM2Func(s) <= areaMaxM2.Value + 1e-9);

                Func<SpaceElem, object> keySel = orderBy switch
                {
                    "id" => s => (object)s.Id.IntValue(),
                    "name" => s => (object)(s.Name ?? ""),
                    "number" => s => (object)(s.Number ?? ""),
                    "area" => s => (object)areaM2Func(s),
                    "level" => s => (object)((doc.GetElement(s.LevelId) as Level)?.Name ?? ""),
                    _ => s => (object)(s.Number ?? "")
                };
                q = desc ? q.OrderByDescending(keySel).ThenBy(s => s.Id.IntValue())
                         : q.OrderBy(keySel).ThenBy(s => s.Id.IntValue());

                int totalCount = q.Count();

                if (skip == 0 && p.ContainsKey("count") && count == 0)
                    return new { ok = true, totalCount };

                var page = q.Skip(skip).Take(count).ToList();

                var spaces = new List<object>(page.Count);
                foreach (var s in page)
                {
                    string levelName = s.LevelId != ElementId.InvalidElementId
                        ? (doc.GetElement(s.LevelId) as Level)?.Name ?? string.Empty
                        : string.Empty;

                    double areaM2 = Math.Round(UnitHelper.InternalToSqm(s.Area), 3);

                    object center = null;
                    if (includeCenter && s.Location is LocationPoint lp)
                    {
                        center = new
                        {
                            x = Math.Round(UnitHelper.InternalToMm(lp.Point.X, doc), 3),
                            y = Math.Round(UnitHelper.InternalToMm(lp.Point.Y, doc), 3),
                            z = Math.Round(UnitHelper.InternalToMm(lp.Point.Z, doc), 3)
                        };
                    }

                    List<object> paramList = null;
                    if (includeParameters)
                    {
                        paramList = s.Parameters
                            .Cast<Parameter>()
                            .Select(pa =>
                            {
                                object val = null;
                                switch (pa.StorageType)
                                {
                                    case StorageType.Double:
                                        {
                                            ForgeTypeId dt = null;
                                            try
                                            {
                                                dt = pa.Definition?.GetDataType();
                                            }
                                            catch
                                            {
                                                dt = null;
                                            }

                                            double v = pa.AsDouble();
                                            if (dt != null && dt == SpecTypeId.Length)
                                                val = Math.Round(UnitHelper.InternalToMm(v, doc), 3);
                                            else if (dt != null && dt == SpecTypeId.Area)
                                                val = Math.Round(UnitHelper.InternalToSqm(v), 6);
                                            else if (dt != null && dt == SpecTypeId.Volume)
                                                val = Math.Round(UnitHelper.InternalToCubicMeters(v), 6);
                                            else if (dt != null && dt == SpecTypeId.Angle)
                                                val = Math.Round(UnitHelper.InternalToDeg(v), 6);
                                            else
                                                val = new { rawInternal = v, dataTypeId = dt?.ToString() ?? "(unknown)" };
                                            break;
                                        }
                                    case StorageType.Integer:
                                        val = pa.AsInteger();
                                        break;
                                    case StorageType.String:
                                        val = pa.AsString() ?? string.Empty;
                                        break;
                                    case StorageType.ElementId:
                                        val = pa.AsElementId().IntValue();
                                        break;
                                }

                                return new
                                {
                                    name = pa.Definition?.Name ?? "(no name)",
                                    id = pa.Id.IntValue(),
                                    storageType = pa.StorageType.ToString(),
                                    value = val
                                };
                            })
                            .Where(x => x.value != null && (!(x.value is string s2) || !string.IsNullOrEmpty(s2)))
                            .Cast<object>() // ★ 匿名型を object にキャスト
                            .ToList();
                    }

                    spaces.Add(new
                    {
                        id = s.Id.IntValue(),
                        elementId = s.Id.IntValue(),
                        uniqueId = s.UniqueId,
                        number = s.Number,
                        name = s.Name,
                        level = levelName,
                        area = areaM2,
                        center,
                        parameters = paramList
                    });
                }

                return new { ok = true, totalCount, spaces };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}

