-- ============================================================================
--  OptiPaie PRO - Migration 0030 : Leave — "Brouillon" (draft) sub-state
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE and retro-compatible with every existing client database.
--
--  A leave request now carries an optional draft flag:
--    IsDraft = 1  -> Brouillon: not yet submitted. Reserves no balance and is
--                    NOT "live" for the overlap rule. Freely editable.
--    IsDraft = 0  -> the request follows its stored Status (En attente / …).
--
--  The Status column and its CHECK (1..4) are LEFT UNTOUCHED, so a fifth status
--  value is never written to an existing base (which would violate that CHECK,
--  or force a destructive table rebuild). Existing rows default to 0, i.e. they
--  keep their exact current meaning — no data is read differently after this.
-- ============================================================================

ALTER TABLE LeaveRequests
    ADD COLUMN IsDraft INTEGER NOT NULL DEFAULT 0 CHECK (IsDraft IN (0, 1));

CREATE INDEX IF NOT EXISTS IX_Leave_Draft ON LeaveRequests (IsDraft);
