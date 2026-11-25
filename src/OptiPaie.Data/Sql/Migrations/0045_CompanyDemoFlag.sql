-- ============================================================================
--  OptiPaie PRO - Migration 0045 : flag the trial demo company
-- ----------------------------------------------------------------------------
--  The trial seeds a demo company ("SARL Atlas Industrie") with fake employees.
--  From now on the seeder marks it with IsDemo = 1 — the dedicated identifier the
--  "remove demo data" action relies on. Every existing company is 0: databases
--  already in the field are recognised ONLY by the demo fingerprint (the four
--  seeded legal identifiers + the exact seeded employee roster), never by name alone.
--  Purely additive.
-- ============================================================================

ALTER TABLE Companies ADD COLUMN IsDemo INTEGER NOT NULL DEFAULT 0;
