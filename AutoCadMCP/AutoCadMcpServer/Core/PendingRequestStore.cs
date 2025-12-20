using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoCadMcpServer.Router;

namespace AutoCadMcpServer.Core
{
    public class PendingRequestStore
    {
        public class PendingJob
        {
            public string Id { get; set; } = string.Empty;
            public string Method { get; set; } = string.Empty;
            public JsonObject Params { get; set; } = new();
            public string Body { get; set; } = string.Empty; // canonical JSON-RPC request body
            public bool Claimed { get; set; }
            public string? ClaimedBy { get; set; }
            public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.Now;
            public DateTimeOffset? ClaimedAt { get; set; }
            public object? Result { get; set; }
        }

        private readonly object _lock = new();
        private readonly List<PendingJob> _jobs = new();

        private static readonly Lazy<PendingRequestStore> _lazy = new(() => new PendingRequestStore());
        public static PendingRequestStore Instance => _lazy.Value;

        public string Enqueue(JsonRpcReq req, string? rawBody = null)
        {
            var idStr = req.id?.ToString();
            if (string.IsNullOrWhiteSpace(idStr)) idStr = Guid.NewGuid().ToString("N");

            // Normalize to a canonical JSON body for the add-in to consume
            var body = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = idStr,
                ["method"] = req.method,
                ["params"] = req.@params ?? new JsonObject()
            };
            var bodyStr = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = false });

            var job = new PendingJob
            {
                Id = idStr!,
                Method = req.method,
                Params = req.@params ?? new JsonObject(),
                Body = bodyStr,
                EnqueuedAt = DateTimeOffset.Now
            };

            lock (_lock) { _jobs.Add(job); }
            return idStr!;
        }

        public PendingJob? TryClaim(string? agent, string acceptMethod)
        {
            lock (_lock)
            {
                foreach (var j in _jobs)
                {
                    if (!j.Claimed && string.Equals(j.Method, acceptMethod, StringComparison.OrdinalIgnoreCase))
                    {
                        j.Claimed = true;
                        j.ClaimedBy = agent;
                        j.ClaimedAt = DateTimeOffset.Now;
                        return j;
                    }
                }
            }
            return null;
        }

        public void PostResult(string id, object result)
        {
            PendingJob? job = null;
            lock (_lock)
            {
                job = _jobs.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.OrdinalIgnoreCase));
                if (job != null) job.Result = result;
            }

            // Also persist a minimal result entry for visibility
            try
            {
                ResultStore.Instance.Put(new ResultStore.JobResult
                {
                    JobId = id,
                    Done = true,
                    Ok = (result as JsonObject)?["ok"]?.GetValue<bool?>() ?? true,
                    Message = "GUI job posted result",
                    Logs = new Dictionary<string, object>(),
                    Stats = new Dictionary<string, object>()
                });
            }
            catch { }
        }

        public (bool Found, object? Result) GetResult(string id)
        {
            PendingJob? job;
            lock (_lock)
            {
                job = _jobs.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.OrdinalIgnoreCase));
                if (job?.Result != null) return (true, job.Result);
            }
            if (ResultStore.Instance.TryGet(id, out var res) && res != null)
                return (true, res);
            return (false, null);
        }
    }
}

