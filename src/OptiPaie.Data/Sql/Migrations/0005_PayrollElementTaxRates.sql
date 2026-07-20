-- ============================================================================
--  OptiPaie DZ - Migration 0005 : configurable CNAS / IRG rates per element
-- ----------------------------------------------------------------------------
--  Lets each payroll element own its legal treatment instead of a fixed
--  yes/no flag: a line can be fully, partially or not subject to CNAS and IRG.
--
--    CnasPercent : NULL keeps the existing IsCnasApplicable yes/no behaviour;
--                  a value 0..100 is the share of the line that is cotisable.
--    IrgPercent  : NULL keeps the existing IsIrgApplicable yes/no behaviour;
--                  a value 0..100 is the share of the line that is taxable.
--
--  Decimals are stored as invariant TEXT (matching the existing money/rate
--  columns and the Dapper decimal type handler). Additive and non-destructive:
--  all existing rows keep NULL and therefore their current behaviour.
-- ============================================================================

ALTER TABLE PayrollElements ADD COLUMN CnasPercent TEXT;
ALTER TABLE PayrollElements ADD COLUMN IrgPercent  TEXT;
