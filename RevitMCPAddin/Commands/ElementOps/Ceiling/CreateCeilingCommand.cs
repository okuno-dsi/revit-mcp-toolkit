// File: RevitMCPAddin/Commands/ElementOps/Ceiling/CreateCeilingCommand.cs  (UnitHelper化)
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Ceiling
{
    public class CreateCeilingCommand : IRevitCommandHandler
    {
        public string CommandName => "create_ceiling";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // レベル
            var levelId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("levelId"));
            var level = doc.GetElement(levelId) as Level
                        ?? throw new InvalidOperationException($"Level not found: {levelId.IntValue()}");

            // 境界ループ（mm入力）
            var pts = p["points"] as JArray;
            if (pts == null || pts.Count < 3)
                throw new InvalidOperationException("Boundary points must have at least 3 vertices.");

            var xyzList = pts.Select(pt =>
                UnitHelper.MmToInternalXYZ(
                    pt["x"].Value<double>(),
                    pt["y"].Value<double>(),
                    0.0  // XY only
                )).ToList();

            double tol = uiapp.Application.ShortCurveTolerance;
            var loop = new CurveLoop();
            for (int i = 0; i < xyzList.Count; i++)
            {
                var a = xyzList[i];
                var b = xyzList[(i + 1) % xyzList.Count];
                if (a.DistanceTo(b) >= tol) loop.Append(Line.CreateBound(a, b));
            }

            // タイプ/オフセット（mm → 内部）
            var typeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("typeId"));
            double offsetMm = p.Value<double?>("offsetMm") ?? 0.0;
            double offsetInternal = UnitHelper.MmToInternalLength(offsetMm);

            using var tx = new Transaction(doc, "Create Ceiling with Offset");
            tx.Start();

            var ceiling = Autodesk.Revit.DB.Ceiling.Create(
                doc,
                new List<CurveLoop> { loop },
                typeId,
                level.Id);

            var height = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
            if (height != null && !height.IsReadOnly) height.Set(offsetInternal);

            tx.Commit();

            return ResultUtil.Ok(new { elementId = ceiling.Id.IntValue() });
        }
    }
}


