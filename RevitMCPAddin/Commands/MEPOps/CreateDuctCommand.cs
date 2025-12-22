// RevitMCPAddin/Commands/MEPOps/CreateDuctCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class CreateDuctCommand : IRevitCommandHandler
    {
        public string CommandName => "create_duct";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var s = (JObject)p["start"];
            var e = (JObject)p["end"];
            var start = UnitHelper.MmToXyz(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z"));
            var end = UnitHelper.MmToXyz(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z"));

            var systemTypeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("systemTypeId"));
            var typeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("ductTypeId"));
            var levelId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("levelId"));

            using (var tx = new Transaction(doc, "Create Duct"))
            {
                try
                {
                    tx.Start();
                    var duct = Duct.Create(doc, systemTypeId, typeId, levelId, start, end);

                    // 形状：丸 or 矩形（mm 入力→内部 ft）
                    if (p.TryGetValue("diameterMm", out var dTok))
                    {
                        var prm = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                        if (prm != null && !prm.IsReadOnly) prm.Set(UnitHelper.MmToFt(dTok.Value<double>()));
                    }
                    else
                    {
                        if (p.TryGetValue("widthMm", out var wTok))
                        {
                            var prmW = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                            if (prmW != null && !prmW.IsReadOnly) prmW.Set(UnitHelper.MmToFt(wTok.Value<double>()));
                        }
                        if (p.TryGetValue("heightMm", out var hTok))
                        {
                            var prmH = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                            if (prmH != null && !prmH.IsReadOnly) prmH.Set(UnitHelper.MmToFt(hTok.Value<double>()));
                        }
                    }

                    tx.Commit();
                    return new
                    {
                        ok = true,
                        elementId = duct.Id.IntValue(),
                        typeId = typeId.IntValue(),
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


