// ================================================================
// File: Commands/ElementOps/GetBoundingBoxCommand.cs (UnitHelper対応版)
// - すべての長さは UnitHelper を介して mm に正規化
// - 応答に units メタを付加
// ================================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // UnitHelper


namespace RevitMCPAddin.Commands.ElementOps
{
    /// <summary>
    /// Retrieves the BoundingBox (min/max) of one or more elements.
    /// Input: { elementIds: [123, 456, ...] }
    /// Output: { ok, totalCount, boxes:[{ elementId, ok, boundingBox:{ min:{x,y,z}, max:{x,y,z} } }] , inputUnits, internalUnits }
    /// </summary>
    public class GetBoundingBoxCommand : IRevitCommandHandler
    {
        public string CommandName => "get_bounding_box";


        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var idList = p["elementIds"]?.ToObject<List<int>>()
            ?? (p["elementId"] != null ? new List<int> { p.Value<int>("elementId") } : new List<int>());


            var results = new List<object>();
            foreach (var id in idList)
            {
                var elem = doc.GetElement(new ElementId(id));
                if (elem == null)
                {
                    results.Add(new { elementId = id, ok = false, message = $"Element {id} not found." });
                    continue;
                }


                var bb = elem.get_BoundingBox(null);
                if (bb == null)
                {
                    results.Add(new { elementId = id, ok = false, message = "BoundingBox not available." });
                    continue;
                }


                var min = bb.Min; var max = bb.Max;
                // UnitHelper で mm に正規化（小数3桁）
                var minMm = new { x = Math.Round(UnitHelper.FtToMm(min.X), 3), y = Math.Round(UnitHelper.FtToMm(min.Y), 3), z = Math.Round(UnitHelper.FtToMm(min.Z), 3) };
                var maxMm = new { x = Math.Round(UnitHelper.FtToMm(max.X), 3), y = Math.Round(UnitHelper.FtToMm(max.Y), 3), z = Math.Round(UnitHelper.FtToMm(max.Z), 3) };


                results.Add(new { elementId = id, ok = true, boundingBox = new { min = minMm, max = maxMm } });
            }


            return new
            {
                ok = true,
                totalCount = results.Count,
                boxes = results,
                inputUnits = UnitHelper.InputUnitsMeta(), // 例: { Length:"mm" }
                internalUnits = UnitHelper.InternalUnitsMeta() // 例: { Length:"ft" }
            };
        }
    }
}