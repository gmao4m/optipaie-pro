-- ============================================================================
--  OptiPaie PRO - Migration 0029 : employee & company identity fields for ATS/DRT
-- ----------------------------------------------------------------------------
--  Feeds the official CNAS attestations (ATS / DRT) with auto-filled data:
--    Employees.BirthPlace  -> NEA  (lieu de naissance)
--    Employees.Address     -> SADRESSE
--    Companies.ManagerName -> NMEMPLOYEUR (nom du responsable)
--    Companies.City        -> LIEU (ville, ligne de signature)
--  Purely ADDITIVE and non-destructive. Existing rows keep NULL for new columns.
-- ============================================================================

ALTER TABLE Employees ADD COLUMN BirthPlace  TEXT;
ALTER TABLE Employees ADD COLUMN Address     TEXT;

ALTER TABLE Companies ADD COLUMN ManagerName TEXT;
ALTER TABLE Companies ADD COLUMN City        TEXT;
