// RevitMCPAddin/Core/Progress/ProgressReporter.cs
#nullable enable
using System;
using System.Diagnostics;
using System.Threading;

namespace RevitMCPAddin.Core.Progress
{
    public sealed class ProgressReporter
    {
        private readonly string _jobId;
        private readonly string _title;
        private readonly int _total;
        private readonly TimeSpan _tick;
        private readonly Action<ProgressState> _publish;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        private int _done;
        private string _message = "";

        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        internal ProgressReporter(string jobId, string title, int total, TimeSpan tick, Action<ProgressState> publish)
        {
            _jobId = jobId ?? "";
            _title = title ?? "";
            _total = total;
            _tick = tick <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : tick;
            _publish = publish ?? (_ => { });
        }

        public int Total => _total;
        public int Done => _done;

        public void SetMessage(string message)
        {
            _message = message ?? "";
            ReportMaybe(force: false);
        }

        public void Step(string? message = null, int delta = 1)
        {
            if (delta <= 0) delta = 1;
            _done += delta;
            if (message != null) _message = message;
            ReportMaybe(force: false);
        }

        public void ReportNow(string? message = null)
        {
            if (message != null) _message = message;
            ReportMaybe(force: true);
        }

        private void ReportMaybe(bool force)
        {
            if (!force && _sw.Elapsed < _tick) return;
            _sw.Restart();

            var s = new ProgressState
            {
                JobId = _jobId,
                Title = _title,
                Total = _total,
                Done = _done,
                Message = _message ?? "",
                UpdatedAtUtc = DateTime.UtcNow
            };

            _publish(s);
        }
    }
}

