namespace RevitMcpServer.Engine
{
    public sealed class FatalStop
    {
        private volatile bool _active;
        public bool IsActive => _active;
        public string? Reason { get; private set; }
        public void Trip(string reason) { _active = true; Reason = reason; }
        public void Reset() { _active = false; Reason = null; }
    }
}

