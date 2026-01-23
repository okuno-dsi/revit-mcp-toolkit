using System.Collections.Concurrent;

namespace RevitMcpServer.Engine
{
    // In-memory fast index: rpc_id -> job_id (best-effort; cleared on process restart)
    public sealed class JobIndex
    {
        private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
        public void Put(string rpcId, string jobId)
        {
            if (string.IsNullOrWhiteSpace(rpcId) || string.IsNullOrWhiteSpace(jobId)) return;
            _map[rpcId] = jobId;
        }
        public bool TryGet(string rpcId, out string jobId)
        {
            jobId = string.Empty;
            if (string.IsNullOrWhiteSpace(rpcId)) return false;
            if (_map.TryGetValue(rpcId, out var value))
            {
                jobId = value ?? string.Empty;
                return true;
            }
            return false;
        }
        public bool Remove(string rpcId)
        {
            if (string.IsNullOrWhiteSpace(rpcId)) return false;
            return _map.TryRemove(rpcId, out _);
        }
    }
}
