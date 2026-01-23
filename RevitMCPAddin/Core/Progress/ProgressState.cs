// RevitMCPAddin/Core/Progress/ProgressState.cs
#nullable enable
using System;

namespace RevitMCPAddin.Core.Progress
{
    public sealed class ProgressState
    {
        public string JobId { get; set; } = "";
        public string Title { get; set; } = "";
        public int Total { get; set; }          // Total <= 0 => indeterminate
        public int Done { get; set; }
        public string Message { get; set; } = "";
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsIndeterminate => Total <= 0;

        public double Percent
        {
            get
            {
                if (Total <= 0) return -1;
                if (Done <= 0) return 0;
                if (Done >= Total) return 100;
                return (double)Done * 100.0 / (double)Total;
            }
        }

        public ProgressState Clone()
        {
            return new ProgressState
            {
                JobId = this.JobId,
                Title = this.Title,
                Total = this.Total,
                Done = this.Done,
                Message = this.Message,
                UpdatedAtUtc = this.UpdatedAtUtc
            };
        }
    }
}

