using Microsoft.AspNetCore.Mvc;
using RevitMcpServer.Engine;

namespace RevitMcpServer.Http
{
    [ApiController]
    public sealed class HealthController : ControllerBase
    {
        private readonly FatalStop _fatal;
        public HealthController(FatalStop fatal) { _fatal = fatal; }

        [HttpGet("/health")]
        public IActionResult Health() => Ok(new { status = _fatal.IsActive ? "degraded" : "ok", fatal_error = _fatal.IsActive, reason = _fatal.Reason });
    }
}

