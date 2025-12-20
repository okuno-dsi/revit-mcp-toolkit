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
            string lastTs = null;
            try
            {
                if (row.ContainsKey("finish_ts") && row["finish_ts"] != null) lastTs = Convert.ToString(row["finish_ts"]);
                else if (row.ContainsKey("heartbeat_ts") && row["heartbeat_ts"] != null) lastTs = Convert.ToString(row["heartbeat_ts"]);
                else if (row.ContainsKey("start_ts") && row["start_ts"] != null) lastTs = Convert.ToString(row["start_ts"]);
                else if (row.ContainsKey("enqueue_ts") && row["enqueue_ts"] != null) lastTs = Convert.ToString(row["enqueue_ts"]);
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
