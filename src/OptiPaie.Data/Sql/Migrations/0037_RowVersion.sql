-- ============================================================================
--  OptiPaie PRO - Migration 0037 : optimistic-concurrency token (RowVersion)
-- ----------------------------------------------------------------------------
--  Additive only. Adds a RowVersion counter to the records most likely to be
--  edited from two screens at once. Every existing row starts at 0, so a single
--  -user install keeps behaving exactly as before (first save matches 0 -> 1).
--  The repositories make each UPDATE conditional on this value, so a second user
--  who saved first is never silently overwritten. Nothing here touches payroll.
-- ============================================================================

ALTER TABLE Employees          ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE EmploymentContracts ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE LeaveRequests       ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Loans               ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE AttendanceRecords   ADD COLUMN RowVersion INTEGER NOT NULL DEFAULT 0;
