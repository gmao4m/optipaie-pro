-- ============================================================================
--  OptiPaie PRO - Migration 0034 : AuditLog.CompanyId (isolation multi-societes)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. Adds a NULLABLE CompanyId so each activity-journal entry can
--  be scoped to the company it belongs to. No CHECK touched, no table rebuilt,
--  no data deleted. Rows written before this update keep CompanyId = NULL: they
--  are EXCLUDED from the per-company journal (never shown, never leaked) but are
--  KEPT in the database -- never removed, never back-filled with a guessed value.
-- ============================================================================

ALTER TABLE AuditLog ADD COLUMN CompanyId INTEGER NULL;

-- Speeds up the per-company activity feed (newest first, one company).
CREATE INDEX IF NOT EXISTS IX_Audit_Company_Time ON AuditLog (CompanyId, CreatedAtUtc);
