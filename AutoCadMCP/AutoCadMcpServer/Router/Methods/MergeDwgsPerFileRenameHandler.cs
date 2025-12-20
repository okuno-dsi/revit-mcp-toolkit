// ================================================================
// File: MergeDwgsPerFileRenameHandler.cs  (fixed: Shift-JIS provider)
// Purpose: JSON-RPC handler to enqueue/execute "per-file layer rename" DWG merge.
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;                 // <-- added
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AutoCadMcpServer.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AutoCadMcpServer.Router;

namespace AutoCadMcpServer.Router.Methods
{
    public static class MergeDwgsPerFileRenameHandler
    {
        /// <summary>
        /// Params (JSON):
        /// {
        ///   "inputs": [ "C:/in/A.dwg", ... ],
        ///   "output": "C:/out/final.dwg",
        ///   "layerStrategy": { "mode": "prefix", "prefix": "" }, // optional
        ///   "include": "*",   // wildcard for layers to be renamed (default "*")
        ///   "exclude": "",    // wildcard for layers to be kept as-is
        ///   "suffix": "_{stem}",  // new name: prefix + old + suffix (stem = source file name without ext)
        ///   "overkill": { "enabled": true, "runTwice": true },
        ///   "doPurge": true, "doAudit": true,
        ///   "layTransDws": "C:/standards/my.dws", // optional
        ///   "timeoutMs": 600000
        /// }
        /// </summary>
        public static async Task<object> Handle(JsonObject p, ILogger logger, IConfiguration config)
        {
            var inputs = (p["inputs"] as JsonArray)?.Select(j => j?.GetValue<string>() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                         ?? throw new RpcError(400, "E_NO_INPUT: inputs missing");
            if (inputs.Count == 0) throw new RpcError(400, "E_NO_INPUT: no inputs");

            var output = p["output"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(output)) throw new RpcError(400, "E_NO_OUTPUT: output missing");

            // Path validations
            foreach (var ip in inputs) PathGuard.EnsureAllowedDwg(ip, config);
            PathGuard.EnsureAllowedOutput(output!, config);

            var accorePath = p["accorePath"]?.GetValue<string>() ?? (config["Accore:Path"] ?? "");
            if (string.IsNullOrWhiteSpace(accorePath)) throw new RpcError(400, "E_NO_ACCORE: accoreconsole.exe path missing");
            if (!File.Exists(accorePath)) throw new RpcError(400, "E_ACCORE_NOT_FOUND: " + accorePath);

            var seed = p["seed"]?.GetValue<string>() ?? (config["Accore:SeedDwg"] ?? "");
            if (string.IsNullOrWhiteSpace(seed) || !File.Exists(seed)) throw new RpcError(400, "E_NO_SEED: seed DWG missing");

            var locale = p["locale"]?.GetValue<string>() ?? (config["Accore:Locale"] ?? "ja-JP");
            var timeoutMs = p["timeoutMs"]?.GetValue<int?>() ?? 600000;

            var include = p["include"]?.GetValue<string>() ?? "*";
            var exclude = p["exclude"]?.GetValue<string>() ?? "";
            var suffix = p["suffix"]?.GetValue<string>() ?? "_{stem}";
            var prefix = (p["layerStrategy"] as JsonObject)?["prefix"]?.GetValue<string>() ?? "";

            var overkill = (p["overkill"] as JsonObject);
            var doPurge = p["doPurge"]?.GetValue<bool?>() ?? true;
            var doAudit = p["doAudit"]?.GetValue<bool?>() ?? true;
            var runTwice = overkill?["runTwice"]?.GetValue<bool?>() ?? false;
            var overkillPasses = (overkill?["enabled"]?.GetValue<bool?>() ?? false) ? (runTwice ? 2 : 1) : 0;

            var dws = p["layTransDws"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(dws) && !File.Exists(dws))
                throw new RpcError(400, "E_DWS_NOT_FOUND: " + dws);

            var stagingRoot = (p["stagingPolicy"] as JsonObject)?["root"]?.GetValue<string>() ??
                              (config["Staging:Root"] ?? Path.Combine(Path.GetTempPath(), "CadJobs", "Staging"));
            Directory.CreateDirectory(stagingRoot);
            var jobDir = Path.Combine(stagingRoot, DateTimeOffset.Now.ToString("yyyyMMdd_HHmmssfff"));
            Directory.CreateDirectory(jobDir);

            var scriptPath = Path.Combine(jobDir, "run.scr");
            var tempOut = Path.Combine(jobDir, "final.dwg"); // always same name inside staging
            var script = ScriptBuilder.BuildPerFileRenameScript(
                inputs, include, exclude, prefix, suffix, tempOut, doPurge, doAudit, overkillPasses, dws
            );
            // write Shift-JIS for Japanese environments
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // <-- added
            var enc = Encoding.GetEncoding(932);                             // <-- changed
            File.WriteAllText(scriptPath, script, enc);

            logger.LogInformation("[MergePerFileRename] script written: {script}", scriptPath);

            var res = AccoreRunner.Run(accorePath, seed, scriptPath, locale, timeoutMs);
            var hasTempOut = File.Exists(tempOut);
            var finalOk = res.Ok && hasTempOut;

            logger.LogInformation("[MergePerFileRename] accore exit={code}, ok={ok}, tempOutExists={temp}",
                res.ExitCode, res.Ok, hasTempOut);

            // finalize: move to output (only when tempOut actually exists)
            if (finalOk)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output!)!);
                if (File.Exists(output!)) File.Delete(output!);
                File.Move(tempOut, output!);
            }
            else if (res.Ok && !hasTempOut)
            {
                logger.LogWarning("[MergePerFileRename] accore reported ok but tempOut missing: {tempOut}", tempOut);
            }

            await ResultStore.Instance.InitAsync(config);
            ResultStore.Instance.Put(new ResultStore.JobResult
            {
                JobId = Guid.NewGuid().ToString("N"),
                Done = true,
                Ok = finalOk,
                Error = finalOk ? null : (res.Error ?? (hasTempOut ? "E_ACCORE_FAIL" : "E_NO_OUTPUT_DWG")),
                Message = finalOk ? "OK" : (hasTempOut ? "accore failed" : "no output DWG created"),
                Outputs = new List<object> { new { outDwg = output, staging = jobDir } },
                Logs = new Dictionary<string, object> {
                    { "stdoutTail", res.StdoutTail }, { "stderrTail", res.StderrTail }
                },
                Stats = new Dictionary<string, object>
                {
                    { "exitCode", res.ExitCode },
                    { "tempOutExists", hasTempOut }
                }
            });

            return new { ok = finalOk, done = true, outDwg = output, exitCode = res.ExitCode };
        }
    }

}
