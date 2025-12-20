// RevitMCPAddin/Commands/ElementOps/StructuralColumn/CreateStructuralColumnCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Linq;

namespace RevitMCPAddin.Commands.ElementOps.StructuralColumn
{
    public class CreateStructuralColumnCommand : IRevitCommandHandler
    {
        public string CommandName => "create_structural_column";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
                var p = (JObject)cmd.Params;

                // Base Level 解決（alias: levelId/levelName も可）
                ElementId baseLevelId = ElementId.InvalidElementId;
                if (p.TryGetValue("baseLevelId", out var blid)) baseLevelId = new ElementId(blid.Value<int>());
                else if (p.TryGetValue("levelId", out var lid)) baseLevelId = new ElementId(lid.Value<int>());

                Level baseLevel = null;
                if (baseLevelId != ElementId.InvalidElementId) baseLevel = doc.GetElement(baseLevelId) as Level;
                if (baseLevel == null)
                {
                    var name = p.Value<string>("baseLevelName") ?? p.Value<string>("levelName");
                    if (!string.IsNullOrWhiteSpace(name))
                        baseLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                            .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
                baseLevel ??= new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                                .OrderBy(l => l.Elevation).FirstOrDefault()
                             ?? throw new InvalidOperationException("基準レベル(Level)が見つかりません。");

                // 位置（mm→ft）
                var pt = p["location"] ?? throw new InvalidOperationException("location が必要です（{x,y,z} in mm）。");
                var loc = UnitHelper.MmToXyz((double)pt["x"], (double)pt["y"], (double)pt["z"]);

                // FamilySymbol（構造柱限定）
                FamilySymbol symbol = null;
                if (p.TryGetValue("typeName", out var tn))
                {
                    var tname = tn.Value<string>();
                    if (!string.IsNullOrWhiteSpace(tname))
                        symbol = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .OfCategory(BuiltInCategory.OST_StructuralColumns)
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(s => s.Name.Equals(tname, StringComparison.OrdinalIgnoreCase));
                }
                if (symbol == null && p.TryGetValue("typeId", out var tid))
                {
                    var cand = doc.GetElement(new ElementId(tid.Value<int>())) as FamilySymbol;
                    if (cand?.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns) symbol = cand;
                }
                symbol ??= new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .OfCategory(BuiltInCategory.OST_StructuralColumns)
                            .Cast<FamilySymbol>()
                            .FirstOrDefault()
                         ?? throw new InvalidOperationException("構造柱のFamilySymbolが見つかりません。");

                // 高さモード
                var htToken = p["heightMm"];
                bool hasTopLevel = p.ContainsKey("topLevelId") || p.ContainsKey("topLevelName");
                bool isLevelToLevel = (htToken == null) ||
                                      (htToken.Type == JTokenType.String && string.Equals(htToken.Value<string>(), "level-to-level", StringComparison.OrdinalIgnoreCase));

                double baseOffsetFt = UnitHelper.MmToFt(p.Value<double?>("baseOffsetMm") ?? 0.0);
                double topOffsetFt = UnitHelper.MmToFt(p.Value<double?>("topOffsetMm") ?? 0.0);
                double heightFt = UnitHelper.MmToFt((htToken is null || htToken.Type == JTokenType.String) ? 3000.0 : htToken.Value<double>());

                Level topLevel = null;
                if (hasTopLevel || isLevelToLevel)
                {
                    if (p.TryGetValue("topLevelId", out var tlid)) topLevel = doc.GetElement(new ElementId(tlid.Value<int>())) as Level;
                    if (topLevel == null && p.TryGetValue("topLevelName", out var tlname))
                        topLevel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                            .FirstOrDefault(l => l.Name.Equals(tlname.Value<string>(), StringComparison.OrdinalIgnoreCase));
                    topLevel ??= new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                                    .Where(l => l.Elevation > baseLevel.Elevation).OrderBy(l => l.Elevation).FirstOrDefault();
                }
                bool useTopConstraint = topLevel != null;

                using (var tx = new Transaction(doc, "Create Structural Column"))
                {
                    tx.Start();
                    if (!symbol.IsActive) symbol.Activate();

                    var col = doc.Create.NewFamilyInstance(loc, symbol, baseLevel, StructuralType.Column);

                    // Base params
                    col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.Set(baseLevel.Id);
                    col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.Set(baseOffsetFt);

                    // Top params
                    var pTop = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                    var pTopOff = col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                    if (useTopConstraint)
                    {
                        pTop?.Set(topLevel.Id);
                        pTopOff?.Set(topOffsetFt);
                    }
                    else
                    {
                        // Top=Base / TopOffset=height
                        pTop?.Set(baseLevel.Id);
                        pTopOff?.Set(heightFt);
                    }

                    tx.Commit();
                    return new
                    {
                        ok = true,
                        elementId = col.Id.IntegerValue,
                        typeId = col.GetTypeId().IntegerValue,
                        baseLevelId = baseLevel.Id.IntegerValue,
                        topLevelId = useTopConstraint ? topLevel.Id.IntegerValue : baseLevel.Id.IntegerValue,
                        mode = useTopConstraint ? "top-constrained" : "base+offset"
                    };
                }
            }
            catch (Exception ex) { return new { ok = false, msg = ex.Message }; }
        }
    }
}
