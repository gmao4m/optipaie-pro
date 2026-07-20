-- ============================================================================
--  OptiPaie DZ - Migration 0004 : company banking / CACOBATPH / currency fields
-- ----------------------------------------------------------------------------
--  Additive and non-destructive; required by the First Run Wizard and the
--  company form. Existing rows keep NULL values.
-- ============================================================================

ALTER TABLE Companies ADD COLUMN Cacobatph   TEXT;
ALTER TABLE Companies ADD COLUMN Bank        TEXT;
ALTER TABLE Companies ADD COLUMN BankAccount TEXT;
ALTER TABLE Companies ADD COLUMN Currency    TEXT;
