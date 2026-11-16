-- ============================================================================
--  OptiPaie PRO - Migration 0042 : link each loan repayment to its payslip
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. Gives every payroll-recorded loan instalment a hard link to
--  the exact payslip that deducted it, so "has this instalment been deducted, and
--  on which bulletin?" is answerable from the data, not inferred. Manual repayments
--  keep PayslipId NULL (they were never tied to a run).
--
--  The column is nullable with no default, so NOTHING computed, declared or printed
--  changes for an upgraded database: existing rows simply gain an empty link that
--  the backfill below then fills wherever the matching issued payslip still exists.
--
--  HISTORICAL RECONSTRUCTION — no re-entry asked of the user. Every existing payroll
--  (non-manual) repayment is matched to the ACTIVE payslip of the same employee and
--  period (via the run's PeriodYear/PeriodMonth). A repayment whose payslip was later
--  cancelled, or that predates the archive, stays NULL and is treated as "recorded,
--  payslip no longer on file" — never as missing.
-- ============================================================================

ALTER TABLE LoanRepayments ADD COLUMN PayslipId INTEGER NULL;

UPDATE LoanRepayments
SET PayslipId = (
    SELECT p.Id
    FROM Payslips p
    INNER JOIN PayrollRuns r ON r.Id = p.RunId
    INNER JOIN Loans      l ON l.Id = LoanRepayments.LoanId
    WHERE p.EmployeeId  = l.EmployeeId
      AND r.PeriodYear  = LoanRepayments.Year
      AND r.PeriodMonth = LoanRepayments.Month
      AND p.Status      = 1          -- the live bulletin for that period
    ORDER BY p.Id DESC
    LIMIT 1
)
WHERE LoanRepayments.IsManual  = 0
  AND LoanRepayments.IsDeleted = 0
  AND LoanRepayments.PayslipId IS NULL;

CREATE INDEX IF NOT EXISTS IX_Repayment_Payslip ON LoanRepayments (PayslipId);
