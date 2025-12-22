#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Step 1: Unified result envelope (non-breaking).
    /// Adds consistent fields to the existing payload object that commands already return.
    /// </summary>
    internal static class RpcResultEnvelope
    {
        // Keep string to be stable and easy to detect by agents.
        public const string SchemaVersion = "revitmcp.result.v1";

        public static JObject StandardizePayload(
            object? rawPayload,
            UIApplication? uiapp,
            string? method,
            long revitMs)
        {
            var payload = EnsureJObject(rawPayload);

            // schema marker (additive)
            if (payload["schema"] == null)
                payload["schema"] = SchemaVersion;

            // ok
            var ok = payload.Value<bool?>("ok");
            if (!ok.HasValue)
            {
                // Tolerate alternate key names from older code.
                ok = payload.Value<bool?>("success");
            }
            payload["ok"] = ok ?? true;

            // code (prefer code, fallback errorCode)
            var code = payload.Value<string>("code");
            if (string.IsNullOrWhiteSpace(code))
            {
                var errorCode = payload.Value<string>("errorCode");
                if (!string.IsNullOrWhiteSpace(errorCode)) code = errorCode;
            }
            if (string.IsNullOrWhiteSpace(code))
                code = (payload.Value<bool>("ok") ? "OK" : "ERROR");
            payload["code"] = code;

            // Keep compatibility: if errorCode is absent but code exists, mirror it.
            if (payload["errorCode"] == null && !string.IsNullOrWhiteSpace(code) && !string.Equals(code, "OK", StringComparison.OrdinalIgnoreCase))
                payload["errorCode"] = code;

            // msg (prefer msg, fallback message)
            var msg = payload.Value<string>("msg");
            if (string.IsNullOrWhiteSpace(msg))
            {
                msg = payload.Value<string>("message");
                if (string.IsNullOrWhiteSpace(msg))
                    msg = payload.Value<bool>("ok") ? "OK" : "Failed";
                payload["msg"] = msg;
            }

            // warnings
            if (payload["warnings"] == null)
                payload["warnings"] = new JArray();
            else if (!(payload["warnings"] is JArray))
                payload["warnings"] = new JArray(payload["warnings"]);

            // nextActions
            if (payload["nextActions"] == null)
                payload["nextActions"] = new JArray();
            else if (!(payload["nextActions"] is JArray))
                payload["nextActions"] = new JArray(payload["nextActions"]);

            // timings (server augments queueWaitMs/totalMs later)
            var timings = payload["timings"] as JObject;
            if (timings == null)
            {
                timings = new JObject();
                payload["timings"] = timings;
            }
            if (timings["revitMs"] == null || timings.Value<long?>("revitMs").GetValueOrDefault(0) <= 0)
                timings["revitMs"] = revitMs;
            if (timings["transactionMs"] == null) timings["transactionMs"] = 0;
            if (timings["regenerateMs"] == null) timings["regenerateMs"] = 0;
            if (timings["uiMs"] == null) timings["uiMs"] = 0;
            if (timings["queueWaitMs"] == null) timings["queueWaitMs"] = 0;
            if (timings["totalMs"] == null) timings["totalMs"] = 0;

            // context (additive)
            var ctx = payload["context"] as JObject;
            if (ctx == null)
            {
                ctx = new JObject();
                payload["context"] = ctx;
            }
            if (!string.IsNullOrWhiteSpace(method) && ctx["method"] == null)
                ctx["method"] = method;

            try
            {
                var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
                var doc = uidoc != null ? uidoc.Document : null;
                if (doc != null)
                {
                    if (ctx["docTitle"] == null) ctx["docTitle"] = doc.Title ?? "";
                    if (ctx["docGuid"] == null)
                    {
                        try
                        {
                            var pi = doc.ProjectInformation;
                            if (pi != null) ctx["docGuid"] = pi.UniqueId ?? "";
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    var av = doc.ActiveView;
                    if (av != null)
                    {
                        if (ctx["activeViewId"] == null) ctx["activeViewId"] = av.Id.IntValue();
                        if (ctx["activeViewName"] == null) ctx["activeViewName"] = av.Name ?? "";
                        if (ctx["activeViewType"] == null) ctx["activeViewType"] = av.ViewType.ToString();
                        if (ctx["rawActiveViewType"] == null) ctx["rawActiveViewType"] = av.GetType().Name;
                    }

                    // Step 7: context token (drift detection)
                    try
                    {
                        if (ctx["contextTokenVersion"] == null) ctx["contextTokenVersion"] = ContextTokenService.TokenVersion;
                        if (ctx["contextRevision"] == null) ctx["contextRevision"] = ContextTokenService.GetRevision(doc);
                        if (ctx["contextToken"] == null && uiapp != null)
                            ctx["contextToken"] = ContextTokenService.GetContextToken(uiapp);

                        // Keep help.get_context/get_context output aligned with envelope context token.
                        // (The document can change during/after handler execution e.g., ledger writes,
                        // so we treat the envelope token as the authoritative “post-call” token.)
                        if (!string.IsNullOrWhiteSpace(method)
                            && string.Equals(CommandNaming.Leaf(method), "get_context", StringComparison.OrdinalIgnoreCase)
                            && payload["data"] is JObject dataObj)
                        {
                            if (dataObj["tokenVersion"] == null || string.IsNullOrWhiteSpace(dataObj.Value<string>("tokenVersion")))
                                dataObj["tokenVersion"] = ctx["contextTokenVersion"];
                            dataObj["revision"] = ctx["contextRevision"];
                            dataObj["contextToken"] = ctx["contextToken"];
                        }
                    }
                    catch { /* ignore */ }
                }
            }
            catch
            {
                // never fail standardization
            }

            return payload;
        }

        public static JObject Fail(string code, string msg, object? data = null)
        {
            var jo = new JObject
            {
                ["ok"] = false,
                ["code"] = code ?? "ERROR",
                ["msg"] = msg ?? "Failed"
            };
            if (data != null)
                jo["data"] = ToJTokenSafe(data);
            return jo;
        }

        private static JObject EnsureJObject(object? raw)
        {
            try
            {
                if (raw == null) return new JObject();
                if (raw is JObject jo) return jo;
                if (raw is JToken tok)
                {
                    if (tok is JObject jo2) return jo2;
                    return new JObject { ["result"] = tok };
                }

                var jt = JToken.FromObject(raw);
                if (jt is JObject obj) return obj;
                return new JObject { ["result"] = jt };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["ok"] = false,
                    ["code"] = "SERIALIZE_ERROR",
                    ["msg"] = "Failed to serialize command result.",
                    ["detail"] = ex.Message
                };
            }
        }

        private static JToken ToJTokenSafe(object value)
        {
            try { return value is JToken jt ? jt : JToken.FromObject(value); }
            catch { return JValue.CreateNull(); }
        }
    }
}

