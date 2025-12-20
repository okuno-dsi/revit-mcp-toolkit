using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    public class CreateWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "create_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var items = p["items"] as JArray;
            if (items == null || items.Count == 0)
                return new { ok = false, msg = "Provide items: [{start,end,wallTypeId?,wallTypeName?,baseLevelId?,topLevelId?,heightMm?,baseOffsetMm?,topOffsetMm?,isStructural?}]" };

            var created = new List<int>();
            using (var tx = new Transaction(doc, "Create Walls (bulk)"))
            {
                tx.Start();
                foreach (var it in items.OfType<JObject>())
                {
                    try
                    {
                        var start = it["start"] as JObject; var end = it["end"] as JObject;
                        if (start == null || end == null) continue;
                        var sp = UnitHelper.MmToXyz(start.Value<double>("x"), start.Value<double>("y"), start.Value<double>("z"));
                        var ep = UnitHelper.MmToXyz(end.Value<double>("x"), end.Value<double>("y"), end.Value<double>("z"));

                        // Level
                        Level baseLevel = null; Level topLevel = null;
                        if (it.TryGetValue("baseLevelId", out var blid)) baseLevel = doc.GetElement(new ElementId(blid.Value<int>())) as Level;
                        if (baseLevel == null && it.TryGetValue("baseLevelName", out var bln)) baseLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => string.Equals(l.Name, bln.Value<string>(), StringComparison.OrdinalIgnoreCase));
                        if (baseLevel == null) baseLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();
                        if (baseLevel == null) continue;

                        if (it.TryGetValue("topLevelId", out var tlid)) topLevel = doc.GetElement(new ElementId(tlid.Value<int>())) as Level;
                        if (topLevel == null && it.TryGetValue("topLevelName", out var tln)) topLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => string.Equals(l.Name, tln.Value<string>(), StringComparison.OrdinalIgnoreCase));

                        // Type
                        WallType wType = null;
                        if (it.TryGetValue("wallTypeId", out var wtid)) wType = doc.GetElement(new ElementId(wtid.Value<int>())) as WallType;
                        if (wType == null && it.TryGetValue("wallTypeName", out var wtn))
                        {
                            var name = wtn.Value<string>();
                            if (!string.IsNullOrWhiteSpace(name)) wType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                        }
                        if (wType == null) wType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault();
                        if (wType == null) continue;

                        bool isStructural = it.Value<bool?>("isStructural") ?? false;
                        double baseOffsetFt = UnitHelper.MmToFt(it.Value<double?>("baseOffsetMm") ?? 0.0);
                        double topOffsetFt = UnitHelper.MmToFt(it.Value<double?>("topOffsetMm") ?? 0.0);
                        double heightFt = UnitHelper.MmToFt(it.Value<double?>("heightMm") ?? 3000.0);

                        var line = Line.CreateBound(sp, ep);
                        var wall = Wall.Create(doc, line, wType.Id, baseLevel.Id, heightFt, baseOffsetFt, false, isStructural);

                        var pBase = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT); if (pBase != null && !pBase.IsReadOnly) pBase.Set(baseLevel.Id);
                        var pBaseOff = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET); if (pBaseOff != null && !pBaseOff.IsReadOnly) pBaseOff.Set(baseOffsetFt);

                        var pTop = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                        var pTopOff = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                        var pUnconn = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);

                        if (topLevel != null)
                        {
                            if (pTop != null && !pTop.IsReadOnly) pTop.Set(topLevel.Id);
                            if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(topOffsetFt);
                            if (pUnconn != null && !pUnconn.IsReadOnly) pUnconn.Set(0.0);
                        }
                        else
                        {
                            if (pTop != null && !pTop.IsReadOnly) pTop.Set(ElementId.InvalidElementId);
                            if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(0.0);
                            if (pUnconn != null && !pUnconn.IsReadOnly) pUnconn.Set(heightFt);
                        }

                        created.Add(wall.Id.IntegerValue);
                    }
                    catch { /* skip one item */ }
                }
                tx.Commit();
            }

            return new { ok = true, createdCount = created.Count, createdElementIds = created };
        }
    }
}
