#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitMcpServer.Capture
{
    /// <summary>
    /// Server-local screenshot capture methods (capture.*) executed via external CaptureAgent.
    /// These do NOT touch Revit API and should work even while Revit is busy.
    /// </summary>
    public sealed class CaptureService
    {
        public bool IsCaptureMethod(string method)
        {
            if (string.IsNullOrWhiteSpace(method)) return false;
            return method.StartsWith("capture.", StringComparison.OrdinalIgnoreCase);
        }

        public async System.Threading.Tasks.Task<object> ExecuteAsync(string method, JsonElement? param)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(method))
                    return new { ok = false, code = "INVALID_METHOD", msg = "method is required" };

                var m = method.Trim().ToLowerInvariant();
                string? exeErr;
                var exePath = CaptureAgentRunner.TryFindExePath(out exeErr);
                if (string.IsNullOrWhiteSpace(exePath))
                    return new { ok = false, code = "CAPTURE_AGENT_NOT_FOUND", msg = exeErr ?? "CaptureAgent not found." };

                var args = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                if (m == "capture.list_windows")
                {
                    var processName = GetString(param, "processName") ?? GetString(param, "process") ?? null;
                    var titleContains = GetString(param, "titleContains") ?? GetString(param, "title") ?? null;
                    var visibleOnly = GetBool(param, "visibleOnly") ?? true;

                    if (!string.IsNullOrWhiteSpace(processName)) args["processName"] = processName;
                    if (!string.IsNullOrWhiteSpace(titleContains)) args["titleContains"] = titleContains;
                    args["visibleOnly"] = visibleOnly ? "true" : "false";

                    var node = await CaptureAgentRunner.RunAsync(exePath!, "list_windows", args).ConfigureAwait(false);
                    return node;
                }

                if (m == "capture.window")
                {
                    var hwnd = GetString(param, "hwnd") ?? GetString(param, "hWnd") ?? null;
                    if (string.IsNullOrWhiteSpace(hwnd))
                    {
                        var n = GetLong(param, "hwnd");
                        if (n.HasValue) hwnd = n.Value.ToString();
                    }

                    if (string.IsNullOrWhiteSpace(hwnd))
                        return new { ok = false, code = "INVALID_PARAMS", msg = "params.hwnd is required." };

                    args["hwnd"] = hwnd;

                    var outDir = GetString(param, "outDir");
                    if (!string.IsNullOrWhiteSpace(outDir)) args["outDir"] = outDir;

                    var prefer = GetBool(param, "preferPrintWindow");
                    if (prefer.HasValue) args["preferPrintWindow"] = prefer.Value ? "true" : "false";

                    var sha = GetBool(param, "includeSha256");
                    if (sha.HasValue) args["includeSha256"] = sha.Value ? "true" : "false";

                    var node = await CaptureAgentRunner.RunAsync(exePath!, "capture_window", args).ConfigureAwait(false);
                    return node;
                }

                if (m == "capture.screen")
                {
                    var outDir = GetString(param, "outDir");
                    if (!string.IsNullOrWhiteSpace(outDir)) args["outDir"] = outDir;

                    var idx = GetInt(param, "monitorIndex");
                    if (idx.HasValue) args["monitorIndex"] = idx.Value.ToString();

                    var sha = GetBool(param, "includeSha256");
                    if (sha.HasValue) args["includeSha256"] = sha.Value ? "true" : "false";

                    var node = await CaptureAgentRunner.RunAsync(exePath!, "capture_screen", args).ConfigureAwait(false);
                    return node;
                }

                if (m == "capture.revit")
                {
                    var target = GetString(param, "target") ?? "active_dialogs";
                    if (string.IsNullOrWhiteSpace(target)) target = "active_dialogs";

                    args["target"] = target;

                    var outDir = GetString(param, "outDir");
                    if (!string.IsNullOrWhiteSpace(outDir)) args["outDir"] = outDir;

                    var prefer = GetBool(param, "preferPrintWindow");
                    if (prefer.HasValue) args["preferPrintWindow"] = prefer.Value ? "true" : "false";

                    var sha = GetBool(param, "includeSha256");
                    if (sha.HasValue) args["includeSha256"] = sha.Value ? "true" : "false";

                    var node = await CaptureAgentRunner.RunAsync(exePath!, "capture_revit", args).ConfigureAwait(false);
                    return node;
                }

                return new { ok = false, code = "UNKNOWN_CAPTURE_METHOD", msg = "Unknown capture method: " + method };
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "CAPTURE_EXEC_FAIL", msg = ex.Message };
            }
        }

        private static bool TryGetProperty(JsonElement? p, string key, out JsonElement value)
        {
            value = default;
            if (!p.HasValue) return false;
            if (p.Value.ValueKind != JsonValueKind.Object) return false;
            if (p.Value.TryGetProperty(key, out value)) return true;
            foreach (var prop in p.Value.EnumerateObject())
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
            return false;
        }

        private static string? GetString(JsonElement? p, string key)
        {
            if (!TryGetProperty(p, key, out var v)) return null;
            try
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString();
                if (v.ValueKind == JsonValueKind.Number) return v.ToString();
                return v.ToString();
            }
            catch { return null; }
        }

        private static bool? GetBool(JsonElement? p, string key)
        {
            if (!TryGetProperty(p, key, out var v)) return null;
            try
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i != 0;
                if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
            }
            catch { }
            return null;
        }

        private static int? GetInt(JsonElement? p, string key)
        {
            if (!TryGetProperty(p, key, out var v)) return null;
            try
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j)) return j;
            }
            catch { }
            return null;
        }

        private static long? GetLong(JsonElement? p, string key)
        {
            if (!TryGetProperty(p, key, out var v)) return null;
            try
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i)) return i;
                if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var j)) return j;
            }
            catch { }
            return null;
        }
    }
}

