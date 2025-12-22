// ================================================================
// File: Commands/DatumOps/GetLevelParametersCommand.cs (UnitHelper統一版)
// - Parameter列挙は UnitHelper.MapParameter(..., mode) に統一
// - unitsMode: "SI" | "Project" | "Raw" | "Both"（未指定は SI）
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DatumOps
{
    public class GetLevelParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_level_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int levelId = p.Value<int>("levelId");
            var level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId)) as Level
                        ?? throw new InvalidOperationException($"Level not found: {levelId}");

            var mode = UnitHelper.ResolveUnitsMode(doc, p);

            var list = level.Parameters
                .Cast<Parameter>()
                .Select(par => UnitHelper.MapParameter(par, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3))
                .ToList();

            return new
            {
                ok = true,
                levelId,
                @params = list,
                units = UnitHelper.DefaultUnitsMeta(),
                mode = mode.ToString()
            };
        }
    }
}

