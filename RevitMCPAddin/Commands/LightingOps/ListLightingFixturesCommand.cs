// ================================================================
// Method : list_lighting_fixtures
// ================================================================
#nullable enable
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.LightingOps
{
    public sealed class ListLightingFixturesCommand : IRevitCommandHandler
    {
        public string CommandName => "list_lighting_fixtures";

        public object Execute(UIApplication uiapp, RequestCommand req)
        {
            try
            {
                var doc = uiapp.ActiveUIDocument.Document;
                var p = ReqShim.Params(req);

                int? viewId = ReqShim.Get<int?>(p, "viewId", null);
                int? levelId = ReqShim.Get<int?>(p, "levelId", null);

                var fixtures = LightingCommon.CollectFixtures(doc, viewId, levelId).ToList();
                var arr = new JArray();

                foreach (var f in fixtures)
                {
                    var watt = LightingCommon.GetWatt(f);
                    var typeName = f.Symbol?.Name ?? "(no type)";
                    var levelName = doc.GetElement(f.LevelId) is Level lv ? lv.Name : null;
                    var loc = LightingCommon.GetLocationPoint(f);

                    var o = new JObject
                    {
                        ["elementId"] = f.Id.IntegerValue,
                        ["name"] = f.Name,
                        ["type"] = typeName,
                        ["level"] = levelName != null ? (JToken)levelName : JValue.CreateNull(),
                        ["watt"] = watt,
                        ["x"] = loc?.X ?? double.NaN,
                        ["y"] = loc?.Y ?? double.NaN,
                        ["z"] = loc?.Z ?? double.NaN
                    };
                    arr.Add(o);
                }

                return new { ok = true, count = arr.Count, fixtures = arr };
            }
            catch (System.Exception ex)
            {
                RevitLogger.LogError("[list_lighting_fixtures] " + ex);
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
