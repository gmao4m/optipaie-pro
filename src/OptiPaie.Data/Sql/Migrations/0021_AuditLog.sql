-- ============================================================================
--  OptiPaie PRO - Migration 0021 : Audit log (journal des modifications)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. An append-only trail: what entity changed, how, an optional
--  old -> new value, who and when. No foreign keys (it references logical entities
--  across modules by type + id); rows are never updated or deleted.
-- ============================================================================

CREATE TABLE IF NOT EXISTS AuditLog (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EntityType   TEXT    NOT NULL,
    EntityId     INTEGER NOT NULL,
    Action       INTEGER NOT NULL,
    Summary      TEXT,
    OldValue     TEXT,
    NewValue     TEXT,
    Actor        TEXT,
    CreatedAtUtc TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Audit_Entity ON AuditLog (EntityType, EntityId);
CREATE INDEX IF NOT EXISTS IX_Audit_Time   ON AuditLog (CreatedAtUtc);
