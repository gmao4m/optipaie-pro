-- 0036: Payslip lifecycle status.
--
-- Enables two new, non-destructive operations on a saved payslip:
--   • CORRECTION — the original is kept and marked Superseded (2); a new corrected
--     version is inserted as Active (1), linked back through SupersedesPayslipId.
--   • DELETION   — the payslip is marked Cancelled (3), never physically removed, so
--     its period slot is freed for a fresh calculation while the record (and its CNAS
--     history) survives for audit. A cancelled payslip can be restored.
--
-- ADDITIVE and SAFE on an already-populated customer database: every existing payslip
-- takes Status = 1 (Active) through the column default, so nothing read, declared or
-- printed changes for a database upgraded from an earlier version.

ALTER TABLE Payslips ADD COLUMN Status INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Payslips ADD COLUMN SupersedesPayslipId INTEGER NULL;
ALTER TABLE Payslips ADD COLUMN RevisionReason TEXT NULL;
ALTER TABLE Payslips ADD COLUMN RevisedByUser TEXT NULL;
ALTER TABLE Payslips ADD COLUMN RevisedAtUtc TEXT NULL;

-- The "one payslip per employee per run" rule now applies to ACTIVE payslips only, so a
-- corrected version coexists with its superseded original and a cancelled slip can be
-- re-generated. Replacing an index is a metadata-only operation — no table rebuild, no
-- row is read or rewritten, so existing data is untouched.
DROP INDEX IF EXISTS UX_Payslips_Run_Employee;
CREATE UNIQUE INDEX UX_Payslips_Run_Employee_Active
    ON Payslips (RunId, EmployeeId) WHERE Status = 1;
CREATE INDEX IF NOT EXISTS IX_Payslips_Status ON Payslips (Status);
