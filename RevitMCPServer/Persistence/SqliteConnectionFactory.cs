using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace RevitMcpServer.Persistence
{
    public static class SqliteConnectionFactory
    {
        private static readonly object _sync = new object();
        private static bool _initialized;

        // Optional runtime configuration
        private static int? _port;
        private static string _queueBaseDirOverride = string.Empty;
        private const string EmbeddedSchema = @"CREATE TABLE IF NOT EXISTS jobs (
  job_id           TEXT PRIMARY KEY,
  method           TEXT NOT NULL,
  params_json      TEXT NOT NULL,
  rpc_id           TEXT NULL,
  idempotency_key  TEXT NULL,
  priority         INTEGER NOT NULL DEFAULT 100,
  state            TEXT NOT NULL DEFAULT 'ENQUEUED',
  enqueue_ts       TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
  start_ts         TEXT NULL,
  heartbeat_ts     TEXT NULL,
  finish_ts        TEXT NULL,
  timeout_sec      INTEGER NOT NULL DEFAULT 15,
  attempts         INTEGER NOT NULL DEFAULT 0,
  max_attempts     INTEGER NOT NULL DEFAULT 3,
  result_json      TEXT NULL,
  error_code       TEXT NULL,
  error_msg        TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_jobs_state_priority ON jobs(state, priority, enqueue_ts);
CREATE INDEX IF NOT EXISTS idx_jobs_idem ON jobs(idempotency_key);
";

        public static void Configure(int? port = null, string? queueBaseDir = null)
        {
            try
            {
                if (port.HasValue && port.Value > 0 && port.Value < 65536)
                    _port = port.Value;
            }
            catch { /* ignore */ }

            try
            {
                // Highest priority: explicit argument
                if (!string.IsNullOrWhiteSpace(queueBaseDir))
                {
                    _queueBaseDirOverride = queueBaseDir;
                }
                else
                {
                    // Next: environment override
                    var env = Environment.GetEnvironmentVariable("REVIT_MCP_QUEUE_DIR")
                              ?? Environment.GetEnvironmentVariable("MCP_QUEUE_DIR");
                    if (!string.IsNullOrWhiteSpace(env)) _queueBaseDirOverride = env;
                }
            }
            catch { /* ignore */ }
        }

        private static string GetDbPath()
        {
            // Base directory resolution (with override)
            string baseDir;
            if (!string.IsNullOrWhiteSpace(_queueBaseDirOverride))
            {
                baseDir = _queueBaseDirOverride;
            }
            else
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                baseDir = Path.Combine(local, "RevitMCP", "queue");
            }

            // Port-specific subdirectory (p<port>) to physically isolate instances
            string dir = baseDir;
            try
            {
                var portEnv = Environment.GetEnvironmentVariable("REVIT_MCP_PORT");
                int portVal = 0;
                if (_port.HasValue) portVal = _port.Value;
                else if (int.TryParse(portEnv, out var p) && p > 0 && p < 65536) portVal = p;
                if (portVal > 0)
                {
                    dir = Path.Combine(baseDir, $"p{portVal}");
                }
            }
            catch { /* ignore, fallback to baseDir */ }

            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "jobs.db");
        }

        public static SqliteConnection Create()
        {
            var path = GetDbPath();
            var conn = new SqliteConnection($"Data Source={path};Cache=Shared");
            void ApplyPragmas(SqliteConnection c)
            {
                try
                {
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = @"PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA busy_timeout=3000;";
                    cmd.ExecuteNonQuery();
                }
                catch { /* best-effort */ }
            }
            if (!_initialized)
            {
                lock (_sync)
                {
                    if (!_initialized)
                    {
                        conn.Open();
                        ApplyPragmas(conn);
                        using (var cmd = conn.CreateCommand())
                        {
                            // Prefer on-disk schema file if present; otherwise fall back to embedded SQL
                            string sql = EmbeddedSchema;
                            try
                            {
                                var schemaPath = Path.Combine(AppContext.BaseDirectory, "Persistence", "Schema.sql");
                                if (!File.Exists(schemaPath))
                                {
                                    var alt = Path.Combine(Directory.GetCurrentDirectory(), "RevitMCPServer", "Persistence", "Schema.sql");
                                    if (File.Exists(alt)) schemaPath = alt; else schemaPath = null;
                                }
                                if (!string.IsNullOrEmpty(schemaPath) && File.Exists(schemaPath))
                                    sql = File.ReadAllText(schemaPath);
                            }
                            catch { /* fall back to embedded */ }
                            cmd.CommandText = sql;
                            cmd.ExecuteNonQuery();
                        }

                        // Ensure new columns exist (simple migration)
                        try
                        {
                            using var check = conn.CreateCommand();
                            check.CommandText = "PRAGMA table_info(jobs);";
                            var hasRpcId = false;
                            var hasTargetPort = false;
                            using (var r = check.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    var name = r.GetString(1);
                                    if (string.Equals(name, "rpc_id", StringComparison.OrdinalIgnoreCase))
                                    {
                                        hasRpcId = true; break;
                                    }
                                    if (string.Equals(name, "target_port", StringComparison.OrdinalIgnoreCase))
                                    {
                                        hasTargetPort = true;
                                    }
                                }
                            }
                            if (!hasRpcId)
                            {
                                using var alter = conn.CreateCommand();
                                alter.CommandText = "ALTER TABLE jobs ADD COLUMN rpc_id TEXT NULL;";
                                alter.ExecuteNonQuery();
                            }
                            if (!hasTargetPort)
                            {
                                using var alter2 = conn.CreateCommand();
                                alter2.CommandText = "ALTER TABLE jobs ADD COLUMN target_port INTEGER NULL;";
                                alter2.ExecuteNonQuery();
                                try
                                {
                                    using var idx = conn.CreateCommand();
                                    idx.CommandText = "CREATE INDEX IF NOT EXISTS idx_jobs_port_state ON jobs(target_port, state, priority, enqueue_ts);";
                                    idx.ExecuteNonQuery();
                                }
                                catch { /* best-effort */ }
                            }
                        }
                        catch { /* best-effort migration */ }
                        _initialized = true;
                        return conn;
                    }
                }
            }

            conn.Open();
            ApplyPragmas(conn);
            return conn;
        }
    }
}
