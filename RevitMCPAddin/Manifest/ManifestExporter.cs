// File: RevitMCPAddin/Manifest/ManifestExporter.cs
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Manifest
{
    public static class ManifestExporter
    {
        private static Newtonsoft.Json.Linq.JToken ExtractParamsExample(string exampleJsonRpc)
        {
            try
            {
                var s = (exampleJsonRpc ?? string.Empty).Trim();
                if (s.Length == 0) return new Newtonsoft.Json.Linq.JObject();
                var obj = Newtonsoft.Json.Linq.JObject.Parse(s);
                var p = obj["params"];
                return p ?? new Newtonsoft.Json.Linq.JObject();
            }
            catch
            {
                return new Newtonsoft.Json.Linq.JObject();
            }
        }

        private static string InferSummary(string method, string[] tags)
        {
            try
            {
                var m = (method ?? string.Empty).Trim();
                if (m.Length == 0) return "Command";
                // Make something readable without relying on per-command annotations.
                // Example: "doc.get_project_info" -> "doc get project info"
                var s = m.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Replace('/', ' ');
                s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
                if (s.Length == 0) s = m;
                if (tags != null && tags.Length > 0)
                {
                    // Prefix with first tag to give a hint of domain.
                    var t0 = (tags[0] ?? string.Empty).Trim();
                    if (t0.Length > 0 && s.IndexOf(t0, StringComparison.OrdinalIgnoreCase) < 0)
                        return t0 + ": " + s;
                }
                return s;
            }
            catch
            {
                return "Command";
            }
        }

        private static string GetSinceString(Assembly asm)
        {
            try
            {
                var v = asm != null ? asm.GetName().Version : null;
                var ver = v != null ? v.ToString() : "0.0.0.0";
                string ts = null;
                try
                {
                    if (asm != null && !string.IsNullOrWhiteSpace(asm.Location) && System.IO.File.Exists(asm.Location))
                        ts = System.IO.File.GetLastWriteTimeUtc(asm.Location).ToString("yyyy-MM-ddTHH:mm:ssZ");
                }
                catch { ts = null; }

                return !string.IsNullOrWhiteSpace(ts) ? (ver + "@" + ts) : ver;
            }
            catch
            {
                return "0.0.0.0";
            }
        }

            public static List<DocMethodLite> BuildFromCommandMetadataRegistry()
            {
                try
                {
                    var metas = CommandMetadataRegistry.GetAll();
                if (metas == null || metas.Count == 0) return new List<DocMethodLite>();

                var tmp = new List<DocMethodLite>(metas.Count * 2);
                foreach (var meta in metas)
                {
                    if (meta == null) continue;
                    if (string.IsNullOrWhiteSpace(meta.name)) continue;

                    var name = meta.name.Trim();
                    var tags = meta.tags ?? new string[0];
                    var summary = meta.summary ?? string.Empty;

                    tmp.Add(new DocMethodLite
                    {
                        Name = name,
                        Summary = summary,
                        Tags = tags
                    });

                    if (meta.aliases != null)
                    {
                        foreach (var a in meta.aliases)
                        {
                            var alias = (a ?? string.Empty).Trim();
                            if (alias.Length == 0) continue;
                            tmp.Add(new DocMethodLite
                            {
                                Name = alias,
                                Summary = summary,
                                Tags = AppendAliasTag(tags)
                            });
                        }
                    }
                }

                // De-dup by method name (prefer first).
                var dict = new Dictionary<string, DocMethodLite>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in tmp)
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.Name)) continue;
                    if (!dict.ContainsKey(m.Name)) dict[m.Name] = m;
                }
                return dict.Values.ToList();
            }
            catch
            {
                return new List<DocMethodLite>();
            }
        }

        private static string[] AppendAliasTag(string[] tags)
        {
            try
            {
                tags = tags ?? new string[0];
                if (tags.Any(t => string.Equals(t, "alias", StringComparison.OrdinalIgnoreCase)))
                    return tags;
                return tags.Concat(new[] { "alias" }).ToArray();
            }
            catch
            {
                return tags ?? new string[0];
            }
        }

        /// <summary>
        /// アドイン内のコマンド（CommandName + Execute(...) を持つ）を列挙してマニフェストを作成。
        /// </summary>
        public static List<DocMethodLite> BuildFromAssembly(Assembly asm)
        {
            var list = new List<DocMethodLite>();
            var types = SafeGetTypes(asm);
            foreach (var t in types)
            {
                if (t.IsAbstract || t.IsInterface) continue;

                var nameProp = t.GetProperty("CommandName", BindingFlags.Instance | BindingFlags.Public);
                if (nameProp == null || nameProp.PropertyType != typeof(string)) continue;

                var exec = t.GetMethod("Execute", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (exec == null) continue; // 署名までは厳密チェックしない

                // 引数なし ctor が無くても、CommandName は static である可能性も小さいので一応 new してみる
                object inst = null;
                try
                {
                    if (t.GetConstructor(Type.EmptyTypes) != null)
                        inst = Activator.CreateInstance(t);
                }
                catch { /* 無視 */ }

                string name = null;
                try
                {
                    var val = inst != null ? nameProp.GetValue(inst, null) : null;
                    name = val as string;
                }
                catch { /* 無視 */ }

                if (string.IsNullOrWhiteSpace(name)) continue;

                var tags = InferTagsFromNamespace(t);
                list.Add(new DocMethodLite
                {
                    Name = name,
                    Summary = "",  // 必要なら属性で付与
                    Tags = tags
                });
            }
            return list;
        }

        public static async Task<bool> PublishAsync(string baseUrl, string source = "RevitAddin")
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();

                // Prefer CommandMetadataRegistry to avoid docs drift and include handler/kind info.
                var metas = CommandMetadataRegistry.GetAll();
                if (metas == null || metas.Count == 0)
                {
                    // Fallback (legacy): only names/tags, no extra capability fields.
                    var cmdsLite = BuildFromAssembly(asm);
                    if (cmdsLite == null || cmdsLite.Count == 0) return false;
                    var legacyManifest = new
                    {
                        Source = source,
                        Commands = cmdsLite.Select(c => new
                        {
                            Name = c.Name,
                            Summary = c.Summary,
                            Tags = c.Tags,
                            ParamsSchema = (object)null, // unknown
                            ResultSchema = (object)null
                        }).ToList()
                    };
                    var legacyJson = JsonConvert.SerializeObject(legacyManifest);
                    using (var http = new HttpClient())
                    {
                        var res = await http.PostAsync(
                            baseUrl.TrimEnd('/') + "/manifest/register",
                            new StringContent(legacyJson, Encoding.UTF8, "application/json")
                        ).ConfigureAwait(false);
                        return res.IsSuccessStatusCode;
                    }
                }

                var manifest = new
                {
                    Source = source,
                    Commands = BuildCapabilityCommandList(metas)
                };

                var json = JsonConvert.SerializeObject(manifest);
                using (var http = new HttpClient())
                {
                    var res = await http.PostAsync(
                        baseUrl.TrimEnd('/') + "/manifest/register",
                        new StringContent(json, Encoding.UTF8, "application/json")
                    ).ConfigureAwait(false);
                    return res.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private static List<object> BuildCapabilityCommandList(IReadOnlyList<RpcCommandMeta> metas)
        {
            var list = new List<object>();
            if (metas == null || metas.Count == 0) return list;

            var asm = Assembly.GetExecutingAssembly();
            var since = GetSinceString(asm);

            foreach (var meta in metas)
            {
                if (meta == null || string.IsNullOrWhiteSpace(meta.name)) continue;

                var canonical = meta.name.Trim();
                if (canonical.Length == 0) continue;

                var tags = meta.tags ?? new string[0];
                var summary = (meta.summary ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(summary))
                    summary = InferSummary(canonical, tags);
                var handler = meta.handlerType ?? string.Empty;

                string transaction = string.Equals(meta.kind, "write", StringComparison.OrdinalIgnoreCase) ? "Write" : "Read";
                var paramsExample = ExtractParamsExample(meta.exampleJsonRpc);
                var resultExample = Newtonsoft.Json.Linq.JObject.FromObject(new { ok = true });

                list.Add(new
                {
                    Name = canonical,
                    Summary = summary,
                    Tags = tags,
                    ParamsSchema = (object)null,
                    ResultSchema = (object)null,
                    ParamsExample = paramsExample,
                    ResultExample = resultExample,
                    RevitHandler = handler,
                    Transaction = transaction,
                    SupportsFamilyKinds = new[] { "Unknown" },
                    Since = since,
                    Deprecated = false
                });

                if (meta.aliases == null) continue;
                foreach (var a in meta.aliases)
                {
                    var alias = (a ?? string.Empty).Trim();
                    if (alias.Length == 0) continue;
                    var aliasTags = AppendAliasTag(tags);
                    // Deprecated alias entries should clearly point to the canonical method,
                    // otherwise agents may treat them as separate valid commands.
                    var aliasSummary = "deprecated alias of " + canonical;
                    list.Add(new
                    {
                        Name = alias,
                        Summary = aliasSummary,
                        Tags = aliasTags,
                        ParamsSchema = (object)null,
                        ResultSchema = (object)null,
                        ParamsExample = paramsExample,
                        ResultExample = resultExample,
                        RevitHandler = handler,
                        Transaction = transaction,
                        SupportsFamilyKinds = new[] { "Unknown" },
                        Since = since,
                        Deprecated = true
                    });
                }
            }

            return list;
        }

        private static string[] InferTagsFromNamespace(Type t)
        {
            // 例: RevitMCPAddin.Commands.ElementOps.Paint.GetFaceRegionsCommand
            // → ["ElementOps","Paint"] をタグに
            var ns = t.Namespace ?? "";
            var parts = ns.Split('.');
            var idx = Array.IndexOf(parts, "Commands");
            if (idx >= 0 && idx + 1 < parts.Length)
            {
                var tags = new List<string>();
                for (int i = idx + 1; i < parts.Length; i++)
                {
                    tags.Add(parts[i]);
                }
                return tags.ToArray();
            }
            return new string[0];
        }

        private static Type[] SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch { return new Type[0]; }
        }

        public sealed class DocMethodLite
        {
            public string Name { get; set; }
            public string Summary { get; set; }
            public string[] Tags { get; set; }
        }
    }
}
