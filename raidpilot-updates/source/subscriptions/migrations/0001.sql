CREATE TABLE IF NOT EXISTS accounts (
  id TEXT PRIMARY KEY, token_hash TEXT NOT NULL UNIQUE, created_at INTEGER NOT NULL,
  subscription_id TEXT UNIQUE, approval_url TEXT, status TEXT NOT NULL DEFAULT 'NEW',
  paid_until INTEGER NOT NULL DEFAULT 0, checked_at INTEGER NOT NULL DEFAULT 0,
  checkout_started INTEGER NOT NULL DEFAULT 0, revision INTEGER NOT NULL DEFAULT 0,
  checkout_id TEXT, last_webhook_at INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS events (id TEXT PRIMARY KEY, received_at INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS revoked_sales (sale_id TEXT PRIMARY KEY, subscription_id TEXT NOT NULL, received_at INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS sales (sale_id TEXT PRIMARY KEY, subscription_id TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS limits (key TEXT NOT NULL, window INTEGER NOT NULL, count INTEGER NOT NULL, PRIMARY KEY(key,window));
CREATE INDEX IF NOT EXISTS account_reconcile ON accounts(status,checked_at);
