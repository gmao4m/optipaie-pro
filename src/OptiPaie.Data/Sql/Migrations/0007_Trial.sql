-- ============================================================================
--  OptiPaie DZ - Migration 0007 : local trial state
-- ----------------------------------------------------------------------------
--  Single-row store for the offline 30-day trial. The Blob holds an encrypted
--  (DPAPI) JSON payload { StartedUtc, ExpiresUtc, LastSeenUtc }. Additive and
--  non-destructive - it does not touch any existing table.
-- ============================================================================

CREATE TABLE IF NOT EXISTS TrialState (
    Id           INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    Blob         TEXT,
    UpdatedAtUtc TEXT
);
