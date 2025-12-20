// RevitMCPAddin/Commands/MEPOps/CreateConduitCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class CreateConduitCommand : IRevitCommandHandler
    {
        public string CommandName => "create_conduit";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var s = (JObject)p["start"];
            var e = (JObject)p["end"];
            var start = UnitHelper.MmToXyz(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z"));
            var end = UnitHelper.MmToXyz(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z"));

            var conduitTypeId = new ElementId(p.Value<int>("conduitTypeId"));
            var levelId = new ElementId(p.Value<int>("levelId"));

            using (var tx = new Transaction(doc, "Create Conduit"))
            {
                try
                {
                    tx.Start();
                    var conduit = Conduit.Create(doc, conduitTypeId, start, end, levelId);

                    if (p.TryGetValue("diameterMm", out var dTok))
                    {
                        var prm = conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                        if (prm != null && !prm.IsReadOnly) prm.Set(UnitHelper.MmToFt(dTok.Value<double>()));
                    }

                    tx.Commit();
                    return new
                    {
                        ok = true,
                        elementId = conduit.Id.IntegerValue,
                        typeId = conduitTypeId.IntegerValue,
                        units = UnitHelper.DefaultUnitsMeta()
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
        }
    }
}
