-- ============================================================================
--  OptiPaie PRO - Migration 0035 : add the "Congé de récupération" leave type
-- ----------------------------------------------------------------------------
--  PURELY ADDITIVE and retro-compatible with every existing client database.
--
--  Adds ONE global row to the configurable LeaveTypes catalogue (introduced by
--  migration 0031). Nothing else changes:
--    * No CHECK is touched or widened. The row maps onto the legacy
--      LeaveRequests.Type column via BaseType = 5 (Special), which the existing
--      CHECK (Type IN 1..5) already allows — no destructive table rebuild.
--    * No existing row is modified. The seed is idempotent (guarded on Code +
--      global scope), so re-running it is a no-op.
--    * No balance changes. Pre-existing requests keep LeaveTypeId = NULL and do
--      NOT point to this type, so a company that never selects it sees zero
--      change anywhere (list, balance, payroll, reports).
--
--  "Congé de récupération" (عطلة الاسترجاع, repos compensateur — loi 90-11 arts
--  31/32) behaves like the paid family-event types already in the catalogue:
--  granted MANUALLY per request, paid by the employer, and NOT decrementing the
--  annual-leave balance (DecrementsAnnualBalance = 0). No automatic accrual.
--
--  PaymentCategory: 1=employeur, 2=CNAS, 3=sans solde.
-- ============================================================================

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'RECOVERY', 'عطلة الاسترجاع', 'Congé de récupération', 5, 1, 0, NULL, 0, 1, 5, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'RECOVERY' AND CompanyId IS NULL);
