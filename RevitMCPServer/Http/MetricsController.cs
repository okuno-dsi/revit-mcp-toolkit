using Microsoft.AspNetCore.Mvc;
using RevitMcpServer.Engine;

namespace RevitMcpServer.Http
{
    [ApiController]
    public sealed class MetricsController : ControllerBase
    {
        private readonly Metrics _m;
        public MetricsController(Metrics m) { _m = m; }
        [HttpGet("/metrics")]
        public IActionResult Get() => Ok(_m.Snapshot());
    }
}

