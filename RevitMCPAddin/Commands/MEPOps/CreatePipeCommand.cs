// RevitMCPAddin/Commands/MEPOps/CreatePipeCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class CreatePipeCommand : IRevitCommandHandler
    {
        public string CommandName => "create_pipe";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var s = (JObject)p["start"];
            var e = (JObject)p["end"];
            var start = UnitHelper.MmToXyz(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z"));
            var end = UnitHelper.MmToXyz(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z"));

            var systemTypeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("systemTypeId"));
            var pipeTypeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("pipeTypeId"));
            var levelId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("levelId"));

            using (var tx = new Transaction(doc, "Create Pipe"))
            {
                try
                {
                    tx.Start();
                    var pipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, start, end);

                    if (p.TryGetValue("diameterMm", out var dTok))
                    {
                        var prm = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        if (prm != null && !prm.IsReadOnly)
                            prm.Set(UnitHelper.MmToFt(dTok.Value<double>()));
                    }

                    // 勾配（‰）：tanθ ≒ permil/1000
                    if (p.TryGetValue("slopePermil", out var sTok))
                    {
                        try
                        {
                            double slope = sTok.Value<double>() / 1000.0;
                            var slopeParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                            if (slopeParam != null && !slopeParam.IsReadOnly) slopeParam.Set(slope);
                        }
                        catch { /* best effort */ }
                    }

                    tx.Commit();
                    return new
                    {
                        ok = true,
                        elementId = pipe.Id.IntValue(),
                        typeId = pipeTypeId.IntValue(),
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


