using Microsoft.AspNetCore.Mvc;
using RevitMcpServer.Engine;
using System;

namespace RevitMcpServer.Http
{
    [ApiController]
    public sealed class JobQueryController : ControllerBase
    {
        private readonly DurableQueue _durable;
        public JobQueryController(DurableQueue durable) { _durable = durable; }

        [HttpGet("/job/{jobId}")]
        public async Task<IActionResult> GetJob(string jobId)
        {
            var row = await _durable.GetAsync(jobId);
            if (row == null) return NotFound(new { ok = false, code = "NOT_FOUND", msg = "job not found" });

            // Compute a weak ETag and Last-Modified from job timestamps
            string lastTs = string.Empty;
            try
            {
                if (row.TryGetValue("finish_ts", out object f) && f != null) lastTs = Convert.ToString(f) ?? string.Empty;
                else if (row.TryGetValue("heartbeat_ts", out object h) && h != null) lastTs = Convert.ToString(h) ?? string.Empty;
                else if (row.TryGetValue("start_ts", out object s) && s != null) lastTs = Convert.ToString(s) ?? string.Empty;
                else if (row.TryGetValue("enqueue_ts", out object e) && e != null) lastTs = Convert.ToString(e) ?? string.Empty;
            }
            catch { }

            var etag = !string.IsNullOrEmpty(lastTs) ? $"W/\"{jobId}:{lastTs}\"" : $"W/\"{jobId}\"";
            var inm = Request.Headers["If-None-Match"].ToString();
            if (!string.IsNullOrEmpty(inm) && string.Equals(inm, etag, StringComparison.Ordinal))
                return StatusCode(304);

            Response.Headers["ETag"] = etag;
            if (!string.IsNullOrEmpty(lastTs)) Response.Headers["Last-Modified"] = lastTs;
            Response.Headers["Cache-Control"] = "no-cache";

            try
            {
                // Provide a hint for clients to back off while the job is not finished
                if (row.ContainsKey("state") && row["state"] != null)
                {
                    var st = Convert.ToString(row["state"]) ?? string.Empty;
                    if (string.Equals(st, "ENQUEUED", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(st, "DISPATCHING", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(st, "RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        Response.Headers["Retry-After"] = "1"; // suggest 1s between polls
                    }
                }
            }
            catch { }
            return Ok(row);
        }

        [HttpGet("/jobs")]
        public async Task<IActionResult> List([FromQuery] string state = "ENQUEUED", [FromQuery] int limit = 50)
            => Ok(await _durable.ListAsync(state, limit));
    }
}
