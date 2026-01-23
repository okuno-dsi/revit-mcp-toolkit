#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Minimal JSON-RPC client for server-local chat methods (chat.*) hosted by RevitMCPServer.
    /// - Uses /rpc/{method}
    /// - Returns the "result" token (or an {ok:false,...} object on errors)
    /// </summary>
    internal static class ChatRpcClient
    {
        private static readonly object _lock = new object();
        private static HttpClient? _client;
        private static int _clientPort;

        private static HttpClient GetClient()
        {
            int port = AppServices.CurrentPort;
            if (port <= 0) port = 5210;
            lock (_lock)
            {
                if (_client != null && _clientPort == port) return _client;

                try { _client?.Dispose(); } catch { }
                _clientPort = port;
                _client = new HttpClient
                {
                    BaseAddress = new Uri("http://127.0.0.1:" + port.ToString() + "/"),
                    Timeout = TimeSpan.FromSeconds(10)
                };
                return _client;
            }
        }

        public static async Task<JToken> CallAsync(string method, JObject? @params)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(method))
                    return new JObject { ["ok"] = false, ["code"] = "INVALID_METHOD", ["msg"] = "method is required" };

                var client = GetClient();
                var reqId = "chatui:" + Guid.NewGuid().ToString("N");

                // IMPORTANT:
                // Revit host environments can load an older Newtonsoft.Json where some JToken APIs are missing.
                // Avoid JsonConvert.SerializeObject(JToken) paths entirely to prevent MissingMethodException.
                object payloadParams;
                try
                {
                    payloadParams = (@params != null) ? ToPlainObject(@params) : new Dictionary<string, object?>();
                }
                catch
                {
                    payloadParams = new Dictionary<string, object?>();
                }

                var body = new Dictionary<string, object?>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = reqId,
                    ["params"] = payloadParams
                };

                var json = SerializeToJson(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                using (var resp = await client.PostAsync("rpc/" + Uri.EscapeDataString(method), content).ConfigureAwait(false))
                {
                    var txt = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(txt))
                        return new JObject { ["ok"] = false, ["code"] = "EMPTY_RESPONSE", ["msg"] = "Empty response body." };

                    JObject? jo = null;
                    try { jo = JObject.Parse(txt); }
                    catch
                    {
                        return new JObject
                        {
                            ["ok"] = false,
                            ["code"] = "INVALID_JSON",
                            ["msg"] = "Response is not JSON.",
                            ["raw"] = txt
                        };
                    }

                    // JSON-RPC envelope
                    var err = jo["error"];
                    if (err != null)
                    {
                        return new JObject
                        {
                            ["ok"] = false,
                            ["code"] = "RPC_ERROR",
                            ["msg"] = (string?)err["message"] ?? "RPC error",
                            ["error"] = err
                        };
                    }

                    var result = jo["result"];
                    if (result != null) return result;
                    return jo;
                }
            }
            catch (Exception ex)
            {
                return new JObject { ["ok"] = false, ["code"] = "CHAT_RPC_FAIL", ["msg"] = ex.Message };
            }
        }

        // NOTE:
        // Revit host AppDomain can be polluted by other add-ins' dependency versions.
        // Avoid System.Text.Json here to prevent TypeLoadException / MissingMethodException
        // from version mismatches (e.g., Utf8JsonWriter.DisposeAsync issues).
        private static string SerializeToJson(object? value)
        {
            var sb = new StringBuilder(4096);
            WriteJsonValue(sb, value);
            return sb.ToString();
        }

        private static void WriteJsonValue(StringBuilder sb, object? value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                WriteJsonString(sb, s);
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is DateTime dt)
            {
                WriteJsonString(sb, dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                return;
            }

            if (value is DateTimeOffset dto)
            {
                WriteJsonString(sb, dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                return;
            }

            if (value is Guid g)
            {
                WriteJsonString(sb, g.ToString("D"));
                return;
            }

            if (value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0");
                return;
            }

            if (value is float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) { sb.Append("null"); return; }
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is decimal dec)
            {
                sb.Append(dec.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is IDictionary<string, object?> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (kv.Key == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    WriteJsonString(sb, kv.Key);
                    sb.Append(':');
                    WriteJsonValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }

            if (value is System.Collections.IEnumerable seq && !(value is string))
            {
                sb.Append('[');
                bool first = true;
                foreach (var it in seq)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteJsonValue(sb, it);
                }
                sb.Append(']');
                return;
            }

            // Fallback
            WriteJsonString(sb, value.ToString() ?? string.Empty);
        }

        private static void WriteJsonString(StringBuilder sb, string s)
        {
            sb.Append('\"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('\"');
        }

        private static object? ToPlainObject(JToken token)
        {
            if (token == null) return null;

            var jv = token as JValue;
            if (jv != null) return jv.Value;

            var jo = token as JObject;
            if (jo != null)
            {
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var prop in jo.Properties())
                {
                    var name = prop != null ? (prop.Name ?? string.Empty) : string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;
                    dict[name] = prop != null ? ToPlainObject(prop.Value) : null;
                }
                return dict;
            }

            var ja = token as JArray;
            if (ja != null)
            {
                var list = new List<object?>(ja.Count);
                foreach (var it in ja)
                    list.Add(ToPlainObject(it));
                return list;
            }

            // Other token kinds are not expected for chat params; omit safely.
            return null;
        }
    }
}
