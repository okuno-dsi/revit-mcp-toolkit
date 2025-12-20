// ================================================================
// File: Core/SpfCatalog.cs
// Purpose: Parse Revit Shared Parameter File (SPF) text to enrich
//          parameter discovery with SPF group and metadata.
// Notes:   This is a tolerant, best-effort parser that supports the
//          common *GROUP / PARAM sections. It prefers tab-separated
//          tokens but falls back to whitespace splitting. Unknown
//          formats are ignored gracefully.
// ================================================================
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    public sealed class SpfCatalog
    {
        public sealed class SpfParam
        {
            public string Guid { get; set; }
            public int? GroupId { get; set; }
            public string GroupName { get; set; }
            public string DataType { get; set; }
            public Dictionary<string, string> Extra { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<int, string> _groupById = new Dictionary<int, string>();
        private readonly Dictionary<string, SpfParam> _byGuid = new Dictionary<string, SpfParam>(StringComparer.OrdinalIgnoreCase);

        public static bool TryLoad(JToken spfConfig, out SpfCatalog catalog, out string error)
        {
            catalog = null; error = null;
            try
            {
                if (spfConfig == null) return false;
                string mode = (spfConfig.Value<string>("mode") ?? "").Trim().ToLowerInvariant();
                string path = spfConfig.Value<string>("path");
                string content = spfConfig.Value<string>("content");
                string encoding = spfConfig.Value<string>("encoding") ?? "utf-8";

                string text = null;
                if (mode == "path" && !string.IsNullOrWhiteSpace(path))
                {
                    try
                    {
                        var enc = GetEncoding(encoding);
                        text = File.ReadAllText(path, enc);
                    }
                    catch (Exception ex) { error = "SPF read failed: " + ex.Message; return false; }
                }
                else if (mode == "inline" && !string.IsNullOrWhiteSpace(content))
                {
                    text = content;
                }
                else
                {
                    return false;
                }

                var cat = new SpfCatalog();
                cat.Parse(text);
                catalog = cat;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static Encoding GetEncoding(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return Encoding.UTF8;
                if (name.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || name.Equals("utf8", StringComparison.OrdinalIgnoreCase))
                    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                return Encoding.GetEncoding(name);
            }
            catch { return Encoding.UTF8; }
        }

        private static readonly Regex GuidRx = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

        private void Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var lines = SplitLines(text);
            foreach (var raw in lines)
            {
                var line = (raw ?? string.Empty).Trim();
                if (line.Length == 0) continue;

                var tokens = SplitTokens(line);
                if (tokens.Length == 0) continue;

                // *GROUP <id> <name>
                if (tokens[0].Equals("*GROUP", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("GROUP", StringComparison.OrdinalIgnoreCase))
                {
                    if (tokens.Length >= 3)
                    {
                        if (int.TryParse(tokens[1], out var gid))
                        {
                            var gname = string.Join(" ", tokens.Skip(2).ToArray());
                            if (!_groupById.ContainsKey(gid)) _groupById[gid] = gname;
                        }
                    }
                    continue;
                }

                // PARAM ... try to pull GUID and group id
                if (tokens[0].Equals("*PARAM", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("PARAM", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var m = GuidRx.Match(line);
                        if (!m.Success) continue;
                        var guid = m.Value;
                        int? gid = null;

                        // simple heuristic: last integer token is often group id
                        for (int i = tokens.Length - 1; i >= 0; i--)
                        {
                            if (int.TryParse(tokens[i], out var g)) { gid = g; break; }
                        }

                        string dataType = null;
                        // heuristic: uppercase type keywords present in tokens
                        foreach (var t in tokens)
                        {
                            var up = t.ToUpperInvariant();
                            if (up == "LENGTH" || up == "AREA" || up == "VOLUME" || up == "ANGLE" || up == "TEXT" || up == "INTEGER" || up == "NUMBER")
                            { dataType = up; break; }
                        }

                        var sp = new SpfParam { Guid = guid, GroupId = gid, DataType = dataType };
                        if (gid.HasValue && _groupById.TryGetValue(gid.Value, out var gname)) sp.GroupName = gname;
                        _byGuid[guid] = sp;
                    }
                    catch { /* ignore this line */ }
                }
            }
        }

        private static string[] SplitTokens(string line)
        {
            if (line.IndexOf('\t') >= 0) return line.Split('\t');
            return line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            using (var sr = new StringReader(text))
            {
                string s; while ((s = sr.ReadLine()) != null) yield return s;
            }
        }

        public bool TryGetByGuid(string guid, out SpfParam meta)
        {
            meta = null; if (string.IsNullOrWhiteSpace(guid)) return false;
            return _byGuid.TryGetValue(guid, out meta);
        }
    }
}

