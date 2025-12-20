using Microsoft.AspNetCore.Mvc;
using RevitMcpServer.Engine;
using System.Collections.Concurrent;

namespace RevitMcpServer.Http
{
    [ApiController]
    public sealed class HeartbeatController : ControllerBase
    {
        private readonly DurableQueue _durable;
        private readonly JobIndex _index;
        public HeartbeatController(DurableQueue d, JobIndex idx) { _durable = d; _index = idx; }
        private static readonly ConcurrentDictionary<string, long> _lastBeat = new(StringComparer.OrdinalIgnoreCase);

        [HttpPost("/heartbeat")]
        [HttpGet("/heartbeat")]
        public async Task<IActionResult> Beat([FromQuery] string? jobId = null, [FromQuery] string? rpcId = null)
        {
            string? id = jobId;
            if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(rpcId))
            {
                if (!_index.TryGet(rpcId!, out var mapped) || string.IsNullOrEmpty(mapped))
                {
                    mapped = await _durable.FindRunningJobIdByRpcIdAsync(rpcId!);
                }
                id = mapped;
            }
            if (string.IsNullOrWhiteSpace(id)) return Ok(new { ok = false, code = "NOT_FOUND", msg = "job id not resolved" });

            // Throttle DB writes: only persist heartbeat at most once per ~2 seconds per job
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var last = _lastBeat.GetOrAdd(id!, 0);
            if (nowMs - last >= 2000)
            {
                try { await _durable.HeartbeatAsync(id!); }
                catch { /* best-effort; avoid surfacing transient SQLITE_BUSY */ }
                _lastBeat[id!] = nowMs;
            }
            return Ok(new { ok = true, ts = nowMs });
        }
    }
}
