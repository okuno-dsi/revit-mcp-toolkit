#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Failures
{
    internal sealed class FailureWhitelistLoadStatus
    {
        public bool ok { get; set; }
        public string? code { get; set; }
        public string? msg { get; set; }
        public string? path { get; set; }
        public string? schema { get; set; }
        public string? version { get; set; } // sha8
    }

    internal sealed class FailureWhitelistRule
    {
        public string Id { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string FailureDefinitionRef { get; set; } = string.Empty;
        public FailureDefinitionId FailureId { get; set; }
        public Guid FailureGuid { get; set; }
        public bool MatchWarning { get; set; }
        public bool MatchError { get; set; }
        public bool AllowDeleteWarning { get; set; }
        public bool AllowResolve { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    internal sealed class FailureWhitelistIndex
    {
        public string Path { get; private set; } = string.Empty;
        public string Schema { get; private set; } = string.Empty;
        public string VersionShort { get; private set; } = string.Empty;

        public bool ForcedModalHandling { get; private set; }
        public bool LoggingEnabled { get; private set; }
        public string LoggingLevel { get; private set; } = "info";
        public bool IncludeDescriptionText { get; private set; } = true;
        public bool IncludeElements { get; private set; } = true;
        public int MaxElementIdsPerFailure { get; private set; } = 50;

        private readonly Dictionary<Guid, FailureWhitelistRule> _rules = new Dictionary<Guid, FailureWhitelistRule>();

        public bool TryGetRule(Guid guid, out FailureWhitelistRule rule)
        {
            return _rules.TryGetValue(guid, out rule!);
        }

        public static FailureWhitelistIndex Build(JObject root, string path, string sha8, out FailureWhitelistLoadStatus status)
        {
            var idx = new FailureWhitelistIndex();
            idx.Path = path ?? string.Empty;
            idx.Schema = (root.Value<string>("schema") ?? string.Empty).Trim();
            idx.VersionShort = (sha8 ?? string.Empty).Length >= 8 ? sha8.Substring(0, 8) : (sha8 ?? string.Empty);

            // policy
            try
            {
                var pol = root["policy"] as JObject;
                if (pol != null)
                {
                    idx.ForcedModalHandling = pol.Value<bool?>("forcedModalHandling") ?? false;
                }
            }
            catch { /* ignore */ }

            // logging
            try
            {
                var log = root["logging"] as JObject;
                if (log != null)
                {
                    idx.LoggingEnabled = log.Value<bool?>("enabled") ?? false;
                    idx.LoggingLevel = (log.Value<string>("level") ?? "info").Trim();
                    idx.IncludeDescriptionText = log.Value<bool?>("includeDescriptionText") ?? true;
                    idx.IncludeElements = log.Value<bool?>("includeElements") ?? true;
                    idx.MaxElementIdsPerFailure = log.Value<int?>("maxElementIdsPerFailure") ?? 50;
                    if (idx.MaxElementIdsPerFailure < 0) idx.MaxElementIdsPerFailure = 0;
                }
            }
            catch { /* ignore */ }

            // rules
            int loaded = 0;
            var rulesArr = root["rules"] as JArray;
            if (rulesArr != null)
            {
                foreach (var t in rulesArr.OfType<JObject>())
                {
                    bool enabled = t.Value<bool?>("enabled") ?? false;
                    if (!enabled) continue;

                    var matchObj = t["match"] as JObject;
                    var failRef = (matchObj != null ? (matchObj.Value<string>("failureDefinitionRef") ?? string.Empty) : string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(failRef)) continue;

                    if (!TryResolveFailureDefinitionId(failRef, out var fid, out var _))
                        continue;

                    var rule = new FailureWhitelistRule
                    {
                        Id = (t.Value<string>("id") ?? string.Empty).Trim(),
                        Enabled = true,
                        FailureDefinitionRef = failRef,
                        FailureId = fid,
                        FailureGuid = fid.Guid,
                        Notes = (t.Value<string>("notes") ?? string.Empty).Trim()
                    };

                    // severity
                    bool matchWarning = false;
                    bool matchError = false;
                    var sevArr = t["severity"] as JArray;
                    if (sevArr != null && sevArr.Count > 0)
                    {
                        foreach (var sTok in sevArr)
                        {
                            if (sTok == null || sTok.Type != JTokenType.String) continue;
                            var s = (sTok.Value<string>() ?? string.Empty).Trim();
                            if (s.Length == 0) continue;
                            if (string.Equals(s, "Warning", StringComparison.OrdinalIgnoreCase)) matchWarning = true;
                            if (string.Equals(s, "Error", StringComparison.OrdinalIgnoreCase)) matchError = true;
                        }
                    }
                    else
                    {
                        // If omitted, treat as both.
                        matchWarning = true;
                        matchError = true;
                    }
                    rule.MatchWarning = matchWarning;
                    rule.MatchError = matchError;

                    // actions
                    bool allowDeleteWarning = false;
                    bool allowResolve = false;
                    var actionsArr = t["actions"] as JArray;
                    if (actionsArr != null)
                    {
                        foreach (var aTok in actionsArr.OfType<JObject>())
                        {
                            var type = (aTok.Value<string>("type") ?? string.Empty).Trim().ToLowerInvariant();
                            if (type == "delete_warning" || type == "delete_warnings") allowDeleteWarning = true;
                            if (type == "resolve_if_possible" || type == "resolve") allowResolve = true;
                        }
                    }
                    rule.AllowDeleteWarning = allowDeleteWarning;
                    rule.AllowResolve = allowResolve;

                    idx._rules[rule.FailureGuid] = rule;
                    loaded++;
                }
            }

            status = new FailureWhitelistLoadStatus
            {
                ok = true,
                code = "OK",
                msg = $"Failure whitelist loaded ({loaded} enabled rules).",
                path = idx.Path,
                schema = idx.Schema,
                version = idx.VersionShort
            };
            return idx;
        }

        private static bool TryResolveFailureDefinitionId(string failureDefinitionRef, out FailureDefinitionId id, out string error)
        {
            id = null!;
            error = string.Empty;

            try
            {
                var s = (failureDefinitionRef ?? string.Empty).Trim();
                if (s.Length == 0)
                {
                    error = "Empty failureDefinitionRef.";
                    return false;
                }

                const string Prefix = "Autodesk.Revit.DB.BuiltInFailures.";
                if (!s.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    error = "Only Autodesk.Revit.DB.BuiltInFailures.* is supported in this version.";
                    return false;
                }

                var tail = s.Substring(Prefix.Length);
                var parts = tail.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    error = "Invalid BuiltInFailures reference (expected NestedType.Member).";
                    return false;
                }

                Type type = typeof(BuiltInFailures);
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var nestedName = parts[i];
                    var nt = type.GetNestedType(nestedName, BindingFlags.Public | BindingFlags.NonPublic);
                    if (nt == null)
                    {
                        error = $"Nested type not found: {type.FullName}+{nestedName}";
                        return false;
                    }
                    type = nt;
                }

                var member = parts[parts.Length - 1];
                if (string.IsNullOrWhiteSpace(member))
                {
                    error = "Missing member name in BuiltInFailures reference.";
                    return false;
                }

                // Property (most common)
                var prop = type.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop != null && prop.PropertyType == typeof(FailureDefinitionId))
                {
                    var v = prop.GetValue(null, null);
                    if (v is FailureDefinitionId fid)
                    {
                        id = fid;
                        return true;
                    }
                }

                // Field (fallback)
                var field = type.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null && field.FieldType == typeof(FailureDefinitionId))
                {
                    var v = field.GetValue(null);
                    if (v is FailureDefinitionId fid)
                    {
                        id = fid;
                        return true;
                    }
                }

                error = $"Member not found or not a FailureDefinitionId: {type.FullName}.{member}";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    internal static class FailureWhitelistService
    {
        private const string EnvWhitelistPath = "REVITMCP_FAILURE_WHITELIST_PATH";
        private const string DefaultFileName = "failure_whitelist.json";

        private static readonly object _gate = new object();
        private static bool _loaded;
        private static FailureWhitelistIndex? _index;
        private static FailureWhitelistLoadStatus _status = new FailureWhitelistLoadStatus { ok = false, code = "NOT_LOADED", msg = "Failure whitelist not loaded." };

        public static FailureWhitelistLoadStatus GetStatus()
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                return new FailureWhitelistLoadStatus
                {
                    ok = _status.ok,
                    code = _status.code,
                    msg = _status.msg,
                    path = _status.path,
                    schema = _status.schema,
                    version = _status.version
                };
            }
        }

        public static bool TryGetIndex(out FailureWhitelistIndex index)
        {
            EnsureLoadedBestEffort();
            lock (_gate)
            {
                index = _index!;
                return _loaded && _index != null;
            }
        }

        private static void EnsureLoadedBestEffort()
        {
            lock (_gate)
            {
                if (_loaded) return;
                try
                {
                    var path = ResolveWhitelistPath();
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        _status = new FailureWhitelistLoadStatus
                        {
                            ok = false,
                            code = "FAILURE_WHITELIST_NOT_FOUND",
                            msg = "failure_whitelist.json not found. Place it in %LOCALAPPDATA%\\RevitMCP, %USERPROFILE%\\Documents\\Codex\\Design, or the add-in folder.",
                            path = path
                        };
                        _loaded = false;
                        _index = null;
                        return;
                    }

                    var bytes = File.ReadAllBytes(path);
                    var json = Encoding.UTF8.GetString(bytes);
                    if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);

                    var root = JObject.Parse(json);
                    var sha8 = Sha256Hex(bytes);
                    _index = FailureWhitelistIndex.Build(root, path, sha8, out _status);
                    _loaded = true;
                }
                catch (Exception ex)
                {
                    _loaded = false;
                    _index = null;
                    _status = new FailureWhitelistLoadStatus
                    {
                        ok = false,
                        code = "FAILURE_WHITELIST_LOAD_FAILED",
                        msg = ex.Message
                    };
                }
            }
        }

        private static string? ResolveWhitelistPath()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable(EnvWhitelistPath);
                if (!string.IsNullOrWhiteSpace(env) && File.Exists(env!)) return env;
            }
            catch { /* ignore */ }

            try
            {
                var p1 = System.IO.Path.Combine(RevitMCPAddin.Core.Paths.LocalRoot, DefaultFileName);
                if (File.Exists(p1)) return p1;
            }
            catch { /* ignore */ }

            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    var p2 = System.IO.Path.Combine(docs, "Codex", "Design", DefaultFileName);
                    if (File.Exists(p2)) return p2;
                }
            }
            catch { /* ignore */ }

            try
            {
                var p3 = System.IO.Path.Combine(RevitMCPAddin.Core.Paths.AddinFolder, "Resources", DefaultFileName);
                if (File.Exists(p3)) return p3;
            }
            catch { /* ignore */ }

            try
            {
                var p4 = System.IO.Path.Combine(RevitMCPAddin.Core.Paths.AddinFolder, DefaultFileName);
                if (File.Exists(p4)) return p4;
            }
            catch { /* ignore */ }

            // Dev convenience: walk up and look for "<ancestor>\\Codex\\Design\\failure_whitelist.json"
            try
            {
                var cur = RevitMCPAddin.Core.Paths.AddinFolder;
                for (int i = 0; i < 8; i++)
                {
                    var parent = System.IO.Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(parent)) break;
                    var cand = System.IO.Path.Combine(parent, "Codex", "Design", DefaultFileName);
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
