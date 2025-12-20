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

namespace RevitMCPAddin.Manifest
{
    public static class ManifestExporter
    {
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
                var cmds = BuildFromAssembly(asm);
                if (cmds.Count == 0) return false;

                var manifest = new
                {
                    Source = source,
                    Commands = cmds.Select(c => new
                    {
                        Name = c.Name,
                        Summary = c.Summary,
                        Tags = c.Tags,
                        ParamsSchema = (object)null, // 今は unknown（object）で登録
                        ResultSchema = (object)null
                    }).ToList()
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
