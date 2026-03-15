#nullable enable
using System;

namespace RevitMCPAddin.Core.ResultDelivery
{
    internal sealed class PendingResultItem
    {
        public string RpcId { get; set; } = string.Empty;
        public string JsonBody { get; set; } = string.Empty;
        public string? HeartbeatKey { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public int Attempt { get; set; } = 0;
        public string? LastError { get; set; }
        public string? StorePath { get; set; }
    }
}
