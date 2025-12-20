using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace RevitMcpServer.Engine
{
    public sealed class Reclaimer
    {
        private readonly DurableQueue _durable; private readonly FatalStop _fatal; private readonly Metrics _metrics;
        public Reclaimer(DurableQueue d, FatalStop f, Metrics m) { _durable=d; _fatal=f; _metrics=m; }
        public async Task RunAsync(CancellationToken stopping)
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    using var conn = Persistence.SqliteConnectionFactory.Create();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"SELECT job_id, timeout_sec, strftime('%s','now') - strftime('%s', IFNULL(heartbeat_ts, start_ts)) AS lag FROM jobs WHERE state='RUNNING';";
                    using var r = await cmd.ExecuteReaderAsync();
                    var toTimeout = new List<string>();
                    while (await r.ReadAsync())
                    {
                        string id = r.GetString(0); int to = r.GetInt32(1); int lag = r.IsDBNull(2) ? to+1 : r.GetInt32(2);
                        if (lag > to) toTimeout.Add(id);
                    }
                    foreach (var id in toTimeout) { await _durable.TimeoutAsync(id); _metrics.IncTimeout(); }

                    using var pick = conn.CreateCommand();
                    pick.CommandText = "SELECT job_id, attempts, max_attempts, state FROM jobs WHERE state IN ('TIMEOUT','FAILED')";
                    using var r2 = await pick.ExecuteReaderAsync();
                    while (await r2.ReadAsync())
                    {
                        string id = r2.GetString(0); int att = r2.GetInt32(1); int max = r2.GetInt32(2); string st = r2.GetString(3);
                        if (att < max) await _durable.RequeueAsync(id);
                        else { await _durable.DeadAsync(id, st, "max attempts exceeded"); _metrics.IncDead(); }
                    }
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
                { /* database is locked */ }
                catch (Exception ex) { _fatal.Trip($"Reclaimer exception: {ex.Message}"); }
                await Task.Delay(5000, stopping);
            }
        }
    }
}

