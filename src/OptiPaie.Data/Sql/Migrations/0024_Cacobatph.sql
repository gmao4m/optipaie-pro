-- ============================================================================
--  OptiPaie PRO - Migration 0024 : CACOBATPH sector opt-in (BTPH)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. Two per-company flags, both OFF by default, so every existing
--  and future company is unchanged unless a user deliberately enables them. They
--  drive an OPTIONAL, additive CACOBATPH overlay on payslips and its declarations;
--  the payroll calculation engine and existing fiche output are never affected.
-- ============================================================================

ALTER TABLE Companies ADD COLUMN BtphSector       INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Companies ADD COLUMN CacobatphEnabled INTEGER NOT NULL DEFAULT 0;
