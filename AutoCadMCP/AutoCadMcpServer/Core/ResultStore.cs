using System.Collections.Concurrent;

namespace AutoCadMcpServer.Core
{
    public class ResultStore
    {
        public class JobResult
        {
            public string JobId { get; set; } = string.Empty;
            public bool Done { get; set; }
            public bool Ok { get; set; }
            public string? Error { get; set; }
            public string? Message { get; set; }
            public List<object> Outputs { get; set; } = new();
            public Dictionary<string, object> Logs { get; set; } = new();
            public Dictionary<string, object> Stats { get; set; } = new();
        }

        private readonly ConcurrentDictionary<string, JobResult> _memory = new();
        private static readonly Lazy<ResultStore> _lazy = new(() => new ResultStore());
        public static ResultStore Instance => _lazy.Value;

        private string _logsRoot = string.Empty;

        public async Task InitAsync(IConfiguration config)
        {
            _logsRoot = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(_logsRoot);
            await Task.CompletedTask;
        }

        public void Put(JobResult res)
        {
            _memory[res.JobId] = res;
            // Optional: also persist minimal JSON per job
            try
            {
                var path = Path.Combine(_logsRoot, res.JobId + ".result.json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(res));
            }
            catch { }
        }

        public bool TryGet(string jobId, out JobResult? res) => _memory.TryGetValue(jobId, out res);
    }
}

