#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Rebar
{
    internal sealed class RebarBarClearanceTableStatus
    {
        public bool ok { get; set; }
        public string? code { get; set; }
        public string? msg { get; set; }
        public string? path { get; set; }
        public string? sha8 { get; set; }
        public int? version { get; set; }
        public string? units_length { get; set; }
        public int count { get; set; }
    }

    internal static class RebarBarClearanceTableService
    {
        private const string EnvPath = "REVITMCP_REBAR_CLEARANCE_TABLE_PATH";
        private const string DefaultFileName = "RebarBarClearanceTable.json";

        private static readonly object _gate = new object();
        private static bool _loaded;
        private static Dictionary<int, double> _byDiaMm = new Dictionary<int, double>();
        private static RebarBarClearanceTableStatus _status = new RebarBarClearanceTableStatus
        {
            ok = false,
            code = "NOT_LOADED",
            msg = "Rebar clearance table not loaded.",
            count = 0
        };

        public static RebarBarClearanceTableStatus GetStatus()
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                return new RebarBarClearanceTableStatus
                {
                    ok = _status.ok,
                    code = _status.code,
                    msg = _status.msg,
                    path = _status.path,
                    sha8 = _status.sha8,
                    version = _status.version,
                    units_length = _status.units_length,
                    count = _status.count
                };
            }
        }

        public static bool TryGetCenterToCenterMm(int diameterMm, out double centerToCenterMm)
        {
            centerToCenterMm = 0.0;
            if (diameterMm <= 0) return false;
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                if (!_loaded) return false;
                if (_byDiaMm.TryGetValue(diameterMm, out var v) && v > 0.0)
                {
                    centerToCenterMm = v;
                    return true;
                }
            }
            return false;
        }

        private static void EnsureLoadedBestEffort()
        {
            lock (_gate)
            {
                if (_loaded) return;

                try
                {
                    var path = ResolvePath();
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        _status = new RebarBarClearanceTableStatus
                        {
                            ok = false,
                            code = "NOT_FOUND",
                            msg = "RebarBarClearanceTable.json not found. Place it in the add-in folder (recommended), %LOCALAPPDATA%\\RevitMCP (override/cache), or %USERPROFILE%\\Documents\\Codex\\Design (dev).",
                            path = path,
                            count = 0
                        };
                        _loaded = false;
                        _byDiaMm = new Dictionary<int, double>();
                        return;
                    }

                    var bytes = File.ReadAllBytes(path);
                    var json = Encoding.UTF8.GetString(bytes);
                    if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);

                    var root = JObject.Parse(json);
                    var dict = new Dictionary<int, double>();

                    int version = 0;
                    try { version = root.Value<int?>("version") ?? 0; } catch { version = 0; }

                    string unitsLen = "mm";
                    try
                    {
                        var u = root["units"] as JObject;
                        if (u != null) unitsLen = (u.Value<string>("length") ?? "mm").Trim();
                    }
                    catch { /* ignore */ }
                    if (string.IsNullOrWhiteSpace(unitsLen)) unitsLen = "mm";

                    void TryAdd(string key, JToken? tok)
                    {
                        if (tok == null) return;
                        if (!(tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float)) return;
                        var s = (key ?? string.Empty).Trim();
                        if (s.Length == 0) return;

                        // Accept "25" or "D25"
                        int dia = 0;
                        if (int.TryParse(s, out var d1)) dia = d1;
                        else
                        {
                            if (s.StartsWith("D", StringComparison.OrdinalIgnoreCase))
                            {
                                var tail = s.Substring(1);
                                if (int.TryParse(tail, out var d2)) dia = d2;
                            }
                        }

                        if (dia <= 0) return;
                        double v = tok.Value<double>();
                        if (v <= 0.0) return;
                        dict[dia] = v;
                    }

                    var byDia = root["byDiameterMm"] as JObject;
                    if (byDia != null)
                    {
                        foreach (var kv in byDia)
                            TryAdd(kv.Key, kv.Value);
                    }

                    var aliases = root["aliases"] as JObject;
                    if (aliases != null)
                    {
                        foreach (var kv in aliases)
                            TryAdd(kv.Key, kv.Value);
                    }

                    if (dict.Count == 0)
                    {
                        _status = new RebarBarClearanceTableStatus
                        {
                            ok = false,
                            code = "INVALID",
                            msg = "RebarBarClearanceTable.json parsed but contained no usable entries.",
                            path = path,
                            version = version,
                            units_length = unitsLen,
                            count = 0
                        };
                        _loaded = false;
                        _byDiaMm = new Dictionary<int, double>();
                        return;
                    }

                    var sha = Sha256Hex(bytes);
                    _byDiaMm = dict;
                    _loaded = true;
                    _status = new RebarBarClearanceTableStatus
                    {
                        ok = true,
                        code = "OK",
                        msg = $"Rebar clearance table loaded ({dict.Count} entries).",
                        path = path,
                        sha8 = sha.Length >= 8 ? sha.Substring(0, 8) : sha,
                        version = version,
                        units_length = unitsLen,
                        count = dict.Count
                    };
                }
                catch (Exception ex)
                {
                    _loaded = false;
                    _byDiaMm = new Dictionary<int, double>();
                    _status = new RebarBarClearanceTableStatus
                    {
                        ok = false,
                        code = "LOAD_FAILED",
                        msg = ex.Message,
                        count = 0
                    };
                }
            }
        }

        private static string? ResolvePath()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable(EnvPath);
                if (!string.IsNullOrWhiteSpace(env) && File.Exists(env!)) return env;
            }
            catch { /* ignore */ }

            try
            {
                var p0 = Path.Combine(RevitMCPAddin.Core.Paths.AddinFolder, "Resources", DefaultFileName);
                if (File.Exists(p0)) return p0;
            }
            catch { /* ignore */ }

            try
            {
                var p0b = Path.Combine(RevitMCPAddin.Core.Paths.AddinFolder, DefaultFileName);
                if (File.Exists(p0b)) return p0b;
            }
            catch { /* ignore */ }

            try
            {
                var p1 = Path.Combine(RevitMCPAddin.Core.Paths.LocalRoot, DefaultFileName);
                if (File.Exists(p1)) return p1;
            }
            catch { /* ignore */ }

            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    var p2 = Path.Combine(docs, "Codex", "Design", DefaultFileName);
                    if (File.Exists(p2)) return p2;
                }
            }
            catch { /* ignore */ }

            // Dev convenience: walk up and look for "<ancestor>\\Codex\\RevitMCPAddin\\RebarBarClearanceTable.json"
            try
            {
                var cur = RevitMCPAddin.Core.Paths.AddinFolder;
                for (int i = 0; i < 8; i++)
                {
                    var parent = Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(parent)) break;
                    var cand = Path.Combine(parent, "Codex", "RevitMCPAddin", DefaultFileName);
                    if (File.Exists(cand)) return cand;
                    cur = parent;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
