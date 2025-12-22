// RevitMCPAddin/Commands/MEPOps/GetMepElementsCommand.cs (UnitHelper対応)
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static RevitMCPAddin.Commands.MEPOps.MepUnits;
using static RevitMCPAddin.Commands.MEPOps.MepCurveInfo;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class GetMepElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_mep_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            var cats = p["categoryIds"]?.ToObject<List<int>>() ?? new List<int>();
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            IEnumerable<Element> q = new FilteredElementCollector(doc).WhereElementIsNotElementType();

            if (cats.Any())
            {
                var bic = new HashSet<int>(cats);
                q = q.Where(e => e.Category != null && bic.Contains(e.Category.Id.IntValue()));
            }
            else
            {
                q = q.Where(e => e is MEPCurve);
            }

            var all = q.Cast<Element>().ToList();
            int total = all.Count;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, totalCount = total, inputUnits = new { Length = "mm" }, internalUnits = new { Length = "ft" } };

            var page = all.Skip(skip).Take(count).ToList();

            var items = page.Select(e =>
            {
                string kind =
                    (e is Duct) ? "Duct" :
                    (e is Pipe) ? "Pipe" :
                    (e is CableTray) ? "CableTray" :
                    (e is Conduit) ? "Conduit" :
                    e.GetType().Name;

                int? systemId = null;
                try { if (e is MEPCurve c && c.MEPSystem != null) systemId = c.MEPSystem.Id.IntValue(); } catch { }

                var endpts = Endpoints(e);
                var shape = (e as MEPCurve) != null ? ShapeInfo(e as MEPCurve) : null;

                return new
                {
                    elementId = e.Id.IntValue(),
                    uniqueId = e.UniqueId,
                    kind,
                    categoryId = e.Category?.Id.IntValue(),
                    typeId = e.GetTypeId().IntValue(),
                    levelId = (e.LevelId != null ? e.LevelId.IntValue() : (int?)null),
                    systemId,
                    lengthMm = LengthMm(e),
                    endpoints = endpts == null ? null : new
                    {
                        start = new { x = FtToMm(endpts.Value.a.X), y = FtToMm(endpts.Value.a.Y), z = FtToMm(endpts.Value.a.Z) },
                        end = new { x = FtToMm(endpts.Value.b.X), y = FtToMm(endpts.Value.b.Y), z = FtToMm(endpts.Value.b.Z) }
                    },
                    shape
                };
            }).ToList();

            return new
            {
                ok = true,
                totalCount = total,
                elements = items,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }
}

