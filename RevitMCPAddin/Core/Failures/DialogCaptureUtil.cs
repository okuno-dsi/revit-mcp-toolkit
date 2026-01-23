// ================================================================
// File: Core/Failures/DialogCaptureUtil.cs
// Purpose:
//   Best-effort capture of active Revit dialogs via CaptureAgent.
//   Used to attach dialog screenshots/OCR to command diagnostics.
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core.Failures
{
    internal sealed class DialogCaptureItem
    {
        public string path { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
        public string risk { get; set; } = string.Empty;
        public int w { get; set; }
        public int h { get; set; }
        public string ocrText { get; set; } = string.Empty;
        public string ocrEngine { get; set; } = string.Empty;
        public string ocrStatus { get; set; } = string.Empty;
    }

    internal sealed class DialogCaptureResult
    {
        public bool ok { get; set; }
        public string error { get; set; } = string.Empty;
        public List<DialogCaptureItem> captures { get; set; } = new List<DialogCaptureItem>();
    }

    internal static class DialogCaptureUtil
    {
        private const int DefaultTimeoutMs = 1500;

        public static DialogCaptureResult TryCaptureActiveDialogs(int timeoutMs = DefaultTimeoutMs)
        {
            var res = new DialogCaptureResult();
            try
            {
                string exePath = FindCaptureAgentExe();
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    res.ok = false;
                    res.error = "CAPTURE_AGENT_NOT_FOUND";
                    return res;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Arguments = "capture_revit --target active_dialogs --ocr true"
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    res.ok = false;
                    res.error = "CAPTURE_AGENT_START_FAIL";
                    return res;
                }

                string stdout = string.Empty;
                string stderr = string.Empty;
                try
                {
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { /* ignore */ }
                        res.ok = false;
                        res.error = "CAPTURE_AGENT_TIMEOUT";
                        return res;
                    }
                    stdout = p.StandardOutput.ReadToEnd();
                    stderr = p.StandardError.ReadToEnd();
                }
                catch
                {
                    res.ok = false;
                    res.error = "CAPTURE_AGENT_IO_FAIL";
                    return res;
                }

                if (p.ExitCode != 0)
                {
                    res.ok = false;
                    res.error = string.IsNullOrWhiteSpace(stderr) ? "CAPTURE_AGENT_EXIT_NONZERO" : stderr.Trim();
                    return res;
                }

                if (string.IsNullOrWhiteSpace(stdout))
                {
                    res.ok = false;
                    res.error = "CAPTURE_AGENT_EMPTY";
                    return res;
                }

                var jo = JObject.Parse(stdout);
                if (jo.Value<bool?>("ok") != true)
                {
                    res.ok = false;
                    res.error = jo.Value<string>("msg") ?? "CAPTURE_AGENT_NOT_OK";
                    return res;
                }

                var arr = jo["captures"] as JArray;
                if (arr != null)
                {
                    foreach (var item in arr.OfType<JObject>())
                    {
                        var cap = new DialogCaptureItem();
                        cap.path = item.Value<string>("path") ?? string.Empty;
                        cap.title = item.Value<string>("title") ?? string.Empty;
                        cap.risk = item.Value<string>("risk") ?? string.Empty;
                        var bounds = item["bounds"] as JObject;
                        if (bounds != null)
                        {
                            cap.w = bounds.Value<int?>("w") ?? 0;
                            cap.h = bounds.Value<int?>("h") ?? 0;
                        }
                        var ocr = item["ocr"] as JObject;
                        if (ocr != null)
                        {
                            cap.ocrText = ocr.Value<string>("text") ?? string.Empty;
                            cap.ocrEngine = ocr.Value<string>("engine") ?? string.Empty;
                            cap.ocrStatus = ocr.Value<string>("status") ?? string.Empty;
                        }
                        res.captures.Add(cap);
                    }
                }

                res.ok = true;
                return res;
            }
            catch (Exception ex)
            {
                res.ok = false;
                res.error = ex.Message;
                return res;
            }
        }

        public static DialogCaptureItem? PickPrimaryCapture(DialogCaptureResult res)
        {
            try
            {
                if (res == null || res.captures == null || res.captures.Count == 0) return null;
                return res.captures
                    .OrderByDescending(c => (long)c.w * (long)c.h)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string FindCaptureAgentExe()
        {
            try
            {
                var env = (Environment.GetEnvironmentVariable("REVIT_MCP_CAPTURE_AGENT_EXE") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
            }
            catch { /* ignore */ }

            string baseDir = string.Empty;
            try
            {
                baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            }
            catch { baseDir = ""; }

            var candidates = new[]
            {
                Path.Combine(baseDir, "server", "capture-agent", "RevitMcp.CaptureAgent.exe"),
                Path.Combine(baseDir, "server", "CaptureAgent", "RevitMcp.CaptureAgent.exe"),
                Path.Combine(baseDir, "server", "RevitMcp.CaptureAgent.exe")
            };

            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                if (!File.Exists(c)) continue;
                var dll = Path.ChangeExtension(c, ".dll");
                if (File.Exists(dll)) return c;
            }

            return string.Empty;
        }
    }
}
