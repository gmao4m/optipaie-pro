-- ============================================================================
--  OptiPaie PRO - Migration 0025 : Asset ownership mode (exclusive vs shared)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE for behaviour. A single flag, OFF by default, so every existing
--  asset stays EXCLUSIVE (one holder at a time) exactly as before.
--
--  The old "one open assignment per asset" unique index blocked shared assets at
--  the DB level, so it is replaced by "one open assignment per (asset, employee)":
--    * shared assets can now have several concurrent holders (different employees);
--    * no employee can ever hold the same asset twice at once;
--    * the exclusive "one holder at a time" rule is enforced in AssetService (which
--      refuses a second holder for a non-shared asset), so nothing loosens for them.
-- ============================================================================

ALTER TABLE Assets ADD COLUMN IsShared INTEGER NOT NULL DEFAULT 0;

DROP INDEX IF EXISTS UX_Assignment_OpenPerAsset;

CREATE UNIQUE INDEX IF NOT EXISTS UX_Assignment_OpenPerAssetEmployee
    ON AssetAssignments (AssetId, EmployeeId)
    WHERE ReturnedDate IS NULL AND IsDeleted = 0;
