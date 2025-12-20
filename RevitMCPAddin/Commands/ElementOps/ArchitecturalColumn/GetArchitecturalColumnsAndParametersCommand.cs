// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/GetArchitecturalColumnsAndParametersCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class GetArchitecturalColumnsAndParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_architectural_columns";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)(cmd.Params ?? new JObject());
                var mode = UnitHelper.ResolveUnitsMode(doc, p);

                int legacySkip = p.Value<int?>("skip") ?? 0;
                int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
                var shape = p["_shape"] as JObject;
                bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
                var pageObj = shape?["page"] as JObject;
                int limit = Math.Max(0, pageObj?.Value<int?>("limit") ?? legacyCount);
                int skip = Math.Max(0, pageObj?.Value<int?>("skip") ?? pageObj?.Value<int?>("offset") ?? legacySkip);
                bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
                int? levelFilter = p.Value<int?>("levelId");
                int? typeFilter = p.Value<int?>("typeId");

                // 2D AABB (mm) → 内部ft
                bool hasBox = p.TryGetValue("bbox", out var bboxTok) && bboxTok is JObject;
                (double X, double Y)? min2 = null, max2 = null;
                if (hasBox)
                {
                    var b = (JObject)bboxTok;
                    var mn = (JObject)b["min"];
                    var mx = (JObject)b["max"];
                    if (mn != null && mx != null)
                    {
                        min2 = (UnitHelper.MmToFt(mn.Value<double>("x")),
                                UnitHelper.MmToFt(mn.Value<double>("y")));
                        max2 = (UnitHelper.MmToFt(mx.Value<double>("x")),
                                UnitHelper.MmToFt(mx.Value<double>("y")));
                    }
                }

                string sortBy = (p.Value<string>("sortBy") ?? "id").ToLowerInvariant();

                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol?.Family?.FamilyCategory?.Id.IntegerValue == (int)BuiltInCategory.OST_Columns)
                    .ToList();

                if (levelFilter.HasValue) all = all.Where(fi => fi.LevelId.IntegerValue == levelFilter.Value).ToList();
                if (typeFilter.HasValue) all = all.Where(fi => fi.GetTypeId().IntegerValue == typeFilter.Value).ToList();

                if (min2 != null && max2 != null)
                {
                    all = all.Where(fi =>
                    {
                        XYZ pt = (fi.Location as LocationPoint)?.Point;
                        if (pt == null) return false;
                        return (pt.X >= min2.Value.X && pt.X <= max2.Value.X &&
                                pt.Y >= min2.Value.Y && pt.Y <= max2.Value.Y);
                    }).ToList();
                }

                all = sortBy switch
                {
                    "level" => all.OrderBy(fi => fi.LevelId.IntegerValue).ToList(),
                    "type" => all.OrderBy(fi => fi.GetTypeId().IntegerValue).ToList(),
                    "x" => all.OrderBy(fi => ((fi.Location as LocationPoint)?.Point?.X) ?? Double.MinValue).ToList(),
                    "y" => all.OrderBy(fi => ((fi.Location as LocationPoint)?.Point?.Y) ?? Double.MinValue).ToList(),
                    "z" => all.OrderBy(fi => ((fi.Location as LocationPoint)?.Point?.Z) ?? Double.MinValue).ToList(),
                    _ => all.OrderBy(fi => fi.Id.IntegerValue).ToList()
                };

                int total = all.Count;

                if (summaryOnly || limit == 0)
                    return new { ok = true, totalCount = total, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };

                var page = all.Skip(skip).Take(limit).ToList();

                if (idsOnly)
                {
                    var ids = page.Select(fi => fi.Id.IntegerValue).ToList();
                    return new { ok = true, totalCount = total, elementIds = ids, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
                }

                var cols = page.Select(fi =>
                {
                    LocationPoint lp = fi.Location as LocationPoint;
                    var bb = fi.get_BoundingBox(null);

                    object bbox = null;
                    if (bb != null)
                    {
                        bbox = new
                        {
                            min = new
                            {
                                x = Math.Round(UnitHelper.FtToMm(bb.Min.X), 3),
                                y = Math.Round(UnitHelper.FtToMm(bb.Min.Y), 3),
                                z = Math.Round(UnitHelper.FtToMm(bb.Min.Z), 3)
                            },
                            max = new
                            {
                                x = Math.Round(UnitHelper.FtToMm(bb.Max.X), 3),
                                y = Math.Round(UnitHelper.FtToMm(bb.Max.Y), 3),
                                z = Math.Round(UnitHelper.FtToMm(bb.Max.Z), 3)
                            }
                        };
                    }

                    object loc = null;
                    if (lp != null)
                    {
                        var (xmm, ymm, zmm) = UnitHelper.XyzToMm(lp.Point);
                        loc = new { x = Math.Round(xmm, 3), y = Math.Round(ymm, 3), z = Math.Round(zmm, 3) };
                    }

                    return new
                    {
                        elementId = fi.Id.IntegerValue,
                        typeId = fi.GetTypeId().IntegerValue,
                        levelId = fi.LevelId.IntegerValue,
                        location = loc,
                        bbox
                    };
                }).ToList();

                return new { ok = true, totalCount = total, columns = cols, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
