using System.Text.Json.Nodes;
using AutoCadMcpServer.Router;
using AutoCadMcpServer.Core;

namespace AutoCadMcpServer.Router.Methods
{
    public static class MergeDwgsHandler
    {
        public static async Task<object> Handle(JsonObject p, ILogger logger, IConfiguration config)
        {
            var inputs = p["inputs"] as JsonArray ?? throw new RpcError(400, "E_NO_INPUT: inputs missing");
            var inputPaths = inputs.Select(x => x?.GetValue<string>() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (inputPaths.Count == 0) throw new RpcError(400, "E_NO_INPUT: no inputs");

            var output = p["output"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(output)) throw new RpcError(400, "E_NO_OUTPUT: output missing");

            foreach (var f in inputPaths) PathGuard.EnsureAllowedDwg(f, config);
            PathGuard.EnsureAllowedOutput(output!, config);

            var accore = p["accore"] as JsonObject ?? new JsonObject();
            var accorePath = accore["path"]?.GetValue<string>() ?? config["Accore:Path"] ?? "";
            var locale = accore["locale"]?.GetValue<string>() ?? config["Accore:Locale"] ?? "ja-JP";
            var seed = accore["seed"]?.GetValue<string>() ?? config["Accore:Seed"] ?? "";
            var timeoutMs = accore["timeoutMs"]?.GetValue<int?>() ?? (int.TryParse(config["Accore:TimeoutMs"], out var tmo) ? tmo : 180000);
            if (timeoutMs > 180000) timeoutMs = 180000;

            var layerStrategy = p["layerStrategy"] as JsonObject;
            var ls = LayerStrategy.From(layerStrategy);

            var post = p["postProcess"] as JsonObject;
            var layTransDws = post?["layTransDws"]?.GetValue<string>();
            var doPurge = post?["purge"]?.GetValue<bool?>() ?? true;
            var doAudit = post?["audit"]?.GetValue<bool?>() ?? true;
            var overkill = OverkillOptions.From(post?["overkill"] as JsonObject);
            var textDedup = TextDedupOptions.From(post?["textDedup"] as JsonObject);

            var stagingRoot = p["stagingPolicy"]?["root"]?.GetValue<string>() ?? config["Staging:Root"] ?? Path.Combine(Path.GetTempPath(), "CadJobs", "Staging");
            var keepTempOnError = p["stagingPolicy"]?["keepTempOnError"]?.GetValue<bool?>() ?? (bool.TryParse(config["Staging:KeepOnError"], out var ko) && ko);
            var atomicWrite = p["stagingPolicy"]?["atomicWrite"]?.GetValue<bool?>() ?? (bool.TryParse(config["Staging:AtomicWrite"], out var aw) && aw);

            var job = Job.CreateMergeJob(inputPaths, output!, accorePath, seed, locale, timeoutMs, ls, layTransDws, doPurge, doAudit, overkill, textDedup, stagingRoot, keepTempOnError, atomicWrite);
            await ResultStore.Instance.InitAsync(config);
            JobQueue.Instance.Init(config, logger);
            JobQueue.Instance.Enqueue(job);

            return new { ok = true, done = false, jobId = job.Id, msg = "Accepted." };
        }
    }
}
