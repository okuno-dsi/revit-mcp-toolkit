using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace RevitMcpServer.Engine
{
    public sealed class DurableQueue
    {
        public async Task<string> EnqueueAsync(string method, string paramsJson, string? idemKey, string? rpcId, int priority, int timeoutSec)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            if (!string.IsNullOrWhiteSpace(idemKey))
            {
                using var check = conn.CreateCommand();
                check.CommandText = "SELECT job_id FROM jobs WHERE idempotency_key=$k LIMIT 1";
                check.Parameters.AddWithValue("$k", idemKey);
                var exist = (string?)await check.ExecuteScalarAsync();
                if (!string.IsNullOrEmpty(exist)) return exist;
            }
            var jobId = Guid.NewGuid().ToString("N");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO jobs(job_id, method, params_json, rpc_id, idempotency_key, priority, state, enqueue_ts, timeout_sec, target_port)
VALUES($id,$m,$p,$rid,$k,$pri,'ENQUEUED',CURRENT_TIMESTAMP,$to,$tp);";
            cmd.Parameters.AddWithValue("$id", jobId);
            cmd.Parameters.AddWithValue("$m", method);
            cmd.Parameters.AddWithValue("$p", paramsJson);
            cmd.Parameters.AddWithValue("$rid", (object?)rpcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$k", (object?)idemKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pri", priority);
            cmd.Parameters.AddWithValue("$to", timeoutSec);
            try
            {
                var tp = RevitMcpServer.Infra.ServerContext.Port;
                if (tp <= 0 || tp >= 65536)
                {
                    var env = Environment.GetEnvironmentVariable("REVIT_MCP_PORT");
                    if (!int.TryParse(env, out tp)) tp = 0;
                }
                cmd.Parameters.AddWithValue("$tp", tp > 0 ? tp : (object)DBNull.Value);
            }
            catch { cmd.Parameters.AddWithValue("$tp", DBNull.Value); }
            await cmd.ExecuteNonQueryAsync();
            return jobId;
        }

        public async Task<dynamic?> GetAsync(string jobId)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM jobs WHERE job_id=$id";
            cmd.Parameters.AddWithValue("$id", jobId);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.GetValue(i);
            return row;
        }

        public async Task<IReadOnlyList<dynamic>> ListAsync(string state, int limit)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM jobs WHERE state=$s ORDER BY priority, enqueue_ts LIMIT $n";
            cmd.Parameters.AddWithValue("$s", state);
            cmd.Parameters.AddWithValue("$n", limit);
            var rows = new List<dynamic>();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }

        public async Task<dynamic?> ClaimAsync()
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var tx = conn.BeginTransaction();
            using (var pick = conn.CreateCommand())
            {
                pick.Transaction = tx;
                // Prefer matching jobs enqueued for this server port. As a fallback,
                // allow NULL/0 target_port for backward compatibility.
                var port = 0;
                try
                {
                    port = RevitMcpServer.Infra.ServerContext.Port;
                    if (port <= 0 || port >= 65536)
                    {
                        var env = Environment.GetEnvironmentVariable("REVIT_MCP_PORT");
                        if (!int.TryParse(env, out port)) port = 0;
                    }
                }
                catch { port = 0; }
                if (port > 0)
                {
                    pick.CommandText = @"SELECT job_id FROM jobs WHERE state='ENQUEUED' AND (target_port=$p OR target_port IS NULL OR target_port=0) ORDER BY CASE WHEN target_port=$p THEN 0 ELSE 1 END, priority, enqueue_ts LIMIT 1;";
                    pick.Parameters.AddWithValue("$p", port);
                }
                else
                {
                    pick.CommandText = @"SELECT job_id FROM jobs WHERE state='ENQUEUED' ORDER BY priority, enqueue_ts LIMIT 1;";
                }
                var jobId = (string?)await pick.ExecuteScalarAsync();
                if (string.IsNullOrEmpty(jobId)) { tx.Commit(); return null; }

                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE jobs SET state='DISPATCHING', start_ts=CURRENT_TIMESTAMP WHERE job_id=$id";
                upd.Parameters.AddWithValue("$id", jobId);
                await upd.ExecuteNonQueryAsync();

                using var get = conn.CreateCommand();
                get.Transaction = tx;
                get.CommandText = "SELECT * FROM jobs WHERE job_id=$id";
                get.Parameters.AddWithValue("$id", jobId);
                using var r = await get.ExecuteReaderAsync();
                await r.ReadAsync();
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.GetValue(i);
                tx.Commit();
                return row;
            }
        }

        public async Task StartRunningAsync(string jobId)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE jobs SET state='RUNNING', heartbeat_ts=CURRENT_TIMESTAMP WHERE job_id=$id";
            cmd.Parameters.AddWithValue("$id", jobId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task HeartbeatAsync(string jobId)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE jobs SET heartbeat_ts=CURRENT_TIMESTAMP WHERE job_id=$id";
            cmd.Parameters.AddWithValue("$id", jobId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CompleteAsync(string jobId, string resultJson)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE jobs SET state='SUCCEEDED', finish_ts=CURRENT_TIMESTAMP, result_json=$r, error_code=NULL, error_msg=NULL WHERE job_id=$id";
            cmd.Parameters.AddWithValue("$id", jobId);
            cmd.Parameters.AddWithValue("$r", resultJson);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task FailAsync(string jobId, string code, string msg)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE jobs SET state='FAILED', finish_ts=CURRENT_TIMESTAMP, error_code=$c, error_msg=$m, attempts=attempts+1 WHERE job_id=$id";
            cmd.Parameters.AddWithValue("$id", jobId);
            cmd.Parameters.AddWithValue("$c", code);
            cmd.Parameters.AddWithValue("$m", msg);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task TimeoutAsync(string jobId)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE jobs SET state='TIMEOUT', finish_ts=CURRENT_TIMESTAMP, error_code='TIMEOUT', error_msg='heartbeat lost', attempts=attempts+1 WHERE job_id=$id";
            cmd.Parameters.AddWithValue("$id", jobId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RequeueAsync(string jobId)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE jobs SET state='ENQUEUED', start_ts=NULL, heartbeat_ts=NULL, finish_ts=NULL WHERE job_id=$id";
            cmd.Parameters.AddWithValue("$id", jobId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeadAsync(string jobId, string code, string msg)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE jobs SET state='DEAD', finish_ts=CURRENT_TIMESTAMP, error_code=$c, error_msg=$m WHERE job_id=$id";
            cmd.Parameters.AddWithValue("$id", jobId);
            cmd.Parameters.AddWithValue("$c", code);
            cmd.Parameters.AddWithValue("$m", msg);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<string?> FindRunningJobIdByRpcIdAsync(string rpcId)
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT job_id FROM jobs WHERE rpc_id=$rid AND state IN ('RUNNING','DISPATCHING') ORDER BY start_ts DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$rid", rpcId);
            var obj = await cmd.ExecuteScalarAsync();
            return obj as string;
        }

        // ----------------------------- Step 10: status helpers -----------------------------

        public async Task<IDictionary<string, long>> CountByStateAsync()
        {
            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();

            var port = ResolveCurrentPort();
            if (port > 0)
            {
                cmd.CommandText = "SELECT state, COUNT(*) AS cnt FROM jobs WHERE (target_port=$p OR target_port IS NULL OR target_port=0) GROUP BY state";
                cmd.Parameters.AddWithValue("$p", port);
            }
            else
            {
                cmd.CommandText = "SELECT state, COUNT(*) AS cnt FROM jobs GROUP BY state";
            }

            var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var state = r.GetString(0);
                var cnt = r.GetInt64(1);
                dict[state] = cnt;
            }
            return dict;
        }

        public async Task<dynamic?> GetLatestJobByStatesAsync(string[] states, string orderByColumnDesc)
        {
            if (states == null || states.Length == 0) return null;

            using var conn = Persistence.SqliteConnectionFactory.Create();
            using var cmd = conn.CreateCommand();

            // States are constants from the server; inline them safely.
            var inList = string.Join(",", states.Select(s => "'" + s.Replace("'", "''") + "'"));

            var port = ResolveCurrentPort();
            if (port > 0)
            {
                cmd.CommandText = $"SELECT * FROM jobs WHERE state IN ({inList}) AND (target_port=$p OR target_port IS NULL OR target_port=0) ORDER BY {orderByColumnDesc} DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$p", port);
            }
            else
            {
                cmd.CommandText = $"SELECT * FROM jobs WHERE state IN ({inList}) ORDER BY {orderByColumnDesc} DESC LIMIT 1";
            }

            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.GetValue(i);
            return row;
        }

        public int ResolveCurrentPort()
        {
            try
            {
                var port = RevitMcpServer.Infra.ServerContext.Port;
                if (port > 0 && port < 65536) return port;

                var env = Environment.GetEnvironmentVariable("REVIT_MCP_PORT");
                if (int.TryParse(env, out port) && port > 0 && port < 65536) return port;
            }
            catch { /* ignore */ }
            return 0;
        }
    }
}
