-- ============================================================================
--  OptiPaie PRO - Migration 0044 : IRG / CNAS on-off switch
-- ----------------------------------------------------------------------------
--  Some customers do not apply the legal deductions at all; they only want
--  attendance and advances linked to salaries. One setting decides it.
--
--  1. The setting, ON for every database (new and existing). INSERT OR IGNORE
--     never overwrites a value a customer may already have chosen.
--  2. The flag is PERSISTED ON EVERY PAYSLIP at the moment it is computed, so a
--     saved payslip is always reprinted exactly as it was paid — even after the
--     setting is later switched off. Every payslip that exists today was computed
--     WITH the deductions, so all are back-filled ON (NOT NULL DEFAULT 1 fills
--     them; the explicit UPDATE states it).
--
--  Purely ADDITIVE: no value computed, declared or printed changes.
-- ============================================================================

INSERT OR IGNORE INTO AppSettings (SettingKey, SettingValue, CreatedAtUtc)
VALUES ('LEGAL_DEDUCTIONS_ENABLED', '1', strftime('%Y-%m-%d %H:%M:%S', 'now'));

ALTER TABLE Payslips ADD COLUMN LegalDeductionsApplied INTEGER NOT NULL DEFAULT 1;

UPDATE Payslips SET LegalDeductionsApplied = 1;
