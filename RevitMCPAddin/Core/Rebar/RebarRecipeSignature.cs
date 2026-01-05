#nullable enable
// ================================================================
// File   : Core/Rebar/RebarRecipeSignature.cs
// Target : .NET Framework 4.8 / C# 8.0
// Purpose: Canonical JSON + SHA-256 signature for rebar "recipes".
// Notes  :
//  - Canonicalization sorts JObject properties recursively.
//  - JArray order is preserved (arrays are assumed to be ordered inputs).
// ================================================================
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Rebar
{
    internal static class RebarRecipeSignature
    {
        public static string Sha256FromJToken(JToken token)
        {
            if (token == null) return string.Empty;
            try
            {
                var normalized = SortJToken(token);
                var json = JsonConvert.SerializeObject(normalized, Formatting.None);
                using (var sha = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var hash = sha.ComputeHash(bytes);
                    return ToHex(hash);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string Sha256FromObject(object obj)
        {
            if (obj == null) return string.Empty;
            try
            {
                var tok = JToken.FromObject(obj);
                return Sha256FromJToken(tok);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        internal static JToken SortJToken(JToken token)
        {
            if (token == null) return JValue.CreateNull();

            if (token is JObject obj)
            {
                var ordered = new JObject();
                foreach (var p in obj.Properties())
                {
                    // First pass collects; we'll sort by name below.
                    ordered[p.Name] = SortJToken(p.Value);
                }

                var sorted = new JObject();
                foreach (var name in ordered.Properties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal))
                {
                    sorted[name] = ordered[name];
                }
                return sorted;
            }

            if (token is JArray arr)
            {
                var a = new JArray();
                foreach (var t in arr)
                    a.Add(SortJToken(t));
                return a;
            }

            // primitives
            return token.DeepClone();
        }
    }
}
