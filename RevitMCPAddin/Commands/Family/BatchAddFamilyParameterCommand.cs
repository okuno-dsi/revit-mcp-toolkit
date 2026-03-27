#nullable enable
using System;
using System.Diagnostics;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Family
{
    [RpcCommand("family.batch_add_parameter_from_folder",
        Category = "Family",
        Tags = new[] { "Family", "Batch", "Parameter", "Offline" },
        Kind = "write",
        Importance = "high",
        Risk = RiskLevel.High,
        Summary = "Open every .rfa in a folder, add family/shared parameters safely, save, and close with per-file results.",
        Constraints = new[]
        {
            "Edits offline .rfa family files only.",
            "Does not modify .rvt project documents.",
            "Does not destructively replace mismatched existing parameters in V1."
        })]
    public sealed class BatchAddFamilyParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "family.batch_add_parameter_from_folder";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (uiapp?.Application == null)
                {
                    return RpcResultEnvelope.StandardizePayload(
                        RpcResultEnvelope.Fail("NO_APP", "Revit application is not available."),
                        uiapp,
                        cmd.Command,
                        sw.ElapsedMilliseconds);
                }

                var service = new FamilyBatchParameterService(uiapp.Application);
                var payload = service.Execute(cmd.Params as JObject ?? new JObject());
                return RpcResultEnvelope.StandardizePayload(payload, uiapp, cmd.Command, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                return RpcResultEnvelope.StandardizePayload(
                    RpcResultEnvelope.Fail("BATCH_FAMILY_PARAMETER_FAILED", ex.Message),
                    uiapp,
                    cmd.Command,
                    sw.ElapsedMilliseconds);
            }
        }
    }
}
