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
            jobId = null;
            if (string.IsNullOrWhiteSpace(rpcId)) return false;
            return _map.TryGetValue(rpcId, out jobId);
        }
        public bool Remove(string rpcId)
        {
            if (string.IsNullOrWhiteSpace(rpcId)) return false;
            return _map.TryRemove(rpcId, out _);
        }
    }
}

