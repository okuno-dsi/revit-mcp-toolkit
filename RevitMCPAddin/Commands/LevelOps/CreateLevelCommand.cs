// ================================================================
// File: Commands/DatumOps/CreateLevelCommand.cs  (UnitHelper統一版)
// Revit 2023 / .NET Framework 4.8
// ================================================================
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DatumOps
{
    public class CreateLevelCommand : IRevitCommandHandler
    {
        public string CommandName => "create_level";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            // 入力は mm 前提 → 内部(ft)へ
            double elevMm = p.Value<double>("elevation");
            double elevFt = UnitHelper.MmToInternal(elevMm, doc);

            var name = p.Value<string>("name");

            using (var tx = new Transaction(doc, "Create Level"))
            {
                tx.Start();
                var lvl = Level.Create(doc, elevFt);
                if (!string.IsNullOrWhiteSpace(name)) lvl.Name = name;
                tx.Commit();

                return new
                {
                    ok = true,
                    levelId = lvl.Id.IntegerValue,
                    elevation = Math.Round(UnitHelper.InternalToMm(lvl.Elevation, doc), 3),
                    name = lvl.Name,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }
        }
    }
}
