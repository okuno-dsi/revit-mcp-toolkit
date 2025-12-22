using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class ListWallParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "list_wall_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, message = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // --- Resolve target ---
            Element elem = null;
            string targetKind = null;
            int? elementId = null;
            int? typeId = null;
            string uniqueId = null;

            int instId = p.Value<int?>("elementId") ?? p.Value<int?>("wallId") ?? 0;
            if (instId > 0)
            {
                elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(instId));
                if (elem is Autodesk.Revit.DB.Wall)
                {
                    targetKind = "instance";
                    elementId = instId;
                    uniqueId = elem.UniqueId ?? string.Empty;
                }
            }
            else
            {
                var uid = p.Value<string>("uniqueId");
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    elem = doc.GetElement(uid);
                    if (elem is Autodesk.Revit.DB.Wall)
                    {
                        targetKind = "instance";
                        elementId = elem.Id.IntValue();
                        uniqueId = elem.UniqueId ?? string.Empty;
                    }
                }
            }

            if (elem == null)
            {
                int tid = p.Value<int?>("typeId") ?? 0;
                string typeName = p.Value<string>("typeName");
                WallType wt = null;

                if (tid > 0)
                    wt = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid)) as WallType;
                else if (!string.IsNullOrWhiteSpace(typeName))
                    wt = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                        .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));

                if (wt != null)
                {
                    elem = wt;
                    targetKind = "type";
                    typeId = wt.Id.IntValue();
                    uniqueId = wt.UniqueId ?? string.Empty;
                }
            }

            if (elem == null)
                return new { ok = false, message = "Wall instance or WallType not found." };

            var paramList = elem.Parameters.Cast<Parameter>()
                .OrderBy(pr => pr.Definition?.Name).ThenBy(pr => pr.Id.IntValue())
                .ToList();
            int totalCount = paramList.Count;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? totalCount;

            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, targetKind, elementId, typeId, uniqueId, totalCount };

            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            if (namesOnly)
            {
                var names = paramList.Skip(skip).Take(count).Select(pr => pr.Definition?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList();
                return new { ok = true, targetKind, elementId, typeId, uniqueId, totalCount, parameterNames = names };
            }
            else
            {
                var mode = UnitHelper.ResolveUnitsMode(doc, p);
                var results = paramList.Skip(skip).Take(count)
                    .Select(pr => UnitHelper.MapParameter(pr, doc, mode))
                    .ToList();
                return new { ok = true, targetKind, elementId, typeId, uniqueId, totalCount, parameters = results };
            }
        }
    }
}


