using System.Collections.Generic;

namespace RevitMCPAddin.Models
{
    public sealed class CreateFlushWallsResponse
    {
        public bool Ok { get; set; } = false;
        public string Message { get; set; } = string.Empty;

        public List<int> CreatedWallIds { get; set; } = new List<int>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}

