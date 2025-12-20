// RevitMCPAddin/Commands/MEPOps/GetMepParametersCommand.cs (UnitHelper対応)
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class GetMepParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_mep_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            Element target = null;
            if (p.TryGetValue("elementId", out var eidTok))
                target = doc.GetElement(new ElementId(eidTok.Value<int>()));
            if (target == null) return new { ok = false, msg = "Element not found." };

            // UnitHelper で出力モードを決定
            var mode = UnitHelper.ResolveUnitsMode(doc, p);
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            bool includeDisplay = p.Value<bool?>("includeDisplay") ?? true;
            bool includeRaw = p.Value<bool?>("includeRaw") ?? true;
            bool includeUnit = p.Value<bool?>("includeUnit") ?? true;
            int siDigits = p.Value<int?>("siDigits") ?? 3;

            var all = target.Parameters
                .Cast<Parameter>()
                .Where(pp => pp.StorageType != StorageType.None)
                .Select(pp => namesOnly
                    ? (object)(pp.Definition?.Name ?? "")
                    : UnitHelper.MapParameter(pp, doc, mode, includeDisplay, includeRaw, siDigits, includeUnit))
                .ToList();

            int total = all.Count;
            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, totalCount = total, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };

            var page = all.Skip(skip).Take(count).ToList();
            return namesOnly
                ? (object)new { ok = true, totalCount = total, names = page, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() }
                : new { ok = true, totalCount = total, parameters = page, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
        }
    }
}
