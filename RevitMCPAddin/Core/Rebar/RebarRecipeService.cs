#nullable enable
// ================================================================
// File   : Core/Rebar/RebarRecipeService.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Build a deterministic "recipe" object + signature from
//          the current auto-rebar plan (for sync checks / ledger).
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Rebar
{
    internal static class RebarRecipeService
    {
        // Bump when generation semantics change in a way that should invalidate "in sync" checks.
        public const string EngineVersion = "RevitMcp.AutoRebarRecipe/1.0.0";

        public static JObject BuildRecipe(
            Document doc,
            Element host,
            JObject planRoot,
            JObject planHostObj,
            JObject requestParams,
            out string signatureSha256)
        {
            signatureSha256 = string.Empty;

            var recipe = new JObject
            {
                ["engineVersion"] = EngineVersion
            };

            try
            {
                recipe["planVersion"] = planRoot != null ? (planRoot.Value<int?>("planVersion") ?? 0) : 0;
            }
            catch { recipe["planVersion"] = 0; }

            // Host identity
            try
            {
                recipe["host"] = new JObject
                {
                    ["elementId"] = host != null ? host.Id.IntValue() : 0,
                    ["uniqueId"] = host != null ? (host.UniqueId ?? string.Empty) : string.Empty
                };

                try
                {
                    int catId = host != null && host.Category != null && host.Category.Id != null ? host.Category.Id.IntValue() : 0;
                    string bic = string.Empty;
                    if (catId != 0)
                    {
                        try { bic = ((BuiltInCategory)catId).ToString(); } catch { bic = string.Empty; }
                    }
                    ((JObject)recipe["host"])["categoryBic"] = bic;
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }

            // Request options that affect planning (kept as-is; canonicalized by signature helper).
            try
            {
                var profile = (requestParams?.Value<string>("profile") ?? string.Empty).Trim();
                if (profile.Length > 0) recipe["profileRequested"] = profile;

                var tag = (requestParams?.Value<string>("tag") ?? string.Empty).Trim();
                if (tag.Length > 0) recipe["tagRequested"] = tag;

                var opts = requestParams?["options"] as JObject;
                if (opts != null) recipe["options"] = (JObject)opts.DeepClone();
            }
            catch { /* ignore */ }

            // Mapping status (avoid machine-specific path)
            try
            {
                var ms = planRoot?["mappingStatus"] as JObject;
                if (ms != null)
                {
                    var o = new JObject
                    {
                        ["ok"] = ms.Value<bool?>("ok") ?? false,
                        ["code"] = ms.Value<string>("code"),
                        ["sha8"] = ms.Value<string>("sha8"),
                        ["version"] = ms.Value<int?>("version"),
                        ["units_length"] = ms.Value<string>("units_length"),
                        ["profile_default"] = ms.Value<string>("profile_default"),
                        ["profiles"] = ms["profiles"] as JArray
                    };
                    recipe["mappingStatus"] = o;
                }
            }
            catch { /* ignore */ }

            // Mapping resolved values/errors for this host (as inputs)
            try
            {
                var mapping = planHostObj?["mapping"] as JObject;
                if (mapping != null)
                {
                    var values = mapping["values"] as JObject;
                    var errors = mapping["errors"] as JArray;

                    var m = new JObject();
                    try
                    {
                        var meta = mapping["mapping"] as JObject;
                        if (meta != null)
                        {
                            m["profile"] = meta.Value<string>("profile");
                            m["sha8"] = meta.Value<string>("sha8");
                            m["version"] = meta.Value<int?>("version");
                            m["units_length"] = meta.Value<string>("units_length");
                        }
                    }
                    catch { /* ignore */ }

                    if (values != null) m["values"] = (JObject)values.DeepClone();

                    if (errors != null && errors.Count > 0)
                    {
                        // Keep only stable info
                        var slim = new JArray();
                        foreach (var e in errors.OfType<JObject>())
                        {
                            slim.Add(new JObject
                            {
                                ["key"] = e.Value<string>("key"),
                                ["code"] = e.Value<string>("code")
                            });
                        }
                        m["errors"] = slim;
                    }

                    recipe["mapping"] = m;
                }
            }
            catch { /* ignore */ }

            // The planned actions (curves + layout + bar types)
            try
            {
                var actions = planHostObj?["actions"] as JArray;
                if (actions != null)
                    recipe["actions"] = (JArray)actions.DeepClone();
            }
            catch { /* ignore */ }

            signatureSha256 = RebarRecipeSignature.Sha256FromJToken(recipe);
            recipe["signatureSha256"] = signatureSha256;
            return recipe;
        }
    }
}

