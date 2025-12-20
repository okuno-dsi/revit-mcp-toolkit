#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Compare
{
    public class DeepCompareSettings
    {
        public double NumericEpsilon { get; set; } = 0.0; // 0 = micro差も検出
        public bool StringCaseInsensitive { get; set; } = false;
        public bool StringTrim { get; set; } = false;
        public bool ArrayOrderInsensitive { get; set; } = false;
        public HashSet<string> IncludeFields { get; set; } = null; // null = 全フィールド
        public HashSet<string> IgnoreFields { get; set; } = null;  // null = 無し
        public int MaxDiffs { get; set; } = 1000; // 安全弁
    }

    public static class DeepJsonComparer
    {
        public static bool AreEqual(JToken left, JToken right, DeepCompareSettings s, string path, List<(string path, JToken l, JToken r)> diffs)
        {
            if (s.MaxDiffs <= 0) return true;
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null)
            {
                diffs.Add((path, left, right)); s.MaxDiffs--; return false;
            }

            if (left.Type != right.Type)
            {
                diffs.Add((path, left, right)); s.MaxDiffs--; return false;
            }

            switch (left.Type)
            {
                case JTokenType.Object:
                    return CompareObjects((JObject)left, (JObject)right, s, path, diffs);
                case JTokenType.Array:
                    return CompareArrays((JArray)left, (JArray)right, s, path, diffs);
                case JTokenType.Float:
                case JTokenType.Integer:
                    {
                        double l = left.Value<double>();
                        double r = right.Value<double>();
                        if (Math.Abs(l - r) <= s.NumericEpsilon) return true;
                        diffs.Add((path, left, right)); s.MaxDiffs--; return false;
                    }
                case JTokenType.String:
                    {
                        string l = left.Value<string>() ?? string.Empty;
                        string r = right.Value<string>() ?? string.Empty;
                        if (s.StringTrim) { l = l.Trim(); r = r.Trim(); }
                        if (s.StringCaseInsensitive) { l = l.ToLowerInvariant(); r = r.ToLowerInvariant(); }
                        if (l == r) return true;
                        diffs.Add((path, left, right)); s.MaxDiffs--; return false;
                    }
                default:
                    {
                        // Bool / Null / Date / etc. → ToString 比較
                        var le = left.ToString(Newtonsoft.Json.Formatting.None);
                        var re = right.ToString(Newtonsoft.Json.Formatting.None);
                        if (le == re) return true;
                        diffs.Add((path, left, right)); s.MaxDiffs--; return false;
                    }
            }
        }

        private static bool FieldAllowed(string name, DeepCompareSettings s)
        {
            if (s.IncludeFields != null && !s.IncludeFields.Contains(name)) return false;
            if (s.IgnoreFields != null && s.IgnoreFields.Contains(name)) return false;
            return true;
        }

        private static bool CompareObjects(JObject lo, JObject ro, DeepCompareSettings s, string path, List<(string path, JToken l, JToken r)> diffs)
        {
            bool eq = true;
            var names = new HashSet<string>(lo.Properties().Select(p => p.Name));
            foreach (var n in ro.Properties()) names.Add(n.Name);
            foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!FieldAllowed(name, s)) continue;
                var l = lo.ContainsKey(name) ? lo[name] : null;
                var r = ro.ContainsKey(name) ? ro[name] : null;
                if (!AreEqual(l, r, s, Join(path, name), diffs)) eq = false;
                if (s.MaxDiffs <= 0) return eq;
            }
            return eq;
        }

        private static bool CompareArrays(JArray la, JArray ra, DeepCompareSettings s, string path, List<(string path, JToken l, JToken r)> diffs)
        {
            if (!s.ArrayOrderInsensitive)
            {
                bool eq = true;
                int max = Math.Max(la.Count, ra.Count);
                for (int i = 0; i < max; i++)
                {
                    var l = i < la.Count ? la[i] : null;
                    var r = i < ra.Count ? ra[i] : null;
                    if (!AreEqual(l, r, s, Join(path, i.ToString()), diffs)) eq = false;
                    if (s.MaxDiffs <= 0) return eq;
                }
                return eq;
            }
            else
            {
                // order-insensitive: multiset via JSON string canonicalization
                var lm = la.Select(j => Canon(j, s)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
                var rm = ra.Select(j => Canon(j, s)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
                bool eq = true;
                var keys = new HashSet<string>(lm.Keys);
                foreach (var k in rm.Keys) keys.Add(k);
                foreach (var k in keys)
                {
                    lm.TryGetValue(k, out int lc); rm.TryGetValue(k, out int rc);
                    if (lc != rc)
                    {
                        diffs.Add((Join(path, "[]"), new JValue(lc), new JValue(rc)));
                        s.MaxDiffs--; eq = false;
                        if (s.MaxDiffs <= 0) return eq;
                    }
                }
                return eq;
            }
        }

        private static string Canon(JToken t, DeepCompareSettings s)
        {
            // Canonicalize token for order-insensitive compare (approximate):
            if (t == null) return "null";
            switch (t.Type)
            {
                case JTokenType.Object:
                    var o = (JObject)t;
                    var names = o.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);
                    var sb = new System.Text.StringBuilder();
                    sb.Append('{');
                    bool first = true;
                    foreach (var n in names)
                    {
                        if (!FieldAllowedGlobal(n, s)) continue;
                        if (!first) sb.Append(','); first = false;
                        sb.Append(n).Append(':').Append(Canon(o[n], s));
                    }
                    sb.Append('}'); return sb.ToString();
                case JTokenType.Array:
                    var a = (JArray)t;
                    var parts = a.Select(x => Canon(x, s)).ToList();
                    if (s.ArrayOrderInsensitive) parts.Sort(StringComparer.Ordinal);
                    return "[" + string.Join(",", parts.ToArray()) + "]";
                case JTokenType.String:
                    var str = t.Value<string>() ?? string.Empty;
                    if (s.StringTrim) str = str.Trim();
                    if (s.StringCaseInsensitive) str = str.ToLowerInvariant();
                    return '"' + str + '"';
                case JTokenType.Float:
                case JTokenType.Integer:
                    return t.Value<double>().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                default:
                    return t.ToString(Newtonsoft.Json.Formatting.None);
            }
        }

        private static bool FieldAllowedGlobal(string name, DeepCompareSettings s)
        {
            if (s.IncludeFields != null && !s.IncludeFields.Contains(name)) return false;
            if (s.IgnoreFields != null && s.IgnoreFields.Contains(name)) return false;
            return true;
        }

        private static string Join(string prefix, string seg)
        {
            if (string.IsNullOrEmpty(prefix)) return seg;
            return prefix + "." + seg;
        }
    }
}
