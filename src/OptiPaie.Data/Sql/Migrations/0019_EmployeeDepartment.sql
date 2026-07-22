-- ============================================================================
--  OptiPaie PRO - Migration 0019 : Employee department
-- ----------------------------------------------------------------------------
--  Adds a Department/Service column to the shared Employees table so the
--  Attendance matrix can group and filter employees by department. Additive and
--  nullable — existing rows keep NULL until edited.
-- ============================================================================

ALTER TABLE Employees ADD COLUMN Department TEXT;
