// ================================================================
// File: Commands/DatumOps/CreateLevelCommand.cs  (UnitHelper統一版)
// Revit 2023 / .NET Framework 4.8
// ================================================================
using System;
using System.Linq;
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
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, code = "NO_ACTIVE_DOCUMENT", msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // 入力は mm 前提 → 内部(ft)へ
            double elevMm = p.Value<double>("elevation");
            double elevFt = UnitHelper.MmToInternal(elevMm, doc);

            var name = p.Value<string>("name");

            string resolvedName = name;
            int createdLevelId = -1;
            try
            {
                using (var tx = new Transaction(doc, "Create Level"))
                {
                    tx.Start();
                    TxnUtil.ConfigureProceedWithWarnings(tx);

                    var lvl = Level.Create(doc, elevFt);
                    createdLevelId = lvl.Id.IntValue();

                    if (!string.IsNullOrWhiteSpace(resolvedName))
                        lvl.Name = resolvedName;
                    else
                        resolvedName = lvl.Name;

                    doc.Regenerate();

                    var txStatus = tx.Commit();
                    if (txStatus != TransactionStatus.Committed)
                    {
                        return new
                        {
                            ok = false,
                            code = "TX_NOT_COMMITTED",
                            msg = "Create Level transaction did not commit.",
                            elevation = Math.Round(elevMm, 3),
                            name = resolvedName,
                            detail = new { transactionStatus = txStatus.ToString() },
                            units = UnitHelper.DefaultUnitsMeta()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new
                {
                    ok = false,
                    code = "LEVEL_CREATE_EXCEPTION",
                    msg = ex.Message,
                    elevation = Math.Round(elevMm, 3),
                    name = resolvedName,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }

            Level createdLevel = null;
            if (createdLevelId > 0)
                createdLevel = doc.GetElement(ElementIdCompat.From(createdLevelId)) as Level;

            if (createdLevel == null && !string.IsNullOrWhiteSpace(resolvedName))
            {
                createdLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => string.Equals(l.Name ?? string.Empty, resolvedName, StringComparison.OrdinalIgnoreCase));
            }

            if (createdLevel == null)
            {
                createdLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => Math.Abs(l.Elevation - elevFt) < 1e-6);
            }

            if (createdLevel == null)
            {
                return new
                {
                    ok = false,
                    code = "LEVEL_CREATE_VERIFY_FAILED",
                    msg = "Level was committed but could not be resolved from the document.",
                    elevation = Math.Round(elevMm, 3),
                    name = resolvedName,
                    units = UnitHelper.DefaultUnitsMeta()
                };
            }

            return new
            {
                ok = true,
                levelId = createdLevel.Id.IntValue(),
                elevation = Math.Round(elevMm, 3),
                name = string.IsNullOrWhiteSpace(resolvedName) ? createdLevel.Name : resolvedName,
                units = UnitHelper.DefaultUnitsMeta()
            };
        }
    }
}

