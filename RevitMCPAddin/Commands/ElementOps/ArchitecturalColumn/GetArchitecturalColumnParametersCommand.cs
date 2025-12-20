// RevitMCPAddin/Commands/ElementOps/ArchitecturalColumn/GetArchitecturalColumnParametersCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.ArchitecturalColumn
{
    public class GetArchitecturalColumnParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_architectural_column_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = (JObject)(cmd.Params ?? new JObject());
                var mode = UnitHelper.ResolveUnitsMode(doc, p);

                int id = p.Value<int>("elementId");
                bool incInst = p.Value<bool?>("includeInstance") ?? true;
                bool incType = p.Value<bool?>("includeType") ?? true;

                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? int.MaxValue;

                bool includeDisplay = p.Value<bool?>("includeDisplay") ?? true;
                bool includeRaw = p.Value<bool?>("includeRaw") ?? true;
                bool includeUnit = p.Value<bool?>("includeUnit") ?? true;
                int siDigits = p.Value<int?>("siDigits") ?? 3;

                var fi = doc.GetElement(new ElementId(id)) as FamilyInstance
                         ?? throw new InvalidOperationException($"Element not found: {id}");

                var list = new List<object>();

                if (incInst)
                {
                    foreach (Parameter prm in fi.Parameters)
                    {
                        if (prm.StorageType == StorageType.None) continue;
                        list.Add(UnitHelper.MapParameter(prm, doc, mode, includeDisplay, includeRaw, siDigits, includeUnit));
                    }
                }

                if (incType)
                {
                    var sym = doc.GetElement(fi.GetTypeId()) as ElementType;
                    if (sym != null)
                    {
                        foreach (Parameter prm in sym.Parameters)
                        {
                            if (prm.StorageType == StorageType.None) continue;
                            list.Add(UnitHelper.MapParameter(prm, doc, mode, includeDisplay, includeRaw, siDigits, includeUnit));
                        }
                    }
                }

                // “値なし” はそのまま返す（フィルタは任意）
                int total = list.Count;
                if (skip == 0 && p.ContainsKey("count") && count == 0)
                    return new { ok = true, totalCount = total, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };

                var page = list.Skip(skip).Take(count).ToList();
                return new { ok = true, totalCount = total, parameters = page, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
