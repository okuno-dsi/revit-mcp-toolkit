// ================================================================
// File: Commands/Spatial/ResolveSpatialSelectionCommand.cs
// Purpose: resolve_spatial_selection (alias) / element.resolve_spatial_selection (canonical)
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Spatial
{
    [RpcCommand("element.resolve_spatial_selection",
        Aliases = new[] { "resolve_spatial_selection" },
        Category = "Spatial",
        Tags = new[] { "Spatial", "Selection" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Summary = "Resolve a Room/Space/Area selection mismatch to the nearest/containing target spatial element.",
        Requires = new[] { "desiredKind" },
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"element.resolve_spatial_selection\", \"params\":{ \"fromSelection\":true, \"desiredKind\":\"room\", \"maxDistanceMeters\":0.5 } }")]
    public sealed class ResolveSpatialSelectionCommand : IRevitCommandHandler
    {
        public string CommandName => "element.resolve_spatial_selection";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = cmd?.Params as JObject ?? new JObject();

            int elementIdInt = p.Value<int?>("elementId") ?? 0;
            bool fromSelection = p.Value<bool?>("fromSelection") ?? false;
            if (elementIdInt <= 0 && fromSelection && uidoc != null)
            {
                try
                {
                    foreach (var id in uidoc.Selection.GetElementIds())
                    {
                        elementIdInt = id.IntValue();
                        break;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (elementIdInt <= 0)
                return new { ok = false, msg = "elementId を指定してください (>0)。または fromSelection=true を指定してください。" };

            var desiredKindStr = (p.Value<string>("desiredKind") ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(desiredKindStr))
                return new { ok = false, msg = "desiredKind を指定してください (room|space|area)。" };

            SpatialKind desired;
            if (!TryParseDesiredKind(desiredKindStr, out desired))
                return new { ok = false, msg = "Unsupported desiredKind: " + desiredKindStr + " (room|space|area)" };

            double maxMeters = p.Value<double?>("maxDistanceMeters") ?? 0.5;
            if (double.IsNaN(maxMeters) || double.IsInfinity(maxMeters) || maxMeters <= 0) maxMeters = 0.5;

            var opt = SpatialResolveOptions.CreateDefaultMeters(maxMeters);

            var inputId = Autodesk.Revit.DB.ElementIdCompat.From(elementIdInt);
            SpatialResolveResult rr;
            bool ok = SpatialElementResolver.TryResolve(doc, inputId, desired, opt, out rr);

            // Log one concise line (policy: %LOCALAPPDATA%\\RevitMCP\\logs)
            try
            {
                var origKind = rr.OriginalKind.HasValue ? rr.OriginalKind.Value.ToString().ToLowerInvariant() : "unknown";
                var distM = double.IsNaN(rr.DistanceInternal) ? double.NaN : UnitUtils.ConvertFromInternalUnits(rr.DistanceInternal, UnitTypeId.Meters);
                RevitLogger.Info(
                    "resolve_spatial_selection ok={0} original={1}({2}) resolved={3}({4}) containment={5} dist={6:0.###}m",
                    ok,
                    rr.OriginalId != null ? rr.OriginalId.IntValue() : 0,
                    origKind,
                    rr.ResolvedId != null ? rr.ResolvedId.IntValue() : 0,
                    desired.ToString().ToLowerInvariant(),
                    rr.ByContainment,
                    distM);
            }
            catch { /* ignore */ }

            if (!ok)
            {
                return new { ok = false, msg = rr.Message };
            }

            var distMeters = UnitUtils.ConvertFromInternalUnits(rr.DistanceInternal, UnitTypeId.Meters);
            return new
            {
                ok = true,
                original = new
                {
                    id = rr.OriginalId != null ? rr.OriginalId.IntValue() : elementIdInt,
                    kind = rr.OriginalKind.HasValue ? rr.OriginalKind.Value.ToString().ToLowerInvariant() : "unknown"
                },
                resolved = new
                {
                    id = rr.ResolvedId != null ? rr.ResolvedId.IntValue() : 0,
                    kind = desired.ToString().ToLowerInvariant()
                },
                byContainment = rr.ByContainment,
                distanceMeters = Math.Round(distMeters, 6),
                msg = rr.Message
            };
        }

        private static bool TryParseDesiredKind(string desiredKind, out SpatialKind kind)
        {
            kind = SpatialKind.Room;
            var k = (desiredKind ?? string.Empty).Trim().ToLowerInvariant();
            if (k == "room") { kind = SpatialKind.Room; return true; }
            if (k == "space") { kind = SpatialKind.Space; return true; }
            if (k == "area") { kind = SpatialKind.Area; return true; }
            return false;
        }
    }
}

