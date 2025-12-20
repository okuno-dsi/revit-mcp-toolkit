CREATE TABLE IF NOT EXISTS jobs (
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
  error_msg        TEXT NULL,
  target_port      INTEGER NULL
);

CREATE INDEX IF NOT EXISTS idx_jobs_state_priority ON jobs(state, priority, enqueue_ts);
CREATE INDEX IF NOT EXISTS idx_jobs_idem ON jobs(idempotency_key);
CREATE INDEX IF NOT EXISTS idx_jobs_port_state ON jobs(target_port, state, priority, enqueue_ts);
