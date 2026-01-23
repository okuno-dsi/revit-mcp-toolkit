using System.Threading;

namespace RevitMcpServer.Engine
{
    public sealed class Metrics
    {
        private long _readCount, _writeCount, _success, _timeout, _dead;
        private long _readMs, _writeMs;
        public void AddRead(long ms, bool ok) { Interlocked.Add(ref _readMs, ms); Interlocked.Increment(ref _readCount); if (ok) Interlocked.Increment(ref _success); }
        public void AddWrite(long ms, bool ok) { Interlocked.Add(ref _writeMs, ms); Interlocked.Increment(ref _writeCount); if (ok) Interlocked.Increment(ref _success); }
        public void IncTimeout() => Interlocked.Increment(ref _timeout);
        public void IncDead() => Interlocked.Increment(ref _dead);
        public object Snapshot() => new {
            avg_read_ms = _readCount==0 ? 0 : (double)_readMs/_readCount,
            avg_write_ms = _writeCount==0 ? 0 : (double)_writeMs/_writeCount,
            success_rate = (_readCount+_writeCount)==0 ? 1.0 : (double)_success/(_readCount+_writeCount),
            timeouts = _timeout, dead = _dead
        };
    }
}

