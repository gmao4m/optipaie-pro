-- ============================================================================
--  OptiPaie PRO - Migration 0039 : extend the RowVersion token to more tables
-- ----------------------------------------------------------------------------
--  Additive. Same optimistic-concurrency token as 0037, now on the remaining
--  single-record editable tables. Every existing row starts at 0, so a single
--  -user install is unaffected. Nothing here touches payroll.
-- ============================================================================

ALTER TABLE Companies       ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE PayrollElements ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Assets          ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;
