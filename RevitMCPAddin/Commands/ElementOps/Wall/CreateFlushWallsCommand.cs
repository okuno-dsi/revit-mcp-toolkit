// RevitMCPAddin/Commands/ElementOps/Wall/CreateFlushWallsCommand.cs
// Create new walls flush (face-aligned) to existing walls.
#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Walls;
using RevitMCPAddin.Models;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    [RpcCommand("element.create_flush_walls",
        Aliases = new[] { "create_flush_walls" },
        Category = "ElementOps/Wall",
        Tags = new[] { "ElementOps", "Wall" },
        Risk = RiskLevel.Medium,
        Summary = "Create new walls flush (face-aligned) to existing walls, on a chosen side and plane reference.",
        Requires = new[] { "newWallTypeNameOrId" },
        Constraints = new[]
        {
            "If sourceWallIds is omitted/empty, current selection is used (walls only).",
            "sideMode: ByGlobalDirection|ByExterior|ByInterior (default ByGlobalDirection).",
            "globalDirection is only used when sideMode=ByGlobalDirection. Example: [0,-1,0] means -Y side.",
            "sourcePlane/newPlane: FinishFace|CoreFace|WallCenterline|CoreCenterline (default FinishFace)."
        },
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"element.create_flush_walls\", \"params\":{ \"sourceWallIds\":[123456], \"newWallTypeNameOrId\":\"(内壁)W5\", \"sideMode\":\"ByGlobalDirection\", \"globalDirection\":[0,-1,0], \"sourcePlane\":\"FinishFace\", \"newPlane\":\"FinishFace\", \"newExteriorMode\":\"MatchSourceExterior\", \"miterJoints\":true, \"copyVerticalConstraints\":true } }")]
    public sealed class CreateFlushWallsCommand : IRevitCommandHandler
    {
        public string CommandName => "create_flush_walls";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = cmd?.Params as JObject ?? new JObject();

                var req = p.ToObject<CreateFlushWallsRequest>() ?? new CreateFlushWallsRequest();

                // Back-compat aliases for new wall type key.
                if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                {
                    req.NewWallTypeNameOrId =
                        (p.Value<string>("newWallTypeNameOrId") ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                        req.NewWallTypeNameOrId = (p.Value<string>("newWallTypeName") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                        req.NewWallTypeNameOrId = (p.Value<string>("wallTypeName") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                        req.NewWallTypeNameOrId = (p.Value<string>("wallTypeId") ?? string.Empty).Trim();
                }

                // Selection fallback
                if (req.SourceWallIds == null) req.SourceWallIds = new List<int>();
                if (req.SourceWallIds.Count == 0)
                {
                    try
                    {
                        var selIds = uiapp.ActiveUIDocument.Selection.GetElementIds();
                        foreach (var id in selIds)
                        {
                            var w = doc.GetElement(id) as Autodesk.Revit.DB.Wall;
                            if (w == null) continue;
                            req.SourceWallIds.Add(id.IntValue());
                        }
                    }
                    catch { /* ignore */ }
                }

                if (req.SourceWallIds == null || req.SourceWallIds.Count == 0)
                    return new { ok = false, code = "INVALID_PARAMS", msg = "sourceWallIds が空です（または選択に Wall がありません）。" };

                if (string.IsNullOrWhiteSpace(req.NewWallTypeNameOrId))
                    return new { ok = false, code = "INVALID_PARAMS", msg = "newWallTypeNameOrId が必要です。" };

                var resp = WallFlushPlacement.Execute(doc, req);

                return new
                {
                    ok = resp.Ok,
                    msg = resp.Message,
                    createdWallIds = resp.CreatedWallIds,
                    warnings = resp.Warnings
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "INTERNAL_ERROR", msg = ex.Message };
            }
        }
    }
}

