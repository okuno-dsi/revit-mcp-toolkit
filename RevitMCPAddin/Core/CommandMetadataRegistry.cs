#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RevitMCPAddin.Core
{
    public sealed class RpcCommandMeta
    {
        public string name { get; set; } = string.Empty;
        public string[] aliases { get; set; } = Array.Empty<string>();
        public string category { get; set; } = "Other";
        public string[] tags { get; set; } = Array.Empty<string>();
        public string kind { get; set; } = "read";               // read|write
        public string importance { get; set; } = "normal";       // low|normal|high
        public string risk { get; set; } = "low";                // low|medium|high
        public string summary { get; set; } = string.Empty;
        public string exampleJsonRpc { get; set; } = string.Empty;
        public string[] requires { get; set; } = Array.Empty<string>();
        public string[] constraints { get; set; } = Array.Empty<string>();
        public string handlerType { get; set; } = string.Empty;
        public string handlerNamespace { get; set; } = string.Empty;
    }

    /// <summary>
    /// Builds a command metadata registry from the actual registered handlers.
    /// This avoids "docs drift" and enables fast command discovery for agents.
    /// </summary>
    public static class CommandMetadataRegistry
    {
        private static readonly object _gate = new object();
        private static Dictionary<string, RpcCommandMeta>? _byMethod; // method/alias -> meta
        private static List<RpcCommandMeta>? _all;                    // canonical list (method -> meta)

        public static void InitializeFromHandlers(IEnumerable<IRevitCommandHandler> handlers)
        {
            if (handlers == null) return;
            lock (_gate)
            {
                if (_byMethod != null && _all != null) return;
                BuildFromHandlersLocked(handlers);
            }
        }

        public static bool IsInitialized
        {
            get { lock (_gate) { return _byMethod != null && _all != null; } }
        }

        public static IReadOnlyList<RpcCommandMeta> GetAll()
        {
            EnsureInitializedBestEffort();
            lock (_gate)
            {
                return _all != null ? (IReadOnlyList<RpcCommandMeta>)_all.ToList() : Array.Empty<RpcCommandMeta>();
            }
        }

        public static bool TryGet(string method, out RpcCommandMeta meta)
        {
            meta = null!;
            if (string.IsNullOrWhiteSpace(method)) return false;
            EnsureInitializedBestEffort();
            lock (_gate)
            {
                if (_byMethod == null) return false;
                return _byMethod.TryGetValue(method.Trim(), out meta!);
            }
        }

        // ------------------------- internal -------------------------

        private static void EnsureInitializedBestEffort()
        {
            lock (_gate)
            {
                if (_byMethod != null && _all != null) return;
                // Best-effort fallback: reflect handlers with default ctor (may be incomplete)
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var iType = typeof(IRevitCommandHandler);
                    var handlers = new List<IRevitCommandHandler>();
                    foreach (var t in SafeGetTypes(asm))
                    {
                        if (t.IsAbstract || t.IsInterface) continue;
                        if (!iType.IsAssignableFrom(t)) continue;
                        if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                        try
                        {
                            var inst = Activator.CreateInstance(t) as IRevitCommandHandler;
                            if (inst != null) handlers.Add(inst);
                        }
                        catch { /* ignore */ }
                    }
                    BuildFromHandlersLocked(handlers);
                }
                catch
                {
                    _byMethod = new Dictionary<string, RpcCommandMeta>(StringComparer.OrdinalIgnoreCase);
                    _all = new List<RpcCommandMeta>();
                }
            }
        }

        private static Type[] SafeGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); } catch { return Array.Empty<Type>(); }
        }

        private static void BuildFromHandlersLocked(IEnumerable<IRevitCommandHandler> handlers)
        {
            var lookup = new Dictionary<string, RpcCommandMeta>(StringComparer.OrdinalIgnoreCase);
            var canonical = new Dictionary<string, RpcCommandMeta>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in handlers)
            {
                if (h == null) continue;
                var type = h.GetType();
                var attr = type.GetCustomAttribute<RpcCommandAttribute>(inherit: true);

                // Dispatch methods (what handlers actually expect in cmd.Command)
                var dispatchMethods = new List<string>();
                try
                {
                    var raw = h.CommandName ?? string.Empty;
                    dispatchMethods.AddRange(
                        raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(x => (x ?? string.Empty).Trim())
                           .Where(x => !string.IsNullOrWhiteSpace(x))
                    );
                }
                catch { /* ignore */ }

                // Additional names from attribute (aliases/canonical override)
                var extraNames = new List<string>();
                if (attr != null)
                {
                    if (!string.IsNullOrWhiteSpace(attr.Name))
                        extraNames.Add(attr.Name.Trim());
                    if (attr.Aliases != null)
                    {
                        foreach (var a in attr.Aliases)
                        {
                            var aa = (a ?? string.Empty).Trim();
                            if (!string.IsNullOrWhiteSpace(aa))
                                extraNames.Add(aa);
                        }
                    }
                }

                dispatchMethods = dispatchMethods.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                extraNames = extraNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                // Fallback: if handler has no explicit CommandName, use attribute Name as dispatch (rare).
                if (dispatchMethods.Count == 0)
                {
                    if (attr != null && !string.IsNullOrWhiteSpace(attr.Name))
                        dispatchMethods.Add(attr.Name.Trim());
                    else
                        continue;
                }

                var tags = (attr != null && attr.Tags != null && attr.Tags.Length > 0)
                    ? attr.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                    : InferTagsFromNamespace(type);

                foreach (var dispatch in dispatchMethods)
                {
                    // Step 4: prefer domain-first canonical name, while keeping legacy dispatch name callable.
                    var canonicalName = CommandNaming.GetCanonical(dispatch, type);

                    // If attribute.Name is a domain-first name and this handler has a single dispatch method,
                    // treat it as an explicit canonical override (optional).
                    if (attr != null && dispatchMethods.Count == 1 && !string.IsNullOrWhiteSpace(attr.Name))
                    {
                        var an = attr.Name.Trim();
                        if (CommandNaming.IsCanonicalLike(an) && !string.Equals(an, dispatch, StringComparison.OrdinalIgnoreCase))
                            canonicalName = an;
                    }
                    if (string.IsNullOrWhiteSpace(canonicalName))
                        canonicalName = dispatch;

                    var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.Equals(dispatch, canonicalName, StringComparison.OrdinalIgnoreCase))
                        aliasSet.Add(dispatch);

                    // Include extraNames if they refer to the same canonical command.
                    foreach (var n in extraNames)
                    {
                        var nn = (n ?? string.Empty).Trim();
                        if (nn.Length == 0) continue;
                        if (string.Equals(nn, canonicalName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(nn, dispatch, StringComparison.OrdinalIgnoreCase))
                        {
                            aliasSet.Add(nn);
                            continue;
                        }
                        var nCanon = CommandNaming.GetCanonical(nn, type);
                        if (string.Equals(nCanon, canonicalName, StringComparison.OrdinalIgnoreCase))
                            aliasSet.Add(nn);
                    }

                    RpcCommandMeta meta;
                    if (canonical.TryGetValue(canonicalName, out var existing))
                    {
                        // Merge aliases into existing meta (keep first meta as source of truth).
                        var merged = (existing.aliases ?? Array.Empty<string>())
                            .Concat(aliasSet)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        existing.aliases = merged;
                        meta = existing;
                    }
                    else
                    {
                        var aliases = aliasSet
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        meta = BuildMeta(canonicalName, aliases, tags, type, attr);
                        canonical[canonicalName] = meta;
                    }

                    // Lookup by canonical + aliases
                    lookup[canonicalName] = meta;
                    foreach (var a in meta.aliases ?? Array.Empty<string>())
                    {
                        if (string.IsNullOrWhiteSpace(a)) continue;
                        if (!lookup.ContainsKey(a)) lookup[a] = meta;
                    }
                }
            }

            _byMethod = lookup;
            _all = canonical.Values
                .OrderBy(m => m.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static RpcCommandMeta BuildMeta(string method, string[] aliases, string[] tags, Type handlerType, RpcCommandAttribute? attr)
        {
            var kind = !string.IsNullOrWhiteSpace(attr?.Kind) ? NormalizeKind(attr!.Kind) : InferKind(method);
            var importance = !string.IsNullOrWhiteSpace(attr?.Importance) ? NormalizeImportance(attr!.Importance) : InferImportance(method, kind);
            var category = !string.IsNullOrWhiteSpace(attr?.Category) ? attr!.Category : InferCategory(tags);
            var risk = InferRiskString(attr, kind, importance);
            var summary = (attr?.Summary ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(summary))
                summary = InferSummary(method, tags);

            var m = new RpcCommandMeta
            {
                name = method,
                aliases = aliases ?? Array.Empty<string>(),
                tags = tags ?? Array.Empty<string>(),
                category = category ?? "Other",
                kind = kind,
                importance = importance,
                risk = risk,
                summary = summary,
                exampleJsonRpc = attr?.ExampleJsonRpc ?? string.Empty,
                requires = attr?.Requires ?? Array.Empty<string>(),
                constraints = attr?.Constraints ?? Array.Empty<string>(),
                handlerType = handlerType.FullName ?? handlerType.Name,
                handlerNamespace = handlerType.Namespace ?? string.Empty
            };

            // Ensure there is always at least a minimal example payload (useful for agents).
            if (string.IsNullOrWhiteSpace(m.exampleJsonRpc))
            {
                m.exampleJsonRpc =
                    "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"" + method + "\", \"params\":{} }";
            }
            return m;
        }

        private static string InferSummary(string method, string[] tags)
        {
            try
            {
                var m = (method ?? string.Empty).Trim();
                if (m.Length == 0) return "Command";

                // Example: "doc.get_project_info" -> "doc get project info"
                var s = m.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Replace('/', ' ');
                s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
                if (s.Length == 0) s = m;

                if (tags != null && tags.Length > 0)
                {
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

        private static string[] InferTagsFromNamespace(Type t)
        {
            try
            {
                // Example: RevitMCPAddin.Commands.ElementOps.Wall.CreateWallCommand
                // -> ["ElementOps","Wall"]
                var ns = t.Namespace ?? string.Empty;
                var parts = ns.Split('.');
                var idx = Array.IndexOf(parts, "Commands");
                if (idx >= 0 && idx + 1 < parts.Length)
                {
                    var tags = new List<string>();
                    for (int i = idx + 1; i < parts.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(parts[i]))
                            tags.Add(parts[i]);
                    }
                    return tags.ToArray();
                }
            }
            catch { /* ignore */ }
            return Array.Empty<string>();
        }

        private static string InferCategory(string[] tags)
        {
            if (tags == null || tags.Length == 0) return "Other";
            // Keep it short: "A/B" if deep namespace
            if (tags.Length == 1) return tags[0];
            return tags[0] + "/" + tags[1];
        }

        private static string NormalizeKind(string s)
        {
            var v = (s ?? string.Empty).Trim().ToLowerInvariant();
            return v == "write" ? "write" : "read";
        }

        private static string NormalizeImportance(string s)
        {
            var v = (s ?? string.Empty).Trim().ToLowerInvariant();
            if (v == "high") return "high";
            if (v == "low") return "low";
            return "normal";
        }

        private static string InferKind(string method)
        {
            var leaf = CommandNaming.Leaf(method);
            var m = (leaf ?? string.Empty).Trim().ToLowerInvariant();
            if (m == "status" || m == "revit_status" || m == "revitstatus" || m.EndsWith("_status"))
                return "read";
            // Canonical domain-first verbs sometimes collapse to a leaf without an underscore prefix.
            // Example: sheet.list -> leaf "list"
            if (m == "list" || m == "get" || m == "find" || m == "search" || m == "describe" || m == "ping")
                return "read";
            if (m.StartsWith("get_") || m.StartsWith("list_") || m.StartsWith("find_") || m.StartsWith("search_") ||
                m.StartsWith("describe_") || m.StartsWith("audit_") || m.StartsWith("validate_") || m.StartsWith("diff_") ||
                m.StartsWith("snapshot_") || m.StartsWith("ping_"))
                return "read";
            return "write";
        }

        private static string InferImportance(string method, string kind)
        {
            if (!string.Equals(kind, "write", StringComparison.OrdinalIgnoreCase)) return "normal";
            var leaf = CommandNaming.Leaf(method);
            var m = (leaf ?? string.Empty).Trim().ToLowerInvariant();
            if (m.StartsWith("delete_") || m.StartsWith("remove_") || m.StartsWith("reset_") || m.StartsWith("clear_") || m.StartsWith("purge_"))
                return "high";
            return "normal";
        }

        private static string InferRiskString(RpcCommandAttribute? attr, string kind, string importance)
        {
            if (attr != null)
            {
                switch (attr.Risk)
                {
                    case RiskLevel.High: return "high";
                    case RiskLevel.Medium: return "medium";
                    default: return "low";
                }
            }

            if (!string.Equals(kind, "write", StringComparison.OrdinalIgnoreCase)) return "low";
            if (string.Equals(importance, "high", StringComparison.OrdinalIgnoreCase)) return "high";
            return "medium";
        }
    }
}
