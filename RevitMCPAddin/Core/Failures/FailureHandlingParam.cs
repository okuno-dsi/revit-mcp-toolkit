#nullable enable
using System;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Failures
{
    internal enum FailureHandlingMode
    {
        Off = 0,
        Rollback = 1,
        Delete = 2,
        Resolve = 3
    }

    internal sealed class FailureHandlingRequest
    {
        public bool ExplicitProvided { get; private set; }
        public FailureHandlingMode Mode { get; private set; } = FailureHandlingMode.Off;
        public string ModeRaw { get; private set; } = string.Empty;

        public bool Enabled
        {
            get { return Mode != FailureHandlingMode.Off; }
        }

        public static FailureHandlingRequest Parse(JObject? p)
        {
            var req = new FailureHandlingRequest();
            if (p == null) return req;

            // Supported keys:
            // - failureHandling: bool | string | object
            // - failure_handling: bool | string | object
            // - failureHandlingMode / failure_handling_mode: string
            JToken? tok = null;

            if (!p.TryGetValue("failureHandling", StringComparison.OrdinalIgnoreCase, out tok))
                p.TryGetValue("failure_handling", StringComparison.OrdinalIgnoreCase, out tok);

            if (tok == null)
            {
                if (!p.TryGetValue("failureHandlingMode", StringComparison.OrdinalIgnoreCase, out tok))
                    p.TryGetValue("failure_handling_mode", StringComparison.OrdinalIgnoreCase, out tok);
            }

            if (tok == null) return req;

            req.ExplicitProvided = true;

            try
            {
                if (tok.Type == JTokenType.Null || tok.Type == JTokenType.Undefined)
                {
                    req.Mode = FailureHandlingMode.Off;
                    req.ModeRaw = "null";
                    return req;
                }

                if (tok.Type == JTokenType.Boolean)
                {
                    var enabled = tok.Value<bool>();
                    req.Mode = enabled ? FailureHandlingMode.Rollback : FailureHandlingMode.Off;
                    req.ModeRaw = enabled ? "rollback" : "off";
                    return req;
                }

                if (tok.Type == JTokenType.String)
                {
                    var s = (tok.Value<string>() ?? string.Empty).Trim();
                    req.ModeRaw = s;
                    req.Mode = ParseMode(s, defaultIfUnknown: FailureHandlingMode.Rollback);
                    return req;
                }

                var obj = tok as JObject;
                if (obj != null)
                {
                    var enabled = obj.Value<bool?>("enabled");
                    var modeStr = (obj.Value<string>("mode") ?? obj.Value<string>("action") ?? obj.Value<string>("strategy") ?? string.Empty).Trim();

                    req.ModeRaw = modeStr;

                    // enabled=false always wins
                    if (enabled.HasValue && enabled.Value == false)
                    {
                        req.Mode = FailureHandlingMode.Off;
                        return req;
                    }

                    // enabled=true or omitted -> infer mode
                    if (string.IsNullOrWhiteSpace(modeStr))
                    {
                        req.Mode = FailureHandlingMode.Rollback;
                        return req;
                    }

                    req.Mode = ParseMode(modeStr, defaultIfUnknown: FailureHandlingMode.Rollback);
                    return req;
                }
            }
            catch
            {
                // Conservative fallback.
                req.Mode = FailureHandlingMode.Rollback;
                return req;
            }

            // Unknown shape -> conservative fallback.
            req.Mode = FailureHandlingMode.Rollback;
            return req;
        }

        private static FailureHandlingMode ParseMode(string mode, FailureHandlingMode defaultIfUnknown)
        {
            var m = (mode ?? string.Empty).Trim().ToLowerInvariant();
            if (m.Length == 0) return defaultIfUnknown;

            if (m == "off" || m == "false" || m == "none" || m == "disabled" || m == "disable")
                return FailureHandlingMode.Off;

            if (m == "rollback" || m == "rollback_on_error" || m == "rollback_on_failure" || m == "rollback_on_any_failure")
                return FailureHandlingMode.Rollback;

            if (m == "delete" || m == "delete_warning" || m == "delete_warnings" || m == "delete_warning_only")
                return FailureHandlingMode.Delete;

            if (m == "resolve" || m == "resolve_if_possible" || m == "auto_resolve")
                return FailureHandlingMode.Resolve;

            return defaultIfUnknown;
        }
    }
}

