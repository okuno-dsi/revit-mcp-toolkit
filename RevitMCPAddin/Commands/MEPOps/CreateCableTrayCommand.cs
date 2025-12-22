// RevitMCPAddin/Commands/MEPOps/CreateCableTrayCommand.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MEPOps
{
    public class CreateCableTrayCommand : IRevitCommandHandler
    {
        public string CommandName => "create_cable_tray";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var s = (JObject)p["start"];
            var e = (JObject)p["end"];
            var start = UnitHelper.MmToXyz(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z"));
            var end = UnitHelper.MmToXyz(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z"));

            var trayTypeId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("trayTypeId"));
            var levelId = Autodesk.Revit.DB.ElementIdCompat.From(p.Value<int>("levelId"));

            using (var tx = new Transaction(doc, "Create CableTray"))
            {
                try
                {
                    tx.Start();
                    var tray = CableTray.Create(doc, trayTypeId, start, end, levelId);

                    if (p.TryGetValue("widthMm", out var wTok))
                    {
                        var prmW = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                        if (prmW != null && !prmW.IsReadOnly) prmW.Set(UnitHelper.MmToFt(wTok.Value<double>()));
                    }
                    if (p.TryGetValue("heightMm", out var hTok))
                    {
                        var prmH = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
                        if (prmH != null && !prmH.IsReadOnly) prmH.Set(UnitHelper.MmToFt(hTok.Value<double>()));
                    }

                    tx.Commit();
                    return new
                    {
                        ok = true,
                        elementId = tray.Id.IntValue(),
                        typeId = trayTypeId.IntValue(),
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


