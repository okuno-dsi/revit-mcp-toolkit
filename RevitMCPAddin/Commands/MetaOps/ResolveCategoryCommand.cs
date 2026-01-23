#nullable enable
// ================================================================
// File: Commands/MetaOps/ResolveCategoryCommand.cs
// Desc: JSON-RPC "meta.resolve_category" (category alias resolver)
// Target: .NET Framework 4.8 / C# 8.0
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.MetaOps
{
    internal sealed class ResolveCategoryParams
    {
        public string? text { get; set; }
        public JObject? context { get; set; }
        public int? maxCandidates { get; set; }
    }

    [RpcCommand("meta.resolve_category",
        Category = "MetaOps",
        Tags = new[] { "category", "resolve", "discovery" },
        Risk = RiskLevel.Low,
        Summary = "Resolve ambiguous category text to BuiltInCategory (OST_*) using alias dictionary.",
        Kind = "read")]
    public sealed class ResolveCategoryCommand : IRevitCommandHandler
    {
        public string CommandName => "meta.resolve_category";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (cmd.Params as JObject) != null
                    ? ((JObject)cmd.Params!).ToObject<ResolveCategoryParams>() ?? new ResolveCategoryParams()
                    : new ResolveCategoryParams();

                var rawText = (p.text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(rawText))
                    return RpcResultEnvelope.Fail(code: "INVALID_PARAMS", msg: "Missing 'text' parameter.");

                int maxCandidates = Math.Max(1, Math.Min(50, p.maxCandidates ?? 5));

                var ctx = ParseContext(p.context as JObject);
                var res = CategoryAliasService.Resolve(rawText, ctx, maxCandidates);
                var dict = CategoryAliasService.GetStatus();

                return new
                {
                    ok = res.ok,
                    msg = res.msg,
                    normalizedText = res.normalizedText,
                    recoveredFromMojibake = res.recoveredFromMojibake,
                    resolved = res.resolved,
                    candidates = res.candidates,
                    dictionary = dict
                };
            }
            catch (Exception ex)
            {
                RevitLogger.Error("meta.resolve_category failed: " + ex);
                return RpcResultEnvelope.Fail(code: "INTERNAL_ERROR", msg: ex.Message);
            }
        }

        private static CategoryResolveContext ParseContext(JObject? ctxObj)
        {
            var ctx = new CategoryResolveContext();
            if (ctxObj == null) return ctx;

            ctx.disciplineHint = ctxObj.Value<string>("disciplineHint");
            ctx.activeViewType = ctxObj.Value<string>("activeViewType");

            if (ctxObj.TryGetValue("selectedCategoryIds", out var catTok) && catTok is JArray arr)
            {
                foreach (var t in arr)
                {
                    if (t == null) continue;
                    var s = t.ToString().Trim();
                    if (string.IsNullOrWhiteSpace(s)) continue;

                    string? resolved = null;

                    if (int.TryParse(s, out var idInt))
                        resolved = TryBuiltInCategoryNameFromInt(idInt);

                    if (resolved == null)
                    {
                        if (Enum.TryParse<BuiltInCategory>(s, true, out var bic) && bic != BuiltInCategory.INVALID)
                            resolved = bic.ToString();
                    }

                    if (resolved == null)
                    {
                        if (CategoryResolver.TryResolveCategory(s, out var bic2) && bic2 != BuiltInCategory.INVALID)
                            resolved = bic2.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(resolved))
                        ctx.selectedCategoryIds.Add(resolved);
                }
            }

            ctx.selectedCategoryIds = ctx.selectedCategoryIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ctx;
        }

        private static string? TryBuiltInCategoryNameFromInt(int id)
        {
            var name = Enum.GetName(typeof(BuiltInCategory), id);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }
}
