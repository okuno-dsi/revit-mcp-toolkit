// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/CreateArchitecturalColumnCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Linq;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class CreateArchitecturalColumnCommand : IRevitCommandHandler
    {
        public string CommandName => "create_architectural_column";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = (JObject)cmd.Params;

                // --- 1) Base Level 解決（Id/Name/最下レベル）
                ElementId baseLevelId = ElementId.InvalidElementId;
                if (p.TryGetValue("baseLevelId", out var blid)) baseLevelId = Autodesk.Revit.DB.ElementIdCompat.From(blid.Value<int>());
                else if (p.TryGetValue("levelId", out var lid)) baseLevelId = Autodesk.Revit.DB.ElementIdCompat.From(lid.Value<int>());

                Level baseLevel = null;
                if (baseLevelId != ElementId.InvalidElementId)
                    baseLevel = doc.GetElement(baseLevelId) as Level;

                if (baseLevel == null)
                {
                    string baseLevelName = null;
                    if (p.TryGetValue("baseLevelName", out var blname)) baseLevelName = blname.Value<string>();
                    else if (p.TryGetValue("levelName", out var lname)) baseLevelName = lname.Value<string>();

                    if (!string.IsNullOrWhiteSpace(baseLevelName))
                    {
                        baseLevel = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level)).Cast<Level>()
                            .FirstOrDefault(l => l.Name.Equals(baseLevelName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                if (baseLevel == null)
                {
                    baseLevel = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .FirstOrDefault();
                    if (baseLevel == null) return new { ok = false, msg = "基準レベル(Level)が見つかりません。" };
                }

                // --- 2) 位置（mm→内部ft）
                var locTok = p["location"];
                if (locTok == null) return new { ok = false, msg = "location が必要です（{x,y,z} in mm）。" };
                var loc = UnitHelper.MmToXyz(
                    locTok.Value<double>("x"),
                    locTok.Value<double>("y"),
                    locTok.Value<double>("z")
                );

                // --- 3) FamilySymbol 解決（意匠柱）
                FamilySymbol symbol = null;

                if (p.TryGetValue("typeName", out var tn))
                {
                    var tname = tn.Value<string>();
                    if (!string.IsNullOrWhiteSpace(tname))
                    {
                        symbol = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .OfCategory(BuiltInCategory.OST_Columns)
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(s => s.Name.Equals(tname, StringComparison.OrdinalIgnoreCase));
                    }
                }

                if (symbol == null && p.TryGetValue("typeId", out var tid))
                {
                    var cand = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>())) as FamilySymbol;
                    if (cand != null && cand.Category != null &&
                        cand.Category.Id.IntValue() == (int)BuiltInCategory.OST_Columns)
                    {
                        symbol = cand;
                    }
                }

                if (symbol == null)
                {
                    symbol = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_Columns)
                        .Cast<FamilySymbol>()
                        .FirstOrDefault();
                    if (symbol == null) return new { ok = false, msg = "意匠柱のFamilySymbolが見つかりません。" };
                }

                // --- 4) 高さモード判定
                var htToken = p["heightMm"];
                bool hasTopLevelParam = p.ContainsKey("topLevelId") || p.ContainsKey("topLevelName");
                bool isLevelToLevel = (htToken == null) ||
                                      (htToken.Type == JTokenType.String &&
                                       string.Equals(htToken.Value<string>(), "level-to-level", StringComparison.OrdinalIgnoreCase));

                // オフセット（mm→ft）
                double baseOffsetFt = UnitHelper.MmToFt(p.Value<double?>("baseOffsetMm") ?? 0.0);
                double topOffsetFt = UnitHelper.MmToFt(p.Value<double?>("topOffsetMm") ?? 0.0);

                // 数値高さ（非拘束時に Top=Base, TopOffset=height）
                double heightFt = UnitHelper.MmToFt(
                    (htToken != null && (htToken.Type == JTokenType.Float || htToken.Type == JTokenType.Integer))
                        ? htToken.Value<double>()
                        : 3000.0 // fallback
                );

                // 上部拘束レベル解決（指定優先→直上階）
                Level topLevel = null;
                if (hasTopLevelParam || isLevelToLevel)
                {
                    if (p.TryGetValue("topLevelId", out var tlid))
                        topLevel = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tlid.Value<int>())) as Level;

                    if (topLevel == null && p.TryGetValue("topLevelName", out var tlname))
                    {
                        var name = tlname.Value<string>();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            topLevel = new FilteredElementCollector(doc)
                                .OfClass(typeof(Level)).Cast<Level>()
                                .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (topLevel == null)
                    {
                        topLevel = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level)).Cast<Level>()
                            .Where(l => l.Elevation > baseLevel.Elevation)
                            .OrderBy(l => l.Elevation)
                            .FirstOrDefault();
                    }
                }

                bool useTopConstraint = (topLevel != null);

                using (var tx = new Transaction(doc, "Create Architectural Column"))
                {
                    tx.Start();

                    if (!symbol.IsActive) symbol.Activate();

                    var col = doc.Create.NewFamilyInstance(loc, symbol, baseLevel, StructuralType.NonStructural);

                    // Base
                    var pBase = col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                    if (pBase != null && !pBase.IsReadOnly) pBase.Set(baseLevel.Id);

                    var pBaseOff = col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                    if (pBaseOff != null && !pBaseOff.IsReadOnly) pBaseOff.Set(baseOffsetFt);

                    // Top
                    var pTop = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                    var pTopOff = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);

                    if (useTopConstraint)
                    {
                        if (pTop != null && !pTop.IsReadOnly) pTop.Set(topLevel.Id);
                        if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(topOffsetFt);
                    }
                    else
                    {
                        if (pTop != null && !pTop.IsReadOnly) pTop.Set(baseLevel.Id);
                        if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(heightFt);
                    }

                    tx.Commit();

                    return new
                    {
                        ok = true,
                        elementId = col.Id.IntValue(),
                        typeId = col.GetTypeId().IntValue(),
                        baseLevelId = baseLevel.Id.IntValue(),
                        topLevelId = useTopConstraint ? topLevel.Id.IntValue() : baseLevel.Id.IntValue(),
                        mode = useTopConstraint ? "top-constrained" : "base+offset",
                        units = UnitHelper.DefaultUnitsMeta()
                    };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}


