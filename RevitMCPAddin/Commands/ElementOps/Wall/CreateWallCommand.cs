// RevitMCPAddin/Commands/ElementOps/Wall/CreateWallCommand.cs
// UnitHelper化: mm→ft 変換は UnitHelper.MmToXyz/MmToFt、units メタ追加
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    using RevitWall = Autodesk.Revit.DB.Wall;
    using RevitWallType = Autodesk.Revit.DB.WallType;
    using RevitLevel = Autodesk.Revit.DB.Level;

    [RpcCommand("element.create_wall",
        Aliases = new[] { "create_wall" },
        Category = "ElementOps/Wall",
        Tags = new[] { "ElementOps", "Wall" },
        Risk = RiskLevel.Medium,
        Summary = "Create a straight wall from start/end points (mm) on a base level, optionally top-constrained.",
        Requires = new[] { "start", "end" },
        Constraints = new[]
        {
            "Coordinates are in model space (mm).",
            "For predictable results, specify wallTypeName (or wallTypeId) and baseLevelId (or baseLevelName).",
            "If heightMm is omitted or set to 'level-to-level', the wall may be constrained to the next level."
        },
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"element.create_wall\", \"params\":{ \"start\":{ \"x\":0, \"y\":0, \"z\":0 }, \"end\":{ \"x\":10000, \"y\":0, \"z\":0 }, \"wallTypeName\":\"(内壁)W5\", \"baseLevelId\":123, \"topLevelId\":456, \"baseOffsetMm\":0, \"topOffsetMm\":0 } }")]
    public class CreateWallCommand : IRevitCommandHandler
    {
        public string CommandName => "create_wall";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
                var p = (JObject)(cmd.Params ?? new JObject());

                // --- 1) Base Level 解決 ---
                ElementId baseLevelId = ElementId.InvalidElementId;
                if (p.TryGetValue("baseLevelId", out var blid)) baseLevelId = Autodesk.Revit.DB.ElementIdCompat.From(blid.Value<int>());
                else if (p.TryGetValue("levelId", out var lid)) baseLevelId = Autodesk.Revit.DB.ElementIdCompat.From(lid.Value<int>());

                RevitLevel baseLevel = null;
                if (baseLevelId != ElementId.InvalidElementId)
                    baseLevel = doc.GetElement(baseLevelId) as RevitLevel;

                if (baseLevel == null)
                {
                    string baseLevelName = null;
                    if (p.TryGetValue("baseLevelName", out var blname)) baseLevelName = blname.Value<string>();
                    else if (p.TryGetValue("levelName", out var lname)) baseLevelName = lname.Value<string>();

                    if (!string.IsNullOrEmpty(baseLevelName))
                    {
                        baseLevel = new FilteredElementCollector(doc)
                            .OfClass(typeof(RevitLevel)).Cast<RevitLevel>()
                            .FirstOrDefault(l => string.Equals(l.Name, baseLevelName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                if (baseLevel == null)
                {
                    baseLevel = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitLevel)).Cast<RevitLevel>()
                        .OrderBy(l => l.Elevation).FirstOrDefault();
                    if (baseLevel == null)
                        return new { ok = false, msg = "No Level found in document." };
                }

                // --- 2) 形状入力（mm→ft） ---
                var s = p["start"]; var e = p["end"];
                if (s == null || e == null)
                    return new { ok = false, msg = "Both 'start' and 'end' must be provided." };

                var start = UnitHelper.MmToXyz(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z"));
                var end = UnitHelper.MmToXyz(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z"));

                // --- 3) WallType 解決 ---
                RevitWallType wType = null;
                if (p.TryGetValue("wallTypeName", out var tn))
                {
                    var tname = tn.Value<string>();
                    if (!string.IsNullOrEmpty(tname))
                    {
                        wType = new FilteredElementCollector(doc)
                            .OfClass(typeof(RevitWallType)).Cast<RevitWallType>()
                            .FirstOrDefault(w => string.Equals(w.Name, tname, StringComparison.OrdinalIgnoreCase));
                    }
                }
                if (wType == null && p.TryGetValue("wallTypeId", out var wid))
                    wType = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(wid.Value<int>())) as RevitWallType;

                if (wType == null)
                {
                    wType = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitWallType)).Cast<RevitWallType>()
                        .FirstOrDefault();
                    if (wType == null)
                        return new { ok = false, msg = "No WallType found in document." };
                }

                // --- 4) 高さモード判定 ---
                var htToken = p["heightMm"];
                bool hasTopLevelParam = p.ContainsKey("topLevelId") || p.ContainsKey("topLevelName");
                bool isLevelToLevel = (htToken == null) ||
                                      (htToken.Type == JTokenType.String &&
                                       string.Equals(htToken.Value<string>(), "level-to-level", StringComparison.OrdinalIgnoreCase));

                double baseOffsetFt = UnitHelper.MmToFt(p.Value<double?>("baseOffsetMm") ?? 0.0);
                double topOffsetFt = UnitHelper.MmToFt(p.Value<double?>("topOffsetMm") ?? 0.0);

                double unconnHeightFt = UnitHelper.MmToFt(
                    (htToken != null && (htToken.Type == JTokenType.Integer || htToken.Type == JTokenType.Float))
                        ? htToken.Value<double>()
                        : 3000.0 // fallback
                );

                RevitLevel topLevel = null;
                if (hasTopLevelParam || isLevelToLevel)
                {
                    if (p.TryGetValue("topLevelId", out var tlid))
                        topLevel = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tlid.Value<int>())) as RevitLevel;

                    if (topLevel == null && p.TryGetValue("topLevelName", out var tlname))
                    {
                        var name = tlname.Value<string>();
                        if (!string.IsNullOrEmpty(name))
                        {
                            topLevel = new FilteredElementCollector(doc)
                                .OfClass(typeof(RevitLevel)).Cast<RevitLevel>()
                                .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (topLevel == null)
                    {
                        topLevel = new FilteredElementCollector(doc)
                            .OfClass(typeof(RevitLevel)).Cast<RevitLevel>()
                            .Where(l => l.Elevation > baseLevel.Elevation)
                            .OrderBy(l => l.Elevation)
                            .FirstOrDefault();
                    }
                }

                bool useTopConstraint = (topLevel != null);
                bool isStructural = p.Value<bool?>("isStructural") ?? false;

                // --- 5) 作成＆拘束設定 ---
                using (var tx = new Transaction(doc, "Create Wall"))
                {
                    tx.Start();
                    TxnUtil.ConfigureProceedWithWarnings(tx);

                    var line = Line.CreateBound(start, end);

                    var wall = RevitWall.Create(
                        doc,
                        line,
                        wType.Id,
                        baseLevel.Id,
                        unconnHeightFt,
                        baseOffsetFt,
                        false,
                        isStructural
                    );

                    // Base
                    var pBase = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    if (pBase != null && !pBase.IsReadOnly) pBase.Set(baseLevel.Id);

                    var pBaseOff = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                    if (pBaseOff != null && !pBaseOff.IsReadOnly) pBaseOff.Set(baseOffsetFt);

                    // Top/Unconnected
                    var pTop = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                    var pTopOff = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                    var pUnconn = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);

                    if (useTopConstraint)
                    {
                        if (pTop != null && !pTop.IsReadOnly) pTop.Set(topLevel.Id);
                        if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(topOffsetFt);
                        if (pUnconn != null && !pUnconn.IsReadOnly) pUnconn.Set(0.0);
                    }
                    else
                    {
                        if (pTop != null && !pTop.IsReadOnly) pTop.Set(ElementId.InvalidElementId);
                        if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(0.0);
                        if (pUnconn != null && !pUnconn.IsReadOnly) pUnconn.Set(unconnHeightFt);
                    }

                    var txStatus = tx.Commit();
                    if (txStatus != TransactionStatus.Committed)
                    {
                        return new
                        {
                            ok = false,
                            code = "TX_NOT_COMMITTED",
                            msg = "Transaction did not commit.",
                            detail = new { transactionStatus = txStatus.ToString() }
                        };
                    }

                    return new
                    {
                        ok = true,
                        elementId = wall.Id.IntValue(),
                        typeId = wall.GetTypeId().IntValue(),
                        baseLevelId = baseLevel.Id.IntValue(),
                        topLevelId = useTopConstraint ? topLevel.Id.IntValue() : (int?)null,
                        mode = useTopConstraint ? "top-constrained" : "unconnected",
                        inputUnits = UnitHelper.InputUnitsMeta(),
                        internalUnits = UnitHelper.InternalUnitsMeta()
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


