using System;

namespace RevitMcpServer.Infra
{
    /// <summary>
    /// Simple runtime context to expose the bound server port to other layers
    /// without threading it through DI for legacy compatibility.
    /// </summary>
    public static class ServerContext
    {
        private static int _port;

        /// <summary>
        /// Gets or sets the current server port (0 if unknown).
        /// </summary>
        public static int Port
        {
            get => _port;
            set
            {
                if (value > 0 && value < 65536)
                {
                    _port = value;
                    try { Environment.SetEnvironmentVariable("REVIT_MCP_PORT", value.ToString()); } catch { /* best-effort */ }
                }
            }
        }
    }
}

