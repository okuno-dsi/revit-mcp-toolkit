// ================================================================
// File: JobQueue.cs  (fixed: Shift-JIS provider)
// Purpose: Execute DWG merge jobs using existing Models.cs types
// ================================================================
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;                  // <-- added
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AutoCadMcpServer.Core
{
    public class JobQueue
    {
        private readonly BlockingCollection<Job> _q = new();
        private readonly List<Task> _workers = new();
        private CancellationTokenSource? _cts;
        private ILogger? _logger;
        private IConfiguration? _config;
        private int _workerCount = 1;

        private static readonly Lazy<JobQueue> _lazy = new(() => new JobQueue());
        public static JobQueue Instance => _lazy.Value;

        public void Init(IConfiguration config, ILogger logger, int workers = 1)
        {
            _config = config;
            _logger = logger;
            _workerCount = Math.Max(1, workers);
            if (_cts != null) return; // already running
            _cts = new CancellationTokenSource();
            for (int i = 0; i < _workerCount; i++)
            {
                _workers.Add(Task.Run(() => Worker(_cts.Token)));
            }
        }

        public void Enqueue(Job job) => _q.Add(job);

        public void Stop()
        {
            try { _q.CompleteAdding(); } catch { }
            try { _cts?.Cancel(); } catch { }
            try { Task.WaitAll(_workers.ToArray(), 3000); } catch { }
        }

        private async Task Worker(CancellationToken ct)
        {
            foreach (var job in _q.GetConsumingEnumerable(ct))
            {
                try
                {
                    await ProcessJob(job, ct);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[JobQueue] job failed: {id}", job.Id);
                    await ResultStore.Instance.InitAsync(_config!);
                    ResultStore.Instance.Put(new ResultStore.JobResult
                    {
                        JobId = job.Id,
                        Done = true,
                        Ok = false,
                        Error = ex.Message,
                        Message = "Job failed",
                        Outputs = new List<object>(),
                        Logs = new Dictionary<string, object>(),
                        Stats = new Dictionary<string, object>()
                    });
                }
            }
        }

        private async Task ProcessJob(Job job, CancellationToken ct)
        {
            _logger?.LogInformation("[JobQueue] start {id} kind={kind}", job.Id, job.Kind);

            var staging = string.IsNullOrWhiteSpace(job.StagingRoot)
                ? Path.Combine(Path.GetTempPath(), "CadJobs", "Staging")
                : job.StagingRoot;

            Directory.CreateDirectory(staging);
            var jobDir = Path.Combine(staging, job.Id);
            Directory.CreateDirectory(jobDir);

            var scriptPath = Path.Combine(jobDir, "run.scr");
            var tempOut = Path.Combine(jobDir, "final.dwg");

            // Build script for per-file rename flow
            var include = "*";
            var exclude = "";
            var prefix = job.LayerStrategy?.Prefix ?? "";
            var suffix = "_{stem}";
            var overkillPasses = (job.Overkill != null && job.Overkill.Enabled)
                                 ? (job.Overkill.RunTwice ? 2 : 1) : 0;

            var script = ScriptBuilder.BuildPerFileRenameScript(
                job.Inputs, include, exclude, prefix, suffix, tempOut,
                job.DoPurge, job.DoAudit, overkillPasses, job.LayTransDws
            );

            // Shift-JIS provider (for .NET 6/8)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // <-- added
            var enc = Encoding.GetEncoding(932);                             // <-- changed
            File.WriteAllText(scriptPath, script, enc);

            var res = AccoreRunner.Run(job.AccorePath, job.SeedPath, scriptPath, job.Locale, job.TimeoutMs);

            if (res.Ok && File.Exists(tempOut))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(job.Output)!);
                var dest = job.Output;
                if (job.AtomicWrite)
                {
                    var tmp = dest + ".tmp_" + job.Id;
                    if (File.Exists(tmp)) File.Delete(tmp);
                    File.Move(tempOut, tmp);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tmp, dest);
                }
                else
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(tempOut, dest);
                }
            }
            else if (!res.Ok && !job.KeepTempOnError)
            {
                TryCleanup(jobDir);
            }

            await ResultStore.Instance.InitAsync(_config!);
            ResultStore.Instance.Put(new ResultStore.JobResult
            {
                JobId = job.Id,
                Done = true,
                Ok = res.Ok,
                Error = res.Ok ? null : (res.Error ?? "E_ACCORE_FAIL"),
                Message = res.Ok ? "OK" : "accore failed",
                Outputs = new List<object> { new { outDwg = job.Output, staging = jobDir } },
                Logs = new Dictionary<string, object> {
                    { "stdoutTail", res.StdoutTail }, { "stderrTail", res.StderrTail }
                },
                Stats = new Dictionary<string, object> { { "exitCode", res.ExitCode } }
            });

            _logger?.LogInformation("[JobQueue] done {id} ok={ok} exit={exit}", job.Id, res.Ok, res.ExitCode);
        }

        private static void TryCleanup(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}