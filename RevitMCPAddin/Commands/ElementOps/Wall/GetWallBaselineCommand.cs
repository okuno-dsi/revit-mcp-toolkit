#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    /// <summary>
    /// 壁の基準線（LocationCurve）を mm 単位で返す。
    /// - C# 8 対応（is not を使わない）
    /// - 例外時は { ok:false, msg:"..." } を返す
    /// </summary>
    [RpcCommand("element.get_wall_baseline",
        Aliases = new[] { "get_wall_baseline" },
        Category = "ElementOps/Wall",
        Tags = new[] { "ElementOps", "Wall" },
        Risk = RiskLevel.Low,
        Summary = "Get a wall LocationCurve baseline (mm).",
        Requires = new[] { "elementId|uniqueId" },
        Constraints = new[] { "Provide elementId (recommended) or uniqueId." },
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"element.get_wall_baseline\", \"params\":{ \"elementId\":123456 } }")]
    public class GetWallBaselineCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_baseline";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd?.Params as JObject ?? new JObject();

            try
            {
                Element elem = CmdUtils.GetElementByIdOrUniqueId(doc, p);
                if (elem == null)
                    return new { ok = false, msg = "要素が見つかりません（elementId / uniqueId を確認してください）。" };

                // C# 8 互換: "is not" を使わない
                Autodesk.Revit.DB.Wall wall = elem as Autodesk.Revit.DB.Wall;
                if (wall == null)
                    return new { ok = false, msg = "指定要素は Wall ではありません。" };

                LocationCurve lc = wall.Location as LocationCurve;
                if (lc == null || lc.Curve == null)
                    return new { ok = false, msg = "Wall に LocationCurve がありません。" };

                JObject baseline = GeometryJsonHelper.CurveToJson(lc.Curve);

                var result = new JObject
                {
                    ["ok"] = true,
                    ["elementId"] = wall.Id.IntValue(),
                    ["baseline"] = baseline
                };
                return result;
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "get_wall_baseline 実行中に例外: " + ex.Message };
            }
        }
    }
}

